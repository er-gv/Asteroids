using System;
using NUnit.Framework;
using UnityEditorMCP.Core;

namespace UnityEditorMCP.Tests.Helpers
{
    [TestFixture]
    public class MainThreadStallPolicyTests
    {
        private static readonly DateTime Now = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static MainThreadStallDecision Evaluate(
            double secondsSinceTick,
            double? handlerRunningSeconds,
            double? queueHeadAgeSeconds,
            string commandType = "refresh_assets")
        {
            return MainThreadStallPolicy.Evaluate(
                Now,
                Now.AddSeconds(-secondsSinceTick),
                handlerRunningSeconds.HasValue ? Now.AddSeconds(-handlerRunningSeconds.Value) : (DateTime?)null,
                commandType,
                queueHeadAgeSeconds.HasValue ? Now.AddSeconds(-queueHeadAgeSeconds.Value) : (DateTime?)null);
        }

        [Test]
        public void FreshTick_IsHealthy_AndNeverFailsQueuedCommands()
        {
            var decision = Evaluate(secondsSinceTick: 0.2, handlerRunningSeconds: null, queueHeadAgeSeconds: 60);

            Assert.AreEqual(MainThreadState.Healthy, decision.State);
            Assert.IsFalse(decision.ShouldFailQueuedCommands);
        }

        [Test]
        public void SilentMainThreadWithNoHandler_IsStalled_AndFailsOldQueuedCommands()
        {
            var decision = Evaluate(secondsSinceTick: 12, handlerRunningSeconds: null, queueHeadAgeSeconds: 11);

            Assert.AreEqual(MainThreadState.Stalled, decision.State);
            Assert.IsTrue(decision.ShouldFailQueuedCommands);
            StringAssert.Contains("has not ticked", decision.Reason);
        }

        [Test]
        public void StalledMainThreadWithEmptyQueue_DoesNotFailAnything()
        {
            var decision = Evaluate(secondsSinceTick: 12, handlerRunningSeconds: null, queueHeadAgeSeconds: null);

            Assert.AreEqual(MainThreadState.Stalled, decision.State);
            Assert.IsFalse(decision.ShouldFailQueuedCommands);
        }

        [Test]
        public void StalledMainThreadWithFreshQueueHead_WaitsForTheStallWindow()
        {
            var decision = Evaluate(secondsSinceTick: 12, handlerRunningSeconds: null, queueHeadAgeSeconds: 1);

            Assert.AreEqual(MainThreadState.Stalled, decision.State);
            Assert.IsFalse(decision.ShouldFailQueuedCommands);
        }

        [Test]
        public void LongRunningHandler_IsBusyNotStalled_AndQueuedCommandsKeepWaiting()
        {
            // The regression this guards: a healthy editor inside AssetDatabase.Refresh stops ticking
            // and used to have every queued command failed with MAIN_THREAD_STALLED after 5s.
            var decision = Evaluate(secondsSinceTick: 90, handlerRunningSeconds: 90, queueHeadAgeSeconds: 85);

            Assert.AreEqual(MainThreadState.BusyInHandler, decision.State);
            Assert.IsFalse(decision.ShouldFailQueuedCommands);
            Assert.AreEqual("refresh_assets", decision.BusyCommandType);
        }

        [Test]
        public void HandlerPastTheCeiling_FailsQueuedCommandsAndNamesTheBusyCommand()
        {
            var decision = MainThreadStallPolicy.Evaluate(
                Now,
                Now.AddSeconds(-700),
                Now.AddSeconds(-700),
                "run_tests",
                Now.AddSeconds(-690));

            Assert.AreEqual(MainThreadState.BusyInHandler, decision.State);
            Assert.IsTrue(decision.ShouldFailQueuedCommands);
            StringAssert.Contains("run_tests", decision.Reason);
        }

