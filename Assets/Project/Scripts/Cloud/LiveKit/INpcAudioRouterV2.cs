using System;
using UnityEngine;

namespace VRAutism.Cloud.LiveKit
{
    /// <summary>
    /// Contract for V2 NPC audio route management.
    /// Maps stable npc_binding_id to scene AudioSource components.
    /// </summary>
    public interface INpcAudioRouterV2
    {
        void RegisterNpcAudioRoute(string npcBindingId, AudioSource source);
        void UnregisterNpcAudioRoute(string npcBindingId);
        bool TryGetNpcAudioRoute(string npcBindingId, out AudioSource source);
        bool SetActiveNpcRoute(string npcBindingId);
        string ActiveNpcBindingId { get; }
    }
}
