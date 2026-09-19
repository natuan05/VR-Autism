using System;
using System.Collections.Generic;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;

namespace VRAutism.Cloud.LiveKit
{
    internal sealed class LiveKitNpcAudioRouter
    {
        private readonly Dictionary<string, (AudioStream stream, GameObject go)> _remoteAudioStreams = new();
        private AudioSource _npcAudioSource;
        private RemoteAudioTrack _pendingAudioTrack;

        private sealed class PendingTrackEntry
        {
            public RemoteAudioTrack Track { get; }
            public string ParticipantIdentity { get; }

            public PendingTrackEntry(RemoteAudioTrack track, string participantIdentity)
            {
                Track = track;
                ParticipantIdentity = participantIdentity;
            }
        }

        private sealed class ActiveTrackEntry
        {
            public RemoteAudioTrack Track { get; set; }
            public IDisposable Stream { get; set; }
            public AudioSource Source { get; set; }
            public string NpcBindingId { get; set; }
        }

        private readonly Dictionary<string, AudioSource> _npcAudioRoutes = new Dictionary<string, AudioSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingTrackEntry> _pendingAudioTracks = new Dictionary<string, PendingTrackEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActiveTrackEntry> _activeAudioStreams = new Dictionary<string, ActiveTrackEntry>(StringComparer.Ordinal);
        private readonly object _audioRoutingLock = new object();
        private string _activeNpcBindingId = string.Empty;

        internal Func<RemoteAudioTrack, AudioSource, IDisposable> StreamFactory { get; set; }

        internal int ActiveV2StreamCount
        {
            get { lock (_audioRoutingLock) return _activeAudioStreams.Count; }
        }

        internal int PendingV2TrackCount
        {
            get { lock (_audioRoutingLock) return _pendingAudioTracks.Count; }
        }

        internal string ActiveNpcBindingId
        {
            get
            {
                lock (_audioRoutingLock)
                {
                    return _activeNpcBindingId;
                }
            }
        }

        internal bool IsV2TrackActive(string trackSid)
        {
            lock (_audioRoutingLock) return _activeAudioStreams.ContainsKey(trackSid);
        }

        internal bool IsV2TrackPending(string trackSid)
        {
            lock (_audioRoutingLock) return _pendingAudioTracks.ContainsKey(trackSid);
        }

        internal void SimulatePendingAudioTrack(string trackSid, string participantIdentity)
        {
            lock (_audioRoutingLock)
            {
                _pendingAudioTracks[trackSid] = new PendingTrackEntry(null, participantIdentity);
            }
        }

        internal void SimulateActiveAudioStream(string trackSid, AudioSource source, string npcBindingId)
        {
            lock (_audioRoutingLock)
            {
                _activeAudioStreams[trackSid] = new ActiveTrackEntry
                {
                    Track = null,
                    Stream = null,
                    Source = source,
                    NpcBindingId = npcBindingId
                };
            }
        }

        internal AudioSource GetActiveStreamSource(string trackSid)
        {
            lock (_audioRoutingLock)
            {
                return _activeAudioStreams.TryGetValue(trackSid, out var entry) ? entry.Source : null;
            }
        }

        internal string GetActiveStreamRoute(string trackSid)
        {
            lock (_audioRoutingLock)
            {
                return _activeAudioStreams.TryGetValue(trackSid, out var entry) ? entry.NpcBindingId : null;
            }
        }

