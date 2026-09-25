using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Telemetry;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonTelemetryWriterV2Tests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        [UnityTest]
        public IEnumerator FailedFirstWrite_RetriesInOrderAndDrains()
        {
            var sink = new FakeSink { FailUpsertAttempts = 1 };
            var delays = new List<TimeSpan>();
            using (var writer = new LessonTelemetryWriterV2(sink, (delay, token) =>
            {
                token.ThrowIfCancellationRequested();
                delays.Add(delay);
                return Task.CompletedTask;
            }))
            {
                TelemetryWriteBatchV2 batch = Batch("run-1", 1, "activation-1");
                writer.Enqueue(batch);

                Task flush = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(flush);

                CollectionAssert.AreEqual(new[] { "state:1", "audit", "state:1", "audit" }, sink.Calls);
                CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(1) }, delays);
                Assert.That(writer.PendingCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator ApplyThenThrow_ReplayKeepsOneStableEventIdentity()
        {
            var sink = new FakeSink { ApplyThenThrowUpsertOnce = true };
            using (var writer = new LessonTelemetryWriterV2(sink, (delay, token) => Task.CompletedTask))
            {
                TelemetryWriteBatchV2 batch = Batch("run-1", 1, "activation-1");
                string eventId = batch.audit_events_by_id.Keys.Single();
                writer.Enqueue(batch);

                Task flush = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(flush);

                Assert.That(sink.UpsertAttempts, Is.EqualTo(2));
                CollectionAssert.AreEqual(new[] { eventId }, sink.StoredEventIds);
                Assert.That(writer.PendingCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator OlderRevisionAfterNewerWrite_DoesNotRollStateBack()
        {
            var sink = new FakeSink();
            using (var writer = new LessonTelemetryWriterV2(sink, (delay, token) => Task.CompletedTask))
            {
                writer.Enqueue(Batch("run-1", 7, "activation-7"));
                writer.Enqueue(Batch("run-1", 4, "activation-4"));

                Task flush = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(flush);

                Assert.That(sink.LatestState.state_revision, Is.EqualTo(7));
                Assert.That(writer.PendingCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator Writer_RejectsASecondRunForTheSameExternalSession()
        {
            using (var writer = new LessonTelemetryWriterV2(new FakeSink(), (delay, token) => Task.CompletedTask))
            {
                writer.Enqueue(Batch("run-1", 1, "activation-1"));

                Assert.Throws<InvalidOperationException>(() => writer.Enqueue(Batch("run-2", 2, "activation-2")));

                Task flush = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(flush);
                Assert.That(writer.PendingCount, Is.Zero);
            }
        }

        [Test]
        public void SessionRegistry_AllowsOnlyOneWriterForAnExternalSession()
        {
            var first = new LessonTelemetryWriterV2(new FakeSink());
            var second = new LessonTelemetryWriterV2(new FakeSink());
            try
            {
                Assert.That(LessonTelemetryWriterV2.TryRegisterSessionWriter("session-owner-test", first), Is.True);
                Assert.That(LessonTelemetryWriterV2.TryRegisterSessionWriter("session-owner-test", second), Is.False);

                LessonTelemetryWriterV2.ReleaseSessionWriter("session-owner-test", first);
                Assert.That(LessonTelemetryWriterV2.TryRegisterSessionWriter("session-owner-test", second), Is.True);
            }
            finally
            {
                LessonTelemetryWriterV2.ReleaseSessionWriter("session-owner-test", first);
                LessonTelemetryWriterV2.ReleaseSessionWriter("session-owner-test", second);
                first.Dispose();
                second.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator CancelledFlushWait_LeavesRetryActiveUntilTheBatchDrains()
        {
            var delayStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowRetry = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sink = new FakeSink { FailUpsertAttempts = int.MaxValue };
            using (var writer = new LessonTelemetryWriterV2(sink, async (delay, token) =>
            {
                delayStarted.TrySetResult(true);
                await Task.WhenAny(allowRetry.Task, Task.Delay(Timeout.Infinite, token));
                token.ThrowIfCancellationRequested();
            }))
            using (var cancellation = new CancellationTokenSource())
            {
                writer.Enqueue(Batch("run-1", 1, "activation-1"));
                Task flush = writer.FlushAsync(cancellation.Token);
                yield return WaitFor(delayStarted.Task);
                cancellation.Cancel();

                int frame = 0;
                while (!flush.IsCompleted && frame++ < 120) yield return null;

                Assert.That(flush.IsCanceled, Is.True, "The bounded flush waiter should cancel without stopping the writer pump.");
                Assert.That(writer.PendingCount, Is.EqualTo(1));
                Assert.That(writer.LastError, Is.Not.Null.And.Not.Empty);

                sink.FailUpsertAttempts = 0;
                allowRetry.TrySetResult(true);
                Task drain = writer.FlushAsync(CancellationToken.None);
                yield return WaitFor(drain);
                Assert.That(writer.PendingCount, Is.Zero);
            }
        }

        private static IEnumerator WaitFor(Task task, int maxFrames = 120)
        {
            int frame = 0;
            while (!task.IsCompleted && frame++ < maxFrames) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Telemetry operation did not complete within the frame bound.");
            task.GetAwaiter().GetResult();
        }

        private static TelemetryWriteBatchV2 Batch(string runId, int revision, string activationId)
        {
            var context = new LessonTelemetryContextV2("session-1", runId, "graph-1", "lesson-1", "launch-1", 2, 3);
            var node = new LessonTelemetryNodeV2("node-1", "Quest", "", 0, activationId);
            var state = new LessonStateV2
            {
                contract_version = 2,
                session_id = context.SessionId,
                run_id = runId,
                graph_id = context.GraphId,
                lesson_id = context.LessonId,
                launch_token = context.LaunchToken,
                lesson_voice_revision = context.LessonVoiceRevision,
                child_phrase_revision = context.ChildPhraseRevision,
                node_id = node.NodeId,
                node_type = node.NodeType,
                node_index = node.NodeIndex,
                activation_id = node.ActivationId,
                status = "running",
                updated_at_utc = T0.AddSeconds(revision).ToString("O"),
                state_revision = revision,
                active_node_ids = new[] { node.NodeId },
                bindings = new LessonBindingV2[0]
            };
            return new LessonTelemetryReducerV2().Observe(
                LessonLifecycleEventV2.NodeEntered(context, node, T0.AddSeconds(revision), revision, state));
        }

        private sealed class FakeSink : ILessonTelemetrySinkV2
        {
            private readonly HashSet<string> _storedEventIds = new HashSet<string>(StringComparer.Ordinal);

            public readonly List<string> Calls = new List<string>();
            public int FailUpsertAttempts;
            public bool ApplyThenThrowUpsertOnce;
            public int UpsertAttempts;
            public LessonStateV2 LatestState;
            public string[] StoredEventIds => _storedEventIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();

            public Task WriteStateAsync(LessonStateV2 state, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Calls.Add("state:" + state.state_revision);
                if (LatestState == null || LatestState.run_id != state.run_id || state.state_revision > LatestState.state_revision)
                    LatestState = state;
                return Task.CompletedTask;
            }

            public Task UpsertBatchAsync(TelemetryWriteBatchV2 batch, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Calls.Add("audit");
                UpsertAttempts++;
                foreach (string eventId in batch.audit_events_by_id.Keys) _storedEventIds.Add(eventId);
                if (ApplyThenThrowUpsertOnce)
                {
                    ApplyThenThrowUpsertOnce = false;
                    throw new InvalidOperationException("Injected apply-then-throw failure.");
                }
                if (FailUpsertAttempts > 0)
                {
                    FailUpsertAttempts--;
                    throw new InvalidOperationException("Injected first-write failure.");
                }
                return Task.CompletedTask;
            }
        }
    }
}
