using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Data.NodeConfigs;
using VRAutism.Gameplay.LessonGraphV2.Questing;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class LessonGraphBindingsTests
    {
        private readonly List<Object> _objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
                if (_objects[index] != null) Object.DestroyImmediate(_objects[index]);
            _objects.Clear();
        }

        [Test]
        public void Awake_WithUniqueEnabledEntries_ResolvesEachExactSource()
        {
            var first = Source("first");
            var second = Source("second");
            var bindings = Bindings(
                new QuestBindingEntry("first", first),
                new QuestBindingEntry("second", second));

            var firstResult = bindings.Resolve("first");
            var secondResult = bindings.Resolve("second");

            Assert.IsTrue(firstResult.IsSuccess);
            Assert.AreSame(first, firstResult.Source);
            Assert.IsTrue(secondResult.IsSuccess);
            Assert.AreSame(second, secondResult.Source);
            Assert.AreEqual(0, bindings.ValidationIssues.Count);
        }

        [Test]
        public void Awake_WithNullSource_RetainsTypedValidationIssue()
        {
            var bindings = Bindings(new QuestBindingEntry("missing-source", null));

            AssertIssue(bindings.ValidationIssues, QuestBindingFailureCodes.NullSource, "missing-source");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Awake_WithBlankEntryId_RetainsTypedValidationIssue(string bindingId)
        {
            var bindings = Bindings(new QuestBindingEntry(bindingId, Source("source-id")));

            AssertIssue(bindings.ValidationIssues, QuestBindingFailureCodes.BlankBindingId, bindingId ?? string.Empty);
        }

        [Test]
        public void Awake_WithDuplicateId_RetainsTypedValidationIssueAndDoesNotResolve()
        {
            var bindings = Bindings(
                new QuestBindingEntry("duplicate", Source("duplicate")),
                new QuestBindingEntry("duplicate", Source("duplicate")));

            AssertIssue(bindings.ValidationIssues, QuestBindingFailureCodes.DuplicateBindingId, "duplicate");
            Assert.IsFalse(bindings.Resolve("duplicate").IsSuccess);
        }

        [Test]
        public void Awake_WithDisabledSource_RetainsUnavailableIssue()
        {
            var source = Source("disabled");
            source.enabled = false;

            var bindings = Bindings(new QuestBindingEntry("disabled", source));

            AssertIssue(bindings.ValidationIssues, QuestBindingFailureCodes.BindingUnavailable, "disabled");
            Assert.AreEqual(QuestBindingFailureCodes.BindingUnavailable, bindings.Resolve("disabled").Issue.Code);
        }

        [Test]
        public void Resolve_MissingId_ReturnsExplicitMissingBindingFailure()
        {
            var bindings = Bindings(new QuestBindingEntry("known", Source("known")));

            var result = bindings.Resolve("unknown");

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(QuestBindingFailureCodes.MissingBinding, result.Issue.Code);
            Assert.AreEqual("unknown", result.Issue.BindingId);
        }

        [Test]
        public void Resolve_RechecksSourceAvailabilityAfterAwake()
        {
            var source = Source("late-disabled");
            var bindings = Bindings(new QuestBindingEntry("late-disabled", source));
            source.enabled = false;

            var result = bindings.Resolve("late-disabled");

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(QuestBindingFailureCodes.BindingUnavailable, result.Issue.Code);
        }

        [Test]
        public void Resolve_RechecksDestroyedSourceAvailabilityAfterAwake()
        {
            var source = Source("late-destroyed");
            var bindings = Bindings(new QuestBindingEntry("late-destroyed", source));
            Object.DestroyImmediate(source.gameObject);

            var result = bindings.Resolve("late-destroyed");

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(QuestBindingFailureCodes.BindingUnavailable, result.Issue.Code);
        }

        [Test]
        public void Awake_WithEntryAndSourceIdMismatch_RetainsTypedValidationIssue()
        {
            var bindings = Bindings(new QuestBindingEntry("entry-id", Source("source-id")));

            AssertIssue(bindings.ValidationIssues, QuestBindingFailureCodes.BindingIdMismatch, "entry-id");
            Assert.IsFalse(bindings.Resolve("entry-id").IsSuccess);
        }

        [Test]
        public void Preflight_WithEveryQuestBindingAvailable_IsReadyWithoutActivation()
        {
            var source = Source("quest-source");
            var bindings = Bindings(new QuestBindingEntry("quest-source", source));
            var graph = Graph(new QuestNodeConfig(new List<string> { "quest-source" }));

            var ready = bindings.IsReady(graph, out var reason);

            Assert.IsTrue(ready, reason);
            Assert.IsNull(reason);
            Assert.AreEqual(QuestSourceState.Inactive, source.State);
            Assert.AreEqual(0, source.ActivationCalls);
            Assert.AreEqual(0, bindings.LastPreflightIssues.Count);
        }

        [Test]
        public void Preflight_WithMissingQuestBinding_ReturnsTypedFailureWithoutActivation()
        {
            var source = Source("known");
            var bindings = Bindings(new QuestBindingEntry("known", source));
            var graph = Graph(new QuestNodeConfig(new List<string> { "missing" }));

            var ready = bindings.IsReady(graph, out var reason);

            Assert.IsFalse(ready);
            StringAssert.Contains(QuestBindingFailureCodes.MissingBinding, reason);
            AssertIssue(bindings.LastPreflightIssues, QuestBindingFailureCodes.MissingBinding, "missing");
            Assert.AreEqual(0, source.ActivationCalls);
        }

        [Test]
        public void Preflight_WithInvalidRegistry_RejectsGraphEvenWhenRequestedBindingIsValid()
        {
            var source = Source("known");
            var bindings = Bindings(
                new QuestBindingEntry("known", source),
                new QuestBindingEntry("broken", null));
            var graph = Graph(new QuestNodeConfig(new List<string> { "known" }));

            var ready = bindings.IsReady(graph, out _);

            Assert.IsFalse(ready);
            AssertIssue(bindings.LastPreflightIssues, QuestBindingFailureCodes.NullSource, "broken");
            Assert.AreEqual(0, source.ActivationCalls);
        }

        private TestQuestSource Source(string bindingId)
        {
            var gameObject = new GameObject($"source-{bindingId}");
            _objects.Add(gameObject);
            var source = gameObject.AddComponent<TestQuestSource>();
            source.Assign(bindingId);
            return source;
        }

        private LessonGraphBindings Bindings(params QuestBindingEntry[] entries)
        {
            var gameObject = new GameObject("bindings");
            gameObject.SetActive(false);
            _objects.Add(gameObject);
            var bindings = gameObject.AddComponent<LessonGraphBindings>();
            typeof(LessonGraphBindings)
                .GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(bindings, new List<QuestBindingEntry>(entries));
            gameObject.SetActive(true);
            typeof(LessonGraphBindings)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bindings, null);
            return bindings;
        }

        private LessonGraph Graph(QuestNodeConfig config)
        {
            var graph = ScriptableObject.CreateInstance<LessonGraph>();
            _objects.Add(graph);
            graph.Editor_SetEntryNodeId("quest");
            graph.Editor_SetNodes(new List<LessonNodeData>
            {
                new LessonNodeData("quest", NodeType.Quest, config),
            });
            graph.Editor_SetEdges(new List<LessonEdgeData>());
            return graph;
        }

        private static void AssertIssue(
            IReadOnlyList<QuestBindingValidationIssue> issues,
            string expectedCode,
            string expectedBindingId)
        {
            Assert.That(issues, Has.Some.Matches<QuestBindingValidationIssue>(issue =>
                issue.Code == expectedCode && issue.BindingId == expectedBindingId));
        }

        private sealed class TestQuestSource : QuestSourceV2
        {
            public int ActivationCalls { get; private set; }
            public void Assign(string bindingId) => typeof(QuestSourceV2)
                .GetField("_bindingId", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(this, bindingId);
            protected override void OnSourceActivated(QuestSourceActivation activation) => ActivationCalls++;
        }
    }
}