        internal void RegisterNpcAudioRoute(string npcBindingId, AudioSource source)
        {
            if (string.IsNullOrWhiteSpace(npcBindingId))
            {
                Debug.LogWarning("[LiveKitService] RegisterNpcAudioRoute: npcBindingId must not be empty or whitespace.");
                return;
            }

            if (source == null)
            {
                Debug.LogWarning($"[LiveKitService] RegisterNpcAudioRoute: source cannot be null for route '{npcBindingId}'.");
                return;
            }

            lock (_audioRoutingLock)
            {
                if (_npcAudioRoutes.TryGetValue(npcBindingId, out var existingSource))
                {
                    if (existingSource != source)
                    {
                        // Route source swap / rebind
                        _npcAudioRoutes[npcBindingId] = source;
                        Debug.Log($"[LiveKitService] 🔄 Swapping AudioSource for route '{npcBindingId}' to '{source.gameObject.name}'");

                        foreach (var entry in _activeAudioStreams.Values)
                        {
                            if (string.Equals(entry.NpcBindingId, npcBindingId, StringComparison.Ordinal))
                            {
                                try { entry.Stream?.Dispose(); } catch { }
                                entry.Source = source;
                                entry.Stream = CreateAudioStream(entry.Track, source);
                                Debug.Log($"[LiveKitService] 🔄 Rebound active stream for track '{entry.Track?.Sid}' to new AudioSource '{source.gameObject.name}'");
                            }
                        }
                    }
                }
                else
                {
                    _npcAudioRoutes[npcBindingId] = source;
                    Debug.Log($"[LiveKitService] 🔊 Registered NPC audio route '{npcBindingId}' -> '{source.gameObject.name}'");
                }

                // If this is currently the active route, re-target active streams to it
                if (string.Equals(_activeNpcBindingId, npcBindingId, StringComparison.Ordinal))
                {
                    foreach (var entry in _activeAudioStreams.Values)
                    {
                        if (entry.Source != source)
                        {
                            try { entry.Stream?.Dispose(); } catch { }
                            entry.Source = source;
                            entry.NpcBindingId = npcBindingId;
                            entry.Stream = CreateAudioStream(entry.Track, source);
                            Debug.Log($"[LiveKitService] 🔄 Re-targeted active stream '{entry.Track?.Sid}' to newly registered active route '{npcBindingId}'");
                        }
                    }
                }

                // Drain any pending tracks matching this route (handles both new routes and route swaps)
                var matchingPendingSids = new List<string>();
                foreach (var kvp in _pendingAudioTracks)
                {
                    if (string.Equals(kvp.Value.ParticipantIdentity, npcBindingId, StringComparison.Ordinal) ||
                        string.Equals(_activeNpcBindingId, npcBindingId, StringComparison.Ordinal))
                    {
                        matchingPendingSids.Add(kvp.Key);
                    }
                }

                foreach (var sid in matchingPendingSids)
                {
                    if (_pendingAudioTracks.TryGetValue(sid, out var pending))
                    {
                        _pendingAudioTracks.Remove(sid);
                        Debug.Log($"[LiveKitService] 🔗 Binding deferred pending audio track '{sid}' to route '{npcBindingId}'");
                        BindV2AudioTrack(pending.Track, source, npcBindingId, sid);
                    }
                }
            }
        }

        internal void UnregisterNpcAudioRoute(string npcBindingId)
        {
            if (string.IsNullOrWhiteSpace(npcBindingId)) return;

            lock (_audioRoutingLock)
            {
                if (string.Equals(_activeNpcBindingId, npcBindingId, StringComparison.Ordinal))
                {
                    _activeNpcBindingId = string.Empty;
                }

                if (_npcAudioRoutes.Remove(npcBindingId))
                {
                    Debug.Log($"[LiveKitService] 🔇 Unregistered NPC audio route '{npcBindingId}'");
                }

                // Cleanup active streams for this route and preserve tracks in pending so re-registering rebinds them
                var activeToRemove = new List<string>();
                foreach (var kvp in _activeAudioStreams)
                {
                    if (string.Equals(kvp.Value.NpcBindingId, npcBindingId, StringComparison.Ordinal))
                    {
                        try { kvp.Value.Stream?.Dispose(); } catch { }
                        if (kvp.Value.Track != null)
                        {
                            _pendingAudioTracks[kvp.Key] = new PendingTrackEntry(kvp.Value.Track, npcBindingId);
                        }
                        activeToRemove.Add(kvp.Key);
                    }
                }
                foreach (var sid in activeToRemove)
                {
                    _activeAudioStreams.Remove(sid);
                }
            }
        }

