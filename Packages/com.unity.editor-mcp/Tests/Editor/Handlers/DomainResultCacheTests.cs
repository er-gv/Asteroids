using System.Collections.Generic;
using NUnit.Framework;
using UnityEditorMCP.Handlers;

namespace UnityEditorMCP.Tests.Handlers
{
    [TestFixture]
    public class DomainResultCacheTests
    {
        private static DomainResultCache<string> WithSnapshot(IDictionary<string, string> snapshot, out int loads)
        {
            int count = 0;
            var captured = snapshot;
            var cache = new DomainResultCache<string>(() =>
            {
                count++;
                return captured;
            });

            loads = count;
            return cache;
        }

        [Test]
        public void ReadLoadsThePersistedSnapshot()
        {
            var cache = new DomainResultCache<string>(() => new Dictionary<string, string> { { "A", "pre-reload" } });

            Assert.AreEqual(1, cache.Count);
            Assert.IsTrue(cache.TryGetValue("A", out string value));
            Assert.AreEqual("pre-reload", value);
        }

        [Test]
        public void WriteLoadsThePersistedSnapshotToo()
        {
            // This is the whole point: the Test Runner's first post-reload TestFinished callback used to
            // write into an empty dictionary without ever loading the snapshot.
            var cache = new DomainResultCache<string>(() => new Dictionary<string, string> { { "A", "pre-reload" } });

            cache.Set("B", "post-reload");

            Assert.AreEqual(2, cache.Count);
            Assert.IsTrue(cache.TryGetValue("A", out string a));
            Assert.AreEqual("pre-reload", a);
        }

        [Test]
        public void ALaterReadNeverReplacesResultsReportedSinceTheReload()
        {
            // The regression: write, then poll. The poll used to load the snapshot and REPLACE the
            // dictionary, discarding "post-reload" until RunFinished happened to repair it.
            int loads = 0;
            var cache = new DomainResultCache<string>(() =>
            {
                loads++;
                return new Dictionary<string, string> { { "A", "pre-reload" }, { "B", "stale" } };
            });

            cache.Set("B", "post-reload");
            cache.EnsureLoaded();

            Assert.AreEqual(1, loads, "the snapshot is merged exactly once per domain");
            Assert.IsTrue(cache.TryGetValue("B", out string b));
            Assert.AreEqual("post-reload", b, "in-memory results from this domain are newer and must win");
            Assert.AreEqual(2, cache.Count);
        }

        [Test]
        public void MergeCountsReportWhatTheSnapshotContributed()
        {
            var cache = new DomainResultCache<string>(() => new Dictionary<string, string>
            {
                { "A", "pre-reload" },
                { "B", "pre-reload" }
            });

            // Set loads first, so both snapshot entries are merged and only then overwritten.
            cache.Set("A", "fresh");

            Assert.AreEqual(2, cache.MergedEntryCount);
            Assert.AreEqual(0, cache.RejectedStaleEntryCount);
            Assert.IsTrue(cache.TryGetValue("A", out string a));
            Assert.AreEqual("fresh", a);
        }

        [Test]
        public void SnapshotNeverOverwritesAnEntryThisDomainAlreadyHolds()
        {
            // Force the ordering that the load-once-before-first-write rule normally prevents: entries
            // already present at the moment the snapshot is merged. The merge must leave them alone,
            // so the invariant holds even if some future caller reaches EnsureLoaded late.
            DomainResultCache<string> cache = null;
            cache = new DomainResultCache<string>(() =>
            {
                cache.Set("A", "post-reload");
                return new Dictionary<string, string> { { "A", "pre-reload" }, { "B", "pre-reload" } };
            });

            cache.EnsureLoaded();

            Assert.IsTrue(cache.TryGetValue("A", out string a));
            Assert.AreEqual("post-reload", a);
            Assert.AreEqual(1, cache.RejectedStaleEntryCount);
            Assert.AreEqual(1, cache.MergedEntryCount);
            Assert.AreEqual(2, cache.Count);
        }

        [Test]
        public void TheSnapshotIsLoadedAtMostOncePerDomain()
        {
            int loads;
            var cache = WithSnapshot(new Dictionary<string, string> { { "A", "1" } }, out loads);

            cache.EnsureLoaded();
            cache.EnsureLoaded();
            cache.Set("B", "2");
            var ignored = cache.Count;
            foreach (var entry in cache.Entries)
            {
                Assert.IsNotNull(entry.Key);
            }

            Assert.AreEqual(1, cache.LoadCount);
        }

        [Test]
        public void MissingSnapshotIsNotAnError()
        {
            var cache = new DomainResultCache<string>(() => null);

            Assert.AreEqual(0, cache.Count);
            Assert.AreEqual(0, cache.LoadCount);
            Assert.IsTrue(cache.IsLoaded);
        }

        [Test]
        public void ClearingMarksTheCacheLoadedSoTheStaleSnapshotNeverComesBack()
        {
            int loads = 0;
            var cache = new DomainResultCache<string>(() =>
            {
                loads++;
                return new Dictionary<string, string> { { "A", "previous run" } };
            });

            // run_tests clears results before starting; the previous run's snapshot must stay gone.
            cache.Clear();
            cache.Set("B", "new run");

            Assert.AreEqual(0, loads);
            Assert.AreEqual(1, cache.Count);
            Assert.IsFalse(cache.TryGetValue("A", out string _));
        }

        [Test]
        public void ClearingAfterALoadDropsTheMergedEntriesToo()
        {
            var cache = new DomainResultCache<string>(() => new Dictionary<string, string> { { "A", "previous run" } });

            cache.EnsureLoaded();
            cache.Clear();

            Assert.AreEqual(0, cache.Count);
        }

        [Test]
        public void ValuesAlsoTriggerTheLoad()
        {
            var cache = new DomainResultCache<string>(() => new Dictionary<string, string> { { "A", "1" } });

            var values = new List<string>(cache.Values);

            Assert.AreEqual(1, values.Count);
        }
    }
}
