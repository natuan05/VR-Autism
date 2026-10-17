using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache.Tests
{
    public sealed class TtsCacheLocalDevelopmentV1Tests
    {
        private const string PreferenceKey = "VRAutism.TtsCache.UseLocalDevelopmentBackendV1";
        private const string LocalBackendUrl = "http://127.0.0.1:3000";
        private const string ProductionBackendUrl = "https://api.example.test";
        private bool _hadPreference;
        private bool _previousPreference;

        [SetUp]
        public void SetUp()
        {
            _hadPreference = EditorPrefs.HasKey(PreferenceKey);
            _previousPreference = EditorPrefs.GetBool(PreferenceKey, false);
            EditorPrefs.DeleteKey(PreferenceKey);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadPreference) EditorPrefs.SetBool(PreferenceKey, _previousPreference);
            else EditorPrefs.DeleteKey(PreferenceKey);
        }

        [Test]
        public void LocalOverrideChangesOnlyTheEditorEffectiveBackendUrl()
        {
            var resolve = GetUrlPolicyMethod("ResolveBackendUrl", typeof(string));
            Assert.AreEqual(ProductionBackendUrl, resolve.Invoke(null, new object[] { ProductionBackendUrl }));

            EditorPrefs.SetBool(PreferenceKey, true);
            Assert.AreEqual(LocalBackendUrl, resolve.Invoke(null, new object[] { ProductionBackendUrl }));
            EditorPrefs.DeleteKey(PreferenceKey);
            Assert.AreEqual(ProductionBackendUrl, resolve.Invoke(null, new object[] { ProductionBackendUrl }),
                "disabling the override restores the configured production URL");
        }

        [TestCase("http://localhost:3000")]
        [TestCase("http://LOCALHOST:3000/base")]
        [TestCase("http://127.0.0.1:3000")]
        public void EditorBackendAcceptsOnlyExplicitLoopbackHttpOrigins(string url)
        {
            Assert.IsTrue((bool)GetUrlPolicyMethod("IsAllowedBackendUrl", typeof(string), typeof(bool))
                .Invoke(null, new object[] { url, true }));
        }

        [TestCase("http://127.0.0.2:3000")]
        [TestCase("http://127.1:3000")]
        [TestCase("http://2130706433:3000")]
        [TestCase("http://localhost.evil.test:3000")]
        [TestCase("http://user@localhost:3000")]
        [TestCase("http://localhost:3000/?token=x")]
        [TestCase("http://localhost:3000/#fragment")]
        [TestCase("ftp://localhost:3000")]
        public void EditorBackendRejectsAmbiguousOrNonLoopbackUrls(string url)
        {
            Assert.IsFalse((bool)GetUrlPolicyMethod("IsAllowedBackendUrl", typeof(string), typeof(bool))
                .Invoke(null, new object[] { url, true }));
        }

        [TestCase("http://localhost:")]
        [TestCase("http://127.0.0.1:")]
        [TestCase("https://api.example.test:")]
        public void BackendRejectsAnExplicitEmptyPort(string url)
        {
            Assert.IsFalse((bool)GetUrlPolicyMethod("IsAllowedBackendUrl", typeof(string), typeof(bool))
                .Invoke(null, new object[] { url, true }));
        }

        [TestCase("https://localhost:3000")]
        [TestCase("https://127.0.0.1:3000")]
        [TestCase("https://127.0.0.2:3000")]
        [TestCase("https://[::1]:3000")]
        public void EditorKeepsExistingHttpsLoopbackBackendSupport(string url)
        {
            Assert.IsTrue((bool)GetUrlPolicyMethod("IsAllowedBackendUrl", typeof(string), typeof(bool))
                .Invoke(null, new object[] { url, true }));
        }

        [TestCase("http://api.example.test")]
        [TestCase("http://localhost:3000")]
        [TestCase("https://127.0.0.2:3000")]
        [TestCase("https://[::1]:3000")]
        [TestCase("https://[::ffff:127.0.0.1]:3000")]
        public void PlayerBackendRejectsHttpAndLoopbackUrls(string url)
        {
            Assert.IsFalse((bool)GetUrlPolicyMethod("IsAllowedBackendUrl", typeof(string), typeof(bool))
                .Invoke(null, new object[] { url, false }));
        }

        [Test]
        public void BearerRequiresSameSchemeHostAndPort()
        {
            var shouldAttach = typeof(TtsCacheBackendClientV1).GetMethod("ShouldAttachBearer", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(shouldAttach);
            Assert.IsTrue((bool)shouldAttach.Invoke(null, new object[] { "https://api.example.test:8443/x", "https://api.example.test:8443", true }));
            Assert.IsFalse((bool)shouldAttach.Invoke(null, new object[] { "https://api.example.test/x", "https://api.example.test:8443", true }));
            Assert.IsFalse((bool)shouldAttach.Invoke(null, new object[] { "http://localhost:3000/x", "https://localhost:3000", true }));
            Assert.IsTrue((bool)shouldAttach.Invoke(null, new object[] { "http://localhost:3000/x", "http://localhost:3000", true }));
            Assert.IsFalse((bool)shouldAttach.Invoke(null, new object[] { "http://api.example.test:3000/x", "http://api.example.test:3000", true }));
            Assert.IsFalse((bool)shouldAttach.Invoke(null, new object[] { "http://localhost:3001/x", "http://localhost:3000", true }));
            Assert.IsFalse((bool)shouldAttach.Invoke(null, new object[] { "http://127.0.0.1:3000/x", "http://localhost:3000", true }));
            Assert.IsFalse((bool)shouldAttach.Invoke(null, new object[] { "https://user@api.example.test:8443/x", "https://api.example.test:8443", true }));
        }

        [TestCase("http://127.0.0.1:3000/signed-object")]
        [TestCase("https://storage.example.test/signed-object#fragment")]
        public async Task DownloadRejectsNonHttpsOrFragmentedStorageUrls(string readUrl)
        {
            var backend = new TtsCacheBackendClientV1(ProductionBackendUrl, "token");
            Assert.ThrowsAsync<CacheValidationException>(async () => await backend.DownloadAsync(readUrl, default));
        }

        private static MethodInfo GetUrlPolicyMethod(string name, params Type[] parameterTypes)
        {
            var policy = typeof(TtsCacheBackendClientV1).Assembly.GetType(
                "VRAutism.Gameplay.LessonGraphV2.AudioCache.TtsCacheBackendUrlPolicyV1");
            Assert.NotNull(policy, "backend URL policy should be compiled with the runtime cache code");
            var method = policy.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic, null, parameterTypes, null);
            Assert.NotNull(method);
            return method;
        }
    }
}
