namespace VRAutism.Gameplay.LessonGraphV2.Data
{
    /// <summary>Serialized lesson node kinds. Existing numeric values are stable.</summary>
    public enum NodeType
    {
        Quest,
        Dialogue,
        Wait,
        Checkpoint,

        Timeline,
        Parallel,
        Gate,
        Loop,
    }
}