        [Test]
        public void HandlerPastTheCeilingWithEmptyQueue_FailsNothing()
        {
            var decision = MainThreadStallPolicy.Evaluate(
                Now,
                Now.AddSeconds(-700),
                Now.AddSeconds(-700),
                "run_tests",
                null);

            Assert.IsFalse(decision.ShouldFailQueuedCommands);
        }

        [Test]
        public void NeverTicked_IsStalled()
        {
            var decision = MainThreadStallPolicy.Evaluate(Now, null, null, null, Now.AddSeconds(-30));

            Assert.AreEqual(MainThreadState.Stalled, decision.State);
            Assert.IsTrue(decision.ShouldFailQueuedCommands);
            Assert.AreEqual(-1, decision.SecondsSinceTick);
            StringAssert.Contains("never ticked", decision.Reason);
        }

        [Test]
        public void CustomThresholdsAreHonoured()
        {
            var healthy = MainThreadStallPolicy.Evaluate(
                Now, Now.AddSeconds(-3), null, null, Now.AddSeconds(-3), stallSeconds: 10);
            Assert.AreEqual(MainThreadState.Healthy, healthy.State);

            var busyPastLowCeiling = MainThreadStallPolicy.Evaluate(
                Now, Now.AddSeconds(-30), Now.AddSeconds(-30), "load_scene", Now.AddSeconds(-30),
                stallSeconds: 5, handlerCeilingSeconds: 20);
            Assert.IsTrue(busyPastLowCeiling.ShouldFailQueuedCommands);
        }

        [Test]
        public void HandlerPastTheCeiling_FailsTheQueueOnlyOncePerEpisode()
        {
            var handlerStarted = Now.AddSeconds(-700);

            var first = MainThreadStallPolicy.Evaluate(
                Now, Now.AddSeconds(-700), handlerStarted, "refresh_assets", Now.AddSeconds(-650));

            Assert.IsTrue(first.ShouldFailQueuedCommands);
            Assert.IsTrue(first.CeilingExceeded);
            StringAssert.Contains("refresh_assets", first.Reason);
            StringAssert.Contains("ceiling", first.Reason);
            StringAssert.Contains("get_editor_state", first.Reason, "the caller must be told what still answers");

            // Same handler, a later watchdog tick, a command queued since: reported already, so the
            // bridge stops failing everything that arrives behind this handler.
            var second = MainThreadStallPolicy.Evaluate(
                Now, Now.AddSeconds(-700), handlerStarted, "refresh_assets", Now.AddSeconds(-5),
                MainThreadStallPolicy.DefaultStallSeconds,
                MainThreadStallPolicy.DefaultHandlerCeilingSeconds,
                handlerStarted);

            Assert.IsFalse(second.ShouldFailQueuedCommands);
            Assert.IsTrue(second.CeilingExceeded);
            Assert.AreEqual(MainThreadState.BusyInHandler, second.State);
        }

        [Test]
        public void ANewHandlerPastTheCeilingIsReportedAgain()
        {
            var reportedHandler = Now.AddSeconds(-2000);
            var currentHandler = Now.AddSeconds(-700);

            var decision = MainThreadStallPolicy.Evaluate(
                Now, Now.AddSeconds(-700), currentHandler, "load_scene", Now.AddSeconds(-30),
                MainThreadStallPolicy.DefaultStallSeconds,
                MainThreadStallPolicy.DefaultHandlerCeilingSeconds,
                reportedHandler);

            Assert.IsTrue(decision.ShouldFailQueuedCommands);
            StringAssert.Contains("load_scene", decision.Reason);
        }

        [Test]
        public void ABusyHandlerBelowTheCeilingNeverSetsCeilingExceeded()
        {
            var decision = Evaluate(secondsSinceTick: 60, handlerRunningSeconds: 60, queueHeadAgeSeconds: 55);

            Assert.AreEqual(MainThreadState.BusyInHandler, decision.State);
            Assert.IsFalse(decision.CeilingExceeded);
            Assert.IsFalse(decision.ShouldFailQueuedCommands);
        }
    }
}
