using System;
using System.Collections.Generic;
using System.Text;

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
            if (!TryReadObject(payload, out fields) || fields.Count != RequiredFields.Length) return false;
            for (var i = 0; i < RequiredFields.Length; i++)
                if (!fields.ContainsKey(RequiredFields[i])) return false;

            int version;
            if (!int.TryParse(fields["contract_version"], out version) || version != LessonRemoteContractV2.ContractVersion) return false;
            if (fields["event"] != LessonRemoteContractV2.CommandEvent) return false;
            if (!IsKnownCommand(fields["command"])) return false;
            if (!Nonblank(fields["command_id"]) || !Nonblank(fields["session_id"]) ||
                !Nonblank(fields["run_id"]) || !Nonblank(fields["node_id"]) || !Nonblank(fields["activation_id"])) return false;
            if (ContainsFirebasePathKeyCharacter(fields["session_id"])) return false;

            var hint = fields["command"] == LessonCommandKindV2.VerbalHint || fields["command"] == LessonCommandKindV2.VisualHint;
            if (hint ? !Nonblank(fields["binding_id"]) : fields["binding_id"].Length != 0) return false;

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
                binding_id = fields["binding_id"]
            };
            reason = LessonCommandReasonV2.None;
            return true;
        }

        private static bool TryReadObject(byte[] payload, out Dictionary<string, string> fields)
        {
            fields = null;
            string json;
            try { json = new UTF8Encoding(false, true).GetString(payload); }
            catch (DecoderFallbackException) { return false; }

            var index = 0;
            SkipWhitespace(json, ref index);
            if (!Consume(json, ref index, '{')) return false;
            var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
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
                }
                else
                {
                    var start = index;
                    while (index < json.Length && json[index] != ',' && json[index] != '}' && !IsJsonWhitespace(json[index])) index++;
                    if (start == index) return false;
                    value = json.Substring(start, index - start);
                    if (key != "contract_version" || value != "2") return false;
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
            value == LessonCommandKindV2.Resume || value == LessonCommandKindV2.VerbalHint || value == LessonCommandKindV2.VisualHint;

        private static bool Nonblank(string value) => !string.IsNullOrWhiteSpace(value);

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
