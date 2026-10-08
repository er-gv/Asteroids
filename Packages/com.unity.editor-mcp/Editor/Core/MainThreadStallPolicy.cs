using System;

namespace UnityEditorMCP.Core
{
    /// <summary>
    /// What the queue watchdog believes the Unity main thread is doing.
    /// </summary>
    public enum MainThreadState
    {
        /// <summary>The editor loop is pumping normally.</summary>
        Healthy,

        /// <summary>
        /// The loop is not pumping because it is *inside one of our own handlers* (AssetDatabase.Refresh,
        /// a large import, entering play mode). Queued commands must keep waiting, not be failed.
        /// </summary>
        BusyInHandler,

        /// <summary>The loop is not pumping and nothing of ours is running: a real stall.</summary>
        Stalled
    }

    /// <summary>
    /// The watchdog's verdict for one tick.
    /// </summary>
    public struct MainThreadStallDecision
    {
        public MainThreadState State;

        /// <summary>True when queued commands should be failed instead of left waiting.</summary>
        public bool ShouldFailQueuedCommands;

        /// <summary>Seconds since the editor loop last ticked, or -1 when it never has.</summary>
        public double SecondsSinceTick;

        /// <summary>Seconds the in-flight handler has been running, or -1 when none is running.</summary>
        public double HandlerRunningSeconds;

        /// <summary>Age of the oldest queued command in seconds, or -1 when the queue is empty.</summary>
        public double QueueHeadAgeSeconds;

        /// <summary>Command type of the in-flight handler, when one is running.</summary>
        public string BusyCommandType;

        /// <summary>
        /// True when an in-flight handler has passed the ceiling. Set whether or not this particular
        /// tick fails anything, so the caller can record the episode it has already reported.
        /// </summary>
        public bool CeilingExceeded;

        /// <summary>Human-readable explanation used as the error message when commands are failed.</summary>
        public string Reason;
    }

    /// <summary>
    /// Pure decision logic for the queue watchdog, kept free of Unity API calls so it can be unit
    /// tested in EditMode.
    ///
    /// The main thread only ticks between handler invocations, so "no tick for N seconds" on its own
    /// is not evidence of a stall: a healthy editor running a long handler looks identical to a
    /// frozen one. The two are told apart by whether a handler is currently in flight.
    /// </summary>
    public static class MainThreadStallPolicy
    {
        /// <summary>No tick for this long, with no handler running, means the editor loop is stalled.</summary>
        public const double DefaultStallSeconds = 5.0;

        /// <summary>
        /// A single handler may hold the main thread for this long before its queued followers are
        /// failed anyway. Long imports and play-mode entry routinely exceed the stall threshold, so
        /// this ceiling is deliberately far higher.
        /// </summary>
        public const double DefaultHandlerCeilingSeconds = 600.0;

        /// <summary>
        /// Decides what to do about the command queue for one watchdog tick.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="lastTickUtc">When the editor loop last ran the heartbeat, or null if never.</param>
        /// <param name="handlerInFlightSinceUtc">When the in-flight handler started, or null if none.</param>
        /// <param name="handlerInFlightCommandType">Command type of the in-flight handler, if any.</param>
        /// <param name="queueHeadEnqueuedUtc">When the oldest queued command was enqueued, or null if the queue is empty.</param>
        /// <param name="stallSeconds">Tick age that counts as a stall.</param>
        /// <param name="handlerCeilingSeconds">Ceiling after which even a running handler stops excusing the queue.</param>
        /// <param name="ceilingAlreadyReportedForHandlerStartedUtc">
        /// Start time of the in-flight handler whose ceiling breach the caller has already reported.
        /// Passing it makes the ceiling fail the queue once per stuck handler instead of failing every
        /// newly queued command on every tick for as long as that handler runs.
        /// </param>
        public static MainThreadStallDecision Evaluate(
            DateTime nowUtc,
            DateTime? lastTickUtc,
            DateTime? handlerInFlightSinceUtc,
            string handlerInFlightCommandType,
            DateTime? queueHeadEnqueuedUtc,
            double stallSeconds = DefaultStallSeconds,
            double handlerCeilingSeconds = DefaultHandlerCeilingSeconds,
            DateTime? ceilingAlreadyReportedForHandlerStartedUtc = null)
        {
            var decision = new MainThreadStallDecision
            {
                State = MainThreadState.Healthy,
                ShouldFailQueuedCommands = false,
                SecondsSinceTick = ElapsedSeconds(nowUtc, lastTickUtc),
                HandlerRunningSeconds = ElapsedSeconds(nowUtc, handlerInFlightSinceUtc),
                QueueHeadAgeSeconds = ElapsedSeconds(nowUtc, queueHeadEnqueuedUtc),
                BusyCommandType = handlerInFlightSinceUtc.HasValue ? handlerInFlightCommandType : null,
                CeilingExceeded = false,
                Reason = null
            };

            bool tickIsFresh = decision.SecondsSinceTick >= 0 && decision.SecondsSinceTick < stallSeconds;
            if (tickIsFresh)
            {
                return decision;
            }

            if (handlerInFlightSinceUtc.HasValue)
            {
                // The main thread is alive, it is just inside one of our handlers.
                decision.State = MainThreadState.BusyInHandler;

                if (decision.HandlerRunningSeconds >= handlerCeilingSeconds)
                {
                    decision.CeilingExceeded = true;

                    // Once per stuck handler, not once per tick: the handler may legitimately keep
                    // running for hours, and failing every command queued after the first report turned
                    // the bridge into a permanent error generator for that editor.
                    bool alreadyReported =
                        ceilingAlreadyReportedForHandlerStartedUtc.HasValue &&
                        ceilingAlreadyReportedForHandlerStartedUtc.Value == handlerInFlightSinceUtc.Value;

                    decision.ShouldFailQueuedCommands = !alreadyReported && decision.QueueHeadAgeSeconds >= 0;
                    decision.Reason =
                        $"Editor main thread has been executing '{DescribeCommand(handlerInFlightCommandType)}' for " +
                        $"{decision.HandlerRunningSeconds:F1}s, past the {handlerCeilingSeconds:F0}s ceiling. " +
                        "ping and get_editor_state still answer on the socket thread and report the " +
                        "in-flight command; other commands stay queued until it finishes";
                }

                return decision;
            }

            decision.State = MainThreadState.Stalled;
            decision.ShouldFailQueuedCommands =
                decision.QueueHeadAgeSeconds >= stallSeconds;

            if (decision.ShouldFailQueuedCommands)
            {
                decision.Reason = decision.SecondsSinceTick < 0
                    ? "Editor main thread has never ticked - the editor loop is not running"
                    : $"Editor main thread has not ticked for {decision.SecondsSinceTick:F1}s - it is probably " +
                      "showing a modal dialog or is backgrounded; bring Unity to the front";
            }

            return decision;
        }

        private static string DescribeCommand(string commandType)
        {
            return string.IsNullOrEmpty(commandType) ? "an editor command" : commandType;
        }

        private static double ElapsedSeconds(DateTime nowUtc, DateTime? sinceUtc)
        {
            if (!sinceUtc.HasValue)
            {
                return -1;
            }

            double elapsed = (nowUtc - sinceUtc.Value).TotalSeconds;
            return elapsed < 0 ? 0 : elapsed;
        }
    }
}
