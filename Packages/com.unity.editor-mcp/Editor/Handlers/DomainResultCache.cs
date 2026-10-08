using System;
using System.Collections.Generic;

namespace UnityEditorMCP.Handlers
{
    /// <summary>
    /// An in-memory dictionary that is backed by a snapshot persisted across domain reloads, with one
    /// rule: the snapshot is merged in exactly once per domain, before the first read *or write*, and
    /// it never overwrites an entry this domain already holds.
    ///
    /// The rule matters because a PlayMode run reloads the domain mid-run. Loading lazily on reads only
    /// meant the Test Runner's post-reload TestFinished callbacks wrote straight into an empty
    /// dictionary, and the next get_test_results poll then *replaced* that dictionary with the
    /// pre-reload snapshot - silently throwing away every result reported since the reload until the
    /// final RunFinished happened to repair it.
    /// </summary>
    /// <typeparam name="TValue">Entry type; the cache never inspects it.</typeparam>
    public sealed class DomainResultCache<TValue>
    {
        private readonly Func<IDictionary<string, TValue>> loadPersisted;
        private readonly Dictionary<string, TValue> entries = new Dictionary<string, TValue>();
        private bool loaded;

        /// <param name="loadPersisted">
        /// Returns the persisted snapshot, or null when there is none. Called at most once per domain.
        /// </param>
        public DomainResultCache(Func<IDictionary<string, TValue>> loadPersisted)
        {
            if (loadPersisted == null)
            {
                throw new ArgumentNullException("loadPersisted");
            }

            this.loadPersisted = loadPersisted;
        }

        /// <summary>Number of times the persisted snapshot was actually loaded. Must never exceed one.</summary>
        public int LoadCount { get; private set; }

        /// <summary>Entries the snapshot supplied that were not already held in this domain.</summary>
        public int MergedEntryCount { get; private set; }

        /// <summary>Entries the snapshot supplied that were dropped because this domain had newer ones.</summary>
        public int RejectedStaleEntryCount { get; private set; }

        public bool IsLoaded
        {
            get { return loaded; }
        }

        public int Count
        {
            get
            {
                EnsureLoaded();
                return entries.Count;
            }
        }

        /// <summary>
        /// Loads and merges the persisted snapshot if this domain has not done so yet. Safe to call
        /// from every entry point; only the first call does anything.
        /// </summary>
        public void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            // Set before loading: a loader that throws must not leave the cache trying again on every
            // single access, and must not be re-entered.
            loaded = true;

            var restored = loadPersisted();
            if (restored == null)
            {
                return;
            }

            LoadCount++;

            foreach (var kvp in restored)
            {
                if (entries.ContainsKey(kvp.Key))
                {
                    // This domain already reported a newer result for that test; the snapshot is older
                    // by construction, so it loses.
                    RejectedStaleEntryCount++;
                    continue;
                }

                entries[kvp.Key] = kvp.Value;
                MergedEntryCount++;
            }
        }

        /// <summary>Writes an entry, loading the persisted snapshot first if that has not happened yet.</summary>
        public void Set(string key, TValue value)
        {
            EnsureLoaded();
            entries[key] = value;
        }

        public bool TryGetValue(string key, out TValue value)
        {
            EnsureLoaded();
            return entries.TryGetValue(key, out value);
        }

        /// <summary>All entries, snapshot included.</summary>
        public IEnumerable<KeyValuePair<string, TValue>> Entries
        {
            get
            {
                EnsureLoaded();
                return entries;
            }
        }

        public IEnumerable<TValue> Values
        {
            get
            {
                EnsureLoaded();
                return entries.Values;
            }
        }

        /// <summary>
        /// Drops everything and marks the cache loaded, so a deliberately cleared cache is never
        /// re-populated from the stale snapshot afterwards.
        /// </summary>
        public void Clear()
        {
            loaded = true;
            entries.Clear();
        }
    }
}
