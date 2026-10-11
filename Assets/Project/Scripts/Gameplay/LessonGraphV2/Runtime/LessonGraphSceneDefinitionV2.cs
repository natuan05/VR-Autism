using System.Collections.Generic;
using UnityEngine;
using VRAutism.Gameplay.LessonGraphV2.Data;
using VRAutism.Gameplay.LessonGraphV2.Questing;
using VRAutism.Gameplay.LessonGraphV2.Remote;
using VRAutism.Gameplay.LessonGraphV2.Runtime.Dialogue;

namespace VRAutism.Gameplay.LessonGraphV2.Runtime
{
    /// <summary>
    /// Scene-authored references for one V2 lesson. This component declares the composition
    /// surface and persistence boundary; it does not start or own execution.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LessonGraphSceneDefinitionV2 : MonoBehaviour
    {
        private static readonly HashSet<LessonGraphSceneDefinitionV2> PersistenceOwners =
            new HashSet<LessonGraphSceneDefinitionV2>();

        [SerializeField] private LessonGraph _lessonGraph;
        [SerializeField] private LessonGraphRunner _runner;
        [SerializeField] private LessonGraphBindings _bindings;
        [SerializeField] private LiveKitDialogueTransportV2 _dialogueTransport;
        [SerializeField] private LiveKitLessonRemoteBridgeV2 _remoteBridge;
        [SerializeField] private TimelinePlaybackController _timelinePlaybackController;
        [SerializeField] private LessonGraphVariableStore _variableStore;

        public LessonGraph LessonGraph => _lessonGraph;
        public LessonGraphRunner Runner => _runner;
        public LessonGraphBindings Bindings => _bindings;
        public LiveKitDialogueTransportV2 DialogueTransport => _dialogueTransport;
        public LiveKitLessonRemoteBridgeV2 RemoteBridge => _remoteBridge;
        public TimelinePlaybackController TimelinePlaybackController => _timelinePlaybackController;
        public LessonGraphVariableStore VariableStore => _variableStore;

        private void OnEnable()
        {
            PersistenceOwners.Add(this);
        }

        private void OnDestroy()
        {
            PersistenceOwners.Remove(this);
        }

        public static bool IsV2PersistenceOwned
        {
            get
            {
                PersistenceOwners.RemoveWhere(definition => definition == null);
                return PersistenceOwners.Count > 0;
            }
        }
    }
}
