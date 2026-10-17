using System;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache.Tests
{
    public sealed class TtsCacheContractV1Tests
    {
        [Test]
        public void DefaultsRoundTripAndProvideNpcMappings()
        {
            var defaults = TtsCacheContractsV1.DefaultTtsConfig();
            var parsed = TtsCacheContractsV1.ParseTtsConfig(JsonUtility.ToJson(defaults));
            Assert.AreEqual(1500, parsed.remote_cache_wait_ms);
            Assert.AreEqual("peer-npc", parsed.npc_profiles[1].profile_id);
        }

        [Test]
        public void ArtifactPathUsesEpochAndRejectsUnsafeComponents()
        {
            var key = new string('a', 64);
            Assert.AreEqual("tts-cache/v1/teacher-npc/" + key + "/r2.wav", TtsCacheContractsV1.ArtifactPath("teacher-npc", key, 2));
            Assert.Throws<CacheValidationException>(() => TtsCacheContractsV1.ArtifactPath("../profile", key, 0));
        }

        [Test]
        public void ValidatesCanonicalWavAndComputesContentMetadata()
        {
            var wav = MakeWav(new byte[] { 1, 0, 2, 0 });
            var metadata = TtsCacheContractsV1.ValidateWav(wav);
            Assert.AreEqual(48, metadata.byte_size);
            Assert.AreEqual(2, metadata.sample_count);
            Assert.AreEqual(0, metadata.duration_ms);
            Assert.AreEqual(64, metadata.sha256.Length);
            wav[20] = 3;
            Assert.Throws<CacheValidationException>(() => TtsCacheContractsV1.ValidateWav(wav));
        }

        [Test]
        public void ParsesTypedCachePacketAndRejectsWrongVersion()
        {
            var packet = "{\"contract_version\":1,\"event\":\"FIXED_AUDIO_STREAM_REQUEST\",\"session_id\":\"s\",\"run_id\":\"r\",\"activation_id\":\"a\",\"invocation_id\":\"i\",\"npc_binding_id\":\"teacher-npc\",\"kind\":\"manual_hint\",\"owner_revision\":1,\"text\":\"Xin chào.\",\"profile_id\":\"teacher-npc\",\"key\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"rejection_epoch\":0}";
            Assert.AreEqual("FIXED_AUDIO_STREAM_REQUEST", TtsCacheContractsV1.ParseCachePacket(packet).@event);
            Assert.Throws<CacheValidationException>(() => TtsCacheContractsV1.ParseCachePacket(packet.Replace("version\":1", "version\":2")));
        }

        [Test]
        public void AcceptsOnlyTheExactFixedAudioHelloEnvelope()
        {
            const string hello = "{\"contract_version\":1,\"event\":\"FIXED_AUDIO_HELLO\",\"session_id\":\"session\",\"agent_instance_id\":\"boot-1\"}";
            Assert.AreEqual("boot-1", TtsCacheContractsV1.ParseCachePacket(hello).agent_instance_id);
            Assert.Throws<CacheValidationException>(() => TtsCacheContractsV1.ParseCachePacket(hello.Replace("}", ",\"run_id\":\"run\"}")));
        }

        [Test]
        public void RejectsExplicitNullForEveryRequiredInvocationField()
        {
            var cases = new[]
            {
                new[] { "FIXED_AUDIO_STREAM_REQUEST", "owner_revision" },
                new[] { "FIXED_AUDIO_STREAM_REQUEST", "text" },
                new[] { "FIXED_AUDIO_STREAM_REQUEST", "profile_id" },
                new[] { "FIXED_AUDIO_STREAM_REQUEST", "key" },
                new[] { "FIXED_AUDIO_STREAM_REQUEST", "rejection_epoch" },
                new[] { "FIXED_AUDIO_CANCEL", "owner_revision" },
                new[] { "FIXED_AUDIO_CANCEL", "reason" },
                new[] { "FIXED_AUDIO_ACTIVITY", "owner_revision" },
                new[] { "FIXED_AUDIO_ACTIVITY", "activity_sequence" },
                new[] { "FIXED_AUDIO_ACTIVITY", "reason" },
                new[] { "FIXED_AUDIO_DONE", "owner_revision" },
                new[] { "FIXED_AUDIO_DONE", "agent_instance_id" },
                new[] { "FIXED_AUDIO_DONE", "reason" },
                new[] { "FIXED_AUDIO_REMINDER_REQUEST", "agent_instance_id" },
                new[] { "FIXED_AUDIO_REMINDER_REQUEST", "text" },
                new[] { "FIXED_AUDIO_REMINDER_REQUEST", "profile_id" },
                new[] { "FIXED_AUDIO_REMINDER_REQUEST", "key" },
                new[] { "FIXED_AUDIO_REMINDER_CANCEL", "agent_instance_id" },
                new[] { "FIXED_AUDIO_REMINDER_CANCEL", "reason" },
            };
            foreach (var testCase in cases)
            {
                var packet = InvocationPacket(testCase[0]);
                packet[testCase[1]] = JValue.CreateNull();
                Assert.Throws<CacheValidationException>(() =>
                    TtsCacheContractsV1.ParseCachePacket(packet.ToString(Newtonsoft.Json.Formatting.None)),
                    testCase[0] + "." + testCase[1] + " must reject JSON null");
            }

            const string reconcile = "{\"contract_version\":1,\"event\":\"FIXED_AUDIO_RECONCILE\",\"session_id\":\"s\",\"run_id\":\"r\",\"agent_instance_id\":\"boot\",\"active_activation_ids\":[],\"current_activity\":null}";
            Assert.DoesNotThrow(() => TtsCacheContractsV1.ParseCachePacket(reconcile));
        }

        [Test]
        public void EnforcesSharedValidAndInvalidPacketFixtures()
        {
            var fixture = TtsCacheKeyV1Tests.Fixture();
            foreach (var packet in (JArray)fixture["valid_packets"])
                Assert.DoesNotThrow(() => TtsCacheContractsV1.ParseCachePacket(packet.ToString(Newtonsoft.Json.Formatting.None)));
            foreach (var packet in (JArray)fixture["invalid_packets"])
                Assert.Throws<CacheValidationException>(() => TtsCacheContractsV1.ParseCachePacket(packet.ToString(Newtonsoft.Json.Formatting.None)));
        }

        static byte[] MakeWav(byte[] pcm)
        {
            var bytes = new byte[44 + pcm.Length];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0); Write32(bytes, 4, bytes.Length - 8);
            Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(bytes, 8); Write32(bytes, 16, 16);
            Write16(bytes, 20, 1); Write16(bytes, 22, 1); Write32(bytes, 24, 24000); Write32(bytes, 28, 48000);
            Write16(bytes, 32, 2); Write16(bytes, 34, 16); Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
            Write32(bytes, 40, pcm.Length); pcm.CopyTo(bytes, 44); return bytes;
        }
        static void Write16(byte[] bytes, int at, int value) { bytes[at] = (byte)value; bytes[at + 1] = (byte)(value >> 8); }
        static void Write32(byte[] bytes, int at, int value) { bytes[at] = (byte)value; bytes[at + 1] = (byte)(value >> 8); bytes[at + 2] = (byte)(value >> 16); bytes[at + 3] = (byte)(value >> 24); }

        static JObject InvocationPacket(string eventName)
        {
            var reminder = eventName.Contains("REMINDER");
            var packet = new JObject
            {
                ["contract_version"] = 1,
                ["event"] = eventName,
                ["session_id"] = "s",
                ["run_id"] = "r",
                ["activation_id"] = "a",
                ["invocation_id"] = "i",
                ["npc_binding_id"] = "teacher-npc",
                ["kind"] = reminder ? "automatic_reminder" : "manual_hint"
            };
            switch (eventName)
            {
                case "FIXED_AUDIO_STREAM_REQUEST":
                    packet["owner_revision"] = 1; packet["text"] = "hello";
                    packet["profile_id"] = "teacher-npc"; packet["key"] = new string('a', 64);
                    packet["rejection_epoch"] = 0; break;
                case "FIXED_AUDIO_CANCEL": packet["owner_revision"] = 1; packet["reason"] = "skip"; break;
                case "FIXED_AUDIO_ACTIVITY":
                    packet["owner_revision"] = 1; packet["activity_sequence"] = 1;
                    packet["phase"] = "preparing"; packet["source"] = "none";
                    packet["status"] = "PENDING"; packet["reason"] = ""; break;
                case "FIXED_AUDIO_DONE":
                    packet["owner_revision"] = 1; packet["agent_instance_id"] = "boot";
                    packet["status"] = "FAILED"; packet["reason"] = "failure"; break;
                case "FIXED_AUDIO_REMINDER_REQUEST":
                    packet["agent_instance_id"] = "boot"; packet["text"] = "hello";
                    packet["profile_id"] = "teacher-npc"; packet["key"] = new string('a', 64); break;
                case "FIXED_AUDIO_REMINDER_CANCEL":
                    packet["agent_instance_id"] = "boot"; packet["reason"] = "skip"; break;
                default: throw new ArgumentOutOfRangeException(nameof(eventName));
            }
            return packet;
        }
    }
}
