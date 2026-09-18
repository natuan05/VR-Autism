using System;
using UnityEngine;
using VRAutism.Cloud.LiveKit;

namespace VRAutism.Gameplay.LessonGraphV2.Integration
{
    /// <summary>
    /// Binds a scene AudioSource to a stable npc_binding_id in LiveKitService.
    /// Manages route registration and unregistration during GameObject lifecycle.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class NpcAudioRouteBindingV2 : MonoBehaviour
    {
        [Tooltip("Stable NPC route identifier matching participant.Identity and DialogueNodeConfig.NpcBindingId.")]
        [SerializeField] private string _npcBindingId = string.Empty;

        [SerializeField] private AudioSource _audioSource;

        private INpcAudioRouterV2 _customRouter;

        public string NpcBindingId => _npcBindingId;
        public AudioSource AudioSource => _audioSource;
        public bool IsBound { get; private set; }

        private INpcAudioRouterV2 Router => _customRouter ?? LiveKitService.Instance;

        private void Awake()
        {
            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }
        }

        private void OnEnable()
        {
            Register();
        }

        private void Start()
        {
            if (!IsBound)
            {
                Register();
            }
        }

        private void OnDisable()
        {
            Unregister();
        }

        private void OnDestroy()
        {
            Unregister();
        }

        public void Bind(string npcBindingId, AudioSource source, INpcAudioRouterV2 router = null)
        {
            Unregister();
            _npcBindingId = npcBindingId ?? string.Empty;
            _audioSource = source;
            _customRouter = router;
            Register();
        }

        public void SetRouter(INpcAudioRouterV2 router)
        {
            if (ReferenceEquals(_customRouter, router)) return;
            if (IsBound)
            {
                Unregister();
                _customRouter = router;
                Register();
            }
            else
            {
                _customRouter = router;
            }
        }

        public void Register()
        {
            if (IsBound) return;
            if (string.IsNullOrWhiteSpace(_npcBindingId)) return;
            if (_audioSource == null) _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null) return;

            var router = Router;
            if (router != null)
            {
                router.RegisterNpcAudioRoute(_npcBindingId, _audioSource);
                IsBound = true;
                Debug.Log($"[LessonGraphV2] NpcAudioRouteBindingV2: Registered route '{_npcBindingId}' on '{gameObject.name}'", this);
            }
        }

        public void Unregister()
        {
            if (!IsBound) return;
            var router = Router;
            if (router != null && !string.IsNullOrWhiteSpace(_npcBindingId))
            {
                router.UnregisterNpcAudioRoute(_npcBindingId);
                Debug.Log($"[LessonGraphV2] NpcAudioRouteBindingV2: Unregistered route '{_npcBindingId}' on '{gameObject.name}'", this);
            }
            IsBound = false;
        }
    }
}
