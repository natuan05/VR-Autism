using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache
{
    [Serializable]
    public sealed class TtsProfileV1
    {
        public string profile_id, provider, model, language, voice_name;
        public double speaking_rate;
        public string provider_encoding;
        public int sample_rate, channels;
        public string audio_format;
    }

    [Serializable]
    public sealed class NpcProfileV1 { public string npc_binding_id, profile_id; }

    [Serializable]
    public sealed class SessionTtsConfigV1
    {
        public int schema_version, remote_cache_wait_ms;
        public TtsProfileV1[] profiles;
        public NpcProfileV1[] npc_profiles;
    }

    [Serializable]
    public sealed class ArtifactV1
    {
        public string profile_id, key;
        public long rejection_epoch;
        public string object_path, generation, sha256;
        public int byte_size, sample_count, duration_ms;
        public string format;
    }

    [Serializable]
    public sealed class InvocationV1
    {
        public string session_id, run_id, activation_id, invocation_id, npc_binding_id, kind;
    }

    [Serializable]
    public sealed class CachePacketV1
    {
        public int contract_version;
        public string @event, session_id, run_id, activation_id, invocation_id, npc_binding_id, kind;
        public long owner_revision, activity_sequence, rejection_epoch;
        public string agent_instance_id, text, profile_id, key, reason, phase, source, status;
        public string[] active_activation_ids, completed_opening_activation_ids;
        public CachePacketV1 current_activity;
        public string object_path, generation, sha256, format;
        public int byte_size, sample_count, duration_ms;
    }

    public sealed class CacheValidationException : Exception
    {
        public CacheValidationException(string message) : base(message) { }
    }

    public struct WavMetadataV1
    {
        public int byte_size, sample_count, duration_ms;
        public string sha256;
    }

    public static class TtsCacheContractsV1
    {
        public const string Topic = "lesson-graph-v2.audio-cache";
        public const string AudioFormat = "wav-pcm16-v1";
        public const int MaxPcmBytes = 4194304;
        public const int MaxWavBytes = MaxPcmBytes + 44;
        public const int MaxPacketBytes = 16384;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly Regex ProfileId = new Regex("^[a-z0-9][a-z0-9-]{0,63}$");
        static readonly Regex KeyHex = new Regex("^[a-f0-9]{64}$");

        static void Require(bool condition, string message)
        {
            if (!condition) throw new CacheValidationException(message);
        }

        static string ValidateText(string value)
        {
            Require(!String.IsNullOrWhiteSpace(value), "nonblank text required");
            try { Utf8.GetByteCount(value); }
            catch (EncoderFallbackException) { throw new CacheValidationException("invalid Unicode"); }
            return value;
        }

        static string CanonicalRate(double rate)
        {
            Require(!Double.IsNaN(rate) && !Double.IsInfinity(rate) && rate >= 0.25 && rate <= 2,
                "invalid speaking rate");
            var raw = rate.ToString("R", CultureInfo.InvariantCulture);
            decimal parsed;
            Require(Decimal.TryParse(raw, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out parsed) && Decimal.Round(parsed, 6) == parsed,
                "unsupported speaking rate precision");
            return parsed == 0 ? "0" : parsed.ToString("G29", CultureInfo.InvariantCulture);
        }

        static JObject ParseObject(string json)
        {
            try { return JObject.Parse(json); }
            catch (Exception e) { throw new CacheValidationException("invalid JSON: " + e.Message); }
        }

        static void RequireExactFields(JObject value, params string[] fields)
        {
            Require(value != null && value.Properties().Count() == fields.Length &&
                    fields.All(field => value.Property(field) != null), "fields do not match contract");
        }

        static long ReadInteger(JToken token, long min = 0, long max = 9007199254740991L)
        {
            Require(token != null && token.Type == JTokenType.Integer, "integer required");
            long value;
            try { value = token.Value<long>(); }
            catch { throw new CacheValidationException("invalid integer"); }
            Require(value >= min && value <= max, "invalid integer");
            return value;
        }

        static string ReadString(JObject value, string field)
        {
            var token = value[field];
            Require(token != null && token.Type == JTokenType.String, "string required: " + field);
            return (string)token;
        }

        static JObject WithoutFields(JObject value, params string[] excluded)
        {
            var result = new JObject();
            foreach (var property in value.Properties())
                if (!excluded.Contains(property.Name)) result.Add(property.Name, property.Value);
            return result;
        }

        static TtsProfileV1 NewProfile(string id, string voice)
        {
            return new TtsProfileV1
            {
                profile_id = id, provider = "google-cloud-tts", model = "chirp_3", language = "vi-VN",
                voice_name = voice, speaking_rate = 1, provider_encoding = "OGG_OPUS", sample_rate = 24000,
                channels = 1, audio_format = AudioFormat
            };
        }

        public static void ValidateProfile(TtsProfileV1 profile)
        {
            Require(profile != null && ProfileId.IsMatch(ValidateText(profile.profile_id)), "unsafe profile ID");
            CanonicalRate(profile.speaking_rate);
            Require(profile.provider == "google-cloud-tts" && profile.model == "chirp_3" &&
                    profile.language == "vi-VN" && profile.provider_encoding == "OGG_OPUS" &&
                    profile.audio_format == AudioFormat && profile.sample_rate == 24000 && profile.channels == 1,
                "unsupported profile configuration");
            Require(Regex.IsMatch(ValidateText(profile.voice_name), "^vi-VN-Chirp3-HD-[A-Za-z]+$"),
                "unsupported voice");
        }

        public static TtsProfileV1 ParseTtsProfile(string json)
        {
            var value = ParseObject(json);
            RequireExactFields(value, "profile_id", "provider", "model", "language", "voice_name",
                "speaking_rate", "provider_encoding", "sample_rate", "channels", "audio_format");
            foreach (var field in new[] { "profile_id", "provider", "model", "language", "voice_name", "provider_encoding", "audio_format" })
                ReadString(value, field);
            Require(value["speaking_rate"].Type == JTokenType.Float || value["speaking_rate"].Type == JTokenType.Integer,
                "invalid speaking rate");
            ReadInteger(value["sample_rate"], 24000, 24000);
            ReadInteger(value["channels"], 1, 1);
            var profile = value.ToObject<TtsProfileV1>();
            ValidateProfile(profile);
            return profile;
        }

        public static SessionTtsConfigV1 DefaultTtsConfig()
        {
            var teacher = NewProfile("teacher-npc", "vi-VN-Chirp3-HD-Aoede");
            var peer = NewProfile("peer-npc", "vi-VN-Chirp3-HD-Puck");
            return new SessionTtsConfigV1
            {
                schema_version = 1,
                remote_cache_wait_ms = 1500,
                profiles = new[] { teacher, peer },
                npc_profiles = new[]
                {
                    new NpcProfileV1 { npc_binding_id = teacher.profile_id, profile_id = teacher.profile_id },
                    new NpcProfileV1 { npc_binding_id = peer.profile_id, profile_id = peer.profile_id }
                }
            };
        }

        public static SessionTtsConfigV1 ParseTtsConfig(string json)
        {
            var value = ParseObject(json);
            RequireExactFields(value, "schema_version", "remote_cache_wait_ms", "profiles", "npc_profiles");
            ReadInteger(value["schema_version"], 1, 1);
            ReadInteger(value["remote_cache_wait_ms"], 0, 10000);
            Require(value["profiles"].Type == JTokenType.Array && value["npc_profiles"].Type == JTokenType.Array,
                "profile arrays required");
            foreach (var profile in (JArray)value["profiles"])
                ParseTtsProfile(profile.ToString(Formatting.None));
            foreach (var mapping in (JArray)value["npc_profiles"])
            {
                RequireExactFields(mapping as JObject, "npc_binding_id", "profile_id");
                ReadString((JObject)mapping, "npc_binding_id");
                ReadString((JObject)mapping, "profile_id");
            }

            var config = value.ToObject<SessionTtsConfigV1>();
            var profileIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in config.profiles)
                Require(profileIds.Add(profile.profile_id), "duplicate profile");
            var npcIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in config.npc_profiles)
                Require(npcIds.Add(ValidateText(mapping.npc_binding_id)) &&
                        profileIds.Contains(ValidateText(mapping.profile_id)), "duplicate or unknown mapping");
            return config;
        }

        public static string ComputeTtsCacheKey(string finalText, TtsProfileV1 profile)
        {
            ValidateProfile(profile);
            ValidateText(finalText);
            var fields = new[]
            {
                "tts-cache-key-v1", profile.profile_id, profile.provider, profile.model, profile.language,
                profile.voice_name, CanonicalRate(profile.speaking_rate), profile.provider_encoding,
                profile.sample_rate.ToString(CultureInfo.InvariantCulture),
                profile.channels.ToString(CultureInfo.InvariantCulture), profile.audio_format, finalText
            };
            using (var stream = new MemoryStream())
            {
                foreach (var field in fields)
                {
                    var bytes = Utf8.GetBytes(field);
                    var length = (uint)bytes.Length;
                    stream.WriteByte((byte)(length >> 24));
                    stream.WriteByte((byte)(length >> 16));
                    stream.WriteByte((byte)(length >> 8));
                    stream.WriteByte((byte)length);
                    stream.Write(bytes, 0, bytes.Length);
                }
                using (var sha = SHA256.Create())
                    return String.Concat(sha.ComputeHash(stream.ToArray())
                        .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        public static string ArtifactPath(string profileId, string key, long rejectionEpoch)
        {
            Require(ProfileId.IsMatch(ValidateText(profileId)) && KeyHex.IsMatch(ValidateText(key)) && rejectionEpoch >= 0,
                "unsafe cache path");
            return "tts-cache/v1/" + profileId + "/" + key + "/r" +
                   rejectionEpoch.ToString(CultureInfo.InvariantCulture) + ".wav";
        }

        public static ArtifactV1 ParseArtifact(string json)
        {
            var value = ParseObject(json);
            RequireExactFields(value, "profile_id", "key", "rejection_epoch", "object_path", "generation",
                "sha256", "byte_size", "sample_count", "duration_ms", "format");
            var epoch = ReadInteger(value["rejection_epoch"]);
            ReadInteger(value["byte_size"], 46, MaxWavBytes);
            var samples = ReadInteger(value["sample_count"], 1, MaxPcmBytes / 2);
            ReadInteger(value["duration_ms"]);
            foreach (var field in new[] { "profile_id", "key", "object_path", "generation", "sha256", "format" })
                ReadString(value, field);
            var artifact = value.ToObject<ArtifactV1>();
            Require(artifact.object_path == ArtifactPath(artifact.profile_id, artifact.key, epoch),
                "artifact path mismatch");
            Require(Regex.IsMatch(artifact.generation, "^[1-9][0-9]*$") && KeyHex.IsMatch(artifact.sha256) &&
                    artifact.format == AudioFormat, "invalid artifact metadata");
            Require(artifact.byte_size == samples * 2 + 44 && artifact.duration_ms == samples * 1000 / 24000,
                "artifact size/duration mismatch");
            return artifact;
        }

        public static CachePacketV1 ParseCachePacket(string json)
        {
            Require(json != null && Utf8.GetByteCount(json) <= MaxPacketBytes, "packet too large");
            var value = ParseObject(json);
            ReadInteger(value["contract_version"], 1, 1);
            var eventName = ReadString(value, "event");
            var common = new[] { "contract_version", "event" };
            if (eventName == "FIXED_AUDIO_HELLO") ValidateHelloPacket(value, common);
            else if (eventName == "AUDIO_CACHE_READY") ValidateReadyPacket(value, common);
            else if (eventName == "FIXED_AUDIO_RECONCILE") ValidateReconcilePacket(value, common);
            else ValidateInvocationPacket(value, common, eventName);
            return value.ToObject<CachePacketV1>();
        }

        static void ValidateHelloPacket(JObject value, string[] common)
        {
            RequireExactFields(value, common.Concat(new[] { "session_id", "agent_instance_id" }).ToArray());
            ValidateText(ReadString(value, "session_id"));
            ValidateText(ReadString(value, "agent_instance_id"));
        }

        static void ValidateReadyPacket(JObject value, string[] common)
        {
            RequireExactFields(value, common.Concat(new[]
            {
                "session_id", "agent_instance_id", "profile_id", "key", "rejection_epoch", "object_path",
                "generation", "sha256", "byte_size", "sample_count", "duration_ms", "format"
            }).ToArray());
            ValidateText(ReadString(value, "session_id"));
            ValidateText(ReadString(value, "agent_instance_id"));
            ParseArtifact(WithoutFields(value, "contract_version", "event", "session_id", "agent_instance_id")
                .ToString(Formatting.None));
        }

        static void ValidateReconcilePacket(JObject value, string[] common)
        {
            RequireExactFields(value, common.Concat(new[]
            {
                "session_id", "run_id", "agent_instance_id", "active_activation_ids", "current_activity"
            }).Concat(value.Property("completed_opening_activation_ids") != null ? new[] { "completed_opening_activation_ids" } : new string[0]).ToArray());
            var sessionId = ValidateText(ReadString(value, "session_id"));
            var runId = ValidateText(ReadString(value, "run_id"));
            ValidateText(ReadString(value, "agent_instance_id"));
            var activations = value["active_activation_ids"];
            Require(activations.Type == JTokenType.Array && ((JArray)activations).Count <= 1024,
                "invalid activations");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var activation in (JArray)activations)
                Require(seen.Add(ValidateText(activation.Type == JTokenType.String ? (string)activation : null)),
                    "duplicate activation");
            if (value.Property("completed_opening_activation_ids") != null)
            {
                var completed = value["completed_opening_activation_ids"] as JArray;
                Require(completed != null && completed.Count <= 1024, "invalid completed opening list");
                var unique = new HashSet<string>(StringComparer.Ordinal);
                foreach (var activation in completed)
                {
                    var id = ValidateText(activation.Type == JTokenType.String ? (string)activation : null);
                    Require(seen.Contains(id) && unique.Add(id), "invalid completed opening identity");
                }
            }
            if (value["current_activity"].Type == JTokenType.Null) return;
            var activity = ParseCachePacket(value["current_activity"].ToString(Formatting.None));
            Require(activity.@event == "FIXED_AUDIO_ACTIVITY" && activity.session_id == sessionId &&
                    activity.run_id == runId, "invalid current activity");
        }

        static void ValidateInvocationPacket(JObject value, string[] common, string eventName)
        {
            var invocation = new[] { "session_id", "run_id", "activation_id", "invocation_id", "npc_binding_id", "kind" };
            string[] extra;
            switch (eventName)
            {
                case "FIXED_AUDIO_STREAM_REQUEST": extra = new[] { "owner_revision", "text", "profile_id", "key", "rejection_epoch" }; break;
                case "FIXED_AUDIO_CANCEL": extra = new[] { "owner_revision", "reason" }; break;
                case "FIXED_AUDIO_ACTIVITY": extra = new[] { "owner_revision", "activity_sequence", "phase", "source", "status", "reason" }; break;
                case "FIXED_AUDIO_DONE": extra = new[] { "owner_revision", "agent_instance_id", "status", "reason" }; break;
                case "FIXED_AUDIO_REMINDER_REQUEST": extra = new[] { "agent_instance_id", "text", "profile_id", "key" }; break;
                case "FIXED_AUDIO_REMINDER_CANCEL": extra = new[] { "agent_instance_id", "reason" }; break;
                default: throw new CacheValidationException("unknown cache event");
            }
            RequireExactFields(value, common.Concat(invocation).Concat(extra).ToArray());
            foreach (var field in invocation) ValidateText(ReadString(value, field));
            var kind = ReadString(value, "kind");
            Require(new[] { "dialogue", "opening", "manual_hint", "automatic_reminder" }.Contains(kind),
                "invalid invocation kind");
            if (eventName.Contains("REMINDER")) Require(kind == "automatic_reminder", "invalid reminder kind");
            foreach (var field in new[] { "owner_revision", "activity_sequence", "rejection_epoch" })
                if (value.Property(field) != null) ReadInteger(value[field]);
            if (value.Property("agent_instance_id") != null)
                ValidateText(ReadString(value, "agent_instance_id"));
            if (value.Property("text") != null)
                Require(ValidateText(ReadString(value, "text")).Length <= 500, "text too long");
            if (value.Property("profile_id") != null)
                Require(ProfileId.IsMatch(ValidateText(ReadString(value, "profile_id"))), "unsafe profile ID");
            if (value.Property("key") != null) Require(KeyHex.IsMatch(ReadString(value, "key")), "invalid key");
            if (value.Property("reason") != null) ReadString(value, "reason");
            ValidateEventState(value, eventName);
        }

        static void ValidateEventState(JObject value, string eventName)
        {
            if (eventName == "FIXED_AUDIO_CANCEL" || eventName == "FIXED_AUDIO_REMINDER_CANCEL")
                Require(new[] { "skip", "scope_changed", "session_ended", "user_speaking", "superseded", "reconfigured" }
                    .Contains(ReadString(value, "reason")), "invalid cancellation reason");
            if (eventName == "FIXED_AUDIO_DONE")
                Require(new[] { "SUCCESS", "FAILED", "CANCELLED" }.Contains(ReadString(value, "status")),
                    "invalid terminal status");
            if (eventName != "FIXED_AUDIO_ACTIVITY") return;
            var phase = ReadString(value, "phase");
            var source = ReadString(value, "source");
            var status = ReadString(value, "status");
            Require(new[] { "preparing", "local_selected", "stream_selected", "playing", "terminal" }.Contains(phase) &&
                    new[] { "none", "local", "stream" }.Contains(source) &&
                    new[] { "PENDING", "SUCCESS", "FAILED", "CANCELLED", "SKIPPED" }.Contains(status),
                "invalid activity");
            Require((phase == "terminal") != (status == "PENDING") &&
                    !(phase == "preparing" && source != "none") &&
                    !(phase == "local_selected" && source != "local") &&
                    !(phase == "stream_selected" && source != "stream") &&
                    !(phase == "playing" && source == "none"), "inconsistent activity");
        }

        static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        }

        static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | bytes[offset + 1] << 8);
        }

        static bool HasTag(byte[] bytes, int offset, string tag)
        {
            for (var i = 0; i < tag.Length; i++)
                if (bytes[offset + i] != (byte)tag[i]) return false;
            return true;
        }

        public static WavMetadataV1 ValidateWav(byte[] bytes)
        {
            Require(bytes != null && bytes.Length >= 46 && bytes.Length <= MaxWavBytes, "invalid WAV size");
            var pcmBytes = bytes.Length - 44;
            Require(HasTag(bytes, 0, "RIFF") && HasTag(bytes, 8, "WAVE") && HasTag(bytes, 12, "fmt ") &&
                    HasTag(bytes, 36, "data") && ReadUInt32(bytes, 4) == bytes.Length - 8 &&
                    ReadUInt32(bytes, 16) == 16 && ReadUInt16(bytes, 20) == 1 && ReadUInt16(bytes, 22) == 1 &&
                    ReadUInt32(bytes, 24) == 24000 && ReadUInt32(bytes, 28) == 48000 &&
                    ReadUInt16(bytes, 32) == 2 && ReadUInt16(bytes, 34) == 16 &&
                    ReadUInt32(bytes, 40) == pcmBytes && pcmBytes > 0 && pcmBytes % 2 == 0,
                "noncanonical PCM16 WAV");
            using (var sha = SHA256.Create())
                return new WavMetadataV1
                {
                    byte_size = bytes.Length,
                    sample_count = pcmBytes / 2,
                    duration_ms = (int)((long)(pcmBytes / 2) * 1000 / 24000),
                    sha256 = String.Concat(sha.ComputeHash(bytes)
                        .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)))
                };
        }
    }
}