        internal bool SetActiveNpcRoute(string npcBindingId)
        {
            if (string.IsNullOrWhiteSpace(npcBindingId))
            {
                Debug.LogWarning("[LiveKitService] SetActiveNpcRoute: npcBindingId must not be empty or whitespace.");
                return false;
            }

            lock (_audioRoutingLock)
            {
                _activeNpcBindingId = npcBindingId;

                if (_npcAudioRoutes.TryGetValue(npcBindingId, out var targetSource) && targetSource != null)
                {
                    foreach (var entry in _activeAudioStreams.Values)
                    {
                        if (entry.Source != targetSource)
                        {
                            try { entry.Stream?.Dispose(); } catch { }
                            entry.Source = targetSource;
                            entry.NpcBindingId = npcBindingId;
                            entry.Stream = CreateAudioStream(entry.Track, targetSource);
                            Debug.Log($"[LiveKitService] 🔄 Dynamically re-routed active stream '{entry.Track?.Sid}' to NPC '{npcBindingId}' ({targetSource.gameObject.name})");
                        }
                    }

                    var drainedSids = new List<string>(_pendingAudioTracks.Keys);
                    foreach (var sid in drainedSids)
                    {
                        var pending = _pendingAudioTracks[sid];
                        _pendingAudioTracks.Remove(sid);
                        Debug.Log($"[LiveKitService] 🔗 Binding deferred pending audio track '{sid}' to active route '{npcBindingId}'");
                        BindV2AudioTrack(pending.Track, targetSource, npcBindingId, sid);
                    }
                    return true;
                }
                else
                {
                    if (_npcAudioRoutes.Count == 0)
                    {
                        if (_npcAudioSource != null)
                        {
                            Debug.LogWarning($"[LiveKitService] No V2 routes registered; active route '{npcBindingId}' will fallback to primary scene AudioSource.");
                        }
                        else
                        {
                            Debug.LogWarning($"[LiveKitService] Route '{npcBindingId}' not yet registered. Audio tracks will remain pending.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[LiveKitService] Route '{npcBindingId}' not yet registered. Active stream deferred until route registration.");
                    }
                    return false;
                }
            }
        }

        internal bool TryGetNpcAudioRoute(string npcBindingId, out AudioSource source)
        {
            if (string.IsNullOrWhiteSpace(npcBindingId))
            {
                source = null;
                return false;
            }
            lock (_audioRoutingLock)
            {
                return _npcAudioRoutes.TryGetValue(npcBindingId, out source) && source != null;
            }
        }

        internal void SetLegacyAudioSource(AudioSource source)
        {
            _npcAudioSource = source;
            if (source != null)
            {
                Debug.Log($"[LiveKitService] 🔊 Đã cập nhật NPC AudioSource: '{source.gameObject.name}'");

                // Nếu có luồng âm thanh AI vừa đăng ký trước đó đang đứng chờ -> Bind ngay vào AudioSource này!
                if (_pendingAudioTrack != null)
                {
                    Debug.Log($"[LiveKitService] 🔗 Tự động kết nối luồng tiếng AI đang chờ vào AudioSource của '{source.gameObject.name}'!");
                    lock (_audioRoutingLock)
                    {
                        _pendingAudioTracks.Remove(_pendingAudioTrack.Sid);
                    }
                    BindAudioTrack(_pendingAudioTrack, source);
                    _pendingAudioTrack = null;
                }
            }
        }

        internal void HandleTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            if (track is RemoteAudioTrack audioTrack)
            {
                string identity = participant?.Identity;
                Debug.Log($"[LiveKitService] 🔊 Remote audio track subscribed: Participant={identity} | Track={audioTrack.Sid}");

                if (string.IsNullOrWhiteSpace(identity))
                {
                    Debug.LogWarning($"[LiveKitService] ❌ Rejected remote audio track '{audioTrack.Sid}': participant identity is null or empty.");
                    return;
                }

                lock (_audioRoutingLock)
                {
                    string targetRouteId = !string.IsNullOrEmpty(_activeNpcBindingId) && _npcAudioRoutes.ContainsKey(_activeNpcBindingId)
                        ? _activeNpcBindingId
                        : identity;

                    // Check V2 routes
                    if (_npcAudioRoutes.TryGetValue(targetRouteId, out var targetSource))
                    {
                        if (targetSource != null)
                        {
                            BindV2AudioTrack(audioTrack, targetSource, targetRouteId);
                            return;
                        }
                        else
                        {
                            Debug.LogWarning($"[LiveKitService] ⏳ AudioSource for route '{targetRouteId}' is destroyed or null. Storing track '{audioTrack.Sid}' as pending.");
                            _pendingAudioTracks[audioTrack.Sid] = new PendingTrackEntry(audioTrack, targetRouteId);
                            return;
                        }
                    }

                    // If V2 routes are registered in the scene, DO NOT fallback to legacy global npcAudioSource.
                    // Queue the track as pending for this target route.
                    if (_npcAudioRoutes.Count > 0)
                    {
                        Debug.Log($"[LiveKitService] ⏳ Unknown or pending V2 route '{targetRouteId}'. Storing track '{audioTrack.Sid}' in pending routes queue.");
                        _pendingAudioTracks[audioTrack.Sid] = new PendingTrackEntry(audioTrack, targetRouteId);
                        return;
                    }

                    // Legacy fallback: only if no V2 routes are registered
                    if (_npcAudioSource == null)
                    {
                        Debug.Log("[LiveKitService] ⏳ Chưa có NPC AudioSource tại thời điểm đăng ký. Đang lưu luồng âm thanh vào hàng chờ (Pending)...");
                        _pendingAudioTrack = audioTrack;
                        _pendingAudioTracks[audioTrack.Sid] = new PendingTrackEntry(audioTrack, identity);
                        return;
                    }

                    BindAudioTrack(audioTrack, _npcAudioSource);
                }
            }
        }

