namespace VRAutism.Gameplay.Actions
{
    /// <summary>
    /// Quest chạm vào là hoàn thành ngay lập tức.
    /// </summary>
    public class TouchQuest : VisualQuest
    {
        protected override void OnBegin()
        {
            base.OnBegin();
            RaiseUIStarted();
        }

        protected override void OnCharacterEnter()
        {
            
            Controller.CompleteActiveQuest();
            RaiseUIFinished();
        }
    }
}
