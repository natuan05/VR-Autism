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

        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private ILiveKitDataPacketClientV2 _client;
        private VoiceQuestActivation _current;
        private bool _terminal;
        private int _mainThreadId;

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
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
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
            _current = request;
            _terminal = false;
            Publish(new Packet { @event = "SET_ACTIVE_QUEST", contract_version = 2, activation_id = request.activation_id, quest_goal = request.quest_goal, phrases = request.phrases.ToArray() });
            return Task.CompletedTask;
        }

        public Task CancelAsync(string activationId, string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_current == null || !string.Equals(_current.activation_id, activationId, StringComparison.Ordinal)) return Task.CompletedTask;
            if (_terminal) return Task.CompletedTask;
            _terminal = true;
            Publish(new Packet { @event = "CANCEL_ACTIVE_QUEST", contract_version = 2, activation_id = activationId, reason = string.IsNullOrWhiteSpace(reason) ? "cancelled" : reason });
            return Task.CompletedTask;
        }

        private void Publish(Packet packet)
        {
            if (_client == null || !_client.IsConnectedV2) return;
            _client.PublishDataV2(Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet)), VoiceQuestTransportV2Constants.Topic, true);
        }

        private void OnReconnected()
        {
            if (_current == null || _terminal) return;
            Publish(new Packet { @event = "SET_ACTIVE_QUEST", contract_version = 2, activation_id = _current.activation_id, quest_goal = _current.quest_goal, phrases = _current.phrases.ToArray() });
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
            if (_terminal && packet.@event != "QUEST_STATUS") return;
            if (packet.@event == "QUEST_MATCHED")
            {
                if (_terminal) return;
                _terminal = true;
                SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, VoiceQuestSignalType.Matched));
                return;
            }
            if (packet.@event != "QUEST_STATUS") return;
            if (string.Equals(packet.status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, VoiceQuestSignalType.Active));
            else if (string.Equals(packet.status, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                _terminal = true;
                SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, VoiceQuestSignalType.Cancelled, packet.reason));
            }
            else if (string.Equals(packet.status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                _terminal = true;
                SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, VoiceQuestSignalType.Failed, packet.reason));
            }
        }

        private void OnDestroy() => Configure(null);
    }
}
