using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Cloud.LiveKit;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue
{
    [DisallowMultipleComponent]
    public sealed class LiveKitDialogueTransportV2 : MonoBehaviour, IDialogueTransportV2
    {
        [Serializable]
        private sealed class SpeakScriptPacket
        {
            public int contract_version = DialogueTransportV2Constants.ContractVersion;
            public string @event = "SPEAK_SCRIPT";
            public string activation_id;
            public string sequence_id;
            public string npc_binding_id;
            public string text;
        }

        [Serializable]
        private sealed class DonePacket
        {
            public int contract_version;
            public string @event;
            public string activation_id;
            public string sequence_id;
            public string npc_binding_id;
            public string status;
            public string reason;
        }

        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private ILiveKitDataPacketClientV2 _client;
        private INpcAudioRouterV2 _router;
        private DialogueRequestV2 _currentRequest;
        private bool _terminal;

        public event Action<DialogueSignalV2> SignalReceived;
        public string CurrentActivationId => _currentRequest?.activation_id ?? string.Empty;
        public string CurrentSequenceId => _currentRequest?.sequence_id ?? string.Empty;

        public void Configure(ILiveKitDataPacketClientV2 client, INpcAudioRouterV2 router = null)
        {
            if (_client != null)
            {
                _client.DataReceivedV2 -= OnDataReceived;
                _client.ReconnectedV2 -= OnReconnected;
            }
            _client = client;
            _router = router ?? (client as INpcAudioRouterV2);
            if (_client != null)
            {
                _client.DataReceivedV2 += OnDataReceived;
                _client.ReconnectedV2 += OnReconnected;
            }
        }

        private void Awake()
        {
            Configure(LiveKitService.Instance, LiveKitService.Instance);
        }

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var callback))
            {
                callback();
            }
        }

        public Task SpeakAsync(DialogueRequestV2 request, CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.activation_id))
                throw new ArgumentException("Dialogue activation_id must be non-empty.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.sequence_id))
                throw new ArgumentException("Dialogue sequence_id must be non-empty.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.text))
                throw new ArgumentException("Dialogue text must be non-empty.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.npc_binding_id))
                throw new ArgumentException("Dialogue npc_binding_id must be non-empty.", nameof(request));

            cancellationToken.ThrowIfCancellationRequested();

            _currentRequest = new DialogueRequestV2(
                request.activation_id,
                request.sequence_id,
                request.text,
                request.npc_binding_id);
            _terminal = false;

            _router?.SetActiveNpcRoute(request.npc_binding_id);

            PublishCurrent();
            return Task.CompletedTask;
        }

        public Task CancelAsync(string activationId, string sequenceId, string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_currentRequest == null ||
                !string.Equals(_currentRequest.activation_id, activationId, StringComparison.Ordinal) ||
                !string.Equals(_currentRequest.sequence_id, sequenceId, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            _terminal = true;
            return Task.CompletedTask;
        }

        private void PublishCurrent()
        {
            if (_client == null || !_client.IsConnectedV2 || _currentRequest == null || _terminal)
                return;

            var packet = new SpeakScriptPacket
            {
                activation_id = _currentRequest.activation_id,
                sequence_id = _currentRequest.sequence_id,
                npc_binding_id = _currentRequest.npc_binding_id,
                text = _currentRequest.text
            };

            var json = JsonUtility.ToJson(packet);
            var bytes = Encoding.UTF8.GetBytes(json);
            _client.PublishDataV2(bytes, DialogueTransportV2Constants.Topic, true);
        }

        private void OnReconnected()
        {
            _mainThreadQueue.Enqueue(() =>
            {
                if (_currentRequest != null && !_terminal)
                {
                    PublishCurrent();
                }
            });
        }

        private void OnDataReceived(byte[] data, string topic)
        {
            if (!string.Equals(topic, DialogueTransportV2Constants.Topic, StringComparison.Ordinal))
                return;

            DonePacket packet;
            try
            {
                var json = Encoding.UTF8.GetString(data);
                packet = JsonUtility.FromJson<DonePacket>(json);
            }
            catch
            {
                return;
            }

            if (packet == null ||
                packet.contract_version != DialogueTransportV2Constants.ContractVersion ||
                !string.Equals(packet.@event, "SPEAK_SCRIPT_DONE", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(packet.activation_id) ||
                string.IsNullOrWhiteSpace(packet.sequence_id))
            {
                return;
            }

            _mainThreadQueue.Enqueue(() => HandleDonePacket(packet));
        }

        private void HandleDonePacket(DonePacket packet)
        {
            if (_currentRequest == null ||
                !string.Equals(_currentRequest.activation_id, packet.activation_id, StringComparison.Ordinal) ||
                !string.Equals(_currentRequest.sequence_id, packet.sequence_id, StringComparison.Ordinal) ||
                (!string.IsNullOrEmpty(_currentRequest.npc_binding_id) && !string.Equals(_currentRequest.npc_binding_id, packet.npc_binding_id, StringComparison.Ordinal)))
            {
                Debug.LogWarning($"[LessonGraphV2] DialogueTransport: Ignored stale/mismatched SPEAK_SCRIPT_DONE act={packet.activation_id} seq={packet.sequence_id} npc={packet.npc_binding_id}");
                return;
            }

            if (_terminal)
            {
                Debug.LogWarning($"[LessonGraphV2] DialogueTransport: Ignored late SPEAK_SCRIPT_DONE after termination act={packet.activation_id} seq={packet.sequence_id}");
                return;
            }

            _terminal = true;

            var signalType = DialogueSignalType.Failed;
            if (string.Equals(packet.status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            {
                signalType = DialogueSignalType.Done;
            }
            else if (string.Equals(packet.status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                signalType = DialogueSignalType.Cancelled;
            }
            else if (string.Equals(packet.status, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                signalType = DialogueSignalType.Failed;
            }

            SignalReceived?.Invoke(new DialogueSignalV2(
                packet.activation_id,
                packet.sequence_id,
                packet.npc_binding_id,
                signalType,
                packet.reason));
        }

        private void OnDestroy()
        {
            Configure(null);
        }
    }
}
