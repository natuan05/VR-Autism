using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Cloud.LiveKit;
using VRAutism.Gameplay.LessonGraphV2.Integration;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class NpcAudioRouteBindingV2Tests
    {
        private sealed class MockRouter : INpcAudioRouterV2
        {
            public readonly Dictionary<string, AudioSource> Routes = new Dictionary<string, AudioSource>();

            public void RegisterNpcAudioRoute(string npcBindingId, AudioSource source)
            {
                if (string.IsNullOrWhiteSpace(npcBindingId) || source == null) return;
                Routes[npcBindingId] = source;
            }

            public void UnregisterNpcAudioRoute(string npcBindingId)
            {
                if (string.IsNullOrWhiteSpace(npcBindingId)) return;
                Routes.Remove(npcBindingId);
            }

            public bool TryGetNpcAudioRoute(string npcBindingId, out AudioSource source)
            {
                if (string.IsNullOrWhiteSpace(npcBindingId))
                {
                    source = null;
                    return false;
                }
                return Routes.TryGetValue(npcBindingId, out source);
            }

            public string ActiveNpcBindingId { get; private set; } = string.Empty;

            public bool SetActiveNpcRoute(string npcBindingId)
            {
                ActiveNpcBindingId = npcBindingId;
                return Routes.ContainsKey(npcBindingId);
            }
        }

        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdObjects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _createdObjects.Clear();
        }

        private AudioSource CreateAudioSource(string name)
        {
            var go = new GameObject(name);
            _createdObjects.Add(go);
            return go.AddComponent<AudioSource>();
        }

        [Test]
        public void LiveKitService_RegisterAndUnregisterRoute()
        {
            var serviceGo = new GameObject("livekit-service-test");
            _createdObjects.Add(serviceGo);
            var service = serviceGo.AddComponent<LiveKitService>();

            var source1 = CreateAudioSource("npc-source-1");
            service.RegisterNpcAudioRoute("teacher-npc", source1);

            Assert.IsTrue(service.TryGetNpcAudioRoute("teacher-npc", out var retrieved));
            Assert.AreSame(source1, retrieved);

            service.UnregisterNpcAudioRoute("teacher-npc");
            Assert.IsFalse(service.TryGetNpcAudioRoute("teacher-npc", out _));
        }

        [Test]
        public void LiveKitService_RouteSwap_ReplacesAudioSource()
        {
            var serviceGo = new GameObject("livekit-service-test");
            _createdObjects.Add(serviceGo);
            var service = serviceGo.AddComponent<LiveKitService>();

            var source1 = CreateAudioSource("npc-source-1");
            var source2 = CreateAudioSource("npc-source-2");

            service.RegisterNpcAudioRoute("teacher-npc", source1);
            Assert.IsTrue(service.TryGetNpcAudioRoute("teacher-npc", out var first));
            Assert.AreSame(source1, first);

            // Rebind / swap to source2
            service.RegisterNpcAudioRoute("teacher-npc", source2);
            Assert.IsTrue(service.TryGetNpcAudioRoute("teacher-npc", out var second));
            Assert.AreSame(source2, second);
        }

        [Test]
        public void LiveKitService_MultiNpcIsolation()
        {
            var serviceGo = new GameObject("livekit-service-test");
            _createdObjects.Add(serviceGo);
            var service = serviceGo.AddComponent<LiveKitService>();

            var teacherSource = CreateAudioSource("teacher-source");
            var peerSource = CreateAudioSource("peer-source");

            service.RegisterNpcAudioRoute("teacher-npc", teacherSource);
            service.RegisterNpcAudioRoute("peer-npc", peerSource);

            Assert.IsTrue(service.TryGetNpcAudioRoute("teacher-npc", out var retrievedTeacher));
            Assert.IsTrue(service.TryGetNpcAudioRoute("peer-npc", out var retrievedPeer));
            Assert.AreSame(teacherSource, retrievedTeacher);
            Assert.AreSame(peerSource, retrievedPeer);
            Assert.AreNotSame(retrievedTeacher, retrievedPeer);
        }

        [Test]
        public void NpcAudioRouteBindingV2_LifecycleRegistersAndUnregisters()
        {
            var router = new MockRouter();
            var go = new GameObject("npc-binding-test");
            _createdObjects.Add(go);
            var source = go.AddComponent<AudioSource>();
            var binding = go.AddComponent<NpcAudioRouteBindingV2>();

            binding.Bind("npc-character-1", source, router);
            Assert.IsTrue(binding.IsBound);
            Assert.IsTrue(router.TryGetNpcAudioRoute("npc-character-1", out var boundSource));
            Assert.AreSame(source, boundSource);

            // Simulate disable
            binding.enabled = false;
            Assert.IsFalse(binding.IsBound);
            Assert.IsFalse(router.TryGetNpcAudioRoute("npc-character-1", out _));

            // Simulate re-enable
            binding.enabled = true;
            Assert.IsTrue(binding.IsBound);
            Assert.IsTrue(router.TryGetNpcAudioRoute("npc-character-1", out boundSource));
            Assert.AreSame(source, boundSource);
        }

        [Test]
        public void LiveKitService_TrackBeforeSource_DrainsPendingTrackUponRegistration()
        {
            var serviceGo = new GameObject("livekit-service-test");
            _createdObjects.Add(serviceGo);
            var service = serviceGo.AddComponent<LiveKitService>();

            // 1. Audio track arrives before NPC source is registered
            service.SimulatePendingAudioTrack("track-sid-1", "teacher-npc");
            Assert.AreEqual(1, service.PendingV2TrackCount);
            Assert.IsTrue(service.IsV2TrackPending("track-sid-1"));

            // 2. NPC route registers later -> pending track is drained
            var source = CreateAudioSource("npc-source");
            service.RegisterNpcAudioRoute("teacher-npc", source);

            Assert.AreEqual(0, service.PendingV2TrackCount);
            Assert.IsFalse(service.IsV2TrackPending("track-sid-1"));
        }

        [Test]
        public void LiveKitService_RouteSwap_DrainsPendingTrackUponSwap()
        {
            var serviceGo = new GameObject("livekit-service-test");
            _createdObjects.Add(serviceGo);
            var service = serviceGo.AddComponent<LiveKitService>();

            var source1 = CreateAudioSource("npc-source-1");
            service.RegisterNpcAudioRoute("teacher-npc", source1);

            // A pending track arrives for this route
            service.SimulatePendingAudioTrack("track-sid-swap", "teacher-npc");
            Assert.AreEqual(1, service.PendingV2TrackCount);

            // Swap to source2 -> pending track is drained
            var source2 = CreateAudioSource("npc-source-2");
            service.RegisterNpcAudioRoute("teacher-npc", source2);

            Assert.AreEqual(0, service.PendingV2TrackCount);
            Assert.IsFalse(service.IsV2TrackPending("track-sid-swap"));
        }

        [Test]
        public void LiveKitService_SetActiveNpcRoute_DynamicSwitching()
        {
            var serviceGo = new GameObject("livekit-service-test");
            _createdObjects.Add(serviceGo);
            var service = serviceGo.AddComponent<LiveKitService>();

            var teacherSource = CreateAudioSource("teacher-source");
            var peerSource = CreateAudioSource("peer-source");

            service.RegisterNpcAudioRoute("teacher-npc", teacherSource);
            service.RegisterNpcAudioRoute("peer-npc", peerSource);

            // Active stream currently bound to teacher
            service.SimulateActiveAudioStream("track-1", teacherSource, "teacher-npc");
            Assert.AreSame(teacherSource, service.GetActiveStreamSource("track-1"));
            Assert.AreEqual("teacher-npc", service.GetActiveStreamRoute("track-1"));

            // Dynamic route switch to peer-npc
            var switched = service.SetActiveNpcRoute("peer-npc");
            Assert.IsTrue(switched);
            Assert.AreEqual("peer-npc", service.ActiveNpcBindingId);
            Assert.AreSame(peerSource, service.GetActiveStreamSource("track-1"));
            Assert.AreEqual("peer-npc", service.GetActiveStreamRoute("track-1"));
        }

        [Test]
        public void LiveKitService_SetActiveNpcRoute_DrainsPendingTrack()
        {
            var serviceGo = new GameObject("livekit-service-test");
            _createdObjects.Add(serviceGo);
            var service = serviceGo.AddComponent<LiveKitService>();

            var teacherSource = CreateAudioSource("teacher-source");
            service.RegisterNpcAudioRoute("teacher-npc", teacherSource);

            // Track arrives with arbitrary agent identity
            service.SimulatePendingAudioTrack("agent-track-1", "agent-identity");
            Assert.AreEqual(1, service.PendingV2TrackCount);

            // Activating teacher-npc route drains the pending track and establishes stream
            var activated = service.SetActiveNpcRoute("teacher-npc");
            Assert.IsTrue(activated);
            Assert.AreEqual(0, service.PendingV2TrackCount);
            Assert.AreEqual(1, service.ActiveV2StreamCount);
            Assert.AreSame(teacherSource, service.GetActiveStreamSource("agent-track-1"));
            Assert.AreEqual("teacher-npc", service.GetActiveStreamRoute("agent-track-1"));
        }
    }
}
