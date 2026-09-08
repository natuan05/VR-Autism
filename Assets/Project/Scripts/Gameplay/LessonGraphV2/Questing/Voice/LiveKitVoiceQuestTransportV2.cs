using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VRAutism.Cloud.LiveKit;

namespace VRAutism.Gameplay.LessonGraphV2.Questing.Voice
{
    [DisallowMultipleComponent]
    public sealed class LiveKitVoiceQuestTransportV2 : MonoBehaviour, IVoiceQuestTransport
    {
        [Serializable] private sealed class Packet
        {
            public string @event;
            public int contract_version;
            public string activation_id;
            public string quest_goal;
            public string[] phrases;
            public string reason;
            public string status;
        }

        [Serializable] private sealed class ActivatePacket
        {
            public int contract_version = 2;
            public string @event = "SET_ACTIVE_QUEST";
            public string activation_id;
            public string quest_goal;
            public string[] phrases;
        }
        [Serializable] private sealed class CancelPacket
        {
            public int contract_version = 2;
            public string @event = "CANCEL_ACTIVE_QUEST";
            public string activation_id;
            public string reason;
        }

        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private ILiveKitDataPacketClientV2 _client;
        private VoiceQuestActivation _current;
        private bool _terminal;
        private Packet _desired;
        private VoiceQuestSignalType? _terminalSignal;

        public event Action<VoiceQuestSignal> SignalReceived;
        public string CurrentActivationId => _current?.activation_id ?? string.Empty;

        public void Configure(ILiveKitDataPacketClientV2 client)
        {
            if (_client != null)
            {
                _client.DataReceivedV2 -= OnDataReceived;
                _client.ReconnectedV2 -= OnReconnected;
            }
            _client = client;
            if (_client != null)
            {
                _client.DataReceivedV2 += OnDataReceived;
                _client.ReconnectedV2 += OnReconnected;
            }
        }

        private void Awake()
        {
            Configure(LiveKitService.Instance);
        }
        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var callback)) callback();
        }

        public Task ActivateAsync(VoiceQuestActivation request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.activation_id))
                throw new ArgumentException("Activation must have an id.", nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            _current = new VoiceQuestActivation(request.activation_id, request.quest_goal, request.phrases);
            _terminal = false;
            _terminalSignal = null;
            _desired = new Packet { @event = "SET_ACTIVE_QUEST", contract_version = 2, activation_id = _current.activation_id, quest_goal = _current.quest_goal, phrases = _current.phrases.ToArray() };
            Publish(_desired);
            return Task.CompletedTask;
        }

        public Task CancelAsync(string activationId, string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_current == null || !string.Equals(_current.activation_id, activationId, StringComparison.Ordinal)) return Task.CompletedTask;
            if (_terminal) return Task.CompletedTask;
            _terminal = true;
            _desired = new Packet { @event = "CANCEL_ACTIVE_QUEST", contract_version = 2, activation_id = activationId, reason = string.IsNullOrWhiteSpace(reason) ? "cancelled" : reason };
            Publish(_desired);
            return Task.CompletedTask;
        }

        private void Publish(Packet packet)
        {
            if (_client == null || !_client.IsConnectedV2) return;
            object envelope = packet.@event == "SET_ACTIVE_QUEST"
                ? (object)new ActivatePacket { activation_id = packet.activation_id, quest_goal = packet.quest_goal, phrases = packet.phrases }
                : new CancelPacket { activation_id = packet.activation_id, reason = packet.reason };
            _client.PublishDataV2(Encoding.UTF8.GetBytes(JsonUtility.ToJson(envelope)), VoiceQuestTransportV2Constants.Topic, true);
        }

        private void OnReconnected()
        {
            _mainThreadQueue.Enqueue(() => { if (_desired != null) Publish(_desired); });
        }

        private void OnDataReceived(byte[] data, string topic)
        {
            if (!string.Equals(topic, VoiceQuestTransportV2Constants.Topic, StringComparison.Ordinal)) return;
            Packet packet;
            try { packet = JsonUtility.FromJson<Packet>(Encoding.UTF8.GetString(data)); }
            catch { return; }
            if (packet == null || packet.contract_version != 2 || string.IsNullOrWhiteSpace(packet.activation_id)) return;
            _mainThreadQueue.Enqueue(() => HandlePacket(packet));
        }

        private void HandlePacket(Packet packet)
        {
            if (_current == null || !string.Equals(_current.activation_id, packet.activation_id, StringComparison.Ordinal)) return;
            // Local cancellation blocks late success immediately; its acknowledgement is still delivered.
            if (_terminal)
            {
                if (_terminalSignal == null && packet.@event == "QUEST_STATUS" &&
                    packet.status == "CANCELLED" && _desired?.@event == "CANCEL_ACTIVE_QUEST")
                {
                    _terminalSignal = VoiceQuestSignalType.Cancelled;
                    SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, VoiceQuestSignalType.Cancelled, packet.reason));
                }
                return;
            }
            if (packet.@event == "QUEST_MATCHED")
            {
                _terminal = true;
                _terminalSignal = VoiceQuestSignalType.Matched;
                SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, VoiceQuestSignalType.Matched));
                return;
            }
            if (packet.@event != "QUEST_STATUS") return;
            if (packet.status == "ACTIVE")
                SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, VoiceQuestSignalType.Active));
            else if (packet.status == "CANCELLED" || packet.status == "FAILED")
            {
                _terminal = true;
                _terminalSignal = packet.status == "CANCELLED" ? VoiceQuestSignalType.Cancelled : VoiceQuestSignalType.Failed;
                SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, _terminalSignal.Value, packet.reason));
            }
            // MATCHED status confirms the QUEST_MATCHED event; it never awards success by itself.
        }

        private void OnDestroy() => Configure(null);
    }
}
