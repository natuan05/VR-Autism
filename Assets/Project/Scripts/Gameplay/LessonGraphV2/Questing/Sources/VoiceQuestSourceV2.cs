using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Questing.Voice;

namespace VRAutism.Gameplay.LessonGraphV2.Questing.Sources
{
    [DisallowMultipleComponent]
    public sealed class VoiceQuestSourceV2 : QuestSourceV2
    {
        [SerializeField] private LiveKitVoiceQuestTransportV2 _transport;

        protected override void Awake()
        {
            base.Awake();
            if (_transport == null) _transport = FindObjectOfType<LiveKitVoiceQuestTransportV2>();
            if (_transport != null) _transport.SignalReceived += OnSignal;
            Terminated += OnTerminated;
        }

        protected override void OnSourceActivated(QuestSourceActivation activation)
        {
            if (_transport == null || !VoicePhraseSnapshotStoreV2.TryGet(BindingId, out var phrase))
            {
                TryFail(activation.ActivationId, "voice_phrase_snapshot_missing");
                return;
            }

            var request = new VoiceQuestActivation(activation.ActivationId, phrase.Goal, phrase.Phrases);
            _transport.ActivateAsync(request, CancellationToken.None).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception, this);
                    TryFail(activation.ActivationId, "voice_transport_failed");
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OnSignal(VoiceQuestSignal signal)
        {
            if (signal == null) return;
            switch (signal.Type)
            {
                case VoiceQuestSignalType.Matched:
                    TryComplete(signal.ActivationId, "voice");
                    break;
                case VoiceQuestSignalType.Cancelled:
                    TryCancel(new QuestSourceCancellation(signal.ActivationId, signal.Reason));
                    break;
                case VoiceQuestSignalType.Failed:
                    TryFail(signal.ActivationId, string.IsNullOrWhiteSpace(signal.Reason) ? "voice_agent_failed" : signal.Reason);
                    break;
            }
        }

        private void OnTerminated(QuestSourceResult result)
        {
            if (result == null || _transport == null || result.Status == QuestSourceTerminalStatus.Completed) return;
            _transport.CancelAsync(result.ActivationId, string.IsNullOrWhiteSpace(result.CancellationReason) ? "source_terminated" : result.CancellationReason, CancellationToken.None);
        }

        protected override void OnSourceCleanup()
        {
            if (_transport != null) _transport.SignalReceived -= OnSignal;
            Terminated -= OnTerminated;
        }
    }
}
