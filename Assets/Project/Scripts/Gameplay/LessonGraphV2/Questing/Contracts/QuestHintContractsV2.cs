using System.Threading;
using System.Threading.Tasks;

namespace VRAutism.Gameplay.LessonGraphV2.Questing
{
    public interface IQuestVisualHintV2
    {
        bool TryShowVisualHint(string activationId);
    }

    public interface IQuestVerbalHintV2
    {
        Task<bool> SendVerbalHintAsync(string activationId, string commandId, CancellationToken cancellationToken);
    }
}
