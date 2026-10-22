using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LiveKit;
using LiveKit.Proto;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine.TestTools;
using VRAutism.Cloud.LiveKit;

namespace VRAutism.Gameplay.LessonGraphV2.AudioCache.Tests
{
    public sealed class TtsUnityCacheIntegrationV1Tests
    {
        [Test]
        public void OnlyCurrentAuthenticatedHelloSignalsVoiceReplayReadiness()
        {
            var go = new UnityEngine.GameObject("voice-replay-ready-test");
            try
            {
                var coordinator = go.AddComponent<FixedAudioPlaybackCoordinatorV1>();
                SetPrivate(coordinator, "_snapshot", SessionTtsSnapshotV1.Parse(SessionJson(), "session-1"));
                SetPrivate(coordinator, "_participantClient", new FakeParticipantClient());
                int readyCount = 0;
                coordinator.AgentReady += () => readyCount++;
                var receive = typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("HandleAgentPacket", BindingFlags.Instance | BindingFlags.NonPublic);
                Action<string, string, string> hello = (sender, session, instance) => receive.Invoke(coordinator, new object[]
                {
                    Encoding.UTF8.GetBytes(new JObject
                    {
                        ["contract_version"] = 1, ["event"] = "FIXED_AUDIO_HELLO",
                        ["session_id"] = session, ["agent_instance_id"] = instance
                    }.ToString()), sender
                });
                hello("foreign", "session-1", "boot-current");
                hello("agent-id", "other-session", "boot-current");
                hello("agent-id", "session-1", "boot-old");
                Assert.AreEqual(0, readyCount);
                hello("agent-id", "session-1", "boot-current");
                Assert.AreEqual(1, readyCount);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void RebuildsValidatedSidecarsAfterIndexDamageAndHonorsActiveInvalidation()
        {
            var dataPath = TestRoot();
            try
            {
                var cache = new TtsLocalCacheV1(dataPath);
                var wav = MakeWav(new byte[] { 1, 0, 2, 0 });
                var metadata = TtsWavValidatorV1.Validate(wav);
                var artifact = new ArtifactV1
                {
                    profile_id = "teacher-npc", key = new string('a', 64), rejection_epoch = 1,
                    object_path = TtsCacheContractsV1.ArtifactPath("teacher-npc", new string('a', 64), 1),
                    generation = "1", sha256 = metadata.sha256, byte_size = metadata.byte_size,
                    sample_count = metadata.sample_count, duration_ms = metadata.duration_ms, format = TtsCacheContractsV1.AudioFormat
                };
                using (var reservation = cache.Reserve(wav.Length + 2048))
                    Assert.IsTrue(cache.Store(artifact, wav, reservation));

                var root = new DirectoryInfo(Path.Combine(dataPath, "tts-cache", "v1"));
                File.WriteAllText(Path.Combine(root.FullName, "index.json"), "broken index");
                var rebuilt = new TtsLocalCacheV1(dataPath);
                Assert.IsTrue(rebuilt.TryAcquire(artifact.profile_id, artifact.key, 0, out var lease), "valid WAV/sidecar recover after index damage");
                rebuilt.Invalidate(artifact);
                Assert.IsFalse(rebuilt.TryAcquire(artifact.profile_id, artifact.key, 0, out _), "invalidated playback data cannot start a second playback");
                lease.Dispose();
                var afterRelease = new TtsLocalCacheV1(dataPath);
                Assert.IsFalse(afterRelease.TryAcquire(artifact.profile_id, artifact.key, 0, out _), "invalidation removes the sidecar when its playback lease ends");
            }
            finally { TryDeleteRoot(dataPath); }
        }

        [Test]
        public void ReopeningValidIndexPreservesSavedLruSequence()
        {
            var dataPath = TestRoot();
            try
            {
                var cache = new TtsLocalCacheV1(dataPath);
                var wav = MakeWav(new byte[] { 1, 0, 2, 0 });
                var wavInfo = TtsWavValidatorV1.Validate(wav);
                var first = MakeArtifact("a", wavInfo);
                var second = MakeArtifact("b", wavInfo);
                using (var reservation = cache.Reserve(wav.Length + 2048)) Assert.IsTrue(cache.Store(first, wav, reservation));
                using (var reservation = cache.Reserve(wav.Length + 2048)) Assert.IsTrue(cache.Store(second, wav, reservation));
                Assert.IsTrue(cache.TryAcquire(first.profile_id, first.key, 0, out var firstLease));
                firstLease.Dispose();

                var indexPath = Path.Combine(dataPath, "tts-cache", "v1", "index.json");
                var before = JObject.Parse(File.ReadAllText(indexPath));
                var beforeFirst = ReadLastUse(before, first.key);
                var beforeSecond = ReadLastUse(before, second.key);
                var reopened = new TtsLocalCacheV1(dataPath);
                var after = JObject.Parse(File.ReadAllText(indexPath));
                Assert.AreEqual(beforeFirst, ReadLastUse(after, first.key));
                Assert.AreEqual(beforeSecond, ReadLastUse(after, second.key));
                Assert.Greater(ReadLastUse(after, first.key), ReadLastUse(after, second.key));
            }
            finally { TryDeleteRoot(dataPath); }
        }

        [Test]
        public void ReservationsIncludeOutstandingDownloadAndIndexHeadroom()
        {
            var dataPath = TestRoot();
            try
            {
                var cache = new TtsLocalCacheV1(dataPath);
                var indexBytes = new FileInfo(Path.Combine(cache.RootPath, "index.json")).Length;
                var firstRequestBytes = TtsLocalCacheV1.CapacityBytes - 12L * 1024 * 1024 - indexBytes;
                using (var first = cache.Reserve(firstRequestBytes))
                {
                    Assert.NotNull(first);
                    Assert.IsNull(cache.Reserve(1), "second writer must account for the first reservation and index temporary");
                }
            }
            finally { TryDeleteRoot(dataPath); }
        }

        [Test]
        public void ActiveLeasePreventsLruCleanupFromDeletingItsAudio()
        {
            var dataPath = TestRoot();
            try
            {
                var cache = new TtsLocalCacheV1(dataPath);
                var wav = MakeWav(new byte[] { 1, 0, 2, 0 });
                var artifact = MakeArtifact("a", TtsWavValidatorV1.Validate(wav));
                using (var reservation = cache.Reserve(wav.Length + 2048)) Assert.IsTrue(cache.Store(artifact, wav, reservation));
                Assert.IsTrue(cache.TryAcquire(artifact.profile_id, artifact.key, 0, out var lease));
                using (var pressure = cache.Reserve(TtsLocalCacheV1.CapacityBytes - 12L * 1024 * 1024 - 100))
                {
                    Assert.IsNull(pressure, "an active reader cannot be evicted to satisfy a reservation");
                    Assert.IsTrue(File.Exists(Path.Combine(dataPath, "tts-cache", "v1", artifact.profile_id,
                        artifact.key, "r1.wav")));
                }
                lease.Dispose();
            }
            finally { TryDeleteRoot(dataPath); }
        }

        [Test]
        public void ServerAgentRoleComesFromParticipantKindRatherThanIdentityText()
        {
            var check = typeof(LiveKitService).GetMethod("IsServerAgentInfo", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(check);
            var spoofedStandard = new ParticipantInfo { Identity = "agent-spoof", Kind = ParticipantKind.Standard };
            var verifiedAgent = new ParticipantInfo { Identity = "ordinary-name", Kind = ParticipantKind.Agent };
            Assert.IsFalse((bool)check.Invoke(null, new object[] { spoofedStandard }));
            Assert.IsTrue((bool)check.Invoke(null, new object[] { verifiedAgent }));
        }

        [Test]
        public void RetiredBootInstanceCannotMatchTheCurrentAgentInstance()
        {
            var check = typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("IsCurrentAgentInstance", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(check);
            Assert.IsFalse((bool)check.Invoke(null, new object[] { "boot-old", "boot-current" }));
            Assert.IsTrue((bool)check.Invoke(null, new object[] { "boot-current", "boot-current" }));
        }

        [Test]
        public void ParsesExactTypescriptLookupAndReconcileResponseFixtures()
        {
            var fixture = JObject.Parse(File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath,
                "Project/Scripts/Gameplay/LessonGraphV2/AudioCache/Tests/Editor/Fixtures/tts-cache-v1.json")));
            var responses = fixture["http_responses"];
            var key = "3e4c476e9b8e3939138a75614e2a65b9be72d508077284ecfeb7df248717bbe8";
            var hit = TtsCacheHttpResponseV1.ParseLookup(responses["lookup_hit"].ToString(), "teacher-npc", key);
            Assert.IsFalse(hit.IsMiss, "the actual hit shape has no miss:false field");
            Assert.AreEqual("123", hit.Artifact.generation);
            Assert.AreEqual(1, hit.Artifact.rejection_epoch);
            var miss = TtsCacheHttpResponseV1.ParseLookup(responses["lookup_miss"].ToString(), "teacher-npc", key);
            Assert.IsTrue(miss.IsMiss);
            Assert.AreEqual(1, miss.RejectionEpoch);

            var artifact = TtsCacheContractsV1.ParseArtifact(responses["lookup_hit"]["artifact"].ToString());
            CollectionAssert.AreEqual(new[] { true }, TtsCacheHttpResponseV1.ParseReconcile(responses["reconcile"].ToString(), new[] { artifact }));
            var stale = (JObject)responses["reconcile"].DeepClone();
            stale["results"][0]["current_epoch"] = 2;
            CollectionAssert.AreEqual(new[] { false }, TtsCacheHttpResponseV1.ParseReconcile(stale.ToString(), new[] { artifact }));
        }

        [Test]
        public void PendingReconciliationAndLaterCacheWorkReuseOneWaitDeadline()
        {
            var start = typeof(TtsAudioResolverV1).GetMethod("StartDeadlineIfMissing", BindingFlags.Static | BindingFlags.NonPublic);
            var remaining = typeof(TtsAudioResolverV1).GetMethod("RemainingBudget", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(start);
            Assert.NotNull(remaining);
            Assert.IsNull(start.Invoke(null, new object[] { null, false }), "completed reconciliation does not start this budget early");

            var deadline = (System.Diagnostics.Stopwatch)start.Invoke(null, new object[] { null, true });
            System.Threading.Thread.Sleep(30);
            Assert.AreSame(deadline, start.Invoke(null, new object[] { deadline, true }),
                "the local-miss phase must retain the reconciliation deadline rather than start a second full budget");
            var budget = (int)remaining.Invoke(null, new object[] { deadline, 1500 });
            Assert.Less(budget, 1500);
            Assert.Greater(budget, 0);
        }

        [Test]
        public void BearerIsLimitedToConfiguredHttpsApiOriginAndJsonBytesDecodeStrictly()
        {
            var shouldAttach = typeof(TtsCacheBackendClientV1).GetMethod("ShouldAttachBearer", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(shouldAttach);
            Assert.IsTrue((bool)shouldAttach.Invoke(null, new object[] { "https://api.example.test/api/tts-cache/lookup", "https://api.example.test", true }));
            Assert.IsFalse((bool)shouldAttach.Invoke(null, new object[] { "https://storage.example.test/signed", "https://api.example.test", false }));
            Assert.IsFalse((bool)shouldAttach.Invoke(null, new object[] { "https://other.example.test/api", "https://api.example.test", true }));
            Assert.Throws<ArgumentException>(() => new TtsCacheBackendClientV1("http://api.example.test", "room-token"));

            var decode = typeof(TtsCacheBackendClientV1).GetMethod("DecodeJsonBytes", BindingFlags.Static | BindingFlags.NonPublic);
            var received = Encoding.UTF8.GetBytes("{\"text\":\"é👋\"}");
            Assert.AreEqual("{\"text\":\"é👋\"}", decode.Invoke(null, new object[] { received }),
                "bounded request-body bytes must be decoded explicitly instead of DownloadHandlerScript.text");
            Assert.Throws<TargetInvocationException>(() => decode.Invoke(null, new object[] { new byte[] { 0xff } }));
        }

        [Test]
        public void AgentRosterSelectsNewestServerRoleAndRejectsQueuedOldIncarnationHello()
        {
            var select = typeof(LiveKitService).GetMethod("SelectCurrentAgentSid", BindingFlags.Static | BindingFlags.NonPublic);
            var sameSid = typeof(LiveKitService).GetMethod("IsCurrentAgentIncarnation", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(select);
            Assert.NotNull(sameSid);
            var args = new object[]
            {
                new List<Tuple<string, string, long>>
                {
                    Tuple.Create("agent", "sid-old", 10L), Tuple.Create("agent", "sid-new", 20L)
                }, false
            };
            Assert.AreEqual("sid-new", select.Invoke(null, args), "newest server-joined agent wins during overlapping restart");
            Assert.IsFalse((bool)args[1]);
            args = new object[] { new List<Tuple<string, string, long>> { Tuple.Create("agent", "sid-new", 20L) }, false };
            Assert.AreEqual("sid-new", select.Invoke(null, args), "after old participant leaves, the new participant remains current");
            Assert.IsTrue((bool)sameSid.Invoke(null, new object[] { "sid-new", "sid-new" }));
            Assert.IsFalse((bool)sameSid.Invoke(null, new object[] { "sid-old", "sid-new" }), "queued HELLO from prior participant incarnation is stale");
            args = new object[]
            {
                new List<Tuple<string, string, long>>
                {
                    Tuple.Create("agent-a", "sid-a", 20L), Tuple.Create("agent-b", "sid-b", 20L)
                }, false
            };
            Assert.IsNull(select.Invoke(null, args), "ambiguous newest server-role roster fails closed");
            Assert.IsTrue((bool)args[1]);
        }

        [TestCase(1L, true)]
        [TestCase(0L, false)]
        public void StreamSelectionBindsPreparingTrackOnlyForCurrentOwner(long selectionRevision, bool expectedSelected)
        {
            var go = new UnityEngine.GameObject("pending-stream-selection-test");
            try
            {
                var source = go.AddComponent<UnityEngine.AudioSource>();
                var routerType = typeof(LiveKitService).Assembly.GetType("VRAutism.Cloud.LiveKit.LiveKitNpcAudioRouter");
                var router = (IFixedAudioOwnershipRouterV1)Activator.CreateInstance(routerType, true);
                routerType.GetMethod("RegisterNpcAudioRoute", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(router, new object[] { "npc", source });
                var createdStreams = 0;
                routerType.GetProperty("StreamFactory", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(router,
                    new Func<RemoteAudioTrack, UnityEngine.AudioSource, IDisposable>((track, target) =>
                    {
                        Assert.AreSame(source, target);
                        createdStreams++;
                        return new MemoryStream();
                    }));
                var invocation = Invocation("first-dialogue", "dialogue");
                using (router.AcquireLocalPlayback(invocation, 1, "npc"))
                {
                    // The first RTC track arrives while cache lookup owns and suspends audio.
                    routerType.GetMethod("SimulatePendingAudioTrack", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(router, new object[] { "first-agent-track", "agent" });

                    Assert.AreEqual(expectedSelected, router.SelectStream(invocation, selectionRevision, "npc"));
                    Assert.AreEqual(expectedSelected ? 1 : 0, createdStreams,
                        "A selected stream must bind the pending track before the agent can finish speaking.");
                    Assert.AreEqual(!expectedSelected, routerType.GetMethod("IsV2TrackPending", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(router, new object[] { "first-agent-track" }));
                    Assert.AreEqual(expectedSelected, routerType.GetMethod("IsV2TrackActive", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(router, new object[] { "first-agent-track" }));
                }
                routerType.GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(router, null);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void LocalTerminalRestoresOrdinaryRouteAndStaleLeaseCannotReleaseNewerOwner()
        {
            var go = new UnityEngine.GameObject("fixed-audio-routing-test");
            try
            {
                var source = go.AddComponent<UnityEngine.AudioSource>();
                var routerType = typeof(LiveKitService).Assembly.GetType("VRAutism.Cloud.LiveKit.LiveKitNpcAudioRouter");
                var router = (IFixedAudioOwnershipRouterV1)Activator.CreateInstance(routerType, true);
                routerType.GetMethod("RegisterNpcAudioRoute", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(router, new object[] { "npc", source });
                var simulatePending = routerType.GetMethod("SimulatePendingAudioTrack", BindingFlags.Instance | BindingFlags.NonPublic);
                var isPending = routerType.GetMethod("IsV2TrackPending", BindingFlags.Instance | BindingFlags.NonPublic);

                var local = Invocation("local-opening", "opening");
                var localLease = router.AcquireLocalPlayback(local, 1, "npc");
                simulatePending.Invoke(router, new object[] { "ordinary-after-local", "agent" });
                router.ReleaseLocalPlayback(local, 1, true);
                localLease.Dispose();
                Assert.IsFalse((bool)isPending.Invoke(router, new object[] { "ordinary-after-local" }),
                    "ordinary RTC audio can bind again once local playback reaches its current terminal");

                var oldOwner = Invocation("old-stream", "dialogue");
                var currentOwner = Invocation("current-stream", "dialogue");
                var staleLease = router.AcquireLocalPlayback(oldOwner, 2, "npc");
                router.AcquireLocalPlayback(currentOwner, 3, "npc");
                simulatePending.Invoke(router, new object[] { "new-owner-pending", "agent" });
                staleLease.Dispose();
                router.ReleaseLocalPlayback(oldOwner, 2, true);
                Assert.IsTrue((bool)isPending.Invoke(router, new object[] { "new-owner-pending" }),
                    "a delayed cancellation for an old owner cannot reopen routing owned by a newer invocation");
                router.ReleaseLocalPlayback(currentOwner, 3, true);
                Assert.IsFalse((bool)isPending.Invoke(router, new object[] { "new-owner-pending" }));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void StreamCancellationBarrierDefersLocalTerminalAndLateOldAckCannotOpenNewOwner()
        {
            var go = new UnityEngine.GameObject("fixed-audio-barrier-test");
            try
            {
                var source = go.AddComponent<UnityEngine.AudioSource>();
                var routerType = typeof(LiveKitService).Assembly.GetType("VRAutism.Cloud.LiveKit.LiveKitNpcAudioRouter");
                var router = (IFixedAudioOwnershipRouterV1)Activator.CreateInstance(routerType, true);
                routerType.GetMethod("RegisterNpcAudioRoute", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(router, new object[] { "npc", source });
                var simulatePending = routerType.GetMethod("SimulatePendingAudioTrack", BindingFlags.Instance | BindingFlags.NonPublic);
                var isPending = routerType.GetMethod("IsV2TrackPending", BindingFlags.Instance | BindingFlags.NonPublic);

                var coordinator = go.AddComponent<FixedAudioPlaybackCoordinatorV1>();
                typeof(FixedAudioPlaybackCoordinatorV1).GetField("_router", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(coordinator, router);
                var oldInvocation = Invocation("old-stream", "dialogue");
                var localInvocation = Invocation("local-opening", "opening");
                var oldLease = router.AcquireLocalPlayback(oldInvocation, 1, "npc");
                var localLease = router.AcquireLocalPlayback(localInvocation, 2, "npc");
                var ownerType = typeof(FixedAudioPlaybackCoordinatorV1).GetNestedType("Owner", BindingFlags.NonPublic);
                var oldOwner = MakeOwner(ownerType, oldInvocation, 1, oldLease, "stream", "boot-1");
                var localOwner = MakeOwner(ownerType, localInvocation, 2, localLease, "local", "");
                var retiring = (System.Collections.IDictionary)typeof(FixedAudioPlaybackCoordinatorV1)
                    .GetField("_retiringStreamOwners", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(coordinator);
                var keyMethod = typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("RetiringStreamKey", BindingFlags.Static | BindingFlags.NonPublic);
                retiring.Add(keyMethod.Invoke(null, new object[] { oldInvocation, 1L }), oldOwner);
                typeof(FixedAudioPlaybackCoordinatorV1).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(coordinator, localOwner);

                simulatePending.Invoke(router, new object[] { "ordinary-after-local", "agent" });
                typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(coordinator, new object[] { localOwner, "SUCCESS", string.Empty });
                Assert.IsTrue((bool)isPending.Invoke(router, new object[] { "ordinary-after-local" }),
                    "local terminal stays suspended while cancellation of the old stream is unconfirmed");

                typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("ReleaseRetiringStreamsFrom", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(coordinator, new object[] { "boot-1" });
                Assert.IsFalse((bool)isPending.Invoke(router, new object[] { "ordinary-after-local" }),
                    "correlated retirement releases the latest completed owner and restores ordinary audio");

                var newerInvocation = Invocation("newer-stream", "dialogue");
                router.AcquireLocalPlayback(newerInvocation, 3, "npc");
                simulatePending.Invoke(router, new object[] { "new-owner-pending", "agent" });
                typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("ReleaseRetiringStreamsFrom", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(coordinator, new object[] { "boot-1" });
                Assert.IsTrue((bool)isPending.Invoke(router, new object[] { "new-owner-pending" }),
                    "an old or duplicate retirement cannot release the newer route owner");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ReadyHintsQueueAndCoalesceUntilForegroundOwnerIsTerminal()
        {
            var go = new UnityEngine.GameObject("fixed-audio-ready-queue-test");
            try
            {
                var coordinator = go.AddComponent<FixedAudioPlaybackCoordinatorV1>();
                var warmer = new FakeWarmer();
                var participant = new FakeParticipantClient();
                SetPrivate(coordinator, "_resolver", warmer);
                SetPrivate(coordinator, "_participantClient", participant);
                SetPrivate(coordinator, "_authenticatedAgentInstanceId", "boot-current");
                SetPrivate(coordinator, "_agentParticipantIdentity", "agent-id");
                var ownerType = typeof(FixedAudioPlaybackCoordinatorV1).GetNestedType("Owner", BindingFlags.NonPublic);
                SetPrivate(coordinator, "_current", Activator.CreateInstance(ownerType, true));

                var candidate = NewReadyCandidate("b");
                var queueReady = typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("QueueReadyWarm", BindingFlags.Instance | BindingFlags.NonPublic);
                queueReady.Invoke(coordinator, new[] { candidate });
                queueReady.Invoke(coordinator, new[] { candidate });
                Assert.AreEqual(0, warmer.WarmCount, "READY must not run hashing, download or disk writes while foreground audio owns playback");

                SetPrivate(coordinator, "_current", null);
                typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("DrainPendingReadyIfIdle", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(coordinator, null);
                Assert.AreEqual(1, warmer.WarmCount, "the coalesced advisory warm begins exactly once after the owner ends");
            }
            finally
            {
                SetPrivate(go.GetComponent<FixedAudioPlaybackCoordinatorV1>(), "_current", null);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DuplicateReadyDuringActiveWarmDoesNotQueueSecondDownload()
        {
            var go = new UnityEngine.GameObject("fixed-audio-active-ready-dedup-test");
            try
            {
                var coordinator = go.AddComponent<FixedAudioPlaybackCoordinatorV1>();
                var warmer = new FakeWarmer(true);
                SetPrivate(coordinator, "_resolver", warmer);
                SetPrivate(coordinator, "_participantClient", new FakeParticipantClient());
                SetPrivate(coordinator, "_authenticatedAgentInstanceId", "boot-current");
                SetPrivate(coordinator, "_agentParticipantIdentity", "agent-id");
                var candidate = NewReadyCandidate("c");
                var queueReady = typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("QueueReadyWarm", BindingFlags.Instance | BindingFlags.NonPublic);
                queueReady.Invoke(coordinator, new[] { candidate });
                Assert.AreEqual(1, warmer.WarmCount, "the first READY starts one background warm");

                queueReady.Invoke(coordinator, new[] { candidate });
                Assert.AreEqual(1, warmer.WarmCount, "a duplicate READY during the active task remains coalesced");
                warmer.Complete(true);
                var update = typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
                var activeField = typeof(FixedAudioPlaybackCoordinatorV1).GetField("_readyWarmActive", BindingFlags.Instance | BindingFlags.NonPublic);
                for (var i = 0; i < 100 && (bool)activeField.GetValue(coordinator); i++)
                {
                    System.Threading.Thread.Sleep(10);
                    update.Invoke(coordinator, null);
                }
                Assert.IsFalse((bool)activeField.GetValue(coordinator), "the completed warm callback is processed on the coordinator thread");
                Assert.AreEqual(1, warmer.WarmCount, "completion must not dispatch another queued lookup or download");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void ReconcilePreservesSuccessfulOpeningEvidenceAfterLaterActivity()
        {
            var go = new UnityEngine.GameObject("fixed-audio-opening-reconcile-test");
            var coordinator = go.AddComponent<FixedAudioPlaybackCoordinatorV1>();
            var dataClient = new FakeDataClient();
            var participant = new FakeParticipantClient();
            SetPrivate(coordinator, "_snapshot", SessionTtsSnapshotV1.Parse(SessionJson(), "session-1"));
            SetPrivate(coordinator, "_client", dataClient);
            SetPrivate(coordinator, "_participantClient", participant);
            SetPrivate(coordinator, "_agentParticipantIdentity", "agent-id");
            SetPrivate(coordinator, "_currentRunId", "run");
            var active = (HashSet<string>)typeof(FixedAudioPlaybackCoordinatorV1)
                .GetField("_activeActivationIds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(coordinator);
            active.Add("opening-activation");
            active.Add("later-activation");

            var ownerType = typeof(FixedAudioPlaybackCoordinatorV1).GetNestedType("Owner", BindingFlags.NonPublic);
            var openingOwner = MakeOwner(ownerType, new InvocationV1
            {
                session_id = "session-1", run_id = "run", activation_id = "opening-activation",
                invocation_id = "opening-call", npc_binding_id = "npc", kind = "opening"
            }, 1, null, "local", string.Empty);
            SetPrivate(coordinator, "_current", openingOwner);
            typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(coordinator, new object[] { openingOwner, "SUCCESS", string.Empty });

            var laterOwner = MakeOwner(ownerType, new InvocationV1
            {
                session_id = "session-1", run_id = "run", activation_id = "later-activation",
                invocation_id = "later-call", npc_binding_id = "npc", kind = "manual_hint"
            }, 2, null, "local", string.Empty);
            typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("PublishActivity", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(coordinator, new object[] { laterOwner, "terminal", "local", "SUCCESS", string.Empty });

            typeof(FixedAudioPlaybackCoordinatorV1).GetMethod("PublishReconcileIfReady", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(coordinator, null);
            var reconcileJson = dataClient.Published.Single(packet => (string)JObject.Parse(packet)["event"] == "FIXED_AUDIO_RECONCILE");
            var reconcile = JObject.Parse(reconcileJson);
            CollectionAssert.AreEqual(new[] { "opening-activation" }, reconcile["completed_opening_activation_ids"].Values<string>());
            Assert.AreEqual("later-call", (string)reconcile["current_activity"]["invocation_id"],
                "latest activity can change without losing opening evidence");
            SetPrivate(coordinator, "_current", null);
            UnityEngine.Object.DestroyImmediate(go);
        }

        [UnityTest]
        public System.Collections.IEnumerator ExplicitLocalCancellationRestoresSourceLoopSetting()
        {
            var go = new UnityEngine.GameObject("local-tts-playback-test");
            var source = go.AddComponent<UnityEngine.AudioSource>();
            var clip = UnityEngine.AudioClip.Create("local-tts-test", 24000, 1, 24000, false);
            try
            {
                source.loop = true;
                var resolved = new ResolvedAudioV1 { Outcome = ResolvedAudioOutcomeV1.LocalReady, Clip = clip };
                var terminal = (LocalTtsPlaybackOutcomeV1?)null;
                var handle = new LocalTtsPlaybackHandleV1(source, resolved, result => terminal = result);
                Assert.IsFalse(source.loop, "looping is disabled while the coordinator owns this clip");
                handle.Cancel();
                Assert.IsTrue(source.loop, "the route's previous loop setting is restored when ownership ends");
                Assert.IsTrue(terminal.HasValue);
                Assert.AreEqual(LocalTtsPlaybackOutcomeV1.Cancelled, terminal.Value);
                yield return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [TestCase("session_id", "null")]
        [TestCase("session_id", "17")]
        [TestCase("pin", "null")]
        [TestCase("pin", "17")]
        [TestCase("child_id", "null")]
        [TestCase("child_id", "17")]
        [TestCase("lesson_id", "null")]
        [TestCase("lesson_id", "17")]
        [TestCase("host_id", "null")]
        [TestCase("host_id", "17")]
        public void RetainedSessionRejectsNonStringOrNullScopeFields(string field, string value)
        {
            var token = JObject.Parse(SessionJson());
            token[field] = value == "null" ? JValue.CreateNull() : JToken.Parse(value);
            Assert.Throws<CacheValidationException>(() => SessionTtsSnapshotV1.Parse(token.ToString(), "session-1"));
        }

        [Test]
        public void RetainedConfigCannotBeMutatedThroughSnapshotGetters()
        {
            var snapshot = SessionTtsSnapshotV1.Parse(SessionJson(), "session-1");
            snapshot.Scope.config.profiles[0].speaking_rate = 0.9;
            snapshot.Config.profiles[0].voice_name = "mutated";
            Assert.AreEqual(1d, snapshot.Config.profiles[0].speaking_rate);
            Assert.AreEqual("vi-VN-Chirp3-HD-Aoede", snapshot.Scope.config.profiles[0].voice_name);
        }

        private static string TestRoot() => Path.Combine(UnityEngine.Application.temporaryCachePath, "tts-cache-v1-tests-" + Guid.NewGuid().ToString("N"));
        private static InvocationV1 Invocation(string id, string kind) => new InvocationV1
        { session_id = "session", run_id = "run", activation_id = "activation", invocation_id = id, npc_binding_id = "npc", kind = kind };
        private static string SessionJson() => new JObject
        {
            ["session_id"] = "session-1", ["pin"] = "123456", ["child_id"] = "child-1",
            ["lesson_id"] = "lesson-1", ["host_id"] = "host-1",
            ["config"] = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(TtsCacheContractsV1.DefaultTtsConfig()))
        }.ToString();
        private static void SetPrivate(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);

        private static object NewReadyCandidate(string suffix)
        {
            var candidateType = typeof(FixedAudioPlaybackCoordinatorV1).GetNestedType("ReadyCandidate", BindingFlags.NonPublic);
            var candidate = Activator.CreateInstance(candidateType, true);
            SetPrivate(candidate, "Artifact", MakeArtifact(suffix,
                new WavMetadataV1 { sha256 = new string('b', 64), byte_size = 48, sample_count = 2, duration_ms = 1 }));
            SetPrivate(candidate, "AgentInstanceId", "boot-current");
            SetPrivate(candidate, "SenderIdentity", "agent-id");
            SetPrivate(candidate, "QueuedAt", UnityEngine.Time.realtimeSinceStartup);
            return candidate;
        }

        private static object MakeOwner(Type ownerType, InvocationV1 invocation, long revision, IDisposable lease, string source, string agentInstance)
        {
            var owner = Activator.CreateInstance(ownerType, true);
            SetPrivate(owner, "Request", new FixedUtteranceV1 { invocation = invocation, owner_revision = revision });
            SetPrivate(owner, "Cancellation", new System.Threading.CancellationTokenSource());
            SetPrivate(owner, "RoutingLease", lease);
            SetPrivate(owner, "Source", source);
            SetPrivate(owner, "AgentInstanceId", agentInstance);
            return owner;
        }

        private sealed class FakeWarmer : ITtsAudioCacheWarmerV1
        {
            public int WarmCount { get; private set; }
            private readonly System.Threading.Tasks.TaskCompletionSource<bool> _completion;
            public FakeWarmer(bool hold = false)
            {
                if (hold) _completion = new System.Threading.Tasks.TaskCompletionSource<bool>(
                    System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            }
            public void Complete(bool result) => _completion?.TrySetResult(result);
            public void BeginLaunchReconciliation() { }
            public void CancelBackgroundWarms() { }
            public System.Threading.Tasks.Task<ResolvedAudioV1> ResolveAsync(FixedUtteranceV1 request, System.Threading.CancellationToken cancellationToken) =>
                throw new NotImplementedException();
            public System.Threading.Tasks.Task<bool> WarmReadyAsync(ArtifactV1 artifact)
            { WarmCount++; return _completion?.Task ?? System.Threading.Tasks.Task.FromResult(true); }
            public void Dispose() { }
        }

        private sealed class FakeParticipantClient : ILiveKitParticipantDataPacketClientV1
        {
            public event Action<byte[], string, string, bool> DataReceivedByParticipantV1;
            public string CurrentAgentInstanceIdV1 => "boot-current";
            public bool IsCurrentAgentParticipantV1(string participantIdentity) => participantIdentity == "agent-id";
        }

        private sealed class FakeDataClient : ILiveKitDataPacketClientV2
        {
            public readonly List<string> Published = new List<string>();
            public bool IsConnectedV2 => true;
            public event Action<byte[], string> DataReceivedV2;
            public event Action ReconnectedV2;
            public void PublishDataV2(byte[] data, string topic, bool reliable) => Published.Add(Encoding.UTF8.GetString(data));
        }
        private static void TryDeleteRoot(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }

        private static byte[] MakeWav(byte[] pcm)
        {
            var bytes = new byte[44 + pcm.Length];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0); Write32(bytes, 4, bytes.Length - 8);
            Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(bytes, 8); Write32(bytes, 16, 16);
            Write16(bytes, 20, 1); Write16(bytes, 22, 1); Write32(bytes, 24, 24000); Write32(bytes, 28, 48000);
            Write16(bytes, 32, 2); Write16(bytes, 34, 16); Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
            Write32(bytes, 40, pcm.Length); pcm.CopyTo(bytes, 44); return bytes;
        }

        private static ArtifactV1 MakeArtifact(string suffix, WavMetadataV1 metadata) => new ArtifactV1
        {
            profile_id = "teacher-npc", key = suffix.PadLeft(64, 'a'), rejection_epoch = 1,
            object_path = TtsCacheContractsV1.ArtifactPath("teacher-npc", suffix.PadLeft(64, 'a'), 1),
            generation = "1", sha256 = metadata.sha256, byte_size = metadata.byte_size,
            sample_count = metadata.sample_count, duration_ms = metadata.duration_ms, format = TtsCacheContractsV1.AudioFormat
        };

        private static long ReadLastUse(JObject index, string key) =>
            (long)index["entries"].Children<JObject>().Single(entry => (string)entry["key"] == key)["last_use"];

        private static void Write16(byte[] bytes, int offset, int value)
        { bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8); }
        private static void Write32(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16); bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}
