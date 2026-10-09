using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            public string npc_binding_id;
            public string command_id;
            public string sequence_id;
            public string direction;
            public string result;
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
            public string npc_binding_id;
            public float speech_silence_timeout_seconds;
        }
        [Serializable] private sealed class CancelPacket
        {
            public int contract_version = 2;
            public string @event = "CANCEL_ACTIVE_QUEST";
            public string activation_id;
            public string reason;
        }
        [Serializable] private sealed class CancelSpeakScriptPacket
        {
            public int contract_version = 2;
            public string @event = VoiceQuestTransportV2Constants.CancelSpeakScriptEvent;
            public string activation_id;
            public string sequence_id;
            public string npc_binding_id;
            public string reason;
        }

        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private readonly HashSet<string> _reminderCommandIds = new HashSet<string>(StringComparer.Ordinal);
        private ILiveKitDataPacketClientV2 _client;
        private INpcAudioRouterV2 _router;
        private ILiveKitMicrophoneControlV2 _microphone;
        private Action<byte[], string> _dataReceivedHandler;
        private Action _reconnectedHandler;
        private VoiceQuestActivation _current;
        private bool _terminal;
        private bool _microphoneEnabled;
        private bool _suspended;
        private bool _destroyed;
        private int _configurationVersion;
        private int _lifecycleVersion;
        private Packet _desired;
        private VoiceQuestSignalType? _terminalSignal;
        private string _activeScriptActivationId = string.Empty;
        private string _activeScriptSequenceId = string.Empty;
        private string _activeScriptNpcBindingId = string.Empty;
        private CancelSpeakScriptPacket _pendingScriptCancellation;

        public event Action<VoiceQuestSignal> SignalReceived;
        public string CurrentActivationId => _current?.activation_id ?? string.Empty;

        public void Configure(
            ILiveKitDataPacketClientV2 client,
            INpcAudioRouterV2 router = null,
            ILiveKitMicrophoneControlV2 microphone = null)
        {
            if (_destroyed) return;
            CancelActiveSpeakScript("transport_reconfigured");
            SetMicrophoneEnabled(false);
            _configurationVersion++;
            if (_client != null)
            {
                _client.DataReceivedV2 -= _dataReceivedHandler;
                _client.ReconnectedV2 -= _reconnectedHandler;
            }
            _client = client;
            _router = router ?? (client as INpcAudioRouterV2);
            _microphone = microphone ?? (client as ILiveKitMicrophoneControlV2);
            if (_client != null)
            {
                var configurationVersion = _configurationVersion;
                _dataReceivedHandler = (data, topic) => OnDataReceived(data, topic, configurationVersion);
                _reconnectedHandler = () => OnReconnected(configurationVersion);
                _client.DataReceivedV2 += _dataReceivedHandler;
                _client.ReconnectedV2 += _reconnectedHandler;
            }
            else
            {
                _dataReceivedHandler = null;
                _reconnectedHandler = null;
            }

            _microphoneEnabled = false;
            if (_client != null && _client.IsConnectedV2 && _pendingScriptCancellation != null)
            {
                PublishSpeakScriptCancellation(_pendingScriptCancellation);
                _pendingScriptCancellation = null;
            }
            if (_client != null && _client.IsConnectedV2 && _current != null && !_terminal)
                SetMicrophoneEnabled(true);
        }

        private void Awake()
        {
            Configure(LiveKitService.Instance, LiveKitService.Instance);
        }
        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var callback)) callback();
        }

        public Task ActivateAsync(VoiceQuestActivation request, CancellationToken cancellationToken)
        {
            if (_destroyed) return Task.CompletedTask;
            if (request == null || string.IsNullOrWhiteSpace(request.activation_id))
                throw new ArgumentException("Activation must have an id.", nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            var isNewActivation = _current == null || !string.Equals(_current.activation_id, request.activation_id, StringComparison.Ordinal);
            if (isNewActivation)
                CancelActiveSpeakScript("lesson_scope_changed");
            _current = new VoiceQuestActivation(request.activation_id, request.quest_goal, request.phrases,
                request.npc_binding_id, request.speech_silence_timeout_seconds);
            if (isNewActivation) _reminderCommandIds.Clear();
            _terminal = false;
            _terminalSignal = null;
            if (_client != null && _client.IsConnectedV2)
                SetMicrophoneEnabled(true);
            if (!string.IsNullOrWhiteSpace(_current.npc_binding_id))
            {
                _router?.SetActiveNpcRoute(_current.npc_binding_id);
            }
            _desired = new Packet { @event = "SET_ACTIVE_QUEST", contract_version = 2, activation_id = _current.activation_id, quest_goal = _current.quest_goal, phrases = _current.phrases.ToArray(), npc_binding_id = _current.npc_binding_id };
            Publish(_desired);
            return Task.CompletedTask;
        }

        public Task CancelAsync(string activationId, string reason, CancellationToken cancellationToken)
        {
            if (_destroyed) return Task.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            if (_current == null || !string.Equals(_current.activation_id, activationId, StringComparison.Ordinal)) return Task.CompletedTask;
            if (_terminal) return Task.CompletedTask;
            CancelActiveSpeakScript("lesson_scope_changed");
            _terminal = true;
            SetMicrophoneEnabled(false);
            _desired = new Packet { @event = "CANCEL_ACTIVE_QUEST", contract_version = 2, activation_id = activationId, reason = string.IsNullOrWhiteSpace(reason) ? "cancelled" : reason };
            Publish(_desired);
            return Task.CompletedTask;
        }

        public Task<bool> SendVerbalHintAsync(VoiceQuestVerbalHint request, CancellationToken cancellationToken)
        {
            if (_destroyed || _suspended) return Task.FromResult(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null || string.IsNullOrWhiteSpace(request.command_id) ||
                string.IsNullOrWhiteSpace(request.activation_id) || string.IsNullOrWhiteSpace(request.npc_binding_id) ||
                _client == null || !_client.IsConnectedV2 || _current == null || _terminal ||
                !string.Equals(_current.activation_id, request.activation_id, StringComparison.Ordinal) ||
                !string.Equals(_current.npc_binding_id, request.npc_binding_id, StringComparison.Ordinal))
                return Task.FromResult(false);

            _client.PublishDataV2(
                Encoding.UTF8.GetBytes(JsonUtility.ToJson(request)),
                VoiceQuestTransportV2Constants.Topic,
                true);
            return Task.FromResult(true);
        }

        public bool PublishSpeakScript(string activationId, string commandId, string npcBindingId, string text)
        {
            if (_destroyed || _suspended || string.IsNullOrWhiteSpace(activationId) ||
                string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(npcBindingId) ||
                string.IsNullOrWhiteSpace(text) || text.Length > VoiceQuestTransportV2Constants.MaxScriptLength ||
                _client == null || !_client.IsConnectedV2 || _current == null || _terminal ||
                !string.Equals(_current.activation_id, activationId, StringComparison.Ordinal) ||
                !string.Equals(_current.npc_binding_id, npcBindingId, StringComparison.Ordinal))
                return false;

            if (_router == null || !_router.SetActiveNpcRoute(npcBindingId))
                return false;
            var packet = new VoiceQuestSpeakScriptV2(activationId, commandId, npcBindingId, text);
            _client.PublishDataV2(
                Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet)),
                VoiceQuestTransportV2Constants.Topic,
                true);
            _activeScriptActivationId = activationId;
            _activeScriptSequenceId = commandId;
            _activeScriptNpcBindingId = npcBindingId;
            return true;
        }

        public bool CancelSpeakScript(string activationId, string sequenceId, string npcBindingId, string reason)
        {
            if (string.IsNullOrWhiteSpace(_activeScriptActivationId) ||
                string.IsNullOrWhiteSpace(_activeScriptSequenceId) ||
                string.IsNullOrWhiteSpace(_activeScriptNpcBindingId) ||
                !string.Equals(_activeScriptActivationId, activationId, StringComparison.Ordinal) ||
                !string.Equals(_activeScriptSequenceId, sequenceId, StringComparison.Ordinal) ||
                !string.Equals(_activeScriptNpcBindingId, npcBindingId, StringComparison.Ordinal))
                return false;

            var packet = new CancelSpeakScriptPacket
            {
                activation_id = _activeScriptActivationId,
                sequence_id = _activeScriptSequenceId,
                npc_binding_id = _activeScriptNpcBindingId,
                reason = NormalizeScriptCancellationReason(reason)
            };
            ClearActiveSpeakScript();
            if (_client == null || !_client.IsConnectedV2 || _suspended)
            {
                _pendingScriptCancellation = packet;
                return true;
            }

            PublishSpeakScriptCancellation(packet);
            return true;
        }

        public bool CancelActiveSpeakScript(string reason)
        {
            return CancelSpeakScript(
                _activeScriptActivationId,
                _activeScriptSequenceId,
                _activeScriptNpcBindingId,
                reason);
        }

        private void Publish(Packet packet)
        {
            if (_destroyed || _suspended || _client == null || !_client.IsConnectedV2) return;
            object envelope = packet.@event == "SET_ACTIVE_QUEST"
                ? (object)new ActivatePacket { activation_id = packet.activation_id, quest_goal = packet.quest_goal, phrases = packet.phrases,
                    npc_binding_id = packet.npc_binding_id, speech_silence_timeout_seconds = _current?.speech_silence_timeout_seconds ?? VoiceQuestTransportV2Constants.DefaultSpeechSilenceTimeoutSeconds }
                : new CancelPacket { activation_id = packet.activation_id, reason = packet.reason };
            _client.PublishDataV2(Encoding.UTF8.GetBytes(JsonUtility.ToJson(envelope)), VoiceQuestTransportV2Constants.Topic, true);
        }

        private void OnReconnected(int configurationVersion)
        {
            if (_destroyed || _suspended) return;
            var lifecycleVersion = _lifecycleVersion;
            _mainThreadQueue.Enqueue(() =>
            {
                if (_destroyed || _suspended || configurationVersion != _configurationVersion || lifecycleVersion != _lifecycleVersion)
                    return;
                if (_current != null && !_terminal)
                {
                    _microphoneEnabled = false;
                    SetMicrophoneEnabled(true);
                }
                if (_pendingScriptCancellation != null)
                {
                    PublishSpeakScriptCancellation(_pendingScriptCancellation);
                    _pendingScriptCancellation = null;
                }
                if (_desired != null)
                    Publish(_desired);
            });
        }

        private void OnDataReceived(byte[] data, string topic, int configurationVersion)
        {
            if (_destroyed || _suspended) return;
            if (!string.Equals(topic, VoiceQuestTransportV2Constants.Topic, StringComparison.Ordinal)) return;
            if (data == null || data.Length == 0) return;
            var json = Encoding.UTF8.GetString(data);
            Packet packet;
            try { packet = JsonUtility.FromJson<Packet>(json); }
            catch { return; }
            if (packet == null || packet.contract_version != 2 || string.IsNullOrWhiteSpace(packet.activation_id)) return;
            if (packet.@event == "ON_REMINDER" && !IsExactAcceptedReminderPacket(json)) return;
            var lifecycleVersion = _lifecycleVersion;
            _mainThreadQueue.Enqueue(() =>
            {
                if (_destroyed || _suspended || configurationVersion != _configurationVersion || lifecycleVersion != _lifecycleVersion)
                    return;
                HandlePacket(packet);
            });
        }

        private static bool IsExactAcceptedReminderPacket(string json)
        {
            try
            {
                JObject payload;
                using (var textReader = new StringReader(json))
                using (var reader = new JsonTextReader(textReader))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    payload = JObject.Load(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    if (reader.Read()) return false;
                }

                var expectedKeys = new[]
                {
                    "contract_version", "event", "direction", "result",
                    "activation_id", "npc_binding_id", "command_id"
                };
                if (payload.Count != expectedKeys.Length) return false;
                foreach (var key in expectedKeys)
                    if (!payload.ContainsKey(key)) return false;

                return payload["contract_version"]?.Type == JTokenType.Integer &&
                    (int)payload["contract_version"] == 2 &&
                    IsStringValue(payload["event"], "ON_REMINDER") &&
                    IsStringValue(payload["direction"], "agent_to_unity") &&
                    IsStringValue(payload["result"], "accepted") &&
                    IsNonBlankString(payload["activation_id"]) &&
                    IsNonBlankString(payload["npc_binding_id"]) &&
                    IsNonBlankString(payload["command_id"]);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsStringValue(JToken token, string expected)
        {
            return token?.Type == JTokenType.String &&
                string.Equals((string)token, expected, StringComparison.Ordinal);
        }

        private static bool IsNonBlankString(JToken token)
        {
            return token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)token);
        }

        private void SetMicrophoneEnabled(bool enabled)
        {
            if (enabled && (_suspended || _destroyed)) return;
            if (_microphoneEnabled == enabled) return;
            _microphoneEnabled = enabled;
            _microphone?.EnableMicrophone(enabled);
        }

        private void HandlePacket(Packet packet)
        {
            if (_current == null || !string.Equals(_current.activation_id, packet.activation_id, StringComparison.Ordinal)) return;
            if (packet.@event == "SPEAK_SCRIPT_DONE")
            {
                if ((packet.status == "SUCCESS" || packet.status == "CANCELLED" || packet.status == "FAILED") &&
                    string.Equals(_activeScriptActivationId, packet.activation_id, StringComparison.Ordinal) &&
                    string.Equals(_activeScriptSequenceId, packet.sequence_id, StringComparison.Ordinal) &&
                    string.Equals(_activeScriptNpcBindingId, packet.npc_binding_id, StringComparison.Ordinal))
                    ClearActiveSpeakScript();
                return;
            }
            if (packet.@event == "ON_REMINDER")
            {
                if (_terminal || packet.direction != "agent_to_unity" || packet.result != "accepted" ||
                    string.IsNullOrWhiteSpace(packet.command_id) || string.IsNullOrWhiteSpace(packet.npc_binding_id) ||
                    !string.Equals(_current.npc_binding_id, packet.npc_binding_id, StringComparison.Ordinal) ||
                    !_reminderCommandIds.Add(packet.command_id))
                    return;

                SignalReceived?.Invoke(new VoiceQuestSignal(
                    packet.activation_id,
                    VoiceQuestSignalType.ReminderAccepted,
                    commandId: packet.command_id,
                    npcBindingId: packet.npc_binding_id));
                return;
            }
            // Local cancellation blocks late success immediately; its acknowledgement is still delivered.
            if (_terminal)
            {
                if (_terminalSignal == null && packet.@event == "QUEST_STATUS" &&
                    packet.status == "CANCELLED" && _desired?.@event == "CANCEL_ACTIVE_QUEST")
                {
                    _terminalSignal = VoiceQuestSignalType.Cancelled;
                    _desired = null;
                    SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, VoiceQuestSignalType.Cancelled, packet.reason));
                }
                return;
            }
            if (packet.@event == "QUEST_MATCHED")
            {
                CancelActiveSpeakScript("lesson_scope_changed");
                _terminal = true;
                _terminalSignal = VoiceQuestSignalType.Matched;
                _desired = null;
                SetMicrophoneEnabled(false);
                SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, VoiceQuestSignalType.Matched));
                return;
            }
            if (packet.@event != "QUEST_STATUS") return;
            if (packet.status == "ACTIVE")
                SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, VoiceQuestSignalType.Active));
            else if (packet.status == "CANCELLED" || packet.status == "FAILED")
            {
                CancelActiveSpeakScript("lesson_scope_changed");
                _terminal = true;
                _terminalSignal = packet.status == "CANCELLED" ? VoiceQuestSignalType.Cancelled : VoiceQuestSignalType.Failed;
                if (_terminalSignal != VoiceQuestSignalType.Cancelled || _desired?.@event != "CANCEL_ACTIVE_QUEST")
                    _desired = null;
                SetMicrophoneEnabled(false);
                SignalReceived?.Invoke(new VoiceQuestSignal(packet.activation_id, _terminalSignal.Value, packet.reason));
            }
            // MATCHED status confirms the QUEST_MATCHED event; it never awards success by itself.
        }

        private void OnEnable()
        {
            _suspended = false;
            if (_client != null && _client.IsConnectedV2)
            {
                if (_pendingScriptCancellation != null)
                {
                    PublishSpeakScriptCancellation(_pendingScriptCancellation);
                    _pendingScriptCancellation = null;
                }
                if (_current != null && !_terminal)
                    SetMicrophoneEnabled(true);
                if (_desired != null)
                    Publish(_desired);
            }
        }

        private void OnDisable()
        {
            CancelActiveSpeakScript("lesson_scope_changed");
            _suspended = true;
            _lifecycleVersion++;
            SetMicrophoneEnabled(false);
        }

        private void OnDestroy()
        {
            _lifecycleVersion++;
            Configure(null);
            _destroyed = true;
        }

        private void PublishSpeakScriptCancellation(CancelSpeakScriptPacket packet)
        {
            if (_destroyed || packet == null || _client == null || !_client.IsConnectedV2) return;
            _client.PublishDataV2(
                Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet)),
                VoiceQuestTransportV2Constants.Topic,
                true);
        }

        private void ClearActiveSpeakScript()
        {
            _activeScriptActivationId = string.Empty;
            _activeScriptSequenceId = string.Empty;
            _activeScriptNpcBindingId = string.Empty;
        }

        private static string NormalizeScriptCancellationReason(string reason)
        {
            return reason == "bridge_unload" || reason == "transport_reconfigured"
                ? reason
                : "lesson_scope_changed";
        }
    }
}
