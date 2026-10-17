using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    [Serializable]
    public sealed class SessionScopeV1
    {
        public string session_id, pin, child_id, lesson_id, host_id;
        public SessionTtsConfigV1 config;
    }

    /// <summary>Immutable config copy and scope fetched for one launched session.</summary>
    public sealed class SessionTtsSnapshotV1
    {
        private readonly SessionScopeV1 _scope;
        public SessionScopeV1 Scope => CloneScope(_scope, Config);
        public SessionTtsConfigV1 Config => TtsCacheContractsV1.ParseTtsConfig(ConfigJson);
        public string ConfigJson { get; }

        private SessionTtsSnapshotV1(SessionScopeV1 scope, string configJson)
        { _scope = scope; ConfigJson = configJson; }

        private static SessionScopeV1 CloneScope(SessionScopeV1 scope, SessionTtsConfigV1 config) => new SessionScopeV1
        {
            session_id = scope.session_id, pin = scope.pin, child_id = scope.child_id,
            lesson_id = scope.lesson_id, host_id = scope.host_id, config = config
        };

        public static SessionTtsSnapshotV1 Parse(string json, string expectedSessionId)
        {
            var root = ParseObject(json);
            var expectedFields = new[] { "session_id", "pin", "child_id", "lesson_id", "host_id", "config" };
            if (root.Count != expectedFields.Length || expectedFields.Any(field => !root.ContainsKey(field)))
                throw new CacheValidationException("Session response fields do not match the retained-session contract.");
            foreach (var field in new[] { "session_id", "pin", "child_id", "lesson_id", "host_id" })
            {
                var token = root[field];
                if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token))
                    throw new CacheValidationException("Session scope field must be a nonblank string: " + field);
            }
            var sessionId = (string)root["session_id"];
            if (!string.Equals(sessionId, expectedSessionId, StringComparison.Ordinal))
                throw new CacheValidationException("Session response identity mismatch.");
            var configToken = root["config"];
            if (configToken == null || configToken.Type != JTokenType.Object)
                throw new CacheValidationException("Retained TTS config is missing.");
            var configJson = configToken.ToString(Formatting.None);
            var config = TtsCacheContractsV1.ParseTtsConfig(configJson);
            return new SessionTtsSnapshotV1(new SessionScopeV1
            {
                session_id = sessionId,
                pin = (string)root["pin"],
                child_id = (string)root["child_id"],
                lesson_id = (string)root["lesson_id"],
                host_id = (string)root["host_id"],
                config = config
            }, configJson);
        }

        private static JObject ParseObject(string json)
        {
            try
            {
                using (var text = new StringReader(json ?? string.Empty))
                using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
                {
                    var value = JObject.Load(reader, new JsonLoadSettings
                    { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new CacheValidationException("Trailing JSON content.");
                    return value;
                }
            }
            catch (CacheValidationException) { throw; }
            catch (Exception exception) { throw new CacheValidationException("Invalid session response JSON: " + exception.Message); }
        }
    }
}
