using System;
using System.Collections.Generic;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>
    /// Scene-owned typed values used by LessonGraph variable conditions.
    /// Initial values are authored in the Inspector; runtime systems can update them through the typed setters.
    /// </summary>
    public sealed class LessonGraphVariableStore : UnityEngine.MonoBehaviour, ILessonVariableSource
    {
        [Serializable]
        private sealed class InitialValue
        {
            public string Name;
            public VariableValueType Type;
            public bool Boolean;
            public int Integer;
            public float Float;
            public string String;

            public LessonVariableValue ToValue()
            {
                switch (Type)
                {
                    case VariableValueType.Boolean: return LessonVariableValue.FromBoolean(Boolean);
                    case VariableValueType.Integer: return LessonVariableValue.FromInteger(Integer);
                    case VariableValueType.Float: return LessonVariableValue.FromFloat(Float);
                    case VariableValueType.String: return LessonVariableValue.FromString(String ?? string.Empty);
                    default: return default;
                }
            }
        }

        [UnityEngine.SerializeField] private List<InitialValue> _initialValues = new List<InitialValue>();
        private readonly object _gate = new object();
        private Dictionary<string, LessonVariableValue> _values;

        private void Awake() => EnsureInitialized();

        public bool TryGetValue(string name, out LessonVariableValue value)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(name))
            {
                value = default;
                return false;
            }

            lock (_gate) return _values.TryGetValue(name, out value);
        }

        public void SetBoolean(string name, bool value) => SetValue(name, LessonVariableValue.FromBoolean(value));
        public void SetInteger(string name, int value) => SetValue(name, LessonVariableValue.FromInteger(value));
        public void SetFloat(string name, float value) => SetValue(name, LessonVariableValue.FromFloat(value));
        public void SetString(string name, string value) => SetValue(name, LessonVariableValue.FromString(value ?? string.Empty));

        public void SetValue(string name, LessonVariableValue value)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Variable name must not be blank.", nameof(name));
            if (!value.IsValid) throw new ArgumentException("Variable value must be a valid typed value.", nameof(value));
            EnsureInitialized();
            lock (_gate) _values[name] = value;
        }

        public bool Remove(string name)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(name)) return false;
            lock (_gate) return _values.Remove(name);
        }

        private void EnsureInitialized()
        {
            lock (_gate)
            {
                if (_values != null) return;
                _values = new Dictionary<string, LessonVariableValue>(StringComparer.Ordinal);
                if (_initialValues == null) return;
                foreach (var initial in _initialValues)
                {
                    if (initial == null || string.IsNullOrWhiteSpace(initial.Name)) continue;
                    if (!Enum.IsDefined(typeof(VariableValueType), initial.Type)) continue;
                    var value = initial.ToValue();
                    if (value.IsValid) _values[initial.Name] = value;
                }
            }
        }
    }
}