        internal void HandleTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            if (track is RemoteAudioTrack audioTrack)
            {
                lock (_audioRoutingLock)
                {
                    if (_activeAudioStreams.TryGetValue(audioTrack.Sid, out var activeEntry))
                    {
                        try { activeEntry.Stream?.Dispose(); } catch { }
                        _activeAudioStreams.Remove(audioTrack.Sid);
                        Debug.Log($"[LiveKitService] 🔇 Cleaned up active V2 AudioStream for unsubscribed track '{audioTrack.Sid}' (route '{activeEntry.NpcBindingId}')");
                    }

                    _pendingAudioTracks.Remove(audioTrack.Sid);
                }

                if (_remoteAudioStreams.TryGetValue(audioTrack.Sid, out var entry))
                {
                    try { entry.stream.Dispose(); } catch { }
                    _remoteAudioStreams.Remove(audioTrack.Sid);
                    Debug.Log($"[LiveKitService] 🔇 Đã dọn dẹp AudioStream cho Track HỦY ĐĂNG KÝ '{audioTrack.Sid}'");
                }
            }
        }

        internal void Reset()
        {
            lock (_audioRoutingLock)
            {
                foreach (var entry in _remoteAudioStreams.Values)
                {
                    try { entry.stream.Dispose(); } catch { }
                    if (entry.go != null && entry.go != _npcAudioSource?.gameObject)
                    {
                        UnityEngine.Object.Destroy(entry.go);
                    }
                }
                _remoteAudioStreams.Clear();

                foreach (var entry in _activeAudioStreams.Values)
                {
                    try { entry.Stream?.Dispose(); } catch { }
                }
                _activeAudioStreams.Clear();
                _pendingAudioTracks.Clear();
                _pendingAudioTrack = null;
            }
        }

        private IDisposable CreateAudioStream(RemoteAudioTrack track, AudioSource targetSource)
        {
            if (StreamFactory != null)
            {
                return StreamFactory(track, targetSource);
            }
            return track != null && targetSource != null ? new AudioStream(track, targetSource) : null;
        }

        private void BindV2AudioTrack(RemoteAudioTrack audioTrack, AudioSource targetSource, string npcBindingId, string trackSid = null)
        {
            string sid = audioTrack?.Sid ?? trackSid;
            if (string.IsNullOrWhiteSpace(sid) || targetSource == null) return;

            if (_activeAudioStreams.TryGetValue(sid, out var existingEntry))
            {
                try { existingEntry.Stream?.Dispose(); } catch { }
                _activeAudioStreams.Remove(sid);
                Debug.Log($"[LiveKitService] 🧹 Disposed duplicate V2 AudioStream for track '{sid}'");
            }

            var stream = CreateAudioStream(audioTrack, targetSource);
            _activeAudioStreams[sid] = new ActiveTrackEntry
            {
                Track = audioTrack,
                Stream = stream,
                Source = targetSource,
                NpcBindingId = npcBindingId
            };

            Debug.Log($"[LiveKitService] 🔊 Bound V2 audio track '{sid}' to NPC AudioSource '{targetSource.gameObject.name}' (route '{npcBindingId}')");
        }

        private void BindAudioTrack(RemoteAudioTrack audioTrack, AudioSource targetSource)
        {
            if (audioTrack == null || targetSource == null) return;

            // Dọn dẹp AudioStream cũ nếu cùng Track SID được đăng ký lại
            if (_remoteAudioStreams.TryGetValue(audioTrack.Sid, out var existingEntry))
            {
                try { existingEntry.stream.Dispose(); } catch { }
                _remoteAudioStreams.Remove(audioTrack.Sid);
                Debug.Log($"[LiveKitService] 🧹 Đã Dispose AudioStream cũ trùng lặp cho Track '{audioTrack.Sid}'");
            }

            AudioStream audiostream = new AudioStream(audioTrack, targetSource);
            _remoteAudioStreams[audioTrack.Sid] = (audiostream, targetSource.gameObject);

            Debug.Log($"[LiveKitService] 🔊 ĐÃ KẾT NỐI LUỒNG TIẾNG AI VÀO AUDIOSOURCE CỦA NPC '{targetSource.gameObject.name}' THÀNH CÔNG!");
        }
    }
}
