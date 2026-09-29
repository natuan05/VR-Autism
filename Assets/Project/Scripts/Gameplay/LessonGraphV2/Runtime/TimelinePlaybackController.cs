using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>Owns the scene-bound director used by Timeline lesson nodes.</summary>
    [DisallowMultipleComponent]
    public sealed class TimelinePlaybackController : MonoBehaviour, ITimelinePlaybackController
    {
        [SerializeField] private PlayableDirector _director;
        private PlaybackSession _activeSession;

        public ITimelinePlaybackSession StartPlayback(TimelineNodeConfig config)
        {
            if (!isActiveAndEnabled || config == null || config.TimelineAsset == null || _director == null ||
                _activeSession != null || _director.state == PlayState.Playing ||
                (_director.playableGraph.IsValid() && _director.playableAsset != null))
                return null;

            if (!(config.TimelineAsset is TimelineAsset timeline))
                return null;

            var matchingSignals = new HashSet<SignalAsset>();
            foreach (var track in timeline.GetOutputTracks())
            {
                if (!(track is SignalTrack)) continue;
                foreach (var marker in track.GetMarkers())
                {
                    if (marker is SignalEmitter emitter && emitter.asset != null &&
                        string.Equals(emitter.asset.name, config.ExpectedSignalName, StringComparison.Ordinal))
                        matchingSignals.Add(emitter.asset);
                }
            }
            if (matchingSignals.Count == 0)
                return null;

            var session = new PlaybackSession(this, _director, config.TimelineAsset, config.ExpectedSignalName);
            try
            {
                session.Attach(matchingSignals, timeline);
                _activeSession = session;
                _director.playableAsset = config.TimelineAsset;
                _director.Play();
                if (_director.state != PlayState.Playing)
                {
                    session.Close(cancelled: true);
                    return null;
                }
                return session;
            }
            catch (Exception)
            {
                session.Close(cancelled: true);
                return null;
            }
        }

        private void OnDisable() => CloseActiveSession();
        private void OnDestroy() => CloseActiveSession();

        private void CloseActiveSession()
        {
            var session = _activeSession;
            _activeSession = null;
            session?.Close(cancelled: true);
        }

        private sealed class PlaybackSession : ITimelinePlaybackSession
        {
            private readonly TimelinePlaybackController _owner;
            private readonly PlayableDirector _director;
            private readonly PlayableAsset _asset;
            private readonly string _expectedSignalName;
            private readonly TaskCompletionSource<bool> _signal =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<TrackBinding> _bindings = new List<TrackBinding>();
            private readonly List<SignalAsset> _registeredSignals = new List<SignalAsset>();
            private SignalReceiver _receiver;
            private bool _active = true;
            private bool _closed;
            private volatile bool _cancelled;

            public PlaybackSession(TimelinePlaybackController owner, PlayableDirector director, PlayableAsset asset,
                string expectedSignalName)
            {
                _owner = owner;
                _director = director;
                _asset = asset;
                _expectedSignalName = expectedSignalName;
            }

            public Task SignalTask => _signal.Task;
            public bool IsCancelled => _cancelled;

            public void Attach(HashSet<SignalAsset> signals, TimelineAsset timeline)
            {
                _receiver = _owner.gameObject.AddComponent<SignalReceiver>();
                foreach (var signal in signals)
                {
                    var capturedSignal = signal;
                    var reaction = new UnityEvent();
                    reaction.AddListener(() => OnSignal(capturedSignal));
                    _receiver.AddReaction(signal, reaction);
                    _registeredSignals.Add(signal);
                }

                foreach (var track in timeline.GetOutputTracks())
                {
                    if (!(track is SignalTrack)) continue;
                    _bindings.Add(new TrackBinding(track, _director.GetGenericBinding(track)));
                    _director.SetGenericBinding(track, _receiver);
                }
            }

            private void OnSignal(SignalAsset signal)
            {
                if (!_active || !ReferenceEquals(_owner._activeSession, this) || signal == null ||
                    !string.Equals(signal.name, _expectedSignalName, StringComparison.Ordinal)) return;
                _active = false;
                _signal.TrySetResult(true);
            }

            public void Dispose() => Close(cancelled: false);

            public void Close(bool cancelled)
            {
                if (cancelled) _cancelled = true;
                if (_closed) return;
                _closed = true;
                _active = false;
                if (ReferenceEquals(_owner._activeSession, this)) _owner._activeSession = null;

                if (_director != null && _director.playableAsset == _asset && _director.playableGraph.IsValid())
                    _director.Stop();

                for (var i = _bindings.Count - 1; i >= 0; i--)
                    if (_director != null) _director.SetGenericBinding(_bindings[i].Track, _bindings[i].PreviousBinding);
                _bindings.Clear();

                if (_receiver != null)
                {
                    foreach (var signal in _registeredSignals)
                        _receiver.Remove(signal);
                    _registeredSignals.Clear();
                    if (Application.isPlaying) UnityEngine.Object.Destroy(_receiver);
                    else UnityEngine.Object.DestroyImmediate(_receiver);
                    _receiver = null;
                }
                if (!_signal.Task.IsCompleted) _signal.TrySetCanceled();
            }
        }

        private readonly struct TrackBinding
        {
            public readonly TrackAsset Track;
            public readonly UnityEngine.Object PreviousBinding;
            public TrackBinding(TrackAsset track, UnityEngine.Object previousBinding)
            { Track = track; PreviousBinding = previousBinding; }
        }
    }
}
