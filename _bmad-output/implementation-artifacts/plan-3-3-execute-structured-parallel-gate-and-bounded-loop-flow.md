# Story 3.3 Implementation Plan

**Goal:** Run structured Parallel, Gate, and bounded Loop nodes safely in schema-2 LessonGraph V2.

**Architecture:** A focused runtime helper owns child tasks and branch cancellation; `LessonGraphRunner` continues to own parent activations, result commitment, and graph routing. A typed variable source feeds a condition evaluator. Execution preflight admits only graphs this runtime can safely execute.

**Tech stack:** Unity C#, `Task`, `CancellationTokenSource`, NUnit EditMode tests.

**Spec:** `spec-3-3-execute-structured-parallel-gate-and-bounded-loop-flow.md` beside this plan.

## Global constraints

Use only the existing managed worktree on `codex/story-3-2`; preserve all Story 3.2 work and the sprint-status change. Read root and test-folder `AGENTS.md`. Run GitNexus upstream `impact` and report blast radius before editing each existing symbol. No Unity batch/full suite, commit, push, scene, vendor, checkpoint/resume, editor, LiveKit, Firebase, Python, or web changes. Keep Story 3.3 in review pending Unity evidence.

## Review focus

- A child returning a wrong node or activation ID fails its branch and cannot win a join.
- A cancelled sibling completing later cannot alter the Gate result.
- An OR Gate with a failed first branch and a successful later branch behaves according to its Parallel join policy.
- A Loop at its limit follows failure routing and does not start an extra body activation.
- A missing or wrongly typed variable cannot accidentally satisfy a condition.

## Task 1: Typed condition evaluation

**Files:** Add `Runtime/LessonVariableValue.cs`, `Runtime/LessonConditionEvaluator.cs` and `.meta`; test `Tests/Editor/StructuredFlowConditionTests.cs` and `.meta`.

**Interfaces:** `ILessonVariableSource.TryGetValue(string name, out LessonVariableValue value)`; `LessonConditionEvaluator.Evaluate(IEdgeCondition condition, NodeStatus status, ILessonVariableSource source)`.

- [ ] Write focused tests for every primitive/operator, missing/type mismatch, nested AND/OR, and status/always conditions.
- [ ] Implement typed value union and recursive evaluator. Keep string comparison ordinal and avoid boxing/dictionary in serialized graph data.
- [ ] Inspect source and tests; Unity execution stays unverified.

## Task 2: Structured owned execution

**Files:** Add `Runtime/StructuredFlowExecution.cs` and `.meta`; test `Tests/Editor/StructuredFlowExecutionTests.cs` and `.meta`.

**Interfaces:** Runtime helper accepts graph, registry, clock, variable source, telemetry, and a visit callback. `ExecuteParallelAsync(NodeExecutionContext)` returns one parent result plus immutable named branch evidence; `ExecuteGate(NodeExecutionContext, evidence)` performs AND/OR; `ExecuteLoopAsync(NodeExecutionContext)` runs owned body iterations. Every owned child receives a fresh activation ID and linked cancellation scope.

- [ ] Test `AllSuccess`, `FirstCompleted`, out-of-order, failure, invalid result, duplicate/stale completion, and cancellation with late child completion.
- [ ] Test Gate AND/OR, absent and duplicate evidence, Loop early exit and exact limit, visit callback counts.
- [ ] Implement parent-owned task lifecycle, cancel losers, validate result IDs, observe abandoned faults, and never follow child edges.

## Task 3: Runner integration and preflight

**Files:** Modify `Runtime/LessonGraphRunner.cs`, `Validation/LessonGraphValidator.cs`, `Tests/Editor/AdvancedGraphRunnerTests.cs`; add or extend focused integration tests.

**Interfaces:** Runner `Configure` accepts optional typed variable source; production graphs with variable conditions require a source. Runner special-cases structured nodes without registry entries, routes successful Parallel to Gate and successful Loop to configured exit, and exposes per-run node/edge visit counts. Existing schema-1 execution and Story 3.2 Timeline registry path remain unchanged.

- [ ] Test full Parallel→Gate and Loop→exit/limit routes, public parent completion exactly once, abort/pause cleanup, and schema-1 regression.
- [ ] Update preflight to allow supported structured execution and reject unsafe Gate entry/ownership or unsupported nesting before activation.
- [ ] Integrate helper into runner activation loop, count visits, and preserve status-edge precedence.

## Handoff

- [ ] Inspect scoped diff and status, run `git diff --check` and GitNexus `detect_changes` after refreshing the index if major new files are added.
- [ ] Obtain one independent review; repair material findings and re-review only those fixes.
- [ ] Leave Story 3.3 in `review`; list changed files, unverified Unity behavior, smallest EditMode filters, and required manual checks in the worktree Unity project.
