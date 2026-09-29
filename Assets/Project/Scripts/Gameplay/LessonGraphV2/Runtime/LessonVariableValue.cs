using System;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    public interface ILessonVariableSource
    {
        bool TryGetValue(string name, out LessonVariableValue value);
    }

    public struct LessonVariableValue
    {
        public VariableValueType Type { get; }
        public bool Boolean { get; }
        public int Integer { get; }
        public float Float { get; }
        public string String { get; }

        private LessonVariableValue(VariableValueType type, bool boolean, int integer, float number, string text)
        {
            Type = type;
            Boolean = boolean;
            Integer = integer;
            Float = number;
            String = text;
        }

        public static LessonVariableValue FromBoolean(bool value) => new LessonVariableValue(VariableValueType.Boolean, value, 0, 0f, null);
        public static LessonVariableValue FromInteger(int value) => new LessonVariableValue(VariableValueType.Integer, false, value, 0f, null);
        public static LessonVariableValue FromFloat(float value) => new LessonVariableValue(VariableValueType.Float, false, 0, value, null);
        public static LessonVariableValue FromString(string value) => new LessonVariableValue(VariableValueType.String, false, 0, 0f, value);

        public bool IsValid => Enum.IsDefined(typeof(VariableValueType), Type) &&
            (Type != VariableValueType.Float || (!float.IsNaN(Float) && !float.IsInfinity(Float))) &&
            (Type != VariableValueType.String || String != null);
    }
}
