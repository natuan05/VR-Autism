using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using VRAutism.Gameplay.LessonGraphV2.Data.EdgeConditions;

namespace VRAutism.Gameplay.LessonGraphV2.Editor
{
    /// <summary>Explicit, Undo-aware controls for SerializeReference edge/loop conditions.</summary>
    internal static class ConditionAuthoringControls
    {
        private const string NoneChoice = "(Select condition type)";

        public static VisualElement CreateReferenceControl(string label, SerializedProperty property,
            Action refresh, int depth = 1, string namePrefix = "condition", Action removeSelf = null)
        {
            var root = new VisualElement { name = namePrefix + "-authoring-controls" };
            if (property == null || property.serializedObject == null) return root;
            if (HasMissingManagedReference(property))
            {
                root.Add(new HelpBox(
                    "This condition has a missing managed-reference type. Its serialized payload is preserved; restore the type before replacing it.",
                    HelpBoxMessageType.Error)
                {
                    name = namePrefix + "-missing-reference-warning",
                });
                return root;
            }
            var current = property.managedReferenceValue as IEdgeCondition;
            var choices = CreateTypeChoices(depth, true);
            var currentName = current?.GetType().Name ?? NoneChoice;
            if (!choices.Contains(currentName)) choices.Add(currentName);
            var selector = new PopupField<string>(label + " Type", choices, currentName)
            {
                name = namePrefix + "-condition-type-field",
            };
            selector.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == currentName) return;
                var replacement = CreateCondition(evt.newValue);
                if (replacement == null) return;
                SetReference(property, replacement, "Change " + label + " Type");
                refresh?.Invoke();
            });
            root.Add(selector);
            if (removeSelf != null)
                root.Add(new Button(() => removeSelf())
                {
                    text = "Remove Child",
                    name = namePrefix + "-remove-child-button",
                });

            if (current is CompositeCondition composite)
            {
                var operatorProperty = property.FindPropertyRelative("_operator");
                if (operatorProperty != null)
                    root.Add(new PropertyField(operatorProperty, "Operator")
                    {
                        name = namePrefix + "-composite-operator-field",
                    });
                var children = property.FindPropertyRelative("_conditions");
                if (children == null || !children.isArray) return root;
                var addChoices = CreateTypeChoices(depth + 1, true);
                var addField = new PopupField<string>("Add Child Condition", addChoices, addChoices[0])
                {
                    name = namePrefix + "-add-child-condition-field",
                };
                addField.RegisterValueChangedCallback(evt =>
                {
                    var child = CreateCondition(evt.newValue);
                    if (child == null) return;
                    var serializedObject = children.serializedObject;
                    Undo.RecordObject(serializedObject.targetObject, "Add Composite Condition Child");
                    var index = children.arraySize++;
                    children.GetArrayElementAtIndex(index).managedReferenceValue = child;
                    serializedObject.ApplyModifiedProperties();
                    refresh?.Invoke();
                });
                root.Add(addField);
                for (var index = 0; index < children.arraySize; index++)
                {
                    var childIndex = index;
                    var childProperty = children.GetArrayElementAtIndex(index);
                    root.Add(CreateReferenceControl($"Child {index + 1}", childProperty, refresh,
                        depth + 1, namePrefix + "-child-" + index,
                        () => RemoveChild(children, childIndex, refresh)));
                }
                if (depth >= 16)
                    root.Add(new HelpBox("Nested composite conditions are limited to 16 levels.", HelpBoxMessageType.Warning));
            }
            else if (current != null && !(current is AlwaysCondition))
            {
                root.Add(new PropertyField(property, label + " Fields")
                {
                    name = namePrefix + "-condition-fields",
                });
            }
            else if (current == null)
            {
                root.Add(new HelpBox("Choose a typed condition to author this field.", HelpBoxMessageType.Info));
            }
            return root;
        }

        private static List<string> CreateTypeChoices(int depth, bool includeNone)
        {
            var choices = new List<string>();
            if (includeNone) choices.Add(NoneChoice);
            choices.Add(nameof(AlwaysCondition));
            choices.Add(nameof(StatusCondition));
            choices.Add(nameof(VariableCondition));
            if (depth <= 16) choices.Add(nameof(CompositeCondition));
            return choices;
        }

        private static IEdgeCondition CreateCondition(string typeName)
        {
            switch (typeName)
            {
                case nameof(AlwaysCondition): return new AlwaysCondition();
                case nameof(StatusCondition): return new StatusCondition(StatusCondition.Success);
                case nameof(VariableCondition): return new VariableCondition();
                case nameof(CompositeCondition): return new CompositeCondition(CompositeConditionOperator.And, null);
                default: return null;
            }
        }

        private static void SetReference(SerializedProperty property, IEdgeCondition value, string undoName)
        {
            var serializedObject = property.serializedObject;
            if (serializedObject?.targetObject == null) return;
            Undo.RecordObject(serializedObject.targetObject, undoName);
            property.managedReferenceValue = value;
            serializedObject.ApplyModifiedProperties();
        }

        private static bool HasMissingManagedReference(SerializedProperty property)
        {
            var target = property?.serializedObject?.targetObject;
            if (target == null || property.managedReferenceValue != null ||
                !SerializationUtility.HasManagedReferencesWithMissingTypes(target)) return false;
            var missingTypes = SerializationUtility.GetManagedReferencesWithMissingTypes(target);
            // Unity exposes a null missing reference through SerializedProperty with ID -2,
            // while the persisted RID is available only in the target's missing-type list.
            // When the nested property cannot be mapped to an exact RID, fail closed rather
            // than risk replacing an unrelated serialized payload.
            if (property.managedReferenceId == -2 && missingTypes.Length > 0) return true;
            for (var index = 0; index < missingTypes.Length; index++)
                if (missingTypes[index].referenceId == property.managedReferenceId) return true;
            return false;
        }

        private static void RemoveChild(SerializedProperty children, int index, Action refresh)
        {
            if (children == null || !children.isArray || index < 0 || index >= children.arraySize) return;
            var serializedObject = children.serializedObject;
            if (serializedObject?.targetObject == null) return;
            Undo.RecordObject(serializedObject.targetObject, "Remove Composite Condition Child");
            var originalSize = children.arraySize;
            children.DeleteArrayElementAtIndex(index);
            if (children.arraySize == originalSize && index < children.arraySize)
                children.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            refresh?.Invoke();
        }
    }
}
