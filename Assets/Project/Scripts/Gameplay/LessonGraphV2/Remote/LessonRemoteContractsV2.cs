using System;

namespace VRAutism.Gameplay.LessonGraphV2.Remote
{
    public static class LessonRemoteContractV2
    {
        public const int ContractVersion = 2;
        public const string RemoteTopic = "lesson-graph-v2.remote";
        public const string StateEvent = "LESSON_STATE";
        public const string CommandEvent = "LESSON_COMMAND";
        public const string CommandResultEvent = "LESSON_COMMAND_RESULT";
    }

    public static class LessonCommandKindV2
    {
        public const string Skip = "SKIP";
        public const string Pause = "PAUSE";
        public const string Resume = "RESUME";
        public const string VerbalHint = "VERBAL_HINT";
        public const string VisualHint = "VISUAL_HINT";
    }

    public static class LessonCommandReasonV2
    {
        public const string None = "NONE";
        public const string Malformed = "MALFORMED";
        public const string WrongSession = "WRONG_SESSION";
        public const string WrongRun = "WRONG_RUN";
        public const string WrongNode = "WRONG_NODE";
        public const string StaleActivation = "STALE_ACTIVATION";
        public const string Duplicate = "DUPLICATE";
        public const string NotActive = "NOT_ACTIVE";
        public const string InvalidState = "INVALID_STATE";
        public const string WrongBinding = "WRONG_BINDING";
        public const string UnsupportedCapability = "UNSUPPORTED_CAPABILITY";
        public const string TransportUnavailable = "TRANSPORT_UNAVAILABLE";
        public const string Cancelled = "CANCELLED";
    }

    [Serializable]
    public sealed class LessonCommandV2
    {
        public int contract_version;
        public string @event;
        public string command_id;
        public string session_id;
        public string run_id;
        public string node_id;
        public string activation_id;
        public string command;
        public string binding_id;
    }

    [Serializable]
    public sealed class LessonCommandResultV2
    {
        public int contract_version;
        public string @event;
        public string command_id;
        public string session_id;
        public string run_id;
        public string node_id;
        public string activation_id;
        public string command;
        public string binding_id;
        public bool accepted;
        public string reason;
        public LessonStateV2 state;
    }

    [Serializable]
    public sealed class LessonBindingV2
    {
        public string binding_id;
        public string npc_binding_id;
        public bool can_verbal_hint;
        public bool can_visual_hint;
    }

    [Serializable]
    public sealed class LessonStateV2
    {
        public int contract_version;
        public string session_id;
        public string run_id;
        public string graph_id;
        public string lesson_id;
        public string launch_token;
        public int lesson_voice_revision;
        public int child_phrase_revision;
        public string node_id;
        public string node_type;
        public int node_index;
        public string activation_id;
        public string status;
        public string checkpoint_id;
        public string updated_at_utc;
        public int state_revision;
        public string[] active_node_ids;
        public string parallel_group_id;
        public LessonBindingV2[] bindings;
    }

    [Serializable]
    public sealed class LessonStatePacketV2
    {
        public int contract_version;
        public string @event;
        public LessonStateV2 state;
    }
}
