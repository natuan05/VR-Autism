# Story 2.4: Loop presentation and independent Dialogue fixes

Authoritative checkout: `D:/Lab/VR-Autism`, branch `main`. All prior implementation and user changes remain uncommitted. The detached `C:/Users/Admin/.codex/worktrees/d51e/VR-Autism` backup was not updated. Do not copy its older files over main.

## Confirmed causes and changes

The reported wet-hands sequence canceled its losing voice quest before entering `wash-prompt-dispense-soap`. The new Dialogue activation was independent, but Python required every SPEAK_SCRIPT activation to match the currently active voice quest. Dialogue is now owned by the script command runtime: unknown independent activations are accepted, known terminal voice activations are rejected, and known active voice activations still require the matching NPC. No artificial SET_ACTIVE_QUEST is sent and the voice quest state is not mutated for independent Dialogue.

Exact activation/sequence/NPC cancellation tombstones cover cancel-before-script delivery. Unity Dialogue sends the existing CANCEL_SPEAK_SCRIPT contract for transmitted requests. A bounded cancellation-only queue in the persistent LiveKitService survives offline transport destruction and drains before reconnect callbacks. Requests never published while offline become terminal locally without filling this queue. A regression covers one transmitted request followed by 65 offline requests and cancellations.

Loop children already had unique activations, but the runner exposed only the parent node and empty bindings. The runner now observes the exact ready child Quest using a local lease, publishes its capabilities through the existing parent state bindings, and maps hint effects to that child activation. Parent command/state identities remain unchanged. The presenter selects sources only from this scope, prefers a contacted Hold source for progress, and clears presentation when the child exits. Pause/rearm, stale command rejection and Loop cleanup are included in the regression. Parallel child observation remains disabled.

## Changed files in this bugfix

Unity:

- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Runtime/LessonGraphRunner.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Runtime/StructuredFlowExecution.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Presentation/LessonGraphHintPresenterV2.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Runtime/Dialogue/LiveKitDialogueTransportV2.cs`
- `Assets/Project/Scripts/Cloud/LiveKit/ILiveKitDataPacketClientV2.cs`
- `Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/QuestHintRoutingV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LiveKitDialogueTransportV2Tests.cs`
- `Assets/Project/Scripts/Cloud/LiveKit/Tests/Editor/LiveKitServiceDecompositionTests.cs`

Python:

- `LiveKitAgent/src/agent_v2.py`
- `LiveKitAgent/src/voice_command_runtime_v2.py`
- `LiveKitAgent/tests/test_speak_script_v2.py`

This handoff is new. The whole HEAD diff includes earlier Story 2.4 changes and user changes; it is not a bugfix-only patch. No scene, font, vendor asset, package, web file or parity matrix was edited for these fixes.

## Verification and review

- Two scoped implementations and one independent review for each coherent change set. Loop review had no findings. Dialogue review found offline destruction and queue overflow issues; both were fixed and their material changes were independently reviewed without remaining findings.
- Root verification: 42 focused Python tests passed across `test_speak_script_v2.py`, `test_agent_v2.py`, `test_verbal_hint_v2.py`, and `test_voice_v2_reconciliation.py`. Ruff passed for the two source files and script test file. No dependencies were installed.
- Scoped tracked bugfix `git diff --check` passed. Whole-repository `git diff --check` reports trailing whitespace in user-modified `Bathroom-V2.unity`; that scene content was preserved.
- Pre-edit symbol impact reports were generally LOW. LiveKitService class was CRITICAL due shared interfaces and handshake flows; PublishCurrent was HIGH. Both warnings were reported before proceeding. Ordinary publish and media paths were preserved.
- Full index refresh completed: 28,572 nodes, 51,928 edges and 300 flows. The CLI run from main (`detect-changes --scope all --repo D:/Lab/VR-Autism`) reports 42 changed files, 374 changed symbols, 33 affected processes, CRITICAL. This covers the whole dirty checkout, not just these bugfixes. MCP `detect_changes` returned the detached checkout's older 34-file scope despite the repo selector; the explicit main-checkout CLI result is authoritative. New/untracked files are listed above separately.

## User-run verification

Unity compilation, EditMode execution and headset behavior have not been run by the agents. Restart the Python agent to load its code before retesting.

Smallest focused EditMode filters:

- `QuestHintRoutingV2Tests.LoopQuestChildUsesExactActivationForHintAndHoldProgressPresentation`
- `LiveKitDialogueTransportV2Tests`
- `DialogueNodeExecutorTests`
- `LiveKitServiceDecompositionTests.DeferredSpeakScriptCancellation_DrainsBeforeInitialAndReconnectCallbacks`

In Bathroom-V2, enter wet-hands Loop. Confirm configured automatic/manual visual hint appears, loading progress increases during hand contact and resets on release. Pause/resume should rearm the child without leaving stale progress or hints. Complete Hold first so Voice loses, then confirm `wash-prompt-dispense-soap` is spoken and Dialogue advances. Also check skip/timeout, disconnect during speech followed by transport destruction and reconnect, and that canceled speech does not restart. Run one LearnToAsk-V2 Dialogue transition as the direct-Dialogue regression check.

Queue limitation: the existing cancellation payload has no room/session key; pending cancellation flushes on the next successful connection of the same persistent service. Current runner activation IDs are GUID-scoped. No wire schema was changed to add room identity.

No commit, staging, Unity launch, deployment or legacy-controller removal was performed.

## Follow-up: EditMode failures and legacy radial progress UI

The user supplied `C:/Users/Admin/Downloads/TestResults_20261008_003803.xml`: 55 executed tests failed. This is not a complete result for the 719 discovered tests. Only proven fixture/API failures were repaired in this pass; the report's other pause/resume, telemetry, routing and outline-cleanup assertions remain unverified.

The generated rectangular progress bar was 1.1 metres wide. Its fill Image had no sprite: bundled uGUI Image.OnPopulateMesh uses the base full-quad rendering path when activeSprite is null, bypassing filled geometry even when Slider updates fillAmount. The replacement clones an inactive legacy Bathroom UI template with the original ring sprite, colours, World Space Canvas and radial slider. Its 540-pixel slider at 0.0002 canvas scale has a 0.108-metre diameter. No generated rectangular fallback remains.

`Bathroom-V2.unity` now contains `LessonGraphHoldProgressTemplateV2`, referenced by the presenter's `Progress Prefab` field. Runtime progress operates on its clone. Contact loss resets the slider and radial fill to zero and hides the retained instance; source exit/teardown clears the owned UI. The template stays inactive and can be styled in Inspector. To adjust it, select the template's Canvas scale or its child Slider size. The legacy defaults are scale `(0.0002, 0.0002, 0.0002)` and Slider size `(540, 540)`. Keep Fill's Source Image `rec.png`, Image Type `Filled`, Fill Method `Radial360`, and the Slider's Fill Rect pointing to that Image. Adjust placement using each Hold source's Hint Progress Anchor.

Presenter-owned UI uses Destroy in PlayMode and DestroyImmediate in EditMode. LiveKit singleton access and explicit Awake avoid DontDestroyOnLoad and runtime service setup in EditMode; the PlayMode persistence and initialization path remains unchanged.

The fixture repairs address inherited private-field reflection, explicit singleton/lifecycle initialization and restoration, the telemetry installer assertion's actual field type, and separate Hold/Voice child objects required by QuestSourceV2's DisallowMultipleComponent. The already-active indicator assertion now matches its constructed active object. Two intentional-error tests expect their precise Error logs while retaining failed-result assertions.

Files changed in this follow-up:

- `Assets/Project/Scenes/Bathroom-V2.unity` (additive inactive radial template and presenter reference)
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Presentation/LessonGraphHintPresenterV2.cs`
- `Assets/Project/Scripts/Cloud/LiveKit/LiveKitService.cs`
- `Assets/Project/Scripts/Cloud/LiveKit/Tests/Editor/LiveKitServiceDecompositionTests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphHintPresenterV2Tests.cs` and `.meta` (new)
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphSensorLifecycleV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/StructuredFlowExecutionTests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonTelemetryAdapterV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/QuestHintRoutingV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/QuestSourceV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/HoldTouchQuestSourceV2Tests.cs`
- `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/LessonGraphRunnerTests.cs`
- This handoff.

One independent review found a deterministic mismatch between a null-root test assertion and retained hidden UI. The material fix now asserts retained/inactive UI with both slider value and radial fill at zero; independent re-review found no remaining issue. All pre-edit impacts in this follow-up were LOW. Full index refresh completed: 28,597 nodes, 51,982 edges, 300 flows. Final main-checkout CLI detect-changes reports 44 changed files, 393 symbols and 40 affected processes, CRITICAL across the full dirty tree. Scoped code/test whitespace checks pass; unrelated pre-existing scene whitespace remains preserved. Unity compilation, test execution and headset appearance remain unverified.

Smallest initial user-run EditMode filters:

- `LessonGraphHintPresenterV2Tests.SetProgress_UsesSpriteBackedRadialFillAndClearsOwnedUiInEditMode`
- `QuestHintRoutingV2Tests.LoopQuestChildUsesExactActivationForHintAndHoldProgressPresentation`
- `LiveKitServiceDecompositionTests.InstanceAndAwake_AreSafeInEditModeWithoutInitializingRuntimeServices`
- `StructuredFlowExecutionTests.ExecuteLoop_ReactivatesHoldAndVoiceSourcesAndSendsSecondVoiceActivation`

Then rerun the affected fixture/error tests: `LessonGraphSensorLifecycleV2Tests`, `LessonTelemetryAdapterV2Tests.FailedReconfigure_DoesNotTransferOwnerFromPreviouslyConfiguredRunner`, `QuestSourceV2Tests.VisualHintPreservesAnIndicatorThatWasAlreadyActive`, `HoldTouchQuestSourceV2Tests.EmptyOrPrunedContacts_ResetDwellAndInvalidDurationFailsDuringActivation`, and `LessonGraphRunnerTests.StartLessonAsync_ExecutorExceptionBecomesFailedNodeResult`.

In the headset, verify the small blue ring appears at the configured hand/object anchor, visibly fills during continuous hold, returns to empty on release, and is absent after completion/pause. This appearance and real contact timing require the user's Unity/headset run.

## Scene reference repair after user import failure

Unity reported broken local PPtr `1489955202` and unloaded Transform children when opening Bathroom-V2. The earlier template copy omitted the legacy `Fill Area` GameObject and RectTransform while retaining their references; the previous static review missed this. Added the two missing documents with new IDs `5900000000000002019` and `5900000000000002020`, and rewired the Slider child and Fill parent to the new area. The template now has 20 documents. This repair changes only `Bathroom-V2.unity`; no C# symbols were edited.

Full scene validation, including stripped prefab headers, finds 299 unique local document IDs, zero missing local references and zero duplicate IDs. Independent review confirmed reciprocal parent/child links, component ownership, the Slider Fill Rect, stretch geometry and SceneRoots registration. The existing unrelated scene whitespace was preserved. Reopen Bathroom-V2 in Unity to confirm import and then run the radial progress filters above; agents have not launched Unity.

## 2026-10-09: exact legacy Bathroom bubble hint copy

The user canceled the proposed camera-orientation investigation and identified the actual mismatch: Bathroom-V2 used the generic SpeechBubblePrefab instead of legacy Bathroom's BubbleQuestion UI. No camera or billboard script changes were made.

Copied the complete legacy BubbleQuestion subtree into Bathroom-V2 as inactive `BubbleQuestionHintTemplateV2`: ten documents with IDs `6300000000000004001` through `6300000000000004010`. The source image sprite, white colour, preserve-aspect setting, 256-by-256 image size, 1080-by-1080 World Space Canvas and 0.0002 scale are preserved. Only IDs, the template's name, its detached root parent and its obsolete cross-scene camera reference were normalized. Existing BillboardEffect uses its unchanged Camera.main fallback.

Bathroom-V2's presenter Bubble Prefab reference now points to the scene template. Existing runtime cloning, anchors and lifecycle logic remain unchanged. The legacy Bathroom scene, LearnToAsk-V2, shared prefab assets, progress/water behavior and parity matrices were not edited for this change.

Changed files: `Assets/Project/Scenes/Bathroom-V2.unity`, new `Assets/Project/Scripts/Gameplay/LessonGraphV2/Tests/Editor/BathroomV2BubbleHintSceneTests.cs` and `.meta`, plus this handoff. The test compares all ten normalized source documents, checks imported sprite/wiring using an isolated preview scene, and validates complete local PPtr closure including stripped prefab headers. The imported legacy sprite must be non-null. Preview-scene cleanup runs in finally.

Static checks: ten copied documents match the source after the stated normalization; 308 unique scene document IDs, zero missing local references, zero duplicate IDs. The new block and test are whitespace-clean; whole dirty-scene diff still reports pre-existing whitespace. Independent review and focused test-fixture re-review are clean. Final main-checkout detect-changes was run; broad pre-existing workspace risk remains CRITICAL. The new test's impact lookup was not found/UNKNOWN because it is unindexed; no existing production C# symbol was edited.

Unity compilation, EditMode execution and headset appearance remain pending. Focused filter: `BathroomV2BubbleHintSceneTests`. After compilation, open Bathroom-V2 and compare the question-mark bubble at the configured hint anchors against legacy Bathroom, including size and disappearance on completion/pause. Inspector template name: `BubbleQuestionHintTemplateV2`; it remains inactive because the presenter displays a clone. Changes are on main and uncommitted.
