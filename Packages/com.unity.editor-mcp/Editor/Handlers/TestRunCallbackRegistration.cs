namespace UnityEditorMCP.Handlers
{
    /// <summary>
    /// Tracks whether this domain currently holds a Test Runner callback registration.
    ///
    /// The Test Framework keeps registered callbacks in its own holder, which is not guaranteed to be
    /// cleared when a domain reload wipes our statics. Guarding registration on a static alone
    /// therefore adds a fresh registration on every reload, and a run then reports each event N times.
    /// This type owns the "exactly one live registration per domain" rule so it can be unit tested
    /// without a Test Runner.
    /// </summary>
    public sealed class TestRunCallbackRegistration
    {
        private bool registered;

        /// <summary>Number of times a registration was actually performed.</summary>
        public int RegisterCount { get; private set; }

        /// <summary>Number of times a registration was actually released.</summary>
        public int UnregisterCount { get; private set; }

        /// <summary>True while this domain holds a registration.</summary>
        public bool IsRegistered
        {
            get { return registered; }
        }

        /// <summary>Registrations currently outstanding. Must never exceed one.</summary>
        public int LiveRegistrations
        {
            get { return RegisterCount - UnregisterCount; }
        }

        /// <summary>
        /// Returns true when the caller should perform the actual RegisterCallbacks call.
        /// </summary>
        public bool ShouldRegister()
        {
            if (registered)
            {
                return false;
            }

            registered = true;
            RegisterCount++;
            return true;
        }

        /// <summary>
        /// Returns true when the caller should perform the actual UnregisterCallbacks call. Called from
        /// beforeAssemblyReload so the outgoing domain's callback is removed from the holder before its
        /// assembly is unloaded.
        /// </summary>
        public bool ShouldUnregister()
        {
            if (!registered)
            {
                return false;
            }

            registered = false;
            UnregisterCount++;
            return true;
        }
    }
}
