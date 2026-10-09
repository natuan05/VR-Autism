using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VRAutism.Gameplay.LessonGraphV2.Questing.Voice;

namespace VRAutism.Gameplay.LessonGraphV2.Remote
{
    public static class LessonCommandCodecV2
    {
        private static readonly string[] RequiredFields =
        {
            "contract_version", "event", "command_id", "session_id", "run_id",
            "node_id", "activation_id", "command", "binding_id"
        };

        public static bool TryParse(byte[] payload, out LessonCommandV2 command, out string reason)
        {
            command = null;
            reason = LessonCommandReasonV2.Malformed;
            if (payload == null || payload.Length == 0) return false;

            Dictionary<string, string> fields;
            HashSet<string> stringFields;
            if (!TryReadObject(payload, out fields, out stringFields)) return false;
            for (var i = 0; i < RequiredFields.Length; i++)
                if (!fields.ContainsKey(RequiredFields[i]) ||
                    (RequiredFields[i] != "contract_version" && !stringFields.Contains(RequiredFields[i]))) return false;

            int version;
            if (!int.TryParse(fields["contract_version"], out version) || version != LessonRemoteContractV2.ContractVersion) return false;
            if (fields["event"] != LessonRemoteContractV2.CommandEvent) return false;
            if (!IsKnownCommand(fields["command"])) return false;
            if (!Nonblank(fields["command_id"]) || !Nonblank(fields["session_id"]) ||
                !Nonblank(fields["run_id"]) || !Nonblank(fields["node_id"]) || !Nonblank(fields["activation_id"])) return false;
            if (ContainsFirebasePathKeyCharacter(fields["session_id"])) return false;

            var hint = fields["command"] == LessonCommandKindV2.VerbalHint || fields["command"] == LessonCommandKindV2.VisualHint;
            if (hint ? !Nonblank(fields["binding_id"]) : fields["binding_id"].Length != 0) return false;

            var volumeCommand = fields["command"] == LessonCommandKindV2.SetVolume;
            var scriptCommand = fields["command"] == LessonCommandKindV2.SpeakScript;
            var expectedFieldCount = RequiredFields.Length + (volumeCommand ? 1 : scriptCommand ? 2 : 0);
            if (fields.Count != expectedFieldCount) return false;

            var volume = 0f;
            if (volumeCommand)
            {
                if (!fields.ContainsKey("volume") || stringFields.Contains("volume") ||
                    !IsJsonNumber(fields["volume"]) ||
                    !float.TryParse(fields["volume"], NumberStyles.Float, CultureInfo.InvariantCulture, out volume) ||
                    float.IsNaN(volume) || float.IsInfinity(volume) || volume < 0f || volume > 1f)
                    return false;
            }

            var npcBindingId = string.Empty;
            var scriptText = string.Empty;
            if (scriptCommand)
            {
                if (!fields.ContainsKey("npc_binding_id") || !stringFields.Contains("npc_binding_id") ||
                    !fields.ContainsKey("text") || !stringFields.Contains("text") ||
                    !Nonblank(fields["npc_binding_id"]) || !Nonblank(fields["text"]) ||
                    fields["text"].Length > VoiceQuestTransportV2Constants.MaxScriptLength)
                    return false;
                npcBindingId = fields["npc_binding_id"];
                scriptText = fields["text"];
            }

            command = new LessonCommandV2
            {
                contract_version = version,
                @event = fields["event"],
                command_id = fields["command_id"],
                session_id = fields["session_id"],
                run_id = fields["run_id"],
                node_id = fields["node_id"],
                activation_id = fields["activation_id"],
                command = fields["command"],
                binding_id = fields["binding_id"],
                volume = volume,
                npc_binding_id = npcBindingId,
                text = scriptText
            };
            reason = LessonCommandReasonV2.None;
            return true;
        }

