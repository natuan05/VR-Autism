using System.Collections.Generic;
using NUnit.Framework;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;
using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    public sealed class StructuredFlowConditionTests
    {
        [TestCase(VariableComparisonOperator.Equal, true)]
        [TestCase(VariableComparisonOperator.NotEqual, false)]
        [TestCase(VariableComparisonOperator.LessThan, false)]
        [TestCase(VariableComparisonOperator.LessThanOrEqual, true)]
        [TestCase(VariableComparisonOperator.GreaterThan, false)]
        [TestCase(VariableComparisonOperator.GreaterThanOrEqual, true)]
        public void Evaluate_IntegerComparisons(VariableComparisonOperator op, bool expected)
        {
            var condition = new VariableCondition("score", VariableValueType.Integer, op, 4);
            Assert.AreEqual(expected, LessonConditionEvaluator.Evaluate(condition, NodeStatus.Success,
                new DictionaryVariableSource("score", LessonVariableValue.FromInteger(4))));
        }

        [TestCase(VariableComparisonOperator.Equal, false)]
        [TestCase(VariableComparisonOperator.NotEqual, true)]
        [TestCase(VariableComparisonOperator.LessThan, false)]
        [TestCase(VariableComparisonOperator.LessThanOrEqual, false)]
        [TestCase(VariableComparisonOperator.GreaterThan, true)]
        [TestCase(VariableComparisonOperator.GreaterThanOrEqual, true)]
        public void Evaluate_FloatComparisons(VariableComparisonOperator op, bool expected)
        {
            var condition = new VariableCondition("ratio", VariableValueType.Float, op, 1f);
            Assert.AreEqual(expected, LessonConditionEvaluator.Evaluate(condition, NodeStatus.Success,
                new DictionaryVariableSource("ratio", LessonVariableValue.FromFloat(2f))));
        }

        [Test]
        public void Evaluate_UsesTypedBooleanFloatAndOrdinalStringComparisons()
        {
            var values = new DictionaryVariableSource(
                new KeyValuePair<string, LessonVariableValue>("enabled", LessonVariableValue.FromBoolean(true)),
                new KeyValuePair<string, LessonVariableValue>("ratio", LessonVariableValue.FromFloat(1.5f)),
                new KeyValuePair<string, LessonVariableValue>("label", LessonVariableValue.FromString("Alpha")));

            Assert.IsTrue(LessonConditionEvaluator.Evaluate(new VariableCondition("enabled", VariableValueType.Boolean,
                VariableComparisonOperator.Equal, true), NodeStatus.Success, values));
            Assert.IsFalse(LessonConditionEvaluator.Evaluate(new VariableCondition("enabled", VariableValueType.Boolean,
                VariableComparisonOperator.NotEqual, true), NodeStatus.Success, values));
            Assert.IsTrue(LessonConditionEvaluator.Evaluate(new VariableCondition("ratio", VariableValueType.Float,
                VariableComparisonOperator.GreaterThan, 1f), NodeStatus.Success, values));
            Assert.IsFalse(LessonConditionEvaluator.Evaluate(new VariableCondition("label", VariableValueType.String,
                VariableComparisonOperator.Equal, "alpha"), NodeStatus.Success, values));
            Assert.IsTrue(LessonConditionEvaluator.Evaluate(new VariableCondition("label", VariableValueType.String,
                VariableComparisonOperator.NotEqual, "alpha"), NodeStatus.Success, values));
        }

        [Test]
        public void Evaluate_MissingOrWronglyTypedVariableFailsClosed()
        {
            var values = new DictionaryVariableSource("count", LessonVariableValue.FromInteger(2));
            Assert.IsFalse(LessonConditionEvaluator.Evaluate(new VariableCondition("absent", VariableValueType.Integer,
                VariableComparisonOperator.Equal, 0), NodeStatus.Success, values));
            Assert.IsFalse(LessonConditionEvaluator.Evaluate(new VariableCondition("count", VariableValueType.Float,
                VariableComparisonOperator.Equal, 2f), NodeStatus.Success, values));
        }

        [Test]
        public void Evaluate_RecursivelyEvaluatesAndOrStatusAndAlways()
        {
            var values = new DictionaryVariableSource("ready", LessonVariableValue.FromBoolean(true));
            var nested = new CompositeCondition(CompositeConditionOperator.And, new IEdgeCondition[]
            {
                new VariableCondition("ready", VariableValueType.Boolean, VariableComparisonOperator.Equal, true),
                new CompositeCondition(CompositeConditionOperator.Or, new IEdgeCondition[]
                {
                    new StatusCondition("failed"), new AlwaysCondition()
                })
            });

            Assert.IsTrue(LessonConditionEvaluator.Evaluate(nested, NodeStatus.Success, values));
            Assert.IsTrue(LessonConditionEvaluator.Evaluate(new StatusCondition("failed"), NodeStatus.Failed, values));
            Assert.IsTrue(LessonConditionEvaluator.Evaluate(new AlwaysCondition(), NodeStatus.Timeout, null));
            Assert.IsFalse(LessonConditionEvaluator.Evaluate(new StatusCondition("success"), NodeStatus.Failed, values));
        }

        private sealed class DictionaryVariableSource : ILessonVariableSource
        {
            private readonly Dictionary<string, LessonVariableValue> _values = new Dictionary<string, LessonVariableValue>();
            public DictionaryVariableSource(string name, LessonVariableValue value) : this(new KeyValuePair<string, LessonVariableValue>(name, value)) { }
            public DictionaryVariableSource(params KeyValuePair<string, LessonVariableValue>[] values)
            {
                foreach (var pair in values) _values.Add(pair.Key, pair.Value);
            }
            public bool TryGetValue(string name, out LessonVariableValue value) => _values.TryGetValue(name, out value);
        }
    }
}
