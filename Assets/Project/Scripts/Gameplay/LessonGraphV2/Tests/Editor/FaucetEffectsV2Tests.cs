using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Integration;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using UnityEngine.TestTools;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class FaucetEffectsV2Tests
    {
        private GameObject _runnerObject;
        private GameObject _effectsObject;
        private GameObject _visualsObject;
        private AudioClip _clip;
        private LessonGraphRunner _runner;
        private RecordingFaucetEffectsV2 _effects;

        [SetUp]
        public void SetUp()
        {
            _runnerObject = new GameObject("faucet-test-runner");
            _runner = _runnerObject.AddComponent<LessonGraphRunner>();
            SetRunnerState(State("session-1", "run-1", "running", 1));

            _visualsObject = new GameObject("faucet-test-visuals");
            var animator = _visualsObject.AddComponent<Animator>();
            var particles = _visualsObject.AddComponent<ParticleSystem>();
            var audio = _visualsObject.AddComponent<AudioSource>();
            _clip = AudioClip.Create("faucet-test-clip", 64, 1, 8000, false);

            _effectsObject = new GameObject("faucet-test-effects");
            _effectsObject.SetActive(false);
            _effects = _effectsObject.AddComponent<RecordingFaucetEffectsV2>();
            SetEffectField("_runner", _runner);
            SetEffectField("_tapAnimator", animator);
            SetEffectField("_runningWater", particles);
            SetEffectField("_waterAudioSource", audio);
            SetEffectField("_waterClip", _clip);
            _effectsObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_effectsObject != null) UnityEngine.Object.DestroyImmediate(_effectsObject);
            if (_visualsObject != null) UnityEngine.Object.DestroyImmediate(_visualsObject);
            if (_runnerObject != null) UnityEngine.Object.DestroyImmediate(_runnerObject);
            if (_clip != null) UnityEngine.Object.DestroyImmediate(_clip);
        }

        [Test]
        public void StartsClosedAndRepeatedOpenCloseCommandsApplyOutputsOnce()
        {
            Assert.That(_effects.IsOpen, Is.False);
            Assert.That(_effects.Calls, Is.EqualTo(new[] { "visual:closed", "water:off", "audio:stop" }));
            _effects.ClearCalls();

            _effects.SetOpen(true);
            _effects.SetOpen(true);
            Assert.That(_effects.IsOpen, Is.True);
            Assert.That(_effects.Calls, Is.EqualTo(new[] { "visual:open", "water:on", "audio:play" }));

            _effects.SetOpen(false);
            _effects.SetOpen(false);
            Assert.That(_effects.IsOpen, Is.False);
            Assert.That(_effects.Calls, Is.EqualTo(new[]
            {
                "visual:open", "water:on", "audio:play", "visual:closed", "water:off", "audio:stop"
            }));
        }

        [Test]
        public void PausingRetainsOpenStateAndSameRunResumeUnpausesOnce()
        {
            _effects.SetOpen(true);
            _effects.ClearCalls();

            PublishState(State("session-1", "run-1", "pausing", 2));
            PublishState(State("session-1", "run-1", "paused", 3));
            Assert.That(_effects.IsOpen, Is.True);
            Assert.That(_effects.Calls, Is.EqualTo(new[] { "audio:pause" }));

            PublishState(State("session-1", "run-1", "running", 4));
            PublishState(State("session-1", "run-1", "running", 5));
            Assert.That(_effects.IsOpen, Is.True);
            Assert.That(_effects.Calls, Is.EqualTo(new[] { "audio:pause", "audio:unpause" }));
        }

        [Test]
        public void ClosingWhilePausedPreventsAudioFromResuming()
        {
            _effects.SetOpen(true);
            PublishState(State("session-1", "run-1", "paused", 2));
            _effects.ClearCalls();

            _effects.SetOpen(false);
            PublishState(State("session-1", "run-1", "running", 3));

            Assert.That(_effects.IsOpen, Is.False);
            Assert.That(_effects.Calls, Is.EqualTo(new[] { "visual:closed", "water:off", "audio:stop" }));
        }

        [TestCase("completed")]
        [TestCase("failed")]
        [TestCase("cancelled")]
        public void TerminalStateClosesAndStopsOpenFaucet(string terminalStatus)
        {
            _effects.SetOpen(true);
            _effects.ClearCalls();

            PublishState(State("session-1", "run-1", terminalStatus, 2));

            Assert.That(_effects.IsOpen, Is.False);
            Assert.That(_effects.Calls, Is.EqualTo(new[] { "visual:closed", "water:off", "audio:stop" }));
        }

        [Test]
        public void NewRunOrSessionClosesThePreviousRun()
        {
            _effects.SetOpen(true);
            _effects.ClearCalls();

            PublishState(State("session-2", "run-2", "running", 1));

            Assert.That(_effects.IsOpen, Is.False);
            Assert.That(_effects.Calls, Is.EqualTo(new[] { "visual:closed", "water:off", "audio:stop" }));
        }

        [Test]
        public void DelayedStatePayloadIsIgnoredWhenRunnerHasAdvanced()
        {
            _effects.SetOpen(true);
            _effects.ClearCalls();
            LessonStateV2 stale = State("session-1", "run-1", "cancelled", 2);
            SetRunnerState(State("session-2", "run-2", "running", 1));

            RaiseState(stale);

            Assert.That(_effects.IsOpen, Is.True);
            Assert.That(_effects.Calls, Is.Empty);
        }

        [Test]
        public void DisableStopsEffectsUnsubscribesAndReenableSubscribesOnce()
        {
            _effects.SetOpen(true);
            _effects.ClearCalls();
            Assert.That(SubscriberCount(), Is.EqualTo(1));

            _effects.enabled = false;

            Assert.That(_effects.IsOpen, Is.False);
            Assert.That(_effects.Calls, Is.EqualTo(new[] { "visual:closed", "water:off", "audio:stop" }));
            Assert.That(SubscriberCount(), Is.EqualTo(0));

            _effects.enabled = true;
            _effects.enabled = false;
            _effects.enabled = true;
            Assert.That(SubscriberCount(), Is.EqualTo(1));
        }

        [Test]
        public void DestroyingEnabledComponentStopsEffectsAndUnsubscribes()
        {
            _effects.SetOpen(true);
            _effects.ClearCalls();
            var destroyedObject = _effectsObject;
            _effectsObject = null;

            UnityEngine.Object.DestroyImmediate(destroyedObject);

            Assert.That(SubscriberCount(), Is.EqualTo(0));
            Assert.That(_effects.Calls, Is.EqualTo(new[] { "visual:closed", "water:off", "audio:stop" }));
        }

        [Test]
        public void OpeningIsRejectedOutsideTheRunningState()
        {
            SetRunnerState(State("session-1", "run-1", "paused", 2));
            PublishState(State("session-1", "run-1", "paused", 2));
            _effects.ClearCalls();

            _effects.SetOpen(true);

            Assert.That(_effects.IsOpen, Is.False);
            Assert.That(_effects.Calls, Is.Empty);
        }

        [Test]
        public void MissingRequiredWiringFailsClosed()
        {
            var brokenObject = new GameObject("faucet-test-missing-wiring");
            brokenObject.SetActive(false);
            var broken = brokenObject.AddComponent<RecordingFaucetEffectsV2>();
            SetEffectField("_runner", null, broken);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("FaucetEffectsV2 requires _runner"));
            brokenObject.SetActive(true);

            broken.SetOpen(true);

            Assert.That(broken.IsOpen, Is.False);
            UnityEngine.Object.DestroyImmediate(brokenObject);
        }

        private static LessonStateV2 State(string sessionId, string runId, string status, int revision) => new LessonStateV2
        {
            session_id = sessionId,
            run_id = runId,
            status = status,
            state_revision = revision
        };

        private void SetRunnerState(LessonStateV2 state) =>
            typeof(LessonGraphRunner).GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_runner, state);

        private void SetEffectField(string fieldName, object value, RecordingFaucetEffectsV2 target = null) =>
            typeof(FaucetEffectsV2).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target == null ? _effects : target, value);

        private void PublishState(LessonStateV2 state)
        {
            SetRunnerState(state);
            RaiseState(state);
        }

        private void RaiseState(LessonStateV2 state)
        {
            FieldInfo field = typeof(LessonGraphRunner).GetField(nameof(LessonGraphRunner.StateChanged), BindingFlags.Instance | BindingFlags.NonPublic);
            ((Action<LessonStateV2>)field.GetValue(_runner))?.Invoke(state);
        }

        private int SubscriberCount()
        {
            FieldInfo field = typeof(LessonGraphRunner).GetField(nameof(LessonGraphRunner.StateChanged), BindingFlags.Instance | BindingFlags.NonPublic);
            return ((Action<LessonStateV2>)field.GetValue(_runner))?.GetInvocationList().Length ?? 0;
        }
    }

    [ExecuteAlways]
    public sealed class RecordingFaucetEffectsV2 : FaucetEffectsV2
    {
        public readonly List<string> Calls = new List<string>();

        public void ClearCalls() => Calls.Clear();

        protected override void ApplyAnimatorState(bool open) => Calls.Add(open ? "visual:open" : "visual:closed");
        protected override void ApplyRunningWater(bool enabled) => Calls.Add(enabled ? "water:on" : "water:off");
        protected override void PlayWaterAudio() => Calls.Add("audio:play");
        protected override void PauseWaterAudio() => Calls.Add("audio:pause");
        protected override void UnPauseWaterAudio() => Calls.Add("audio:unpause");
        protected override void StopWaterAudio() => Calls.Add("audio:stop");
    }
}
