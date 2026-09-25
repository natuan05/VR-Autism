using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Questing.Voice;

namespace VRAutism.Gameplay.LessonGraphV2.Questing.Sources
{
    [DisallowMultipleComponent]
    public sealed class VoiceQuestSourceV2 : QuestSourceV2, IQuestVerbalHintV2
    {
        [Tooltip("NPC audio route identity for voice opening prompt, verbal hints, and reminders.")]
        [SerializeField] private string _npcBindingId = "teacher-npc";
        [SerializeField] private LiveKitVoiceQuestTransportV2 _transport;
        private IVoiceQuestTransport _transportOverride;
        private IVoiceQuestTransport _signalTransport;
        private bool _initialized;

        public string NpcBindingId => _npcBindingId;
        public bool CanSendVerbalHint => GetTransport() != null && !string.IsNullOrWhiteSpace(_npcBindingId);

        public void ConfigureNpcBindingId(string npcBindingId)
        {
            _npcBindingId = npcBindingId;
        }

        public void ConfigureTransport(IVoiceQuestTransport transport)
        {
            _transportOverride = transport;
            BindTransport();
        }

        protected override void Awake()
        {
            base.Awake();
            if (_initialized) return;
            _initialized = true;
            if (_transport == null) _transport = FindObjectOfType<LiveKitVoiceQuestTransportV2>();
            BindTerminationHandler();
            BindTransport();
        }

        protected override void OnSourceActivated(QuestSourceActivation activation)
        {
            BindTerminationHandler();
            BindTransport();
            var transport = GetTransport();
            if (transport == null || !VoicePhraseSnapshotStoreV2.TryGet(BindingId, out var phrase))
            {
                TryFail(activation.ActivationId, "voice_phrase_snapshot_missing");
                return;
            }

            var request = new VoiceQuestActivation(activation.ActivationId, phrase.Goal, phrase.Phrases, _npcBindingId);
            transport.ActivateAsync(request, CancellationToken.None).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception, this);
                    TryFail(activation.ActivationId, "voice_transport_failed");
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        public async Task<bool> SendVerbalHintAsync(string activationId, string commandId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transport = GetTransport();
            if (State != QuestSourceState.Active || !string.Equals(CurrentActivationId, activationId, StringComparison.Ordinal) ||
                transport == null || string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(_npcBindingId))
                return false;

            return await transport.SendVerbalHintAsync(
                new VoiceQuestVerbalHint(activationId, commandId, _npcBindingId), cancellationToken);
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
            var transport = GetTransport();
            if (result == null || transport == null || result.Status == QuestSourceTerminalStatus.Completed) return;
            transport.CancelAsync(result.ActivationId, string.IsNullOrWhiteSpace(result.CancellationReason) ? "source_terminated" : result.CancellationReason, CancellationToken.None);
        }

        protected override void OnSourceCleanup()
        {
            if (_signalTransport != null) _signalTransport.SignalReceived -= OnSignal;
            _signalTransport = null;
            Terminated -= OnTerminated;
        }

        private IVoiceQuestTransport GetTransport() => _transportOverride ?? (IVoiceQuestTransport)_transport;

        private void BindTerminationHandler()
        {
            Terminated -= OnTerminated;
            Terminated += OnTerminated;
        }

        private void BindTransport()
        {
            var transport = GetTransport();
            if (ReferenceEquals(_signalTransport, transport)) return;
            if (_signalTransport != null) _signalTransport.SignalReceived -= OnSignal;
            _signalTransport = transport;
            if (_signalTransport != null) _signalTransport.SignalReceived += OnSignal;
        }
    }
}
