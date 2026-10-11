using VRAutism.Gameplay.LessonGraphV2.Runtime;

namespace VRAutism.Gameplay.LessonGraphV2.Tests.Editor
{
    /// <summary>Explicit test-only owner for runner tests that exercise isolated graphs.</summary>
    internal static class LessonGraphRunnerTestOwnershipV2
    {
        public static void Attach(LessonGraphRunner runner)
        {
            runner.RegisterStartOwner(new ExplicitTestOwner());
        }

        private sealed class ExplicitTestOwner : ILessonRunnerStartOwnerV2
        {
            private bool _startRequested;
            public bool IsAvailable => true;
            public bool CanAuthorizeLessonStart(out string reason)
            {
                reason = _startRequested ? "The test owner has already authorized its run." : null;
                return !_startRequested;
            }
            public void MarkLessonStartRequested() => _startRequested = true;
        }
    }
}
