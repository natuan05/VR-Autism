using UnityEngine;
using UnityEngine.SceneManagement;
using Firebase.Firestore;
using Firebase.Extensions;
using VRAutism.Core.Models;
using VRAutism.Gameplay.LessonGraphV2.Phrases;
using VRAutism.Gameplay.LessonGraphV2.Runtime;
using VRAutism.Gameplay.LessonGraphV2.AudioCache;
using System.Threading;

namespace VRAutism.Gameplay.WaitingArea{
    /// <summary>
    /// Trạm nhận lệnh trên màn hình Chờ VR (Lobby).
    /// Khi Web gửi lệnh "Bắt đầu bài học":
    /// 1. Fetch lesson metadata từ Firestore (lessons/{lessonId})
    /// 2. Lưu vào SessionContext
    /// 3. Gọi SceneManager.LoadScene
    /// </summary>
    public class SceneMenuController : MonoBehaviour
    {
        public static SceneMenuController Instance;

        [Header("TTS Cache Backend")]
        [Tooltip("HTTPS base URL supplied by the deployment. Do not use localhost on headset builds.")]
        [SerializeField] private string _ttsCacheBackendUrl;

        private bool _ownsSingleton;
        private bool _hasStarted;
        private bool _launchInProgress;
        private LessonLaunchAttemptV2 _activeLaunchAttempt;
        private Cloud.RTDB.PairingManager _subscribedPairingManager;
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                _ownsSingleton = false;
                enabled = false;
                return;
            }

