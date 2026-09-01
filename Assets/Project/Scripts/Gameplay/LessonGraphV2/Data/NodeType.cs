namespace VRAutism.Gameplay.LessonGraphV2.Data
{
    /// <summary>
    /// Phase 1 node types. Timeline, Parallel, Gate are Phase 2 — validator rejects them.
    /// </summary>
    public enum NodeType
    {
        Quest,
        Dialogue,
        Wait,
        Checkpoint,

        // Phase 2 — declared here so assets referencing them still compile,
        // but LessonGraphValidator will reject any graph containing these types.
        Timeline,
        Parallel,
        Gate,
    }
}
