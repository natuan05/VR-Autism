using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    /// <summary>Bounded, rebuildable file cache. A file becomes visible only after validation and atomic rename.</summary>
    public sealed class TtsLocalCacheV1
    {
        public const long CapacityBytes = 256000000;
        private const int MaximumEntries = 16384;
        private const long IndexWriteHeadroomBytes = 12 * 1024 * 1024;
        private const string DirectoryName = "tts-cache/v1";
        private readonly string _root;
        private readonly string _indexPath;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _leases = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _reservations = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly HashSet<string> _invalidateOnRelease = new HashSet<string>(StringComparer.Ordinal);
        private long _sequence;

        [Serializable]
        private sealed class IndexFile { public long sequence; public List<Entry> entries = new List<Entry>(); }

        [Serializable]
        private sealed class Entry
        {
            public string profile_id, key, metadata_file, wav_file, artifact_json;
            public long last_use;
            public long stored_size;
        }

        public string RootPath => _root;

        public TtsLocalCacheV1(string persistentDataPath)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath)) throw new ArgumentException("Persistent path required.", nameof(persistentDataPath));
            _root = Path.GetFullPath(Path.Combine(persistentDataPath, DirectoryName.Replace('/', Path.DirectorySeparatorChar)));
            _indexPath = Path.Combine(_root, "index.json");
            Directory.CreateDirectory(_root);
            EnsureInsideRoot(_root);
            if (HasReparsePath(_root)) throw new InvalidDataException("Cache root cannot be a reparse point.");
            RebuildAndLoad();
        }

        public string[] GetInventory()
        {
            lock (_gate)
                return _entries.Values.Select(entry => entry.artifact_json).Where(value => !string.IsNullOrEmpty(value)).ToArray();
        }

        public void Invalidate(ArtifactV1 artifact)
        {
            if (artifact == null) return;
            var id = Identity(artifact.profile_id, artifact.key, artifact.rejection_epoch);
            lock (_gate)
            {
                if (_entries.TryGetValue(id, out var entry) && !_leases.ContainsKey(id))
                {
                    RemoveEntry(id, entry);
                    SaveIndex();
                }
                else if (_leases.ContainsKey(id)) _invalidateOnRelease.Add(id);
            }
        }

        public Reservation Reserve(long bytes)
        {
            if (bytes <= 0 || bytes > CapacityBytes - IndexWriteHeadroomBytes) return null;
            var reserved = checked(bytes + IndexWriteHeadroomBytes);
            lock (_gate)
            {
                var token = Guid.NewGuid().ToString("N");
                CleanupFor(reserved);
                if (MeasureDisk() + ReservedBytes() + reserved > CapacityBytes) return null;
                _reservations.Add(token, reserved);
                return new Reservation(this, token, bytes);
            }
        }

        public bool TryAcquire(string profileId, string key, long rejectionEpoch, out LocalAudioLease lease)
        {
            lease = null;
            lock (_gate)
            {
                var id = Identity(profileId, key, rejectionEpoch);
                if (_invalidateOnRelease.Contains(id)) return false;
                if (!_entries.ContainsKey(id) && rejectionEpoch == 0)
                {
                    var current = _entries.Where(pair => pair.Value.profile_id == profileId && pair.Value.key == key)
                        .Where(pair => !_invalidateOnRelease.Contains(pair.Key))
                        .OrderByDescending(pair => ParseEpoch(pair.Value.artifact_json)).FirstOrDefault();
                    if (current.Value != null) id = current.Key;
                }
                if (!_entries.TryGetValue(id, out var entry)) return false;
                try
                {
                    var wavPath = ResolveRelative(entry.wav_file);
                    var metadataPath = ResolveRelative(entry.metadata_file);
                    if (!File.Exists(wavPath) || !File.Exists(metadataPath) || HasReparsePath(wavPath) || HasReparsePath(metadataPath))
                    {
                        RemoveEntry(id, entry);
                        SaveIndex();
                        return false;
                    }
                    var artifact = TtsCacheContractsV1.ParseArtifact(entry.artifact_json);
                    if (new FileInfo(metadataPath).Length > 4096) throw new InvalidDataException("Cache metadata is too large.");
                    var sidecar = TtsCacheContractsV1.ParseArtifact(File.ReadAllText(metadataPath));
                    if (!string.Equals(sidecar.object_path, artifact.object_path, StringComparison.Ordinal) ||
                        !string.Equals(sidecar.generation, artifact.generation, StringComparison.Ordinal) ||
                        !string.Equals(sidecar.sha256, artifact.sha256, StringComparison.Ordinal))
                        throw new InvalidDataException("Cache sidecar does not match its index entry.");
                    var wavLength = new FileInfo(wavPath).Length;
                    if (wavLength != artifact.byte_size || wavLength > TtsCacheContractsV1.MaxWavBytes)
                        throw new InvalidDataException("Cached WAV size does not match its metadata.");
                    var bytes = File.ReadAllBytes(wavPath);
                    var metadata = TtsWavValidatorV1.Validate(bytes);
                    if (metadata.byte_size != artifact.byte_size || metadata.sample_count != artifact.sample_count ||
                        !string.Equals(metadata.sha256, artifact.sha256, StringComparison.Ordinal))
                    {
                        RemoveEntry(id, entry);
                        SaveIndex();
                        return false;
                    }
                    entry.last_use = ++_sequence;
                    _leases[id] = _leases.TryGetValue(id, out var count) ? count + 1 : 1;
                    SaveIndex();
                    lease = new LocalAudioLease(this, id, artifact, bytes);
                    return true;
                }
                catch
                {
                    RemoveEntry(id, entry);
                    SaveIndex();
                    return false;
                }
            }
        }

        public bool Store(ArtifactV1 artifact, byte[] wavBytes, Reservation reservation)
        {
            if (artifact == null || wavBytes == null || reservation == null || !reservation.IsOwnedBy(this)) return false;
            if (!TtsWavValidatorV1.TryValidate(wavBytes, out var wav, out _)) return false;
            return StoreValidated(artifact, wavBytes, wav, reservation);
        }

        /// <summary>Stores the same private buffer already validated by the caller, without hashing it again.</summary>
        internal bool StoreValidated(ArtifactV1 artifact, byte[] wavBytes, WavMetadataV1 wav, Reservation reservation)
        {
            if (artifact == null || wavBytes == null || reservation == null || !reservation.IsOwnedBy(this)) return false;
            try { artifact = TtsCacheContractsV1.ParseArtifact(JsonConvert.SerializeObject(artifact)); }
            catch { return false; }
            if (wavBytes.Length != wav.byte_size || artifact.byte_size != wav.byte_size ||
                artifact.sample_count != wav.sample_count || artifact.duration_ms != wav.duration_ms ||
                !string.Equals(artifact.sha256, wav.sha256, StringComparison.Ordinal)) return false;
            string id = Identity(artifact.profile_id, artifact.key, artifact.rejection_epoch);
            lock (_gate)
            {
                var wavRelative = artifact.profile_id + "/" + artifact.key + "/r" + artifact.rejection_epoch + ".wav";
                var metadataRelative = artifact.profile_id + "/" + artifact.key + "/r" + artifact.rejection_epoch + ".json";
                var wavPath = ResolveRelative(wavRelative);
                var metadataPath = ResolveRelative(metadataRelative);
                var tempWav = wavPath + ".tmp";
                var tempMetadata = metadataPath + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(wavPath));
                EnsureInsideRoot(Path.GetDirectoryName(wavPath));
                if (HasReparsePath(Path.GetDirectoryName(wavPath))) return false;
                var artifactJson = JsonConvert.SerializeObject(artifact);
                if (MeasureDisk() + wavBytes.LongLength + System.Text.Encoding.UTF8.GetByteCount(artifactJson) > CapacityBytes)
                    return false;
                try
                {
                    File.WriteAllBytes(tempWav, wavBytes);
                    File.WriteAllText(tempMetadata, artifactJson);
                    if (new FileInfo(tempWav).Length + new FileInfo(tempMetadata).Length > reservation.Bytes)
                        return false;
                    ReplaceAtomically(tempWav, wavPath);
                    ReplaceAtomically(tempMetadata, metadataPath);
                    if (_entries.TryGetValue(id, out var previous))
                        _entries.Remove(id);
                    _entries[id] = new Entry
                    {
                        profile_id = artifact.profile_id,
                        key = artifact.key,
                        wav_file = wavRelative,
                        metadata_file = metadataRelative,
                        artifact_json = artifactJson,
                        last_use = ++_sequence,
                        stored_size = wavBytes.Length + new FileInfo(metadataPath).Length
                    };
                    SaveIndex();
                    return true;
                }
                catch
                {
                    TryDelete(tempWav);
                    TryDelete(tempMetadata);
                    return false;
                }
                finally
                {
                    TryDelete(tempWav);
                    TryDelete(tempMetadata);
                }
            }
        }

        private void RebuildAndLoad()
        {
            lock (_gate)
            {
                DeleteAbandonedTemps(_root);
                try
                {
                    if (File.Exists(_indexPath) && !HasReparsePath(_indexPath))
                    {
                        var index = JsonConvert.DeserializeObject<IndexFile>(File.ReadAllText(_indexPath));
                        if (index != null && index.entries != null)
                        {
                            _sequence = Math.Max(0, index.sequence);
                            foreach (var entry in index.entries)
                            {
                                if (entry == null || string.IsNullOrWhiteSpace(entry.artifact_json)) continue;
                                var artifact = TtsCacheContractsV1.ParseArtifact(entry.artifact_json);
                                var expectedWav = artifact.profile_id + "/" + artifact.key + "/r" + artifact.rejection_epoch + ".wav";
                                var expectedMetadata = artifact.profile_id + "/" + artifact.key + "/r" + artifact.rejection_epoch + ".json";
                                if (!string.Equals(entry.wav_file, expectedWav, StringComparison.Ordinal) ||
                                    !string.Equals(entry.metadata_file, expectedMetadata, StringComparison.Ordinal)) continue;
                                var id = Identity(artifact.profile_id, artifact.key, artifact.rejection_epoch);
                                var wavPath = ResolveRelative(entry.wav_file);
                                var metadataPath = ResolveRelative(entry.metadata_file);
                                if (!File.Exists(wavPath) || !File.Exists(metadataPath) || HasReparsePath(wavPath) || HasReparsePath(metadataPath)) continue;
                                _entries[id] = entry;
                            }
                        }
                    }
                }
                catch { _entries.Clear(); }
                RecoverSidecars();
                // Disk files are accepted only when their sidecar and WAV agree, so a bad index never creates a hit.
                SaveIndex();
                CleanupFor(0);
            }
        }

        private void CleanupFor(long requested)
        {
            long total = MeasureDisk() + ReservedBytes();
            foreach (var item in _entries.OrderBy(pair => pair.Value.last_use).ThenBy(pair => pair.Key, StringComparer.Ordinal).ToArray())
            {
                if (total + requested <= CapacityBytes && _entries.Count < MaximumEntries) break;
                if (_leases.ContainsKey(item.Key)) continue;
                total -= item.Value.stored_size;
                RemoveEntry(item.Key, item.Value);
            }
            if (total + requested > CapacityBytes)
            {
                var owned = new HashSet<string>(_entries.Values.SelectMany(entry => new[] { entry.wav_file, entry.metadata_file })
                    .Where(path => !string.IsNullOrEmpty(path)).Select(path => ResolveRelative(path)), StringComparer.OrdinalIgnoreCase);
                foreach (var pattern in new[] { "*.wav", "*.json" })
                {
                    foreach (var file in SafeEnumerateFiles(_root, pattern, 100000))
                    {
                        if (total + requested <= CapacityBytes) break;
                        if (owned.Contains(file) || string.Equals(file, _indexPath, StringComparison.OrdinalIgnoreCase) || HasReparsePath(file)) continue;
                        try { total -= new FileInfo(file).Length; File.Delete(file); } catch { }
                    }
                }
            }
            SaveIndex();
        }

        private long MeasureDisk()
        {
            if (!Directory.Exists(_root)) return 0;
            long total = 0;
            foreach (var file in SafeEnumerateFiles(_root, "*", 100000))
            {
                if (HasReparsePath(file)) continue;
                try { total += new FileInfo(file).Length; } catch { }
            }
            return total;
        }

        private long ReservedBytes() => _reservations.Values.Sum();

        private void RemoveEntry(string id, Entry entry)
        {
            _entries.Remove(id);
            TryDeleteOwned(entry.wav_file);
            TryDeleteOwned(entry.metadata_file);
        }

        private void TryDeleteOwned(string relative)
        {
            try
            {
                var path = ResolveRelative(relative);
                if (!HasReparsePath(path)) TryDelete(path);
            }
            catch { }
        }

        private void SaveIndex()
        {
            var temp = _indexPath + ".tmp";
            try
            {
                var json = JsonConvert.SerializeObject(new IndexFile { sequence = _sequence, entries = _entries.Values.ToList() });
                // Index replacement briefly owns both files. Skipping an oversized rebuildable index is
                // safer than exceeding the disk cap; validated sidecars recover the entries on next launch.
                if (MeasureDisk() + System.Text.Encoding.UTF8.GetByteCount(json) > CapacityBytes)
                { TryDelete(temp); return; }
                File.WriteAllText(temp, json);
                ReplaceAtomically(temp, _indexPath);
            }
            catch { TryDelete(temp); }
        }

        private string ResolveRelative(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Invalid cache-relative path.");
            var path = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
            EnsureInsideRoot(path);
            return path;
        }

        private void EnsureInsideRoot(string path)
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var root = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), _root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cache path escaped the owned directory.");
        }

        private bool HasReparsePath(string path)
        {
            try
            {
                var current = Path.GetFullPath(path);
                while (!string.IsNullOrEmpty(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
                    current = Path.GetDirectoryName(current);
                }
            }
            catch { return true; }
            return false;
        }

        private void RecoverSidecars()
        {
            foreach (var metadataPath in SafeEnumerateFiles(_root, "*.json", 100000))
            {
                if (string.Equals(metadataPath, _indexPath, StringComparison.OrdinalIgnoreCase) || HasReparsePath(metadataPath)) continue;
                try
                {
                    if (new FileInfo(metadataPath).Length > 4096) continue;
                    var artifactJson = File.ReadAllText(metadataPath);
                    var artifact = TtsCacheContractsV1.ParseArtifact(artifactJson);
                    var expectedMetadata = ResolveRelative(artifact.profile_id + "/" + artifact.key + "/r" + artifact.rejection_epoch + ".json");
                    if (!string.Equals(metadataPath, expectedMetadata, StringComparison.OrdinalIgnoreCase)) continue;
                    var wavRelative = artifact.profile_id + "/" + artifact.key + "/r" + artifact.rejection_epoch + ".wav";
                    var wavPath = ResolveRelative(wavRelative);
                    if (!File.Exists(wavPath) || HasReparsePath(wavPath)) continue;
                    if (new FileInfo(wavPath).Length != artifact.byte_size || artifact.byte_size > TtsCacheContractsV1.MaxWavBytes) continue;
                    var bytes = File.ReadAllBytes(wavPath);
                    var metadata = TtsWavValidatorV1.Validate(bytes);
                    if (metadata.byte_size != artifact.byte_size || metadata.sample_count != artifact.sample_count ||
                        !string.Equals(metadata.sha256, artifact.sha256, StringComparison.Ordinal)) continue;
                    var id = Identity(artifact.profile_id, artifact.key, artifact.rejection_epoch);
                    // Preserve valid indexed LRU values; recover only entries whose index was lost.
                    if (_entries.ContainsKey(id)) continue;
                    _entries[id] = new Entry
                    {
                        profile_id = artifact.profile_id, key = artifact.key, wav_file = wavRelative,
                        metadata_file = artifact.profile_id + "/" + artifact.key + "/r" + artifact.rejection_epoch + ".json",
                        artifact_json = artifactJson, last_use = ++_sequence,
                        stored_size = new FileInfo(wavPath).Length + new FileInfo(metadataPath).Length
                    };
                }
                catch { }
            }
        }

        private static long ParseEpoch(string artifactJson)
        { try { return TtsCacheContractsV1.ParseArtifact(artifactJson).rejection_epoch; } catch { return -1; } }

        private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern, int maximumEntries)
        {
            var pending = new Stack<string>();
            var yielded = 0;
            pending.Push(root);
            while (pending.Count > 0 && yielded < maximumEntries)
            {
                var directory = pending.Pop();
                string[] files;
                string[] directories;
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
                    directories = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly);
                }
                catch { continue; }
                foreach (var file in files)
                {
                    if (++yielded > maximumEntries) yield break;
                    var safe = false;
                    try { safe = (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0; } catch { }
                    if (safe) yield return file;
                }
                foreach (var child in directories)
                {
                    if (++yielded > maximumEntries) yield break;
                    var safe = false;
                    try { safe = (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0; } catch { }
                    if (safe) pending.Push(child);
                }
            }
        }

        private void DeleteAbandonedTemps(string path)
        {
            foreach (var file in SafeEnumerateFiles(path, "*.tmp", 10000))
                try { if (!HasReparsePath(file)) File.Delete(file); } catch { }
        }

        private static void ReplaceAtomically(string source, string target)
        {
            if (File.Exists(target)) File.Replace(source, target, null);
            else File.Move(source, target);
        }

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        private static string Identity(string profileId, string key, long epoch) => profileId + "/" + key + "/" + epoch;

        private void ReleaseLease(string id)
        {
            lock (_gate)
            {
                if (!_leases.TryGetValue(id, out var count)) return;
                if (count <= 1)
                {
                    _leases.Remove(id);
                    if (_invalidateOnRelease.Remove(id) && _entries.TryGetValue(id, out var entry))
                    { RemoveEntry(id, entry); SaveIndex(); }
                }
                else _leases[id] = count - 1;
            }
        }

        private void ReleaseReservation(string token)
        {
            lock (_gate) _reservations.Remove(token);
        }

        public sealed class Reservation : IDisposable
        {
            private TtsLocalCacheV1 _owner;
            private readonly string _token;
            public long Bytes { get; }
            internal Reservation(TtsLocalCacheV1 owner, string token, long bytes) { _owner = owner; _token = token; Bytes = bytes; }
            internal bool IsOwnedBy(TtsLocalCacheV1 owner) => ReferenceEquals(_owner, owner);
            public void Dispose() { var owner = Interlocked.Exchange(ref _owner, null); owner?.ReleaseReservation(_token); }
        }

        public sealed class LocalAudioLease : IDisposable
        {
            private TtsLocalCacheV1 _owner;
            private readonly string _identity;
            public ArtifactV1 Artifact { get; }
            public byte[] WavBytes { get; }
            internal LocalAudioLease(TtsLocalCacheV1 owner, string identity, ArtifactV1 artifact, byte[] bytes)
            { _owner = owner; _identity = identity; Artifact = artifact; WavBytes = bytes; }
            public void Dispose() { var owner = Interlocked.Exchange(ref _owner, null); owner?.ReleaseLease(_identity); }
        }
    }
}
