using System;

namespace UnityEditorMCP.Handlers
{
    /// <summary>
    /// What run_tests should do about a run that is already marked running.
    /// </summary>
    public enum TestRunStartAction
    {
        /// <summary>Nothing is running; start immediately.</summary>
        Start,

        /// <summary>The previous run is finished, cancelled or provably gone; drop its state and start.</summary>
        DiscardAndStart,

        /// <summary>
        /// A live run is in the way and the caller asked for force: try to cancel it through the Test
        /// Runner API first, then ask the policy again with the outcome.
        /// </summary>
        CancelThenStart,

        /// <summary>Refuse to start; <see cref="TestRunStartDecision.Reason"/> explains why.</summary>
        Refuse
    }

    /// <summary>One run_tests admission decision.</summary>
    public struct TestRunStartDecision
    {
        public TestRunStartAction Action;

        /// <summary>Why the request was refused, or why the old run was discarded. Null when irrelevant.</summary>
        public string Reason;

        /// <summary>Status to record for the displaced run when the action discards one.</summary>
        public string DisplacedRunStatus;
    }

    /// <summary>
    /// Pure admission control for test runs, kept free of Unity API calls so every branch can be unit
    /// tested in EditMode.
    ///
    /// The rule this type exists to enforce: <c>force: true</c> may not simply forget about a run.
    /// Forgetting the bookkeeping does not stop the Unity Test Framework, which keeps executing and
    /// then interleaves its callbacks with the second run's - two runs writing one result dictionary.
    /// So force must first try to genuinely cancel, and may only start a new run once the old one was
    /// cancelled or can be shown to be gone.
    /// </summary>
    public static class TestRunStartPolicy
    {
        /// <summary>
        /// True only when a run marked running can be shown to be gone: nothing has reported progress
        /// for the whole window AND the domain that was reporting it no longer exists. Either signal
        /// alone is not enough - a single test that runs for ten minutes produces no progress, and a
        /// PlayMode run legitimately reloads the domain and then keeps reporting.
        /// </summary>
        /// <param name="secondsSinceLastProgress">Seconds since the last progress mark, or -1 when there is none.</param>
        /// <param name="domainChangedSinceLastProgress">True when the domain was reloaded after that mark.</param>
        /// <param name="staleAfterSeconds">Silence window that counts as suspicious.</param>
        public static bool IsProvablyGone(
            double secondsSinceLastProgress,
            bool domainChangedSinceLastProgress,
            double staleAfterSeconds)
        {
            if (secondsSinceLastProgress < 0)
            {
                // Marked running with no progress mark at all: nothing is tracking this run.
                return true;
            }

            if (secondsSinceLastProgress < staleAfterSeconds)
            {
                return false;
            }

            return domainChangedSinceLastProgress;
        }

        /// <summary>
        /// First half of the decision, taken before any cancellation is attempted.
        /// </summary>
        public static TestRunStartDecision Evaluate(
            bool runMarkedRunning,
            bool force,
            double secondsSinceLastProgress,
            bool domainChangedSinceLastProgress,
            double staleAfterSeconds,
            string runGuid)
        {
            if (!runMarkedRunning)
            {
                return new TestRunStartDecision { Action = TestRunStartAction.Start };
            }

            if (IsProvablyGone(secondsSinceLastProgress, domainChangedSinceLastProgress, staleAfterSeconds))
            {
                return new TestRunStartDecision
                {
                    Action = TestRunStartAction.DiscardAndStart,
                    DisplacedRunStatus = "aborted",
                    Reason = "Discarding a test run that is provably gone: " + DescribeProgress(secondsSinceLastProgress) +
                             " and the editor domain has reloaded since."
                };
            }

            if (force)
            {
                return new TestRunStartDecision { Action = TestRunStartAction.CancelThenStart };
            }

            return new TestRunStartDecision
            {
                Action = TestRunStartAction.Refuse,
                Reason = "A test run is already in progress" + DescribeRun(runGuid, secondsSinceLastProgress) + "." +
                         " Wait for it to finish, call cancel_tests, or pass force: true to cancel it and start a new run."
            };
        }

        /// <summary>
        /// Second half of the decision, taken with the outcome of the cancellation attempt that
        /// <see cref="TestRunStartAction.CancelThenStart"/> asked for.
        /// </summary>
        /// <param name="cancelSucceeded">True when the Test Runner API accepted the cancellation.</param>
        /// <param name="cancelDetail">Why it did not, when it did not.</param>
        public static TestRunStartDecision EvaluateAfterCancelAttempt(
            bool cancelSucceeded,
            string cancelDetail,
            double secondsSinceLastProgress,
            bool domainChangedSinceLastProgress,
            double staleAfterSeconds,
            string runGuid)
        {
            if (cancelSucceeded)
            {
                return new TestRunStartDecision
                {
                    Action = TestRunStartAction.DiscardAndStart,
                    DisplacedRunStatus = "cancelled",
                    Reason = "Cancelled the in-progress test run because force was requested."
                };
            }

            if (IsProvablyGone(secondsSinceLastProgress, domainChangedSinceLastProgress, staleAfterSeconds))
            {
                return new TestRunStartDecision
                {
                    Action = TestRunStartAction.DiscardAndStart,
                    DisplacedRunStatus = "aborted",
                    Reason = "Cancellation was unavailable (" + Describe(cancelDetail) + ") but the run is provably gone; discarding it."
                };
            }

            return new TestRunStartDecision
            {
                Action = TestRunStartAction.Refuse,
                Reason = "force: true could not cancel the test run that is already in progress" +
                         DescribeRun(runGuid, secondsSinceLastProgress) + ": " + Describe(cancelDetail) + "." +
                         " A new run is not started, because the Unity Test Framework would keep executing the old one" +
                         " and both runs would report into the same results." +
                         " Stop the run from the Test Runner window, or wait for it to finish or to become provably lost" +
                         " (no progress plus a domain reload), then try again."
            };
        }

        private static string DescribeRun(string runGuid, double secondsSinceLastProgress)
        {
            string guidPart = string.IsNullOrEmpty(runGuid) ? "no runGuid recorded" : "runGuid '" + runGuid + "'";
            return " (" + guidPart + ", " + DescribeProgress(secondsSinceLastProgress) + ")";
        }

        private static string DescribeProgress(double secondsSinceLastProgress)
        {
            return secondsSinceLastProgress < 0
                ? "no progress has ever been reported"
                : string.Format("last progress {0:F0}s ago", secondsSinceLastProgress);
        }

        private static string Describe(string detail)
        {
            return string.IsNullOrEmpty(detail) ? "no detail was reported" : detail;
        }
    }
}
