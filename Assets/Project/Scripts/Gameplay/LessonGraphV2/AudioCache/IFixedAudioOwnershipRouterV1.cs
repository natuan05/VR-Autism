using System;
using System.Threading;
using System.Threading.Tasks;
using VRAutism.Gameplay.LessonGraphV2.AudioCache;

namespace VRAutism.Cloud.LiveKit
{
    /// <summary>Small advisory-cache surface used by the fixed playback coordinator.</summary>
    public interface ITtsAudioCacheWarmerV1 : IDisposable
    {
        void BeginLaunchReconciliation();
        void CancelBackgroundWarms();
        Task<ResolvedAudioV1> ResolveAsync(FixedUtteranceV1 request, CancellationToken cancellationToken);
        Task<bool> WarmReadyAsync(ArtifactV1 announced);
    }

    /// <summary>Additive routing capability; legacy audio-router fakes and callers remain unchanged.</summary>
    public interface IFixedAudioOwnershipRouterV1
    {
        IDisposable AcquireLocalPlayback(InvocationV1 invocation, long ownerRevision, string npcBindingId);
        bool SelectStream(InvocationV1 invocation, long ownerRevision, string npcBindingId);
        void ReleaseLocalPlayback(InvocationV1 invocation, long ownerRevision, bool resumeOrdinaryAudio);
    }

    /// <summary>Participant-aware packet capability needed to authenticate cache lifecycle events.</summary>
    public interface ILiveKitParticipantDataPacketClientV1
    {
        event Action<byte[], string, string, bool> DataReceivedByParticipantV1;
        bool IsCurrentAgentParticipantV1(string participantIdentity);
        string CurrentAgentInstanceIdV1 { get; }
    }
}
