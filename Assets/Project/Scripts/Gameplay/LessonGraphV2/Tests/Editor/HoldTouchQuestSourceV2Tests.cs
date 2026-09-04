using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Questing;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class HoldTouchQuestSourceV2Tests
    {
        private readonly List<Object> _objects = new List<Object>();
        [TearDown] public void TearDown() { foreach (var item in _objects) if (item != null) Object.DestroyImmediate(item); _objects.Clear(); }

        [Test]
        public void ValidContactHeldForDuration_CompletesWithHoldTouch()
        {
            var source = Source(1 << 8, 2f); var collider = ColliderOnLayer(8); QuestSourceResult result = null;
            source.Terminated += value => result = value;
            source.TryActivate(Activation("hold"));
            Invoke(source, "OnTriggerEnter", collider); source.Time = 1.99d; Invoke(source, "Update");
            Assert.IsNull(result);
            source.Time = 2d; Invoke(source, "Update");
            Assert.NotNull(result); Assert.AreEqual("hold_touch", result.CompletionChannel);
        }

        [Test]
        public void PartialExitRetainsDwellButEmptyContactSetResetsIt()
        {
            var source = Source(1 << 8, 2f); var first = ColliderOnLayer(8); var second = ColliderOnLayer(8);
            source.TryActivate(Activation("hold")); Invoke(source, "OnTriggerEnter", first); source.Time = 1d; Invoke(source, "OnTriggerEnter", second);
            Invoke(source, "OnTriggerExit", first); source.Time = 2d; Invoke(source, "Update"); Assert.AreEqual(QuestSourceState.Completed, source.State);
        }

        [Test]
        public void EmptyOrPrunedContacts_ResetDwellAndInvalidDurationFailsDuringActivation()
        {
            var source = Source(1 << 8, 2f); var collider = ColliderOnLayer(8);
            source.TryActivate(Activation("reset")); Invoke(source, "OnTriggerEnter", collider); source.Time = 1d; Invoke(source, "OnTriggerExit", collider);
            Invoke(source, "OnTriggerEnter", collider); source.Time = 2.5d; Invoke(source, "Update"); Assert.AreEqual(QuestSourceState.Active, source.State);
            collider.enabled = false; Invoke(source, "Update"); collider.enabled = true; Invoke(source, "OnTriggerEnter", collider); source.Time = 4.5d; Invoke(source, "Update"); Assert.AreEqual(QuestSourceState.Completed, source.State);

            var invalid = Source(1 << 8, 0f); invalid.TryActivate(Activation("invalid"));
            Assert.AreEqual(QuestSourceState.Failed, invalid.State);
        }

        private TestHoldTouchQuestSource Source(int mask, float duration)
        {
            var go = new GameObject("hold-source"); _objects.Add(go); var source = go.AddComponent<TestHoldTouchQuestSource>();
            Set(typeof(QuestSourceV2), source, "_bindingId", "hold"); Set(typeof(HoldTouchQuestSourceV2), source, "_interactorLayers", (LayerMask)mask); Set(typeof(HoldTouchQuestSourceV2), source, "_holdDurationSeconds", duration); typeof(QuestSourceV2).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, null); return source;
        }
        private Collider ColliderOnLayer(int layer) { var go = new GameObject("interactor"); go.layer = layer; _objects.Add(go); return go.AddComponent<BoxCollider>(); }
        private static QuestSourceActivation Activation(string id) => new QuestSourceActivation(id, System.DateTimeOffset.UtcNow, 0d);
        private static void Set(System.Type type, object target, string field, object value) => type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string method, object argument = null) => typeof(HoldTouchQuestSourceV2).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, argument == null ? null : new[] { argument });
        private sealed class TestHoldTouchQuestSource : HoldTouchQuestSourceV2 { public double Time; protected override double UnscaledMonotonicSeconds => Time; }
    }
}