            Instance = this;
            _ownsSingleton = true;
        }

        private void Start()
        {
            if (!_ownsSingleton || Instance != this || _hasStarted) return;
            _hasStarted = true;

            // Đảm bảo khi ở GameMenu, luồng LiveKit POV và Mic luôn tắt hoàn toàn
            if (Cloud.LiveKit.LiveKitService.Instance != null)
            {
                Cloud.LiveKit.LiveKitService.Instance.Disconnect();
            }

            _subscribedPairingManager = Cloud.RTDB.PairingManager.Instance;
            if (_subscribedPairingManager != null)
            {
                _subscribedPairingManager.OnNewSessionCommand += LoadRemoteLesson;
            }
        }

        private async void LoadRemoteLesson(string childId, string sceneName, string lessonId, string sessionId, string hostId, string livekitToken)
        {
            if (!_ownsSingleton || Instance != this) return;
            if (string.IsNullOrWhiteSpace(lessonId) || string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogWarning("[SceneMenuController] lessonId hoặc sceneName trống. Bỏ qua lệnh không đầy đủ.");
                return;
            }
            if (_launchInProgress)
            {
                Debug.LogWarning("[SceneMenuController] Lesson launch already in progress. Ignoring duplicate session command.");
                return;
            }

            var launchToken = System.Guid.NewGuid().ToString("N");
            if (!LessonLaunchCoordinatorV2.TryBeginLaunch(sessionId, lessonId, sceneName, launchToken, out var attempt))
            {
                Debug.LogWarning("[SceneMenuController] Another accepted or pending lesson launch owns the scene transition.");
                return;
            }
            _launchInProgress = true;
            _activeLaunchAttempt = attempt;
            AcceptedLessonLaunchV2 acceptedLaunch = null;
            SessionTtsSnapshotV1 ttsSnapshot = null;
            try
            {
                Debug.Log($"[SceneMenuController] Received lesson command. Child: {childId}, lesson: {lessonId}, scene: {sceneName}, session: {sessionId}");
                VoicePhraseSnapshotStoreV2.Clear();
                var effectiveTtsCacheBackendUrl = TtsCacheBackendUrlPolicyV1.ResolveBackendUrl(_ttsCacheBackendUrl);

                var cacheSchemaVersion = _subscribedPairingManager != null
                    ? _subscribedPairingManager.CurrentTtsCacheSchemaVersion : 0;
                if (cacheSchemaVersion < 0 || cacheSchemaVersion > 1)
                {
                    Debug.LogError("[TTS Cache] Unsupported RTDB tts_cache_schema_version; session launch is blocked.");
                    return;
                }
                if (cacheSchemaVersion == 1)
                {
                    try
                    {
                        var cacheBackend = new TtsCacheBackendClientV1(effectiveTtsCacheBackendUrl, livekitToken);
                        ttsSnapshot = await cacheBackend.GetRetainedSessionAsync(sessionId, CancellationToken.None);
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogError("[TTS Cache] Retained session configuration could not be loaded; launch is blocked: " + exception.Message);
                        return;
                    }
                    if (!LessonLaunchCoordinatorV2.IsCurrentAttempt(attempt)) return;
                }

                var db = FirebaseFirestore.DefaultInstance;
                var lessonTask = db.Collection(Cloud.FirebasePaths.Lessons).Document(lessonId).GetSnapshotAsync();
                var childTask = string.IsNullOrEmpty(childId)
                    ? System.Threading.Tasks.Task.FromResult<DocumentSnapshot>(null)
                    : db.Collection("child_profiles").Document(childId).GetSnapshotAsync();
                var childPhraseTask = string.IsNullOrEmpty(childId)
                    ? System.Threading.Tasks.Task.FromResult<DocumentSnapshot>(null)
                    : db.Collection(Cloud.FirebasePaths.ChildPhraseSets).Document($"{childId}__{lessonId}").GetSnapshotAsync();

                DocumentSnapshot doc = null;
                try { doc = await lessonTask; }
                catch (System.Exception exception)
                {
                    Debug.LogError($"[SceneMenuController] Lesson metadata fetch failed: {exception.Message}");
                }
                if (!LessonLaunchCoordinatorV2.IsCurrentAttempt(attempt)) return;

                var lessonName = sceneName;
                var levelName = "Vô danh";
                var levelIndex = 0;
                var lessonType = string.Empty;
                if (doc != null && doc.Exists)
                {
                    lessonName = doc.ContainsField("lesson_name") ? doc.GetValue<string>("lesson_name") : sceneName;
                    levelName = doc.ContainsField("level_name") ? doc.GetValue<string>("level_name") : "Vô danh";
                    lessonType = doc.ContainsField("type") ? doc.GetValue<string>("type") : string.Empty;
                    if (doc.ContainsField("level_index"))
                    {
                        try { levelIndex = System.Convert.ToInt32(doc.GetValue<object>("level_index")); }
                        catch { levelIndex = 0; }
                    }
                }
                else Debug.LogWarning($"[SceneMenuController] Lesson document '{lessonId}' is unavailable; using the provided scene metadata.");

                bool isV2Lesson = false;
                bool invalidVoiceSchema = false;
                try
                {
                    isV2Lesson = doc != null && doc.Exists && doc.ContainsField("voice_schema_version") &&
                        System.Convert.ToInt32(doc.GetValue<object>("voice_schema_version")) >= 2;
                }
                catch (System.Exception exception)
                {
                    invalidVoiceSchema = true;
                    Debug.LogError($"[LessonGraphV2] Lesson voice schema metadata is invalid: {exception.Message}");
                }
                if (invalidVoiceSchema)
                {
                    VoicePhraseSnapshotStoreV2.Clear();
                    return;
                }

                bool v2SnapshotReady = !isV2Lesson;
                if (isV2Lesson)
                {
                    try
                    {
                        var lessonQuests = new System.Collections.Generic.List<VoiceQuestPhraseV2>();
                        var rawQuests = doc.GetValue<object>("quests") as System.Collections.IList;
                        if (rawQuests == null) throw new System.Exception("V2 lesson quests are missing.");
                        foreach (var rawQuest in rawQuests)
                        {
                            if (!(rawQuest is System.Collections.IDictionary map))
                                throw new System.Exception("V2 lesson quest is not a map.");
                            lessonQuests.Add(new VoiceQuestPhraseV2
                            {
                                binding_id = map["binding_id"]?.ToString(),
                                goal = map["goal"]?.ToString() ?? string.Empty,
                                default_phrases = ToStringList(map["default_phrases"])
                            });
                        }

                        var additions = new System.Collections.Generic.List<VoiceQuestPhraseAdditionV2>();
                        DocumentSnapshot phraseDoc = null;
                        try { phraseDoc = await childPhraseTask; }
                        catch (System.Exception exception)
                        {
                            Debug.LogWarning($"[LessonGraphV2] Child phrase additions unavailable; using defaults: {exception.Message}");
                        }
                        if (!LessonLaunchCoordinatorV2.IsCurrentAttempt(attempt)) return;
                        if (phraseDoc != null && phraseDoc.Exists && phraseDoc.ContainsField("quest_additions") &&
                            phraseDoc.GetValue<object>("quest_additions") is System.Collections.IList rawAdditions)
                        {
                            foreach (var rawAddition in rawAdditions)
                            {
                                if (!(rawAddition is System.Collections.IDictionary map)) continue;
                                additions.Add(new VoiceQuestPhraseAdditionV2
                                {
                                    binding_id = map["binding_id"]?.ToString(),
                                    phrases = ToStringList(map["phrases"])
                                });
                            }
                        }

                        int lessonRevision = doc.ContainsField("voice_revision")
                            ? System.Convert.ToInt32(doc.GetValue<object>("voice_revision")) : 0;
                        int childRevision = phraseDoc != null && phraseDoc.Exists && phraseDoc.ContainsField("revision")
                            ? System.Convert.ToInt32(phraseDoc.GetValue<object>("revision")) : 0;
                        new FirestoreVoicePhraseLoaderV2().ResolveSessionSnapshot(launchToken, lessonId,
                            lessonRevision, childRevision, lessonQuests, additions);
                        v2SnapshotReady = true;
                        Debug.Log($"[LessonGraphV2] Matching phrase snapshot ready for lesson='{lessonId}' with {lessonQuests.Count} bindings.");
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogError($"[LessonGraphV2] Phrase snapshot failed; V2 scene activation is blocked: {exception.Message}");
                    }
                }

                float childMaxVolume = 0.5f;
                var lessonParams = LessonParameters.Default;
                var activePhrases = new System.Collections.Generic.List<string[]>();
                try
                {
                    DocumentSnapshot childDoc = await childTask;
                    if (!LessonLaunchCoordinatorV2.IsCurrentAttempt(attempt)) return;
                    if (childDoc != null && childDoc.Exists)
                    {
                        bool hasMaxVolume = false;
                        if (childDoc.ContainsField("max_volume"))
                        {
                            try { childMaxVolume = System.Convert.ToSingle(childDoc.GetValue<object>("max_volume")); hasMaxVolume = true; }
                            catch { }
                        }
                        else if (childDoc.ContainsField("maxVolume"))
                        {
                            try { childMaxVolume = System.Convert.ToSingle(childDoc.GetValue<object>("maxVolume")); hasMaxVolume = true; }
                            catch { }
                        }
                        if (!hasMaxVolume && childDoc.ContainsField("sound_sensitivity"))
                        {
                            try
                            {
                                int sensitivity = System.Convert.ToInt32(childDoc.GetValue<object>("sound_sensitivity"));
                                childMaxVolume = Mathf.Clamp(1.0f - sensitivity * 0.15f, 0.1f, 1.0f);
                            }
                            catch { }
                        }

                        System.Collections.Generic.Dictionary<string, object> lessonParamsMap = null;
                        if (childDoc.ContainsField("default_lesson_params"))
                        {
                            try { lessonParamsMap = childDoc.GetValue<System.Collections.Generic.Dictionary<string, object>>("default_lesson_params"); }
                            catch
                            {
                                var rawMap = childDoc.GetValue<object>("default_lesson_params");
                                if (rawMap is System.Collections.IDictionary map)
                                {
                                    lessonParamsMap = new System.Collections.Generic.Dictionary<string, object>();
                                    foreach (System.Collections.DictionaryEntry entry in map)
                                        lessonParamsMap[entry.Key.ToString()] = entry.Value;
                                }
                            }
                        }
                        if (lessonParamsMap != null && lessonParamsMap.Count > 0)
                            lessonParams = LessonParameters.FromDictionary(lessonParamsMap);

                        if (childDoc.ContainsField("quick_phrases") &&
                            childDoc.GetValue<object>("quick_phrases") is System.Collections.IDictionary quickPhraseMap &&
                            quickPhraseMap.Contains(lessonId) && quickPhraseMap[lessonId] is System.Collections.IList quickPhraseQuests)
                        {
                            foreach (var quest in quickPhraseQuests)
                            {
                                if (quest is System.Collections.IDictionary questMap && questMap.Contains("phrases") &&
                                    questMap["phrases"] is System.Collections.IList phrases)
                                {
                                    var phraseList = new System.Collections.Generic.List<string>();
                                    foreach (var phrase in phrases) if (phrase != null) phraseList.Add(phrase.ToString());
                                    activePhrases.Add(phraseList.ToArray());
                                }
                            }
                        }
                    }
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"[SceneMenuController] Child profile unavailable; using lesson defaults: {exception.Message}");
                }
                if (!LessonLaunchCoordinatorV2.IsCurrentAttempt(attempt)) return;

                if (activePhrases.Count == 0 && doc != null && doc.Exists && doc.ContainsField("quests") &&
                    doc.GetValue<object>("quests") is System.Collections.IList lessonPhraseQuests)
                {
                    foreach (var quest in lessonPhraseQuests)
                    {
                        if (quest is System.Collections.IDictionary questMap && questMap.Contains("default_phrases") &&
                            questMap["default_phrases"] is System.Collections.IList phrases)
                        {
                            var phraseList = new System.Collections.Generic.List<string>();
                            foreach (var phrase in phrases) if (phrase != null) phraseList.Add(phrase.ToString());
                            activePhrases.Add(phraseList.ToArray());
                        }
                    }
                }

                if (isV2Lesson && !v2SnapshotReady)
                {
                    Debug.LogError("[LessonGraphV2] Scene activation blocked because required phrase metadata is unavailable.");
                    VoicePhraseSnapshotStoreV2.Clear();
                    return;
                }
                var sessionContext = VRAutism.Core.SessionContext.Instance;
                if (isV2Lesson && sessionContext == null)
                {
                    Debug.LogError("[LessonGraphV2] Scene activation blocked because SessionContext is unavailable.");
                    VoicePhraseSnapshotStoreV2.Clear();
                    return;
                }
                if (isV2Lesson && !LessonLaunchCoordinatorV2.TryAcceptLaunch(attempt, childId, hostId,
                    out acceptedLaunch, out var acceptReason, ttsSnapshot))
                {
                    Debug.LogError("[LessonGraphV2] Scene activation blocked: " + acceptReason);
                    VoicePhraseSnapshotStoreV2.Clear();
                    return;
                }

                if (sessionContext != null)
                {
                    sessionContext.SessionId = sessionId;
                    sessionContext.ChildId = childId;
                    sessionContext.LessonId = lessonId;
                    sessionContext.HostId = hostId ?? string.Empty;
                    sessionContext.LiveKitToken = livekitToken ?? string.Empty;
                    sessionContext.LessonName = lessonName;
                    sessionContext.LevelName = levelName;
                    sessionContext.LevelIndex = levelIndex;
                    sessionContext.LessonType = lessonType;
                    sessionContext.MaxVolume = childMaxVolume;
                    sessionContext.CurrentParams = lessonParams;
                    sessionContext.SetActivePhrases(activePhrases);
                    sessionContext.TtsCacheBackendUrl = effectiveTtsCacheBackendUrl ?? string.Empty;
                    sessionContext.SetTtsCacheSnapshot(ttsSnapshot);

                    // The audio coordinator survives scene changes; bind it before the next room's HELLO arrives.
                    var fixedAudio = ttsSnapshot != null
                        ? FixedAudioPlaybackCoordinatorV1.Instance
                        : FindObjectOfType<FixedAudioPlaybackCoordinatorV1>();
                    if (fixedAudio != null && (ttsSnapshot == null || fixedAudio.SessionId != sessionId))
                        fixedAudio.Configure(ttsSnapshot, effectiveTtsCacheBackendUrl, livekitToken,
                            Cloud.LiveKit.LiveKitService.Instance, Cloud.LiveKit.LiveKitService.Instance);
                    if (ttsSnapshot != null)
                        Debug.Log($"[TTS Audio] event=SESSION_BOUND session={sessionId}");
                }

                var load = SceneManager.LoadSceneAsync(sceneName);
                if (load == null)
                {
                    LessonLaunchCoordinatorV2.CancelAcceptedLaunch(acceptedLaunch);
                    if (VoicePhraseSnapshotStoreV2.LaunchToken == launchToken) VoicePhraseSnapshotStoreV2.Clear();
                    Debug.LogError($"[SceneMenuController] Scene load could not be queued: {sceneName}");
                    return;
                }
                Debug.Log($"[SceneMenuController] Loading lesson scene: {sceneName}");
            }
            catch (System.Exception exception)
            {
                LessonLaunchCoordinatorV2.CancelAcceptedLaunch(acceptedLaunch);
                LessonLaunchCoordinatorV2.CancelAttempt(attempt);
                if (VoicePhraseSnapshotStoreV2.LaunchToken == launchToken) VoicePhraseSnapshotStoreV2.Clear();
                Debug.LogError($"[SceneMenuController] Lesson launch failed before scene activation: {exception}");
            }
            finally
            {
                LessonLaunchCoordinatorV2.CancelAttempt(attempt);
                if (acceptedLaunch == null && VoicePhraseSnapshotStoreV2.LaunchToken == launchToken)
                    VoicePhraseSnapshotStoreV2.Clear();
                if (ReferenceEquals(_activeLaunchAttempt, attempt)) _activeLaunchAttempt = null;
                _launchInProgress = false;
            }
        }
        private static System.Collections.Generic.List<string> ToStringList(object raw)
        {
            var values = new System.Collections.Generic.List<string>();
            if (!(raw is System.Collections.IList list)) return values;
            foreach (var item in list) if (item != null) values.Add(item.ToString());
            return values;
        }

        private void OnDestroy()
        {
            if (_activeLaunchAttempt != null)
            {
                LessonLaunchCoordinatorV2.CancelAttempt(_activeLaunchAttempt);
                _activeLaunchAttempt = null;
            }
            if (_subscribedPairingManager != null)
            {
                _subscribedPairingManager.OnNewSessionCommand -= LoadRemoteLesson;
                _subscribedPairingManager = null;
            }

            if (Instance == this) Instance = null;
            _ownsSingleton = false;
        }
    }
}
