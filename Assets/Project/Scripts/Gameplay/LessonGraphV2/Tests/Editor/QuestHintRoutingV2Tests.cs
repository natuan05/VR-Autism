using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Plugins.QuickOutline.Scripts;
using VRAutism.Core;
using VRAutism.Core.Models;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Questing.Sources;
using VRAutism.Gameplay.LessonGraphV2.Questing.Voice;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Executors;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class QuestHintRoutingV2Tests
    {
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        private SessionContext _previousSessionContext;

        [SetUp]
        public void SetUp()
        {
            _previousSessionContext = SessionContext.Instance;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _objects)
            {
                if (item is GameObject gameObject)
                    gameObject.GetComponent<HintSource>()?.DeferredVerbalReply?.TrySetResult(false);
            }
            foreach (var item in _objects)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _objects.Clear();
            SessionContext.Instance = _previousSessionContext;
        }

        [Test]
        public void OutlineTargetUsesDisabledProfileBaselineAndKeepsItsMeshActive()
        {
            SetVisualGuidance(false);
            var target = OutlineTarget("profile-disabled-outline");
            var outline = target.GetComponent<Outline>();
            outline.enabled = true;
            var source = Source("outline-binding", target);
            InitializeSource(source);

            Assert.That(outline.enabled, Is.False, "Awake must turn off an assigned Outline before activation.");

            Assert.That(source.TryActivate(new QuestSourceActivation("outline-disabled", DateTimeOffset.UtcNow, 0d)), Is.True);

            Assert.That(outline.enabled, Is.False);
            Assert.That(target.activeSelf, Is.True, "The outline target is the interactable mesh and must stay active.");
            source.CancelCurrent();
        }

        [Test]
        public void OutlineTargetUsesEnabledProfileBaseline()
        {
            SetVisualGuidance(true);
            var target = OutlineTarget("profile-enabled-outline");
            var outline = target.GetComponent<Outline>();
            var source = Source("outline-enabled-binding", target);
            InitializeSource(source);

            Assert.That(source.TryActivate(new QuestSourceActivation("outline-enabled", DateTimeOffset.UtcNow, 0d)), Is.True);

            Assert.That(outline.enabled, Is.True);
            Assert.That(target.activeSelf, Is.True);
            source.CancelCurrent();
            Assert.That(outline.enabled, Is.False, "Terminal cleanup must always turn the outline off.");
        }

        [Test]
        public void ExplicitVisualHintCanEnableOutlineWhenProfileBaselineIsOffThenCleanupTurnsItOff()
        {
            SetVisualGuidance(false);
            var target = OutlineTarget("manual-hint-outline");
            var source = Source("manual-outline-binding", target);
            InitializeSource(source);
            const string activationId = "manual-outline-activation";
            Assert.That(source.TryActivate(new QuestSourceActivation(activationId, DateTimeOffset.UtcNow, 0d)), Is.True);
            Assert.That(target.GetComponent<Outline>().enabled, Is.False);

            Assert.That(source.TryShowVisualHint(activationId), Is.True);
            Assert.That(target.GetComponent<Outline>().enabled, Is.True);
            Assert.That(target.activeSelf, Is.True);

            source.CancelCurrent();
            Assert.That(target.GetComponent<Outline>().enabled, Is.False);
            Assert.That(target.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator VisualHintChangesOnlyTheSelectedActiveBinding()
        {
            var firstIndicator = Indicator("first-indicator");
            var secondIndicator = Indicator("second-indicator");
            var first = Source("soap-touch", firstIndicator);
            var second = Source("towel-touch", secondIndicator);
            var executor = new QuestNodeExecutor(new Resolver(first, second), new NeverClock());
            var execution = executor.ExecuteAsync(Context("activation-1", "soap-touch", "towel-touch"));

            var hint = executor.TryApplyHintAsync(Command("hint-1", "activation-1", "VISUAL_HINT", "soap-touch"));
            yield return CompleteWithinFrames(hint);

            Assert.That(hint.GetAwaiter().GetResult().accepted, Is.True);
            Assert.That(hint.GetAwaiter().GetResult().reason, Is.EqualTo(LessonCommandReasonV2.None));
            Assert.That(firstIndicator.activeSelf, Is.True);
            Assert.That(secondIndicator.activeSelf, Is.False, "A hint for soap-touch must not change towel-touch's indicator.");

            first.CancelCurrent();
            yield return CompleteWithinFrames(execution);
        }

        [UnityTest]
        public IEnumerator VerbalHintReachesOnlyTheSelectedActiveBinding()
        {
            var first = Source("teacher", null);
            var second = Source("peer", null);
            var executor = new QuestNodeExecutor(new Resolver(first, second), new NeverClock());
            var execution = executor.ExecuteAsync(Context("activation-2", "teacher", "peer"));

            var hint = executor.TryApplyHintAsync(Command("hint-2", "activation-2", "VERBAL_HINT", "teacher"));
            yield return CompleteWithinFrames(hint);

            Assert.That(hint.GetAwaiter().GetResult().accepted, Is.True);
            Assert.That(first.VerbalHintCalls, Is.EqualTo(1));
            Assert.That(first.LastHintActivationId, Is.EqualTo("activation-2"));
            Assert.That(first.LastHintCommandId, Is.EqualTo("hint-2"));
            Assert.That(second.VerbalHintCalls, Is.Zero);

            first.CancelCurrent();
            yield return CompleteWithinFrames(execution);
        }

        [UnityTest]
        public IEnumerator ExecutorRejectsWrongBindingUnsupportedAndStaleHints()
        {
            var source = Source("soap-touch", null);
            var executor = new QuestNodeExecutor(new Resolver(source), new NeverClock());
            var execution = executor.ExecuteAsync(Context("activation-3", "soap-touch"));

            var wrongBinding = executor.TryApplyHintAsync(Command("wrong-binding", "activation-3", "VISUAL_HINT", "other-binding"));
            yield return CompleteWithinFrames(wrongBinding);
            Assert.That(wrongBinding.GetAwaiter().GetResult().reason, Is.EqualTo(LessonCommandReasonV2.WrongBinding));

            var unsupported = executor.TryApplyHintAsync(Command("unsupported", "activation-3", "VISUAL_HINT", "soap-touch"));
            yield return CompleteWithinFrames(unsupported);
            Assert.That(unsupported.GetAwaiter().GetResult().reason, Is.EqualTo(LessonCommandReasonV2.UnsupportedCapability));

            var stale = executor.TryApplyHintAsync(Command("stale", "old-activation", "VERBAL_HINT", "soap-touch"));
            yield return CompleteWithinFrames(stale);
            Assert.That(stale.GetAwaiter().GetResult().reason, Is.EqualTo(LessonCommandReasonV2.StaleActivation));
            Assert.That(source.VerbalHintCalls, Is.Zero);

            source.CancelCurrent();
            yield return CompleteWithinFrames(execution);
        }

        [UnityTest]
        public IEnumerator ExecutorRejectsHintsAfterActivationCleanup()
        {
            var source = Source("soap-touch", Indicator("indicator"));
            var executor = new QuestNodeExecutor(new Resolver(source), new NeverClock());
            var execution = executor.ExecuteAsync(Context("activation-4", "soap-touch"));
            source.CompleteCurrent();
            yield return CompleteWithinFrames(execution);

            var hint = executor.TryApplyHintAsync(Command("late", "activation-4", "VISUAL_HINT", "soap-touch"));
            yield return CompleteWithinFrames(hint);

            Assert.That(hint.GetAwaiter().GetResult().accepted, Is.False);
            Assert.That(hint.GetAwaiter().GetResult().reason, Is.EqualTo(LessonCommandReasonV2.StaleActivation));
            Assert.That(source.gameObject.GetComponent<HintSource>().TryShowVisualHint("activation-4"), Is.False);
        }

        [UnityTest]
        public IEnumerator ExecutorKeepsPublishedVerbalHintAcceptedAfterActivationCleanup()
        {
            var source = Source("teacher", null);
            source.DeferredVerbalReply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var executor = new QuestNodeExecutor(new Resolver(source), new NeverClock());
            var execution = executor.ExecuteAsync(Context("activation-5", "teacher"));
            var hint = executor.TryApplyHintAsync(Command("delayed-hint", "activation-5", "VERBAL_HINT", "teacher"));
            Assert.That(hint.IsCompleted, Is.False);

            source.CompleteCurrent();
            yield return CompleteWithinFrames(execution);
            source.DeferredVerbalReply.TrySetResult(true);
            yield return CompleteWithinFrames(hint);

            Assert.That(hint.GetAwaiter().GetResult().accepted, Is.True);
            Assert.That(hint.GetAwaiter().GetResult().reason, Is.EqualTo(LessonCommandReasonV2.None));
        }

        [UnityTest]
        public IEnumerator RunnerRoutesVisualHintAndPublishesCurrentBindingCapabilities()
        {
            var indicator = Indicator("soap-indicator");
            var source = Source("soap-touch", indicator);
            var graph = Graph("soap-touch");
            var runnerObject = new GameObject("hint-runner");
            _objects.Add(runnerObject);
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            runner.Configure(graph, new Registry(new QuestNodeExecutor(new Resolver(source), new NeverClock())), clock: new NeverClock());
            runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));

            var lesson = runner.StartLessonAsync();
            var ready = runner.CurrentState;
            Assert.That(ready.status, Is.EqualTo("running"));
            Assert.That(ready.bindings, Has.Length.EqualTo(1));
            Assert.That(ready.bindings[0].binding_id, Is.EqualTo("soap-touch"));
            Assert.That(ready.bindings[0].can_visual_hint, Is.True);
            Assert.That(ready.bindings[0].can_verbal_hint, Is.True);

            var command = Command("runner-hint", ready.activation_id, "VISUAL_HINT", "soap-touch");
            command.session_id = "session-1";
            command.run_id = ready.run_id;
            command.node_id = ready.node_id;
            var result = runner.ApplyCommandAsync(command);
            yield return CompleteWithinFrames(result);

            Assert.That(result.GetAwaiter().GetResult().accepted, Is.True);
            Assert.That(indicator.activeSelf, Is.True);
            runner.AbortLesson();
            yield return CompleteWithinFrames(lesson);
        }

        [UnityTest]
        public IEnumerator RunnerReportsPublishedVerbalHintAcceptedWithFreshStateAfterNodeCompletes()
        {
            var source = Source("teacher", null);
            source.DeferredVerbalReply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var graph = Graph("teacher");
            var runnerObject = new GameObject("late-hint-runner");
            _objects.Add(runnerObject);
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            runner.Configure(graph, new Registry(new QuestNodeExecutor(new Resolver(source), new NeverClock())), clock: new NeverClock());
            runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));

            var lesson = runner.StartLessonAsync();
            var state = runner.CurrentState;
            var command = Command("late-runner-hint", state.activation_id, "VERBAL_HINT", "teacher");
            command.session_id = "session-1";
            command.run_id = state.run_id;
            command.node_id = state.node_id;
            var hint = runner.ApplyCommandAsync(command);
            Assert.That(hint.IsCompleted, Is.False);
            Assert.That(source.VerbalHintCalls, Is.EqualTo(1), "The active source received the hint before the node advanced.");

            source.CompleteCurrent();
            yield return CompleteWithinFrames(lesson);
            source.DeferredVerbalReply.TrySetResult(true);
            yield return CompleteWithinFrames(hint);

            var result = hint.GetAwaiter().GetResult();
            Assert.That(result.accepted, Is.True, "A packet already published remains accepted after the active node advances.");
            Assert.That(result.reason, Is.EqualTo(LessonCommandReasonV2.None));
            Assert.That(result.state.status, Is.EqualTo("completed"), "The result carries the latest authoritative runner state.");
            Assert.That(result.state.node_id, Is.EqualTo("quest-1"));
        }

        [UnityTest]
        public IEnumerator RunnerRejectsReentrantPauseAfterSourceBeginsTerminalCompletion()
        {
            var source = Source("teacher", null);
            var graph = Graph("teacher");
            var runnerObject = new GameObject("terminal-pause-race-runner");
            _objects.Add(runnerObject);
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            runner.Configure(graph, new Registry(new QuestNodeExecutor(new Resolver(source), new NeverClock())), clock: new NeverClock());
            runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));

            Task<LessonCommandResultV2> pause = null;
            var pauseCompletedInsideSourceCallback = false;
            var terminalResults = 0;
            source.Terminated += _ => terminalResults++;
            source.StateChanged += state =>
            {
                if (state != QuestSourceState.Completing || pause != null) return;
                pause = runner.ApplyCommandAsync(RunnerCommand(runner.CurrentState, "reentrant-pause-after-result", "PAUSE"));
                pauseCompletedInsideSourceCallback = pause.IsCompleted;
            };

            var lesson = runner.StartLessonAsync();
            yield return UntilFrames(() => source.State == QuestSourceState.Active && runner.CurrentState?.status == "running");
            source.CompleteCurrent();

            Assert.That(pause, Is.Not.Null);
            yield return CompleteWithinFrames(pause);
            var pauseResult = pause.GetAwaiter().GetResult();
            if (pauseResult.accepted)
                runner.AbortLesson();
            yield return CompleteWithinFrames(lesson);

            Assert.That(pauseCompletedInsideSourceCallback, Is.True,
                "PAUSE must reject synchronously while a terminal source result is being published.");
            Assert.That(pauseResult.accepted, Is.False);
            Assert.That(pauseResult.reason, Is.EqualTo(LessonCommandReasonV2.InvalidState));
            Assert.That(pauseResult.state.status, Is.EqualTo("running"), "Rejected PAUSE must leave runner state unchanged.");
            Assert.That(lesson.GetAwaiter().GetResult().IsSuccess, Is.True);
            Assert.That(source.State, Is.EqualTo(QuestSourceState.Completed));
            Assert.That(terminalResults, Is.EqualTo(1), "Terminal completion emits only one result.");
            Assert.That(source.TryActivate(new QuestSourceActivation("second-activation", DateTimeOffset.UtcNow, 0d)), Is.False,
                "Completed source remains one-shot.");
        }

        [UnityTest]
        public IEnumerator RunnerRejectsPauseWhenExecutionTaskCompletedBeforeRunnerCommit()
        {
            var executor = new PendingExecutor();
            var runnerObject = new GameObject("post-executor-pause-race-runner");
            _objects.Add(runnerObject);
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            runner.Configure(Graph("teacher"), new Registry(executor), clock: new NeverClock());
            runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));

            var lesson = runner.StartLessonAsync();
            yield return UntilFrames(() => executor.Context != null && runner.CurrentState?.status == "running");
            executor.Complete(NodeStatus.Success);

            var pause = runner.ApplyCommandAsync(RunnerCommand(runner.CurrentState, "pause-after-executor-result", "PAUSE"));
            var pauseCompletedBeforeRunnerCommit = pause.IsCompleted;
            yield return CompleteWithinFrames(pause);
            var pauseResult = pause.GetAwaiter().GetResult();
            if (pauseResult.accepted)
                runner.AbortLesson();
            yield return CompleteWithinFrames(lesson);

            Assert.That(pauseCompletedBeforeRunnerCommit, Is.True,
                "PAUSE must reject immediately while the completed executor result awaits runner commit.");
            Assert.That(pauseResult.accepted, Is.False);
            Assert.That(pauseResult.reason, Is.EqualTo(LessonCommandReasonV2.InvalidState));
            Assert.That(pauseResult.state.status, Is.EqualTo("running"), "Rejected PAUSE must leave runner state unchanged.");
            Assert.That(lesson.GetAwaiter().GetResult().IsSuccess, Is.True);
            Assert.That(runner.CurrentState.status, Is.EqualTo("completed"));
        }

        [UnityTest]
        public IEnumerator RunnerRejectsVerbalHintWhenTransportSendFails()
        {
            var source = Source("teacher", null);
            source.DeferredVerbalReply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var graph = Graph("teacher");
            var runnerObject = new GameObject("failed-verbal-hint-runner");
            _objects.Add(runnerObject);
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            runner.Configure(graph, new Registry(new QuestNodeExecutor(new Resolver(source), new NeverClock())), clock: new NeverClock());
            runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));

            var lesson = runner.StartLessonAsync();
            yield return UntilFrames(() => source.State == QuestSourceState.Active && runner.CurrentState?.status == "running");
            var command = RunnerCommand(runner.CurrentState, "failed-verbal-hint", "VERBAL_HINT");
            command.binding_id = "teacher";
            var hint = runner.ApplyCommandAsync(command);
            Assert.That(hint.IsCompleted, Is.False);
            source.DeferredVerbalReply.TrySetResult(false);
            yield return CompleteWithinFrames(hint);
            var result = hint.GetAwaiter().GetResult();

            runner.AbortLesson();
            yield return CompleteWithinFrames(lesson);

            Assert.That(result.accepted, Is.False);
            Assert.That(result.reason, Is.EqualTo(LessonCommandReasonV2.TransportUnavailable));
            Assert.That(result.state.status, Is.EqualTo("running"), "A failed send does not advance lesson state.");
            Assert.That(source.VerbalHintCalls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ReentrantResumeFromPausedStateWaitsForVoiceSourceRearm()
        {
            VoicePhraseSnapshotStoreV2.Replace(new Dictionary<string, VoiceQuestPhraseSnapshotV2>
            {
                ["voice-binding"] = new VoiceQuestPhraseSnapshotV2("voice-binding", "Ask", new[] { "Please" })
            });

            var voiceObject = new GameObject("reentrant-pause-resume-voice-source");
            _objects.Add(voiceObject);
            var voice = voiceObject.AddComponent<VoiceQuestSourceV2>();
            Set(typeof(QuestSourceV2), voice, "_bindingId", "voice-binding");
            var voiceTransport = new SignalTransport();
            voice.ConfigureTransport(voiceTransport);

            var runnerObject = new GameObject("reentrant-pause-resume-runner");
            _objects.Add(runnerObject);
            var runner = runnerObject.AddComponent<LessonGraphRunner>();
            runner.Configure(Graph("voice-binding"), new Registry(new QuestNodeExecutor(new Resolver(voice), new NeverClock())), clock: new NeverClock());
            runner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));

            Task<LessonCommandResultV2> reentrantResume = null;
            var sourceStateWhenResumeRequested = QuestSourceState.Inactive;
            var resumeCompletedInsidePausedCallback = false;
            runner.StateChanged += state =>
            {
                if (state == null || state.status != "paused" || reentrantResume != null) return;
                sourceStateWhenResumeRequested = voice.State;
                reentrantResume = runner.ApplyCommandAsync(RunnerCommand(state, "reentrant-resume", "RESUME"));
                resumeCompletedInsidePausedCallback = reentrantResume.IsCompleted;
            };

            var lesson = runner.StartLessonAsync();
            yield return UntilFrames(() => voice.State == QuestSourceState.Active && runner.CurrentState?.bindings?.Length == 1);
            var oldActivationId = runner.CurrentState.activation_id;
            var pause = runner.ApplyCommandAsync(RunnerCommand(runner.CurrentState, "reentrant-pause", "PAUSE"));
            yield return CompleteWithinFrames(pause);

            Assert.That(pause.GetAwaiter().GetResult().accepted, Is.True);
            Assert.That(pause.GetAwaiter().GetResult().state.status, Is.EqualTo("paused"));
            Assert.That(sourceStateWhenResumeRequested, Is.EqualTo(QuestSourceState.Cancelled),
                "The paused snapshot is published before the executor rearms its source.");
            Assert.That(reentrantResume, Is.Not.Null, "A synchronous paused-state listener should be able to request resume.");
            Assert.That(resumeCompletedInsidePausedCallback, Is.False,
                "RESUME must wait while the runner is still publishing paused state and rearming sources.");

            yield return CompleteWithinFrames(reentrantResume);
            var resumeResult = reentrantResume.GetAwaiter().GetResult();
            Assert.That(resumeResult.accepted, Is.True);
            Assert.That(resumeResult.state.status, Is.EqualTo("running"));
            yield return UntilFrames(() => voice.State == QuestSourceState.Active && runner.CurrentState.activation_id != oldActivationId);
            var newActivationId = runner.CurrentState.activation_id;
            Assert.That(voiceTransport.Activations, Has.Count.EqualTo(2),
                "The resumed activation must start after the cancelled source is rearmed.");
            Assert.That(voiceTransport.Activations[1].activation_id, Is.EqualTo(newActivationId));

            voiceTransport.Emit(new VoiceQuestSignal(oldActivationId, VoiceQuestSignalType.Matched));
            Assert.That(voice.State, Is.EqualTo(QuestSourceState.Active),
                "A late callback from the cancelled activation must not complete the resumed source.");
            voiceTransport.Emit(new VoiceQuestSignal(newActivationId, VoiceQuestSignalType.Matched));
            yield return CompleteWithinFrames(lesson);
            Assert.That(lesson.GetAwaiter().GetResult().IsSuccess, Is.True);
        }

        [UnityTest]
        public IEnumerator RunnerPauseResumeRearmsVoiceAndTouchSourcesForFreshActivation()
        {
            VoicePhraseSnapshotStoreV2.Replace(new Dictionary<string, VoiceQuestPhraseSnapshotV2>
            {
                ["voice-binding"] = new VoiceQuestPhraseSnapshotV2("voice-binding", "Ask", new[] { "Please" })
            });

            var voiceObject = new GameObject("pause-resume-voice-source");
            _objects.Add(voiceObject);
            var voice = voiceObject.AddComponent<VoiceQuestSourceV2>();
            Set(typeof(QuestSourceV2), voice, "_bindingId", "voice-binding");
            var voiceTransport = new SignalTransport();
            voice.ConfigureTransport(voiceTransport);
            var voiceGraph = Graph("voice-binding");
            var voiceRunnerObject = new GameObject("pause-resume-voice-runner");
            _objects.Add(voiceRunnerObject);
            var voiceRunner = voiceRunnerObject.AddComponent<LessonGraphRunner>();
            voiceRunner.Configure(voiceGraph, new Registry(new QuestNodeExecutor(new Resolver(voice), new NeverClock())), clock: new NeverClock());
            voiceRunner.ConfigureSession(new LessonSessionContextV2("session-1", "lesson-1", "launch-1", 1, 1));

            var voiceLesson = voiceRunner.StartLessonAsync();
            yield return UntilFrames(() => voice.State == QuestSourceState.Active && voiceRunner.CurrentState?.bindings?.Length == 1);
            var oldVoiceActivation = voiceRunner.CurrentState.activation_id;
            var pauseVoice = voiceRunner.ApplyCommandAsync(RunnerCommand(voiceRunner.CurrentState, "pause-voice", "PAUSE"));
            yield return CompleteWithinFrames(pauseVoice);

            Assert.That(pauseVoice.GetAwaiter().GetResult().accepted, Is.True);
            Assert.That(voiceRunner.CurrentState.status, Is.EqualTo("paused"));
            Assert.That(voice.State, Is.EqualTo(QuestSourceState.Inactive));

            var resumeVoice = voiceRunner.ApplyCommandAsync(RunnerCommand(voiceRunner.CurrentState, "resume-voice", "RESUME"));
            yield return CompleteWithinFrames(resumeVoice);
            yield return UntilFrames(() => voice.State == QuestSourceState.Active && voiceRunner.CurrentState.activation_id != oldVoiceActivation);
            var newVoiceActivation = voiceRunner.CurrentState.activation_id;
            Assert.That(voiceTransport.Activations, Has.Count.EqualTo(2));
            Assert.That(voiceTransport.Activations[1].activation_id, Is.EqualTo(newVoiceActivation));

            voiceTransport.Emit(new VoiceQuestSignal(oldVoiceActivation, VoiceQuestSignalType.Matched));
            Assert.That(voice.State, Is.EqualTo(QuestSourceState.Active), "A late signal from the paused activation must not complete the resumed source.");
            voiceTransport.Emit(new VoiceQuestSignal(newVoiceActivation, VoiceQuestSignalType.Matched));
            yield return CompleteWithinFrames(voiceLesson);
            Assert.That(voiceLesson.GetAwaiter().GetResult().IsSuccess, Is.True);

            var touchObject = new GameObject("pause-resume-touch-source");
            _objects.Add(touchObject);
            var touch = touchObject.AddComponent<HoldTouchQuestSourceV2>();
            Set(typeof(QuestSourceV2), touch, "_bindingId", "touch-binding");
            var touchGraph = Graph("touch-binding");
            var touchRunnerObject = new GameObject("pause-resume-touch-runner");
            _objects.Add(touchRunnerObject);
            var touchRunner = touchRunnerObject.AddComponent<LessonGraphRunner>();
            touchRunner.Configure(touchGraph, new Registry(new QuestNodeExecutor(new Resolver(touch), new NeverClock())), clock: new NeverClock());
            touchRunner.ConfigureSession(new LessonSessionContextV2("session-2", "lesson-2", "launch-2", 1, 1));

            var touchLesson = touchRunner.StartLessonAsync();
            yield return UntilFrames(() => touch.State == QuestSourceState.Active && touchRunner.CurrentState?.bindings?.Length == 1);
            var oldTouchActivation = touchRunner.CurrentState.activation_id;
            var pauseTouch = touchRunner.ApplyCommandAsync(RunnerCommand(touchRunner.CurrentState, "pause-touch", "PAUSE"));
            yield return CompleteWithinFrames(pauseTouch);
            Assert.That(pauseTouch.GetAwaiter().GetResult().accepted, Is.True);
            Assert.That(touch.State, Is.EqualTo(QuestSourceState.Inactive));

            var staleContactObject = new GameObject("stale-touch-contact");
            _objects.Add(staleContactObject);
            var staleCollider = staleContactObject.AddComponent<SphereCollider>();
            var contacts = Get<HashSet<Collider>>(typeof(HoldTouchQuestSourceV2), touch, "_contacts");
            contacts.Add(staleCollider);
            Set(typeof(HoldTouchQuestSourceV2), touch, "_contactStartedAt", 123d);

            var resumeTouch = touchRunner.ApplyCommandAsync(RunnerCommand(touchRunner.CurrentState, "resume-touch", "RESUME"));
            yield return CompleteWithinFrames(resumeTouch);
            yield return UntilFrames(() => touch.State == QuestSourceState.Active && touchRunner.CurrentState.activation_id != oldTouchActivation);
            var newTouchActivation = touchRunner.CurrentState.activation_id;
            Assert.That(contacts, Is.Empty, "A fresh activation must discard contacts carried over from a cancelled attempt.");
            Assert.That(double.IsNaN((double)Get(typeof(HoldTouchQuestSourceV2), touch, "_contactStartedAt")), Is.True,
                "A fresh activation must reset the previous hold timer.");

            Assert.That(CompleteTouch(touch, oldTouchActivation), Is.False,
                "A completion callback correlated to the cancelled activation must be ignored after resume.");
            Assert.That(touchRunner.CurrentState.status, Is.EqualTo("running"));
            Assert.That(CompleteTouch(touch, newTouchActivation), Is.True);
            yield return CompleteWithinFrames(touchLesson);
            Assert.That(touchLesson.GetAwaiter().GetResult().IsSuccess, Is.True);
        }

        private HintSource Source(string bindingId, GameObject indicator)
        {
            var gameObject = new GameObject("hint-source-" + bindingId);
            _objects.Add(gameObject);
            var source = gameObject.AddComponent<HintSource>();
            Set(typeof(QuestSourceV2), source, "_bindingId", bindingId);
            Set(typeof(QuestSourceV2), source, "_visualHintIndicator", indicator);
            return source;
        }

        private GameObject Indicator(string name)
        {
            var indicator = new GameObject(name);
            indicator.SetActive(false);
            _objects.Add(indicator);
            return indicator;
        }

        private GameObject OutlineTarget(string name)
        {
            var target = new GameObject(name);
            _objects.Add(target);
            var mesh = new Mesh();
            _objects.Add(mesh);
            target.AddComponent<MeshFilter>().sharedMesh = mesh;
            target.AddComponent<MeshRenderer>();
            target.AddComponent<Outline>();
            return target;
        }

        private void SetVisualGuidance(bool enabled)
        {
            var contextObject = new GameObject("hint-routing-session-context");
            contextObject.SetActive(false);
            _objects.Add(contextObject);
            var context = contextObject.AddComponent<SessionContext>();
            context.CurrentParams = new LessonParameters();
            context.CurrentParams.Actions.EnableVisualGuidance = enabled;
            SessionContext.Instance = context;
        }

        private static void InitializeSource(QuestSourceV2 source)
        {
            typeof(QuestSourceV2)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(source, null);
        }

        private LessonGraph Graph(params string[] bindingIds)
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            _objects.Add(graph);
            graph.name = "hint-routing-test-graph";
            graph.Editor_SetEntryNodeId("quest-1");
            graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("quest-1", NodeType.Quest, new QuestNodeConfig(new List<string>(bindingIds), -1f))
            });
            graph.Editor_SetEdges(new List<LessonEdgeData>());
            return graph;
        }

        private static LessonCommandV2 RunnerCommand(LessonStateV2 state, string commandId, string kind)
        {
            var command = Command(commandId, state.activation_id, kind, string.Empty);
            command.session_id = state.session_id;
            command.run_id = state.run_id;
            command.node_id = state.node_id;
            return command;
        }

        private static bool CompleteTouch(HoldTouchQuestSourceV2 source, string activationId) =>
            (bool)typeof(QuestSourceV2).GetMethod("TryComplete", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string), typeof(string) }, null).Invoke(source, new object[] { activationId, "hold_touch" });

        private static NodeExecutionContext Context(string activationId, params string[] bindings) =>
            new NodeExecutionContext("run-1", activationId, "graph-1",
                new LessonNodeData("quest-1", NodeType.Quest, new QuestNodeConfig(new List<string>(bindings), -1f)),
                0d, CancellationToken.None, CancellationToken.None, CancellationToken.None, null, new NeverClock());

        private static LessonCommandV2 Command(string id, string activationId, string kind, string bindingId) =>
            new LessonCommandV2
            {
                contract_version = LessonRemoteContractV2.ContractVersion,
                @event = LessonRemoteContractV2.CommandEvent,
                command_id = id,
                session_id = "session-1",
                run_id = "run-1",
                node_id = "quest-1",
                activation_id = activationId,
                command = kind,
                binding_id = bindingId
            };

        private static IEnumerator CompleteWithinFrames(Task task)
        {
            for (var frame = 0; frame < 30 && !task.IsCompleted; frame++) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Task did not complete within 30 editor frames.");
        }

        private static IEnumerator UntilFrames(Func<bool> condition, int maxFrames = 30)
        {
            for (var frame = 0; frame < maxFrames && !condition(); frame++) yield return null;
            Assert.That(condition(), Is.True, $"Condition did not become true within {maxFrames} editor frames.");
        }

        private static void Set(Type type, object target, string field, object value) =>
            type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static object Get(Type type, object target, string field) =>
            type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static T Get<T>(Type type, object target, string field) => (T)Get(type, target, field);

        private sealed class HintSource : QuestSourceV2, IQuestVerbalHintV2
        {
            public int VerbalHintCalls { get; private set; }
            public string LastHintActivationId { get; private set; }
            public string LastHintCommandId { get; private set; }
            public TaskCompletionSource<bool> DeferredVerbalReply { get; set; }

            public Task<bool> SendVerbalHintAsync(string activationId, string commandId, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerbalHintCalls++;
                LastHintActivationId = activationId;
                LastHintCommandId = commandId;
                if (DeferredVerbalReply != null) return DeferredVerbalReply.Task;
                return Task.FromResult(State == QuestSourceState.Active && CurrentActivationId == activationId);
            }

            public void CancelCurrent() => TryCancel(new QuestSourceCancellation(CurrentActivationId, "test-cleanup"));
            public void CompleteCurrent() => TryComplete(CurrentActivationId, "test");
        }

        private sealed class Resolver : IQuestBindingResolver
        {
            private readonly Dictionary<string, QuestSourceV2> _sources = new Dictionary<string, QuestSourceV2>(StringComparer.Ordinal);
            public Resolver(params QuestSourceV2[] sources) { foreach (var source in sources) _sources.Add(source.BindingId, source); }
            public QuestBindingResolution Resolve(string id) => _sources.TryGetValue(id, out var source)
                ? QuestBindingResolution.Success(source)
                : QuestBindingResolution.Failure(new QuestBindingValidationIssue(QuestBindingFailureCodes.MissingBinding, id, "missing"));
        }

        private sealed class SignalTransport : IVoiceQuestTransport
        {
            public event Action<VoiceQuestSignal> SignalReceived;
            public readonly List<VoiceQuestActivation> Activations = new List<VoiceQuestActivation>();
            public Task ActivateAsync(VoiceQuestActivation request, CancellationToken cancellationToken)
            {
                Activations.Add(request);
                return Task.CompletedTask;
            }
            public Task CancelAsync(string activationId, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<bool> SendVerbalHintAsync(VoiceQuestVerbalHint request, CancellationToken cancellationToken) => Task.FromResult(true);
            public void Emit(VoiceQuestSignal signal) => SignalReceived?.Invoke(signal);
        }

        private sealed class PendingExecutor : INodeExecutor
        {
            private readonly TaskCompletionSource<NodeResult> _completion =
                new TaskCompletionSource<NodeResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            public NodeExecutionContext Context { get; private set; }

            public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
            {
                Context = context;
                return _completion.Task;
            }

            public void Complete(NodeStatus status) => _completion.TrySetResult(
                NodeResult.Completed(Context.Node.Id, Context.ActivationId, status, Context.ElapsedSeconds));
        }

        private sealed class Registry : INodeExecutorRegistry
        {
            private readonly INodeExecutor _executor;
            public Registry(INodeExecutor executor) { _executor = executor; }
            public bool TryGet(NodeType type, out INodeExecutor executor) { executor = _executor; return type == NodeType.Quest; }
        }

        private sealed class NeverClock : INodeClock
        {
            public double ElapsedSeconds => 7d;
            public Task Delay(float seconds, CancellationToken cancellationToken) => new TaskCompletionSource<bool>().Task;
        }
    }
}
