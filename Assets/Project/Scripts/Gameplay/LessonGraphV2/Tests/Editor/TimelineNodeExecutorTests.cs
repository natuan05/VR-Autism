using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class TimelineNodeExecutorTests
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _objects) if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _objects.Clear();
        }

        [UnityTest]
        public IEnumerator ConfiguredSignal_ReturnsSuccessOnceAndPreservesActivationIdentity()
        {
            var playback = new FakePlayback();
            var clock = new ManualClock();
            var executor = new TimelineNodeExecutor(playback, clock);
            var context = Context(Config(TimelineTimeoutOutcome.Timeout), clock, "activation-1");
            var task = executor.ExecuteAsync(context);
            Assert.AreSame(context.Node.Config, playback.LastConfig);
            Assert.IsFalse(task.IsCompleted);

            playback.LastSession.EmitSignal();
            playback.LastSession.EmitSignal();
            yield return CompleteWithinFrames(task);

            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(NodeStatus.Success, result.Status);
            Assert.AreEqual("timeline-node", result.NodeId);
            Assert.AreEqual("activation-1", result.ActivationId);
            Assert.AreEqual(1, playback.LastSession.DisposeCount);
        }

        [UnityTest]
        public IEnumerator LocalTimeout_UsesConfiguredOutcome()
        {
            var playback = new FakePlayback();
            var clock = new ManualClock();
            var executor = new TimelineNodeExecutor(playback, clock);
            var task = executor.ExecuteAsync(Context(Config(TimelineTimeoutOutcome.Failed), clock, "timeout-1"));
            clock.CompleteDelay();
            yield return CompleteWithinFrames(task);
            Assert.AreEqual(NodeStatus.Failed, task.GetAwaiter().GetResult().Status);
            Assert.AreEqual(1, playback.LastSession.DisposeCount);
        }

        [UnityTest]
        public IEnumerator ExternalTimeout_UsesConfiguredTimeoutStatus()
        {
            var playback = new FakePlayback();
            var clock = new ManualClock();
            using (var timeout = new CancellationTokenSource())
            {
                var task = new TimelineNodeExecutor(playback, clock).ExecuteAsync(
                    Context(Config(TimelineTimeoutOutcome.Timeout), clock, "timeout-2", timeout: timeout.Token));
                timeout.Cancel();
                yield return CompleteWithinFrames(task);
                Assert.AreEqual(NodeStatus.Timeout, task.GetAwaiter().GetResult().Status);
            }
            Assert.AreEqual(1, playback.LastSession.DisposeCount);
        }

        [UnityTest]
        public IEnumerator Skip_ReturnsSkippedAndClosesPlayback()
        {
            var playback = new FakePlayback();
            var clock = new ManualClock();
            using (var skip = new CancellationTokenSource())
            {
                var task = new TimelineNodeExecutor(playback, clock).ExecuteAsync(
                    Context(Config(TimelineTimeoutOutcome.Timeout), clock, "skip-1", skip: skip.Token));
                skip.Cancel();
                yield return CompleteWithinFrames(task);
                Assert.AreEqual(NodeStatus.Skipped, task.GetAwaiter().GetResult().Status);
            }
            Assert.AreEqual(1, playback.LastSession.DisposeCount);
        }

        [UnityTest]
        public IEnumerator Abort_ThrowsCancellationAndClosesPlayback()
        {
            var playback = new FakePlayback();
            var clock = new ManualClock();
            using (var abort = new CancellationTokenSource())
            {
                var task = new TimelineNodeExecutor(playback, clock).ExecuteAsync(
                    Context(Config(TimelineTimeoutOutcome.Timeout), clock, "abort-1", cancellation: abort.Token));
                var cancelledSession = playback.LastSession;
                abort.Cancel();
                cancelledSession.EmitSignal();
                yield return CompleteWithinFrames(task);
                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            }
            Assert.AreEqual(1, playback.LastSession.DisposeCount);
        }

        [UnityTest]
        public IEnumerator Abort_WakesExecutorWhenClockIgnoresCancellation()
        {
            var playback = new FakePlayback();
            var clock = new NonCooperativeClock();
            using (var abort = new CancellationTokenSource())
            {
                var task = new TimelineNodeExecutor(playback, clock).ExecuteAsync(
                    Context(Config(TimelineTimeoutOutcome.Timeout), clock, "abort-non-cooperative",
                        cancellation: abort.Token));
                abort.Cancel();
                yield return CompleteWithinFrames(task);
                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            }
            Assert.AreEqual(1, playback.LastSession.DisposeCount);
        }

        [UnityTest]
        public IEnumerator OldActivationSignal_CannotCompleteNextActivation()
        {
            var playback = new FakePlayback();
            var clock = new ManualClock();
            var executor = new TimelineNodeExecutor(playback, clock);
            var firstTask = executor.ExecuteAsync(Context(Config(TimelineTimeoutOutcome.Timeout), clock, "first"));
            var oldSession = playback.LastSession;
            oldSession.EmitSignal();
            yield return CompleteWithinFrames(firstTask);

            var secondTask = executor.ExecuteAsync(Context(Config(TimelineTimeoutOutcome.Timeout), clock, "second"));
            oldSession.EmitSignal();
            yield return null;
            Assert.IsFalse(secondTask.IsCompleted, "A callback retained by the previous activation must be ignored.");
            playback.LastSession.EmitSignal();
            yield return CompleteWithinFrames(secondTask);
            Assert.AreEqual(NodeStatus.Success, secondTask.GetAwaiter().GetResult().Status);
        }

        [UnityTest]
        public IEnumerator MissingPlaybackBinding_FailsSafely()
        {
            var clock = new ManualClock();
            var task = new TimelineNodeExecutor(new FakePlayback { ReturnNull = true }, clock)
                .ExecuteAsync(Context(Config(TimelineTimeoutOutcome.Timeout), clock, "missing"));
            yield return CompleteWithinFrames(task);
            Assert.AreEqual(NodeStatus.Failed, task.GetAwaiter().GetResult().Status);
        }

        [UnityTest]
        public IEnumerator SceneDisable_StopsAndRestoresSignalBinding()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var signal = ScriptableObject.CreateInstance<SignalAsset>();
            var directorObject = new GameObject("timeline-test-director");
            var previousObject = new GameObject("timeline-test-previous-binding");
            _objects.Add(timeline);
            _objects.Add(signal);
            _objects.Add(directorObject);
            _objects.Add(previousObject);

            var signalTrack = timeline.CreateTrack<SignalTrack>(null, "signals");
            var emitter = signalTrack.CreateMarker<SignalEmitter>(0.5d);
            emitter.asset = signal;
            signal.name = "finished";
            var director = directorObject.AddComponent<PlayableDirector>();
            var previousBinding = previousObject.AddComponent<SignalReceiver>();
            director.SetGenericBinding(signalTrack, previousBinding);
            var controller = directorObject.AddComponent<TimelinePlaybackController>();
            typeof(TimelinePlaybackController).GetField("_director", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(controller, director);

            var session = controller.StartPlayback(Config(TimelineTimeoutOutcome.Timeout, timeline));
            Assert.IsNotNull(session);
            Assert.AreSame(controller.GetComponent<SignalReceiver>(), director.GetGenericBinding(signalTrack));
            controller.enabled = false;
            yield return null;
            Assert.AreSame(previousBinding, director.GetGenericBinding(signalTrack));
            Assert.AreEqual(PlayState.Paused, director.state);
            Assert.Throws<OperationCanceledException>(() => session.SignalTask.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator SceneDisableAfterSignal_BeforeExecutorContinuationCancelsActivation()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var signal = ScriptableObject.CreateInstance<SignalAsset>();
            var directorObject = new GameObject("timeline-scene-cancel-race-test");
            _objects.Add(timeline);
            _objects.Add(signal);
            _objects.Add(directorObject);
            signal.name = "finished";
            var signalTrack = timeline.CreateTrack<SignalTrack>(null, "signals");
            var emitter = signalTrack.CreateMarker<SignalEmitter>(0.5d);
            emitter.asset = signal;
            var director = directorObject.AddComponent<PlayableDirector>();
            var controller = directorObject.AddComponent<TimelinePlaybackController>();
            typeof(TimelinePlaybackController).GetField("_director", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(controller, director);
            var clock = new ManualClock();
            var task = new TimelineNodeExecutor(controller, clock).ExecuteAsync(Context(
                Config(TimelineTimeoutOutcome.Timeout, timeline), clock, "scene-race"));
            var receiver = directorObject.GetComponent<SignalReceiver>();
            Assert.IsNotNull(receiver);

            receiver.OnNotify(Playable.Null, emitter, null);
            controller.enabled = false;
            yield return CompleteWithinFrames(task);
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult(),
                "Scene cleanup must win over a signal whose result continuation has not committed.");
            Assert.AreEqual(PlayState.Paused, director.state);
            Assert.IsFalse(director.playableGraph.IsValid());
        }

        [UnityTest]
        public IEnumerator DestroyController_ClosesSessionAndIgnoresCapturedLateSignal()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var signal = ScriptableObject.CreateInstance<SignalAsset>();
            var directorObject = new GameObject("timeline-destroy-lifecycle-test");
            var previousObject = new GameObject("timeline-destroy-previous-binding");
            _objects.Add(timeline);
            _objects.Add(signal);
            _objects.Add(directorObject);
            _objects.Add(previousObject);
            signal.name = "finished";
            var signalTrack = timeline.CreateTrack<SignalTrack>(null, "signals");
            var emitter = signalTrack.CreateMarker<SignalEmitter>(0.5d);
            emitter.asset = signal;
            var director = directorObject.AddComponent<PlayableDirector>();
            var previousBinding = previousObject.AddComponent<SignalReceiver>();
            director.SetGenericBinding(signalTrack, previousBinding);
            var controller = directorObject.AddComponent<TimelinePlaybackController>();
            typeof(TimelinePlaybackController).GetField("_director", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(controller, director);
            var session = controller.StartPlayback(Config(TimelineTimeoutOutcome.Timeout, timeline));
            Assert.IsNotNull(session);
            var receiver = directorObject.GetComponent<SignalReceiver>();
            var capturedLateSignal = receiver.GetReaction(signal);

            UnityEngine.Object.DestroyImmediate(controller);
            capturedLateSignal.Invoke();
            yield return null;

            Assert.IsTrue(session.SignalTask.IsCanceled);
            Assert.AreSame(previousBinding, director.GetGenericBinding(signalTrack));
            Assert.IsFalse(director.playableGraph.IsValid());
        }

        [UnityTest]
        public IEnumerator UnknownAndWrongSignals_AreIgnoredUntilConfiguredAssetArrives()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var expected = ScriptableObject.CreateInstance<SignalAsset>();
            var unknown = ScriptableObject.CreateInstance<SignalAsset>();
            var directorObject = new GameObject("timeline-signal-filter-test");
            _objects.Add(timeline);
            _objects.Add(expected);
            _objects.Add(unknown);
            _objects.Add(directorObject);
            expected.name = "finished";
            unknown.name = "unrelated";
            var signalTrack = timeline.CreateTrack<SignalTrack>(null, "signals");
            var expectedEmitter = signalTrack.CreateMarker<SignalEmitter>(0d);
            expectedEmitter.asset = expected;
            var unknownEmitter = signalTrack.CreateMarker<SignalEmitter>(0.5d);
            unknownEmitter.asset = unknown;

            var director = directorObject.AddComponent<PlayableDirector>();
            var controller = directorObject.AddComponent<TimelinePlaybackController>();
            typeof(TimelinePlaybackController).GetField("_director", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(controller, director);
            var session = controller.StartPlayback(Config(TimelineTimeoutOutcome.Timeout, timeline));
            Assert.IsNotNull(session);
            var receiver = directorObject.GetComponent<SignalReceiver>();
            CollectionAssert.AreEqual(new[] { expected }, receiver.GetRegisteredSignals());

            receiver.OnNotify(Playable.Null, unknownEmitter, null);
            Assert.IsFalse(session.SignalTask.IsCompleted, "An unregistered signal asset must not complete playback.");
            receiver.OnNotify(Playable.Null, expectedEmitter, null);
            Assert.IsTrue(session.SignalTask.IsCompleted, "The exact configured signal asset should complete playback.");
            session.Dispose();
            Assert.IsNull(directorObject.GetComponent<SignalReceiver>());
            yield return null;
        }

        private TimelineNodeConfig Config(TimelineTimeoutOutcome outcome, PlayableAsset asset = null)
        {
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<TimelineAsset>();
                _objects.Add(asset);
            }
            return new TimelineNodeConfig(asset, "finished", 5f, outcome);
        }

        private static NodeExecutionContext Context(TimelineNodeConfig config, INodeClock clock, string activation,
            CancellationToken cancellation = default, CancellationToken skip = default, CancellationToken timeout = default) =>
            new NodeExecutionContext("run-1", activation, "graph-1",
                new LessonNodeData("timeline-node", NodeType.Timeline, config), 0d,
                cancellation, skip, timeout, null, clock);

        private static IEnumerator CompleteWithinFrames(Task task)
        {
            for (var frame = 0; frame < 20 && !task.IsCompleted; frame++) yield return null;
            Assert.IsTrue(task.IsCompleted, "Task did not complete within 20 EditMode frames.");
        }

        private sealed class FakePlayback : ITimelinePlaybackController
        {
            public bool ReturnNull;
            public FakeSession LastSession;
            public ITimelinePlaybackSession StartPlayback(TimelineNodeConfig config)
            {
                LastConfig = config;
                if (ReturnNull) return null;
                return LastSession = new FakeSession();
            }
            public TimelineNodeConfig LastConfig { get; private set; }
        }

        private sealed class FakeSession : ITimelinePlaybackSession
        {
            private readonly TaskCompletionSource<bool> _signal = new TaskCompletionSource<bool>();
            public Task SignalTask => _signal.Task;
            public bool IsCancelled { get; private set; }
            public int DisposeCount { get; private set; }
            public void EmitSignal() => _signal.TrySetResult(true);
            public void Dispose()
            {
                DisposeCount++;
                if (!_signal.Task.IsCompleted)
                {
                    IsCancelled = true;
                    _signal.TrySetCanceled();
                }
            }
        }

        private sealed class ManualClock : INodeClock
        {
            private readonly TaskCompletionSource<bool> _delay = new TaskCompletionSource<bool>();
            public double ElapsedSeconds => 1d;
            public Task Delay(float seconds, CancellationToken cancellationToken)
            {
                if (cancellationToken.CanBeCanceled)
                    cancellationToken.Register(() => _delay.TrySetCanceled());
                return _delay.Task;
            }
            public void CompleteDelay() => _delay.TrySetResult(true);
        }

        private sealed class NonCooperativeClock : INodeClock
        {
            public double ElapsedSeconds => 0d;
            public Task Delay(float seconds, CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite);
        }
    }
}
