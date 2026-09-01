using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace VRAutism.Gameplay.LessonGraphV2.Validation
{
    /// <summary>
    /// Immutable result of a graph preflight validation run.
    /// Invariants:
    ///   - IsValid == true  → Errors is empty.
    ///   - IsValid == false → Errors has at least one entry.
    /// The backing array is copied on construction; callers cannot mutate it after the fact.
    /// </summary>
    public sealed class GraphValidationResult
    {
        public bool IsValid { get; }
        public IReadOnlyList<GraphValidationError> Errors { get; }

        private GraphValidationResult(bool isValid, ReadOnlyCollection<GraphValidationError> errors)
        {
            IsValid = isValid;
            Errors  = errors;
        }

        /// <summary>Returns a successful result with an empty error list.</summary>
        public static GraphValidationResult Ok() =>
            new GraphValidationResult(true, Array.AsReadOnly(Array.Empty<GraphValidationError>()));

        /// <summary>
        /// Returns a failed result containing the supplied errors.
        /// </summary>
        /// <exception cref="ArgumentNullException">errors is null.</exception>
        /// <exception cref="ArgumentException">errors is empty — a failed result must explain why.</exception>
        public static GraphValidationResult Fail(IReadOnlyList<GraphValidationError> errors)
        {
            if (errors == null)
                throw new ArgumentNullException(nameof(errors),
                    "Cannot create a failed result with a null error list.");

            if (errors.Count == 0)
                throw new ArgumentException(
                    "Cannot create a failed result with zero errors. Provide at least one GraphValidationError.",
                    nameof(errors));

            // Copy defensively so the caller's list cannot mutate the result after creation.
            var copy = new GraphValidationError[errors.Count];
            for (int i = 0; i < errors.Count; i++)
                copy[i] = errors[i];

            return new GraphValidationResult(false, Array.AsReadOnly(copy));
        }

        public override string ToString()
        {
            if (IsValid) return "GraphValidationResult: OK";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"GraphValidationResult: FAILED ({Errors.Count} error(s))");
            foreach (var e in Errors)
                sb.AppendLine($"  {e}");
            return sb.ToString();
        }
    }
}
