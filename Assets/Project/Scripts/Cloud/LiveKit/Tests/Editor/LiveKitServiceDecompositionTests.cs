using System.Collections.Generic;
using NUnit.Framework;

namespace VRAutism.Cloud.LiveKit.Tests.Editor
{
    public sealed class LiveKitServiceDecompositionTests
    {
        [Test]
        public void MainThreadExecutor_PreservesOrderAndDropsStaleCommands()
        {
            var executor = new LiveKitMainThreadExecutor();
            var calls = new List<string>();

            executor.AdvanceGeneration(2);
            Assert.IsTrue(executor.Post(1, () => calls.Add("stale")));
            Assert.IsTrue(executor.Post(2, () => calls.Add("first")));
            Assert.IsTrue(executor.Post(2, () => calls.Add("second")));
            executor.Drain();

            CollectionAssert.AreEqual(new[] { "first", "second" }, calls);
            executor.Close();
            Assert.IsFalse(executor.Post(2, () => calls.Add("closed")));
        }
    }
}
