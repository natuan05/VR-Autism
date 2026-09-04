# Unity Editor Test Rules

## Known pitfalls

- Task/Unity-frame tests: use `[UnityTest] IEnumerator` + bounded helper (`CompleteWithinFrames`); avoid `[Test] IEnumerator` and unbounded `async Task` waits — EditMode may not pump continuations, causing hangs.
- Every fixture `TaskCompletionSource` needs explicit completion; timeout must fail test, never wait forever.
- Parameterized coroutine tests: split into separate `[UnityTest]` cases when Unity Test Framework adapter lacks support.
