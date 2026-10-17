using System;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    public enum LocalTtsPlaybackOutcomeV1 { Success, Cancelled, Failed }

    /// <summary>Owns a single local AudioSource playback and its cache lease until terminal.</summary>
    public sealed class LocalTtsPlaybackHandleV1 : IDisposable
    {
        private readonly AudioSource _source;
        private readonly ResolvedAudioV1 _resolved;
        private readonly Action<LocalTtsPlaybackOutcomeV1> _terminal;
        private readonly float _startedAt;
        private readonly float _expectedDurationSeconds;
        private readonly bool _previousLoop;
        private bool _observedPlayback;
        private bool _finished;

        public LocalTtsPlaybackHandleV1(AudioSource source, ResolvedAudioV1 resolved, Action<LocalTtsPlaybackOutcomeV1> terminal)
        {
            _source = source != null ? source : throw new ArgumentNullException(nameof(source));
            _resolved = resolved != null ? resolved : throw new ArgumentNullException(nameof(resolved));
            _terminal = terminal;
            if (_resolved.Outcome != ResolvedAudioOutcomeV1.LocalReady || _resolved.Clip == null)
                throw new ArgumentException("A ready local clip is required.", nameof(resolved));
            if (_resolved.Clip.length <= 0f) throw new ArgumentException("A local clip must have finite positive duration.", nameof(resolved));
            _previousLoop = _source.loop;
            _source.loop = false;
            _source.Stop();
            _source.clip = _resolved.Clip;
            _startedAt = Time.realtimeSinceStartup;
            _expectedDurationSeconds = Mathf.Max(0.05f, _resolved.Clip.length);
            _source.Play();
        }

        public bool IsTerminal => _finished;

        public void Tick()
        {
            if (_finished) return;
            if (_source == null) { Finish(LocalTtsPlaybackOutcomeV1.Failed); return; }
            if (_source.isPlaying)
            {
                _observedPlayback = true;
                if (Time.realtimeSinceStartup - _startedAt > _expectedDurationSeconds + 0.5f)
                {
                    _source.Stop();
                    Finish(LocalTtsPlaybackOutcomeV1.Failed);
                }
                return;
            }
            var elapsed = Time.realtimeSinceStartup - _startedAt;
            if (_observedPlayback)
                Finish(elapsed + 0.05f >= _expectedDurationSeconds ? LocalTtsPlaybackOutcomeV1.Success : LocalTtsPlaybackOutcomeV1.Failed);
            else if (elapsed >= 0.75f) Finish(LocalTtsPlaybackOutcomeV1.Failed);
        }

        public void Cancel()
        {
            if (_finished) return;
            if (_source != null)
            {
                _source.Stop();
                if (_source.clip == _resolved.Clip) _source.clip = null;
            }
            Finish(LocalTtsPlaybackOutcomeV1.Cancelled);
        }

        public void Dispose() => Cancel();

        private void Finish(LocalTtsPlaybackOutcomeV1 outcome)
        {
            if (_finished) return;
            _finished = true;
            if (_source != null)
            {
                if (_source.clip == _resolved.Clip) _source.clip = null;
                _source.loop = _previousLoop;
            }
            _resolved.Dispose();
            _terminal?.Invoke(outcome);
        }
    }
}
