using System;
using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache.Tests
{
    public sealed class TtsCacheKeyV1Tests
    {
        [Test]
        public void MatchesSharedTeacherVectorsAndCanonicalRate()
        {
            var profile = TtsCacheContractsV1.DefaultTtsConfig().profiles[0];
            Assert.AreEqual("3e4c476e9b8e3939138a75614e2a65b9be72d508077284ecfeb7df248717bbe8", TtsCacheKeyV1.Compute("Xin chào.", profile));
            profile.speaking_rate = 0.9;
            Assert.AreEqual("892436b94b4bfbc26c3d5209aa12c4ab622ed00ed0fe75cb6729e19dd4e958d8", TtsCacheKeyV1.Compute("Xin chào.", profile));
            profile.speaking_rate = 1.0;
            Assert.AreEqual("3e4c476e9b8e3939138a75614e2a65b9be72d508077284ecfeb7df248717bbe8", TtsCacheKeyV1.Compute("Xin chào.", profile));
        }

        [Test]
        public void PreservesUnicodeNormalizationAndWhitespaceInIdentity()
        {
            var profile = TtsCacheContractsV1.DefaultTtsConfig().profiles[0];
            Assert.AreNotEqual(TtsCacheKeyV1.Compute("é", profile), TtsCacheKeyV1.Compute("e\u0301", profile));
            Assert.AreNotEqual(TtsCacheKeyV1.Compute("Hello  world!", profile), TtsCacheKeyV1.Compute("Hello world!", profile));
        }

        [Test]
        public void RejectsRatePrecisionBeyondSixDecimals()
        {
            var profile = TtsCacheContractsV1.DefaultTtsConfig().profiles[0];
            profile.speaking_rate = 0.1234567;
            Assert.Throws<CacheValidationException>(() => TtsCacheKeyV1.Compute("hello", profile));
            profile.speaking_rate = 0.9000000000000001;
            Assert.Throws<CacheValidationException>(() => TtsCacheKeyV1.Compute("hello", profile));
        }

        [Test]
        public void MatchesEverySharedFixtureKey()
        {
            var fixture = Fixture();
            foreach (var vector in (JArray)fixture["keys"])
            {
                var profile = TtsCacheContractsV1.ParseTtsProfile(vector["profile"].ToString(Newtonsoft.Json.Formatting.None));
                Assert.AreEqual((string)vector["key"], TtsCacheKeyV1.Compute((string)vector["text"], profile));
            }
        }

        internal static JObject Fixture()
        {
            var path = Path.Combine(Application.dataPath, "Project/Scripts/Gameplay/LessonGraphV2/AudioCache/Tests/Editor/Fixtures/tts-cache-v1.json");
            return JObject.Parse(File.ReadAllText(path));
        }
    }
}
