using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Questing;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class QuestSourceV2Tests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in _objects)
                if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        [Test]
        public void FreshActivation_TransitionsToActive_AndCompletesExactlyOnce()
        {
            var source = Source("touch-cup");
            var states = new List<QuestSourceState>();
            var results = new List<QuestSourceResult>();
            source.StateChanged += states.Add;
            source.Terminated += results.Add;

            Assert.IsTrue(source.TryActivate(Activation("activation-1")));
            Assert.IsTrue(source.Complete("activation-1", "touch"));

            CollectionAssert.AreEqual(new[]
            {
                QuestSourceState.Activating,
                QuestSourceState.Active,
                QuestSourceState.Completing,
                QuestSourceState.Completed,
            }, states);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("activation-1", results[0].ActivationId);
            Assert.AreEqual("touch-cup", results[0].BindingId);
            Assert.AreEqual("touch", results[0].CompletionChannel);
            Assert.AreEqual(QuestSourceTerminalStatus.Completed, results[0].Status);
            Assert.AreEqual(new DateTimeOffset(2026, 9, 2, 8, 30, 0, TimeSpan.Zero), results[0].CompletedAtUtc);
            Assert.AreEqual(42.5d, results[0].CompletedAtMonotonicSeconds);
        }

        [Test]
        public void Cancellation_FromActive_EmitsOneCorrelatedTerminalResult()
        {
            var source = Source("hold-door");
            var results = new List<QuestSourceResult>();
            source.Terminated += results.Add;
            source.TryActivate(Activation("activation-2"));

            Assert.IsTrue(source.TryCancel(new QuestSourceCancellation("activation-2", "first_win")));
            Assert.IsFalse(source.TryCancel(new QuestSourceCancellation("activation-2", "duplicate")));

            Assert.AreEqual(QuestSourceState.Cancelled, source.State);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(QuestSourceTerminalStatus.Cancelled, results[0].Status);
            Assert.AreEqual(string.Empty, results[0].FailureCode);
            Assert.AreEqual("first_win", results[0].CancellationReason);
        }

        [Test]
        public void Failure_FromActive_EmitsOneCorrelatedTerminalResult()
        {
            var source = Source("voice-answer");
            QuestSourceResult result = null;
            source.Terminated += emitted => result = emitted;
            source.TryActivate(Activation("activation-3"));

            Assert.IsTrue(source.Fail("activation-3", QuestSourceFailureCodes.BindingUnavailable));

            Assert.AreEqual(QuestSourceState.Failed, source.State);
            Assert.NotNull(result);
            Assert.AreEqual(QuestSourceTerminalStatus.Failed, result.Status);
            Assert.AreEqual(QuestSourceFailureCodes.BindingUnavailable, result.FailureCode);
        }

        [Test]
        public void StaleAndDuplicateTerminalSignals_DoNotChangeStateOrEmitAgain()
        {
            var source = Source("touch-book");
            var results = 0;
            source.Terminated += _ => results++;
            source.TryActivate(Activation("current"));

            Assert.IsFalse(source.Complete("stale", "touch"));
            Assert.AreEqual(QuestSourceState.Active, source.State);
            Assert.IsTrue(source.Complete("current", "touch"));
            Assert.IsFalse(source.Complete("current", "touch"));
            Assert.IsFalse(source.Fail("current", QuestSourceFailureCodes.BindingUnavailable));

            Assert.AreEqual(QuestSourceState.Completed, source.State);
            Assert.AreEqual(1, results);
        }

        [Test]
        public void CompletionDuringActivation_DoesNotRegressTerminalStateToActive()
        {
            var source = Source("instant", completeDuringActivation: true);
            var results = 0;
            source.Terminated += _ => results++;

            Assert.IsTrue(source.TryActivate(Activation("instant-activation")));

            Assert.AreEqual(QuestSourceState.Completed, source.State);
            Assert.AreEqual(1, results);
        }

        [Test]
        public void DisableWhileActive_EmitsUnavailableFailureBeforeCleanup()
        {
            var source = Source("disable-me");
            source.Terminated += _ => source.ObserverSawTerminal = true;
            source.TryActivate(Activation("active-disable"));

            source.enabled = false;
            InvokeLifecycle(source, "OnDisable");

            Assert.AreEqual(QuestSourceState.Failed, source.State);
            Assert.AreEqual(1, source.TerminalEventsObserved);
            Assert.IsTrue(source.TerminalWasObservedBeforeCleanup);
            Assert.AreEqual(1, source.CleanupCalls);
        }

        [Test]
        public void DestroyWhileActive_EmitsUnavailableFailureOnlyOnce()
        {
            var source = Source("destroy-me");
            var gameObject = source.gameObject;
            source.Terminated += _ => source.ObserverSawTerminal = true;
            source.TryActivate(Activation("active-destroy"));

            InvokeLifecycle(source, "OnDestroy");
            UnityEngine.Object.DestroyImmediate(gameObject);

            Assert.AreEqual(1, source.TerminalEventsObserved);
            Assert.IsTrue(source.TerminalWasObservedBeforeCleanup);
            _objects.Remove(gameObject);
        }

        [Test]
        public void DisableWhileInactive_DoesNotCreateATerminalResult()
        {
            var source = Source("inactive");

            source.enabled = false;
            InvokeLifecycle(source, "OnDisable");

            Assert.AreEqual(QuestSourceState.Inactive, source.State);
            Assert.AreEqual(0, source.TerminalEventsObserved);
        }

        [Test]
        public void TerminalSource_RejectsReuseWithoutPublicResetSemantics()
        {
            var source = Source("one-shot");
            source.TryActivate(Activation("first"));
            source.Complete("first", "touch");

            Assert.IsFalse(source.TryActivate(Activation("second")));
            Assert.AreEqual("first", source.CurrentActivationId);
        }

        private TestQuestSource Source(string bindingId, bool completeDuringActivation = false)
        {
            var gameObject = new GameObject($"source-{bindingId}");
            _objects.Add(gameObject);
            var source = gameObject.AddComponent<TestQuestSource>();
            source.SetBindingId(bindingId);
            InvokeLifecycle(source, "Awake");
            source.CompleteDuringActivation = completeDuringActivation;
            source.Terminated += _ => source.TerminalEventsObserved++;
            return source;
        }

        private static QuestSourceActivation Activation(string activationId) =>
            new QuestSourceActivation(
                activationId,
                new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero),
                12.25d);

        private static void InvokeLifecycle(QuestSourceV2 source, string methodName)
        {
            typeof(QuestSourceV2)
                .GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(source, null);
        }

        private sealed class TestQuestSource : QuestSourceV2
        {
            public bool CompleteDuringActivation { get; set; }
            public int TerminalEventsObserved { get; set; }
            public bool ObserverSawTerminal { get; set; }
            public bool TerminalWasObservedBeforeCleanup { get; private set; }
            public int CleanupCalls { get; private set; }

            public void SetBindingId(string bindingId) => typeof(QuestSourceV2)
                .GetField("_bindingId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(this, bindingId);

            public bool Complete(string activationId, string channel) =>
                TryComplete(
                    activationId,
                    channel,
                    new DateTimeOffset(2026, 9, 2, 8, 30, 0, TimeSpan.Zero),
                    42.5d);

            public bool Fail(string activationId, string code) =>
                TryFail(
                    activationId,
                    code,
                    new DateTimeOffset(2026, 9, 2, 8, 45, 0, TimeSpan.Zero),
                    51d);

            protected override void OnSourceActivated(QuestSourceActivation activation)
            {
                if (CompleteDuringActivation) Complete(activation.ActivationId, "instant");
            }

            protected override void OnSourceCleanup()
            {
                CleanupCalls++;
                TerminalWasObservedBeforeCleanup = ObserverSawTerminal;
            }
        }
    }
}
