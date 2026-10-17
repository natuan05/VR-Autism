using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Cloud.LiveKit;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    [Serializable]
    public sealed class FixedUtteranceV1
    {
        public InvocationV1 invocation;
        public long owner_revision;
        public string text, profile_id, key;
        public long rejection_epoch;
        public TtsProfileV1 profile;
    }

    public enum ResolvedAudioOutcomeV1 { LocalReady, StreamRequired, Cancelled, Failed }

    public sealed class ResolvedAudioV1 : IDisposable
    {
        private Stopwatch _remoteDeadline;
        private int _remoteBudgetMs;
        public ResolvedAudioOutcomeV1 Outcome { get; internal set; }
        public AudioClip Clip { get; internal set; }
        public ArtifactV1 Artifact { get; internal set; }
        public TtsLocalCacheV1.LocalAudioLease Lease { get; internal set; }
        public string Reason { get; internal set; }
        public bool IsWithinRemoteBudget => _remoteDeadline == null || _remoteDeadline.ElapsedMilliseconds <= _remoteBudgetMs;
        internal void SetRemoteDeadline(Stopwatch deadline, int budgetMs)
        { _remoteDeadline = deadline; _remoteBudgetMs = budgetMs; }
        public void Dispose()
        {
            Lease?.Dispose(); Lease = null;
            if (Clip != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(Clip);
                else UnityEngine.Object.DestroyImmediate(Clip);
            }
            Clip = null;
        }
    }

    /// <summary>Resolves fixed utterances from local/remote cache before selecting LiveKit streaming once.</summary>
    public sealed class TtsAudioResolverV1 : ITtsAudioCacheWarmerV1
    {
        private readonly TtsLocalCacheV1 _cache;
        private readonly TtsCacheBackendClientV1 _backend;
        private readonly SessionTtsSnapshotV1 _snapshot;
        private readonly string _sessionId;
        private readonly object _reconcileGate = new object();
        private readonly SemaphoreSlim _remoteWorkGate = new SemaphoreSlim(1, 1);
        private readonly object _warmGate = new object();
        private readonly Dictionary<string, Task<bool>> _warmJobs = new Dictionary<string, Task<bool>>(StringComparer.Ordinal);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource _warmGeneration = new CancellationTokenSource();
        private Task _reconcileTask;
        private bool _reconcileSucceeded;
        private bool _disposed;

        public TtsAudioResolverV1(TtsLocalCacheV1 cache, TtsCacheBackendClientV1 backend, SessionTtsSnapshotV1 snapshot)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _sessionId = snapshot.Scope.session_id;
        }

        public async Task ReconcileLaunchAsync(CancellationToken cancellationToken)
        {
            Task pending;
            lock (_reconcileGate)
            {
                if (_reconcileTask == null) _reconcileTask = ReconcileCoreAsync(cancellationToken);
                pending = _reconcileTask;
            }
            if (cancellationToken.CanBeCanceled)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
                {
                    var winner = await Task.WhenAny(pending, cancelled.Task);
                    if (winner != pending) cancellationToken.ThrowIfCancellationRequested();
                }
            }
            try
            {
                await pending;
                lock (_reconcileGate)
                    if (!_reconcileSucceeded && ReferenceEquals(_reconcileTask, pending)) _reconcileTask = null;
            }
            catch
            {
                lock (_reconcileGate) if (ReferenceEquals(_reconcileTask, pending)) _reconcileTask = null;
                throw;
            }
        }

        public void BeginLaunchReconciliation() => ObserveLaunchReconciliationAsync();

        private async void ObserveLaunchReconciliationAsync()
        {
            try { await ReconcileLaunchAsync(_lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception exception) { UnityEngine.Debug.LogWarning("TTS launch reconciliation missed: " + exception.Message); }
        }

        private async Task ReconcileCoreAsync(CancellationToken cancellationToken)
        {
            var jsonEntries = _cache.GetInventory();
            var artifacts = new List<ArtifactV1>();
            var configuredProfiles = new HashSet<string>(
                _snapshot.Config.profiles.Select(profile => profile.profile_id), StringComparer.Ordinal);
            foreach (var json in jsonEntries)
            {
                try
                {
                    var artifact = TtsCacheContractsV1.ParseArtifact(json);
                    if (configuredProfiles.Contains(artifact.profile_id)) artifacts.Add(artifact);
                    else _cache.Invalidate(artifact);
                }
                catch { }
            }
            if (artifacts.Count == 0) { _reconcileSucceeded = true; return; }
            var valid = await _backend.ReconcileAsync(_sessionId, artifacts, cancellationToken);
            // A response must cover every entry. Unverified items are removed before the first local hit.
            for (var i = 0; i < artifacts.Count; i++)
                if (valid == null || i >= valid.Length || !valid[i]) _cache.Invalidate(artifacts[i]);
            _reconcileSucceeded = valid != null && valid.Length == artifacts.Count;
        }

        public async Task<ResolvedAudioV1> ResolveAsync(FixedUtteranceV1 request, CancellationToken cancellationToken)
        {
            if (_disposed) return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.Cancelled };
            if (request == null || request.invocation == null || request.profile == null ||
                string.IsNullOrWhiteSpace(request.text))
                return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.Failed, Reason = "INVALID_REQUEST" };
            try
            {
                TtsCacheContractsV1.ValidateProfile(request.profile);
                var key = TtsCacheContractsV1.ComputeTtsCacheKey(request.text, request.profile);
                if (!string.Equals(key, request.key, StringComparison.Ordinal) || !string.Equals(request.profile_id, request.profile.profile_id, StringComparison.Ordinal))
                    return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.Failed, Reason = "CACHE_KEY_MISMATCH" };
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
                {
                    var token = linked.Token;
                    var budgetMs = _snapshot.Config.remote_cache_wait_ms;
                    Stopwatch deadline = null;
                    if (!_reconcileSucceeded)
                    {
                        // If launch verification is still pending, its wait and all subsequent
                        // local/remote preparation share one deadline for this utterance.
                        deadline = StartDeadlineIfMissing(deadline, true);
                        using (var reconciliationBudget = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            reconciliationBudget.CancelAfter(Math.Min(TtsCacheBackendClientV1.ReconcileRequestTimeoutMs,
                                RemainingBudget(deadline, budgetMs)));
                            try { await ReconcileLaunchAsync(reconciliationBudget.Token); }
                            catch (OperationCanceledException) when (!token.IsCancellationRequested)
                            { return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.StreamRequired }; }
                        }
                        try { token.ThrowIfCancellationRequested(); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested)
                        { return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.Cancelled }; }
                        catch { return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.StreamRequired }; }
                    }
                    if (!_reconcileSucceeded) return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.StreamRequired };
                    token.ThrowIfCancellationRequested();
                    if (_cache.TryAcquire(request.profile_id, request.key, request.rejection_epoch, out var local))
                    {
                        var localResolved = BuildResolved(local.Artifact, local.WavBytes, local);
                        if (deadline == null) return localResolved;
                        localResolved.SetRemoteDeadline(deadline, budgetMs);
                        if (localResolved.IsWithinRemoteBudget) return localResolved;
                        localResolved.Dispose();
                        return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.StreamRequired };
                    }

                    // With a completed launch pass, the budget starts exactly at the local miss.
                    if (deadline != null && deadline.ElapsedMilliseconds >= budgetMs)
                        return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.StreamRequired };
                    deadline = StartDeadlineIfMissing(deadline, true);
                    using (var remoteCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        remoteCts.CancelAfter(RemainingBudget(deadline, budgetMs));
                    var gateAcquired = false;
                    try
                    {
                        await _remoteWorkGate.WaitAsync(remoteCts.Token);
                        gateAcquired = true;
                        if (deadline.ElapsedMilliseconds >= budgetMs)
                            return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.StreamRequired };
                        var lookup = await _backend.LookupAsync(_sessionId, request.profile_id, request.key, remoteCts.Token,
                            RemainingBudget(deadline, budgetMs));
                        request.rejection_epoch = lookup.RejectionEpoch;
                        if (!lookup.IsMiss && deadline.ElapsedMilliseconds < budgetMs)
                        {
                            if (!string.Equals(lookup.Artifact.profile_id, request.profile_id, StringComparison.Ordinal) ||
                                !string.Equals(lookup.Artifact.key, request.key, StringComparison.Ordinal))
                                throw new CacheValidationException("Lookup returned a different cache identity.");
                            request.rejection_epoch = lookup.Artifact.rejection_epoch;
                            var bytes = await _backend.DownloadAsync(lookup.ReadUrl, remoteCts.Token,
                                RemainingBudget(deadline, budgetMs));
                            var metadata = TtsWavValidatorV1.Validate(bytes);
                            if (metadata.byte_size != lookup.Artifact.byte_size || metadata.sample_count != lookup.Artifact.sample_count ||
                                !string.Equals(metadata.sha256, lookup.Artifact.sha256, StringComparison.Ordinal))
                                throw new CacheValidationException("Downloaded artifact metadata mismatch.");
                            if (deadline.ElapsedMilliseconds <= budgetMs)
                            {
                                var reservation = _cache.Reserve(bytes.Length + 2048);
                                if (reservation != null)
                                {
                                    using (reservation) _cache.StoreValidated(lookup.Artifact, bytes, metadata, reservation);
                                }
                                var remoteResolved = BuildResolved(lookup.Artifact, bytes, null);
                                remoteResolved.SetRemoteDeadline(deadline, budgetMs);
                                if (remoteResolved.IsWithinRemoteBudget) return remoteResolved;
                                remoteResolved.Dispose();
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                    catch (OperationCanceledException) { return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.Cancelled }; }
                    catch (Exception exception) { UnityEngine.Debug.LogWarning("TTS cache lookup missed: " + exception.Message); }
                    finally { if (gateAcquired) _remoteWorkGate.Release(); }
                    }
                    token.ThrowIfCancellationRequested();
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.StreamRequired };
            }
            catch (OperationCanceledException)
            {
                return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.Cancelled };
            }
            catch (Exception exception)
            {
                return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.Failed, Reason = exception.Message };
            }
        }

        /// <summary>Warms persistent storage from a current-boot READY hint without affecting playback ownership.</summary>
        public Task<bool> WarmReadyAsync(ArtifactV1 announced)
        {
            if (_disposed || announced == null) return Task.FromResult(false);
            ArtifactV1 artifact;
            try { artifact = TtsCacheContractsV1.ParseArtifact(Newtonsoft.Json.JsonConvert.SerializeObject(announced)); }
            catch { return Task.FromResult(false); }
            if (!_snapshot.Config.profiles.Any(profile => profile.profile_id == artifact.profile_id)) return Task.FromResult(false);
            var jobKey = artifact.profile_id + "/" + artifact.key + "/" + artifact.rejection_epoch;
            lock (_warmGate)
            {
                if (_warmJobs.TryGetValue(jobKey, out var pending)) return pending;
                if (_warmJobs.Count >= 32) return Task.FromResult(false);
                var completion = new TaskCompletionSource<bool>();
                _warmJobs.Add(jobKey, completion.Task);
                RunWarmReadyAsync(jobKey, artifact, completion);
                return completion.Task;
            }
        }

        private async void RunWarmReadyAsync(string jobKey, ArtifactV1 announced, TaskCompletionSource<bool> completion)
        {
            var stored = false;
            TtsLocalCacheV1.Reservation reservation = null;
            var gateAcquired = false;
            CancellationTokenSource warmDeadline = null;
            try
            {
                CancellationToken generationToken;
                lock (_warmGate) generationToken = _warmGeneration.Token;
                warmDeadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, generationToken);
                warmDeadline.CancelAfter(TtsCacheBackendClientV1.BackgroundWarmTimeoutMs);
                var token = warmDeadline.Token;
                await _remoteWorkGate.WaitAsync(token);
                gateAcquired = true;
                var current = await _backend.LookupAsync(_sessionId, announced.profile_id, announced.key, token,
                    TtsCacheBackendClientV1.BackgroundWarmTimeoutMs);
                if (current.IsMiss || !SameArtifact(current.Artifact, announced)) return;
                reservation = _cache.Reserve(TtsCacheContractsV1.MaxWavBytes + 4096L);
                if (reservation == null) return;
                var bytes = await _backend.DownloadAsync(current.ReadUrl, token, TtsCacheBackendClientV1.BackgroundWarmTimeoutMs);
                var metadata = TtsWavValidatorV1.Validate(bytes);
                if (metadata.byte_size != current.Artifact.byte_size || metadata.sample_count != current.Artifact.sample_count ||
                    !string.Equals(metadata.sha256, current.Artifact.sha256, StringComparison.Ordinal)) return;
                // Recheck immediately before commit so a READY from a just-rejected epoch cannot be restored.
                var confirmation = await _backend.LookupAsync(_sessionId, announced.profile_id, announced.key, token,
                    TtsCacheBackendClientV1.BackgroundWarmTimeoutMs);
                if (confirmation.IsMiss || !SameArtifact(confirmation.Artifact, current.Artifact)) return;
                token.ThrowIfCancellationRequested();
                stored = _cache.StoreValidated(current.Artifact, bytes, metadata, reservation);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { UnityEngine.Debug.LogWarning("TTS cache warm missed: " + exception.Message); }
            finally
            {
                reservation?.Dispose();
                if (gateAcquired) _remoteWorkGate.Release();
                warmDeadline?.Dispose();
                lock (_warmGate) _warmJobs.Remove(jobKey);
                completion.TrySetResult(stored);
            }
        }

        private static bool SameArtifact(ArtifactV1 left, ArtifactV1 right) => left != null && right != null &&
            left.profile_id == right.profile_id && left.key == right.key && left.rejection_epoch == right.rejection_epoch &&
            left.object_path == right.object_path && left.generation == right.generation && left.sha256 == right.sha256 &&
            left.byte_size == right.byte_size && left.sample_count == right.sample_count &&
            left.duration_ms == right.duration_ms && left.format == right.format;

        internal static Stopwatch StartDeadlineIfMissing(Stopwatch deadline, bool shouldStart) =>
            deadline ?? (shouldStart ? Stopwatch.StartNew() : null);

        internal static int RemainingBudget(Stopwatch deadline, int budgetMs) =>
            Math.Max(1, budgetMs - (int)Math.Min(int.MaxValue, deadline.ElapsedMilliseconds));

        public void CancelBackgroundWarms()
        {
            lock (_warmGate)
            {
                if (_disposed) return;
                var previous = _warmGeneration;
                _warmGeneration = new CancellationTokenSource();
                try { previous.Cancel(); } catch (ObjectDisposedException) { }
                previous.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_warmGate)
            {
                try { _warmGeneration.Cancel(); } catch (ObjectDisposedException) { }
                _warmGeneration.Dispose();
            }
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        private static ResolvedAudioV1 BuildResolved(ArtifactV1 artifact, byte[] wav, TtsLocalCacheV1.LocalAudioLease lease)
        {
            // Both resolver downloads and local leases have already validated this
            // buffer against the artifact. Reuse its verified sample count for decoding.
            var clip = DecodeClip(wav, artifact.sample_count);
            if (clip == null)
            {
                lease?.Dispose();
                return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.Failed, Reason = "AUDIO_DECODE_FAILED" };
            }
            return new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.LocalReady, Artifact = artifact, Clip = clip, Lease = lease };
        }

        private static AudioClip DecodeClip(byte[] wav, int sampleCount)
        {
            var pcm = new float[sampleCount];
            for (var i = 0; i < sampleCount; i++)
            {
                short sample = (short)(wav[44 + i * 2] | (wav[45 + i * 2] << 8));
                pcm[i] = sample / 32768f;
            }
            var clip = AudioClip.Create("TTS Cache", sampleCount, 1, 24000, false);
            if (clip == null || !clip.SetData(pcm, 0))
            {
                if (clip != null) UnityEngine.Object.Destroy(clip);
                return null;
            }
            return clip;
        }
    }
}
