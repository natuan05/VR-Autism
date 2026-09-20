using System;
using System.Text;
using UnityEngine;

namespace VRAutism.Cloud.LiveKit
{
    internal sealed class LegacyVoicePacketAdapter
    {
        private readonly LiveKitDataPacketTransport _transport;

        [Serializable]
        private sealed class DataPacketEvent
        {
            public string @event;
            public string quest_name;
            public string status;
            public string reason;
            public string text;
        }

        internal event Action SpeechMatched;
        internal event Action<string> AgentError;
        internal event Action<string, string> QuestStatusUpdated;

        internal LegacyVoicePacketAdapter(LiveKitDataPacketTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        internal void SendActiveQuest(string questName, string[] defaultPhrases)
        {
            string phrasesJson = defaultPhrases != null && defaultPhrases.Length > 0
                ? "[\"" + string.Join("\",\"", defaultPhrases) + "\"]"
                : "[]";

            string jsonPayload = $"{{\"event\":\"SET_ACTIVE_QUEST\",\"quest_name\":\"{questName}\",\"default_phrases\":{phrasesJson}}}";
            _transport.PublishLegacy(Encoding.UTF8.GetBytes(jsonPayload), true);
            Debug.Log($"[LiveKitService] 📡 GỬI DỮ LIỆU QUEST LÊN SERVER: {jsonPayload}");
        }

        internal void SendVerbalHint()
        {
            const string jsonPayload = "{\"event\":\"VERBAL_HINT\"}";
            _transport.PublishLegacy(Encoding.UTF8.GetBytes(jsonPayload), true);
            Debug.Log($"[LiveKitService] 📡 GỬI VERBAL_HINT LÊN AGENT: {jsonPayload}");
        }

        internal void SendOnReminder()
        {
            const string jsonPayload = "{\"event\":\"ON_REMINDER\"}";
            _transport.PublishLegacy(Encoding.UTF8.GetBytes(jsonPayload), true);
            Debug.Log($"[LiveKitService] 📡 GỬI ON_REMINDER LÊN AGENT: {jsonPayload}");
        }

        internal void HandleIncoming(byte[] data, string topic)
        {
            string json = Encoding.UTF8.GetString(data);
            Debug.Log($"[LiveKitService] 📥 NHẬN GÓI TIN TỪ (legacy): {json}");

            try
            {
                var packet = JsonUtility.FromJson<DataPacketEvent>(json);
                if (packet == null || string.IsNullOrEmpty(packet.@event))
                {
                    if (json.Contains("QUEST_MATCHED"))
                    {
                        SpeechMatched?.Invoke();
                    }
                    return;
                }

                switch (packet.@event)
                {
                    case "QUEST_MATCHED":
                        Debug.Log("[LiveKitService] 🎯 QUEST_MATCHED -> Kích hoạt OnSpeechMatched!");
                        SpeechMatched?.Invoke();
                        break;

                    case "AGENT_INIT_FAILED":
                        Debug.LogError($"[LiveKitService] ❌ AGENT_INIT_FAILED: {packet.reason}");
                        AgentError?.Invoke(packet.reason);
                        break;

                    case "QUEST_STATUS":
                        Debug.Log($"[LiveKitService] 📋 QUEST_STATUS: {packet.quest_name} -> {packet.status}");
                        QuestStatusUpdated?.Invoke(packet.quest_name, packet.status);
                        break;

                    default:
                        Debug.Log($"[LiveKitService] ℹ️ Unhandled packet event: {packet.@event}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiveKitService] ❌ Lỗi parse DataPacket: {ex.Message}");
            }
        }
    }
}
