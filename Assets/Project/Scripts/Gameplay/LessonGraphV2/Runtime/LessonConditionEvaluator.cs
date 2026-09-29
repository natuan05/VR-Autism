using System;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    public static class LessonConditionEvaluator
    {
        public static bool Evaluate(IEdgeCondition condition, NodeStatus status, ILessonVariableSource source)
        {
            if (condition is AlwaysCondition) return true;
            if (condition is StatusCondition statusCondition)
                return string.Equals(statusCondition.RequiredStatus, NodeStatusCondition.ToCondition(status), StringComparison.Ordinal);
            if (condition is VariableCondition variableCondition)
                return EvaluateVariable(variableCondition, source);
            if (condition is CompositeCondition composite)
                return EvaluateComposite(composite, status, source);
            return false;
        }

        private static bool EvaluateVariable(VariableCondition condition, ILessonVariableSource source)
        {
            if (source == null || string.IsNullOrWhiteSpace(condition.VariableName) ||
                !source.TryGetValue(condition.VariableName, out var value) || !value.IsValid ||
                value.Type != condition.ValueType)
                return false;

            switch (condition.ValueType)
            {
                case VariableValueType.Boolean:
                    if (condition.Operator == VariableComparisonOperator.Equal) return value.Boolean == condition.BooleanValue;
                    if (condition.Operator == VariableComparisonOperator.NotEqual) return value.Boolean != condition.BooleanValue;
                    return false;
                case VariableValueType.Integer:
                    return Compare(value.Integer, condition.IntegerValue, condition.Operator);
                case VariableValueType.Float:
                    if (float.IsNaN(condition.FloatValue) || float.IsInfinity(condition.FloatValue)) return false;
                    return Compare(value.Float, condition.FloatValue, condition.Operator);
                case VariableValueType.String:
                    int comparison = string.Compare(value.String, condition.StringValue, StringComparison.Ordinal);
                    if (condition.Operator == VariableComparisonOperator.Equal) return comparison == 0;
                    if (condition.Operator == VariableComparisonOperator.NotEqual) return comparison != 0;
                    return false;
                default:
                    return false;
            }
        }

        private static bool EvaluateComposite(CompositeCondition composite, NodeStatus status, ILessonVariableSource source)
        {
            if (composite.Conditions == null || composite.Conditions.Count == 0) return false;
            if (composite.Operator == CompositeConditionOperator.And)
            {
                foreach (var child in composite.Conditions)
                    if (!Evaluate(child, status, source)) return false;
                return true;
            }
            if (composite.Operator == CompositeConditionOperator.Or)
            {
                foreach (var child in composite.Conditions)
                    if (Evaluate(child, status, source)) return true;
            }
            return false;
        }

        private static bool Compare(int left, int right, VariableComparisonOperator op)
        {
            switch (op)
            {
                case VariableComparisonOperator.Equal: return left == right;
                case VariableComparisonOperator.NotEqual: return left != right;
                case VariableComparisonOperator.LessThan: return left < right;
                case VariableComparisonOperator.LessThanOrEqual: return left <= right;
                case VariableComparisonOperator.GreaterThan: return left > right;
                case VariableComparisonOperator.GreaterThanOrEqual: return left >= right;
                default: return false;
            }
        }

        private static bool Compare(float left, float right, VariableComparisonOperator op)
        {
            switch (op)
            {
                case VariableComparisonOperator.Equal: return left == right;
                case VariableComparisonOperator.NotEqual: return left != right;
                case VariableComparisonOperator.LessThan: return left < right;
                case VariableComparisonOperator.LessThanOrEqual: return left <= right;
                case VariableComparisonOperator.GreaterThan: return left > right;
                case VariableComparisonOperator.GreaterThanOrEqual: return left >= right;
                default: return false;
            }
        }
    }
}