        private static bool TryReadObject(byte[] payload, out Dictionary<string, string> fields, out HashSet<string> stringFields)
        {
            fields = null;
            stringFields = null;
            string json;
            try { json = new UTF8Encoding(false, true).GetString(payload); }
            catch (DecoderFallbackException) { return false; }

            var index = 0;
            SkipWhitespace(json, ref index);
            if (!Consume(json, ref index, '{')) return false;
            var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
            var parsedStringFields = new HashSet<string>(StringComparer.Ordinal);
            SkipWhitespace(json, ref index);
            if (Consume(json, ref index, '}'))
            {
                SkipWhitespace(json, ref index);
                if (index != json.Length) return false;
                fields = parsed;
                return true;
            }

            var closed = false;
            while (index < json.Length)
            {
                string key;
                string value;
                if (!ReadString(json, ref index, out key)) return false;
                SkipWhitespace(json, ref index);
                if (!Consume(json, ref index, ':')) return false;
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == '"')
                {
                    if (key == "contract_version") return false;
                    if (!ReadString(json, ref index, out value)) return false;
                    parsedStringFields.Add(key);
                }
                else
                {
                    var start = index;
                    while (index < json.Length && json[index] != ',' && json[index] != '}' && !IsJsonWhitespace(json[index])) index++;
                    if (start == index) return false;
                    value = json.Substring(start, index - start);
                    if (key != "contract_version" && key != "volume") return false;
                    if (key == "contract_version" && value != "2") return false;
                }

                if (parsed.ContainsKey(key)) return false;
                parsed.Add(key, value);
                SkipWhitespace(json, ref index);
                if (Consume(json, ref index, '}')) { closed = true; break; }
                if (!Consume(json, ref index, ',')) return false;
                SkipWhitespace(json, ref index);
            }

            SkipWhitespace(json, ref index);
            if (!closed || index != json.Length) return false;
            fields = parsed;
            stringFields = parsedStringFields;
            return true;
        }

        private static bool ReadString(string json, ref int index, out string value)
        {
            value = null;
            if (!Consume(json, ref index, '"')) return false;
            var result = new StringBuilder();
            while (index < json.Length)
            {
                var c = json[index++];
                if (c == '"') { value = result.ToString(); return true; }
                if (c < 0x20) return false;
                if (c != '\\') { result.Append(c); continue; }
                if (index >= json.Length) return false;
                var escaped = json[index++];
                switch (escaped)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (index + 4 > json.Length) return false;
                        var code = 0;
                        for (var digitIndex = 0; digitIndex < 4; digitIndex++)
                        {
                            var digit = HexDigitValue(json[index + digitIndex]);
                            if (digit < 0) return false;
                            code = (code << 4) | digit;
                        }
                        result.Append((char)code);
                        index += 4;
                        break;
                    default: return false;
                }
            }
            return false;
        }

        private static int HexDigitValue(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }

        private static bool IsKnownCommand(string value) => value == LessonCommandKindV2.Skip || value == LessonCommandKindV2.Pause ||
            value == LessonCommandKindV2.Resume || value == LessonCommandKindV2.VerbalHint || value == LessonCommandKindV2.VisualHint ||
            value == LessonCommandKindV2.SetVolume || value == LessonCommandKindV2.SpeakScript;

        private static bool Nonblank(string value) => !string.IsNullOrWhiteSpace(value);

        private static bool IsJsonNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var index = value[0] == '-' ? 1 : 0;
            if (index >= value.Length) return false;
            if (value[index] == '0')
            {
                index++;
                if (index < value.Length && IsAsciiDigit(value[index])) return false;
            }
            else
            {
                if (value[index] < '1' || value[index] > '9') return false;
                while (index < value.Length && IsAsciiDigit(value[index])) index++;
            }

            if (index < value.Length && value[index] == '.')
            {
                index++;
                var fractionStart = index;
                while (index < value.Length && IsAsciiDigit(value[index])) index++;
                if (index == fractionStart) return false;
            }

            if (index < value.Length && (value[index] == 'e' || value[index] == 'E'))
            {
                index++;
                if (index < value.Length && (value[index] == '+' || value[index] == '-')) index++;
                var exponentStart = index;
                while (index < value.Length && IsAsciiDigit(value[index])) index++;
                if (index == exponentStart) return false;
            }

            return index == value.Length;
        }

        private static bool IsAsciiDigit(char value) => value >= '0' && value <= '9';

        private static bool ContainsFirebasePathKeyCharacter(string value) => value.IndexOfAny(new[] { '/', '.', '#', '$', '[', ']' }) >= 0;

        private static void SkipWhitespace(string value, ref int index)
        {
            while (index < value.Length && IsJsonWhitespace(value[index])) index++;
        }

        private static bool IsJsonWhitespace(char value) => value == ' ' || value == '\t' || value == '\r' || value == '\n';

        private static bool Consume(string value, ref int index, char expected)
        {
            if (index >= value.Length || value[index] != expected) return false;
            index++;
            return true;
        }
    }
}
