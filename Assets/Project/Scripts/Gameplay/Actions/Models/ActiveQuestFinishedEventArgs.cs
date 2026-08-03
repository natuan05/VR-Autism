using System;

namespace VRAutism.Gameplay.Actions
{
    public class ActiveQuestFinishedEventArgs : EventArgs
    {
        public int QuestIndex { get; }
        public string QuestName { get; }
        public string CompletionStatus { get; }
        public int HintsVerbal { get; }
        public int HintsVisual { get; }
        public int HintsPhysical { get; }
        public double ResponseTimeFromHint { get; }

        public ActiveQuestFinishedEventArgs(
            int questIndex,
            string questName,
            string completionStatus,
            int hintsVerbal = 0,
            int hintsVisual = 0,
            int hintsPhysical = 0,
            double responseTimeFromHint = -1.0)
        {
            QuestIndex = questIndex;
            QuestName = questName;
            CompletionStatus = completionStatus;
            HintsVerbal = hintsVerbal;
            HintsVisual = hintsVisual;
            HintsPhysical = hintsPhysical;
            ResponseTimeFromHint = responseTimeFromHint;
        }
    }
}
