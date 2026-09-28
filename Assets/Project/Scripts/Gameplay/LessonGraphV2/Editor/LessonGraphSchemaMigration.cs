using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Validation;

namespace VRAutism.Gameplay.LessonGraphV2.Editor
{
    public static class LessonGraphSchemaMigration
    {
        public static bool TryCreateSchemaTwoCopy(LessonGraph source, out LessonGraph copy,
            out GraphValidationResult validation, out string error)
        {
            copy = null;
            validation = null;
            error = string.Empty;
            if (source == null) { error = "Select a LessonGraph asset."; return false; }
            if (source.SchemaVersion != 1)
            {
                error = $"Only saved schema-1 assets can be migrated. Source version: {source.SchemaVersion}.";
                return false;
            }
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                error = "Save the source LessonGraph asset before migrating it.";
                return false;
            }

            var candidate = UnityEngine.Object.Instantiate(source);
            candidate.name = source.name + "_Schema2";
            candidate.Editor_SetSchemaVersion(2);
            validation = LessonGraphValidator.Validate(candidate);
            if (!validation.IsValid)
            {
                error = validation.ToString();
                UnityEngine.Object.DestroyImmediate(candidate);
                return false;
            }

            var destination = string.Empty;
            try
            {
                var directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
                destination = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{source.name}_Schema2.asset");
                AssetDatabase.CreateAsset(candidate, destination);
                AssetDatabase.SaveAssets();
                copy = candidate;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (!string.IsNullOrEmpty(destination) && AssetDatabase.LoadAssetAtPath<LessonGraph>(destination) != null)
                {
                    AssetDatabase.DeleteAsset(destination);
                    candidate = null;
                }
                if (candidate != null && AssetDatabase.GetAssetPath(candidate) == string.Empty)
                    UnityEngine.Object.DestroyImmediate(candidate);
                return false;
            }
        }
    }
}
