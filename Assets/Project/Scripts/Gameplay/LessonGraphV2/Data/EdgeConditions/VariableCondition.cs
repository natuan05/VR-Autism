using System;
using UnityEngine;

namespace VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions
{
    public enum VariableValueType { Boolean, Integer, Float, String }
    public enum VariableComparisonOperator { Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual }

    [Serializable]
    public sealed class VariableCondition : IEdgeCondition
    {
        [SerializeField] private string _variableName = string.Empty;
        [SerializeField] private VariableValueType _valueType;
        [SerializeField] private VariableComparisonOperator _operator;
        [SerializeField] private bool _booleanValue;
        [SerializeField] private int _integerValue;
        [SerializeField] private float _floatValue;
        [SerializeField] private string _stringValue = string.Empty;
        public string VariableName => _variableName;
        public VariableValueType ValueType => _valueType;
        public VariableComparisonOperator Operator => _operator;
        public bool BooleanValue => _booleanValue;
        public int IntegerValue => _integerValue;
        public float FloatValue => _floatValue;
        public string StringValue => _stringValue;

        public VariableCondition(string variableName, VariableValueType valueType, VariableComparisonOperator op, bool value)
        { _variableName = variableName ?? string.Empty; _valueType = valueType; _operator = op; _booleanValue = value; }
        public VariableCondition(string variableName, VariableValueType valueType, VariableComparisonOperator op, int value)
        { _variableName = variableName ?? string.Empty; _valueType = valueType; _operator = op; _integerValue = value; }
        public VariableCondition(string variableName, VariableValueType valueType, VariableComparisonOperator op, float value)
        { _variableName = variableName ?? string.Empty; _valueType = valueType; _operator = op; _floatValue = value; }
        public VariableCondition(string variableName, VariableValueType valueType, VariableComparisonOperator op, string value)
        { _variableName = variableName ?? string.Empty; _valueType = valueType; _operator = op; _stringValue = value ?? string.Empty; }
        public VariableCondition() { }
    }
}
