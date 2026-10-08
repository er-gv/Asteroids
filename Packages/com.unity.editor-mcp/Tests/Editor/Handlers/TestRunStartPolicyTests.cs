using NUnit.Framework;
using UnityEditorMCP.Handlers;

namespace UnityEditorMCP.Tests.Handlers
{
    [TestFixture]
    public class TestRunStartPolicyTests
    {
        private const double Stale = 300.0;

        [Test]
        public void NothingRunningStartsImmediately()
        {
            var decision = TestRunStartPolicy.Evaluate(false, false, -1, false, Stale, string.Empty);

            Assert.AreEqual(TestRunStartAction.Start, decision.Action);
        }

        [Test]
        public void ForceIsIrrelevantWhenNothingIsRunning()
        {
            var decision = TestRunStartPolicy.Evaluate(false, true, -1, false, Stale, string.Empty);

            Assert.AreEqual(TestRunStartAction.Start, decision.Action);
        }

        [Test]
        public void LiveRunWithoutForceIsRefused()
        {
            var decision = TestRunStartPolicy.Evaluate(true, false, 12, false, Stale, "guid-1");

            Assert.AreEqual(TestRunStartAction.Refuse, decision.Action);
            StringAssert.Contains("guid-1", decision.Reason);
            StringAssert.Contains("cancel_tests", decision.Reason);
            StringAssert.Contains("force: true", decision.Reason);
        }

        [Test]
        public void SilentButRecentlyReloadedRunIsStillLive()
        {
            // A PlayMode run reloads the domain and then keeps reporting: the reload alone proves
            // nothing while progress is fresh.
            var decision = TestRunStartPolicy.Evaluate(true, false, 5, true, Stale, "guid-1");

            Assert.AreEqual(TestRunStartAction.Refuse, decision.Action);
        }

        [Test]
        public void LongSilentRunInTheSameDomainIsStillLive()
        {
            // One test that runs for ten minutes looks exactly like a lost run, except that the domain
            // reporting it is still here.
            var decision = TestRunStartPolicy.Evaluate(true, false, 600, false, Stale, "guid-1");

            Assert.AreEqual(TestRunStartAction.Refuse, decision.Action);
        }

        [Test]
        public void ForceOnALiveRunMustCancelFirst()
        {
            var decision = TestRunStartPolicy.Evaluate(true, true, 12, false, Stale, "guid-1");

            Assert.AreEqual(TestRunStartAction.CancelThenStart, decision.Action);
        }

        [Test]
        public void ProvablyGoneRunIsDiscardedWithoutForce()
        {
            var decision = TestRunStartPolicy.Evaluate(true, false, 600, true, Stale, "guid-1");

            Assert.AreEqual(TestRunStartAction.DiscardAndStart, decision.Action);
            Assert.AreEqual("aborted", decision.DisplacedRunStatus);
        }

        [Test]
        public void RunWithNoProgressMarkAtAllIsProvablyGone()
        {
            var decision = TestRunStartPolicy.Evaluate(true, false, -1, false, Stale, string.Empty);

            Assert.AreEqual(TestRunStartAction.DiscardAndStart, decision.Action);
            Assert.AreEqual("aborted", decision.DisplacedRunStatus);
        }

        [Test]
        public void ProvablyGoneRunSkipsTheCancelAttemptEvenWithForce()
        {
            var decision = TestRunStartPolicy.Evaluate(true, true, 600, true, Stale, "guid-1");

            Assert.AreEqual(TestRunStartAction.DiscardAndStart, decision.Action);
        }

        [Test]
        public void AcceptedCancellationLetsTheNewRunStart()
        {
            var decision = TestRunStartPolicy.EvaluateAfterCancelAttempt(
                true, "cancelled", 12, false, Stale, "guid-1");

            Assert.AreEqual(TestRunStartAction.DiscardAndStart, decision.Action);
            Assert.AreEqual("cancelled", decision.DisplacedRunStatus);
        }

        [Test]
        public void RefusedCancellationOnALiveRunRefusesTheNewRun()
        {
            var decision = TestRunStartPolicy.EvaluateAfterCancelAttempt(
                false,
                "TestRunnerApi.CancelTestRun is not available in this Unity Test Framework version",
                12,
                false,
                Stale,
                "guid-1");

            Assert.AreEqual(TestRunStartAction.Refuse, decision.Action);
            StringAssert.Contains("guid-1", decision.Reason);
            StringAssert.Contains("CancelTestRun is not available", decision.Reason);
            StringAssert.Contains("Test Runner window", decision.Reason);
        }

        [Test]
        public void RefusedCancellationStillDiscardsAProvablyGoneRun()
        {
            var decision = TestRunStartPolicy.EvaluateAfterCancelAttempt(
                false, "the Test Runner rejected the cancellation", 600, true, Stale, "guid-1");

            Assert.AreEqual(TestRunStartAction.DiscardAndStart, decision.Action);
            Assert.AreEqual("aborted", decision.DisplacedRunStatus);
        }

        [Test]
        public void RefusalExplainsItselfWhenNoGuidWasRecorded()
        {
            var decision = TestRunStartPolicy.EvaluateAfterCancelAttempt(
                false, "no run guid was recorded for this run", 12, false, Stale, string.Empty);

            Assert.AreEqual(TestRunStartAction.Refuse, decision.Action);
            StringAssert.Contains("no runGuid recorded", decision.Reason);
        }

        [Test]
        public void ProvablyGoneNeedsBothSilenceAndADomainReload()
        {
            Assert.IsFalse(TestRunStartPolicy.IsProvablyGone(10, false, Stale));
            Assert.IsFalse(TestRunStartPolicy.IsProvablyGone(10, true, Stale));
            Assert.IsFalse(TestRunStartPolicy.IsProvablyGone(600, false, Stale));
            Assert.IsTrue(TestRunStartPolicy.IsProvablyGone(600, true, Stale));
            Assert.IsTrue(TestRunStartPolicy.IsProvablyGone(-1, false, Stale));
        }

        [Test]
        public void StaleWindowIsHonoured()
        {
            Assert.IsFalse(TestRunStartPolicy.IsProvablyGone(9.99, true, 10));
            Assert.IsTrue(TestRunStartPolicy.IsProvablyGone(10, true, 10));
        }
    }
}
