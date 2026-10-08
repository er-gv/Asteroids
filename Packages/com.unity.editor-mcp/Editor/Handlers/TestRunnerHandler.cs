using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using ApiTestMode = UnityEditor.TestTools.TestRunner.Api.TestMode;
using UnityEditorMCP.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityEditorMCP.Handlers
{
    /// <summary>
    /// Handles Unity Test Runner operations for executing and managing tests
    /// </summary>
    [InitializeOnLoad]
    public static class TestRunnerHandler
    {
        private static TestRunnerApi testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
        private static TestRunCallback currentCallback;
        private static readonly TestRunCallbackRegistration callbackRegistration = new TestRunCallbackRegistration();

        /// <summary>
        /// In-memory cache of the last run's results. A domain reload wipes it, so it is backed by
        /// SessionState (see <see cref="SaveResults"/>) and merged back in exactly once per domain -
        /// before the first read or write, and without clobbering anything reported since the reload.
        /// </summary>
        private static readonly DomainResultCache<TestResult> lastTestResults =
            new DomainResultCache<TestResult>(LoadPersistedResults);
        private const ApiTestMode AllTestModes = ApiTestMode.EditMode | ApiTestMode.PlayMode;

        /// <summary>Identifies this loaded domain; a different value means the domain was reloaded.</summary>
        private static readonly string DomainId = Guid.NewGuid().ToString("N");

        // Run bookkeeping lives in SessionState so it survives the domain reloads that entering play
        // mode (or a recompile) triggers. Plain statics used to be wiped by the reload, leaving
        // isRunning stuck at true forever with no callbacks registered to ever clear it.
        private const string RunningKey = "UnityEditorMCP.TestRun.Running";
        private const string RunGuidKey = "UnityEditorMCP.TestRun.Guid";
        private const string RunModeKey = "UnityEditorMCP.TestRun.Mode";
        private const string RunStartedKey = "UnityEditorMCP.TestRun.StartedAtTicks";
        private const string RunProgressKey = "UnityEditorMCP.TestRun.LastProgressTicks";
        private const string RunProgressDomainKey = "UnityEditorMCP.TestRun.LastProgressDomain";
        private const string RunStatusKey = "UnityEditorMCP.TestRun.LastStatus";
        private const string ResultsKey = "UnityEditorMCP.TestRun.Results";

        /// <summary>Bounds on what is persisted to SessionState, which is an in-memory editor store.</summary>
        private const int MaxPersistedResults = 500;
        private const int MaxPersistedTextLength = 2000;

        /// <summary>
        /// How long a run may report no progress at all before it is *suspected* to be lost. On its own
        /// this proves nothing - a single long test looks exactly the same - so it is always combined
        /// with a domain-reload check before a run is treated as gone.
        /// </summary>
        private const double DefaultStaleRunSeconds = 300.0;

        static TestRunnerHandler()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            // Re-attach to the Test Runner after a domain reload; otherwise RunFinished never arrives
            // for a run that started before the reload.
            if (IsRunMarkedRunning())
            {
                EnsureCallbacksRegistered();
            }
        }

        /// <summary>
        /// Hands the outgoing domain's state over cleanly: persist results that would otherwise be lost
        /// and remove this domain's callback from the Test Framework's holder, which outlives us.
        /// </summary>
        private static void OnBeforeAssemblyReload()
        {
            SaveResults();
            UnregisterCallbacks();
        }

        /// <summary>
        /// Lists all available tests in the project
        /// </summary>
        public static object ListTests(JObject parameters)
        {
            try
            {
                var testMode = ParseTestMode(parameters?["testMode"]?.ToString());
                var filterPattern = parameters?["filter"]?.ToString();
                var includeCategories = parameters?["includeCategories"]?.ToObject<string[]>();
                var excludeCategories = parameters?["excludeCategories"]?.ToObject<string[]>();

                var tests = DiscoverTests(testMode, filterPattern, includeCategories, excludeCategories)
                    .Select(test => new
                    {
                        name = test.Name,
                        methodName = test.MethodName,
                        className = test.ClassName,
                        assemblyName = test.AssemblyName,
                        testMode = test.TestMode.ToString(),
                        categories = test.Categories,
                        isAsync = test.IsAsync
                    })
                    .Cast<object>()
                    .ToList();

                return new
                {
                    tests = tests.ToArray(),
                    totalCount = tests.Count,
                    testMode = testMode.ToString(),
                    message = $"Found {tests.Count} tests"
                };
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to list tests: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs specified tests or all tests
        /// </summary>
        public static object RunTests(JObject parameters)
        {
            try
            {
                // Exactly one run at a time, and never two live ones. force: true does NOT mean "forget
                // the bookkeeping": a genuinely live Unity Test Framework run keeps executing whatever
                // this handler believes, so force has to actually cancel it first and may only start a
                // new run once the old one was cancelled or can be shown to be gone.
                if (IsRunMarkedRunning())
                {
                    bool force = parameters?["force"]?.ToObject<bool>() ?? false;
                    string existingRunGuid = SessionState.GetString(RunGuidKey, string.Empty);

                    var decision = TestRunStartPolicy.Evaluate(
                        true,
                        force,
                        SecondsSinceLastProgress(),
                        DomainChangedSinceLastProgress(),
                        DefaultStaleRunSeconds,
                        existingRunGuid);

                    if (decision.Action == TestRunStartAction.CancelThenStart)
                    {
                        bool cancelled = TryCancelTestRun(existingRunGuid, out string cancelDetail);
                        if (cancelled && EditorApplication.isPlaying)
                        {
                            // A PlayMode run also needs play mode to end before the editor is free.
                            EditorApplication.isPlaying = false;
                        }

                        decision = TestRunStartPolicy.EvaluateAfterCancelAttempt(
                            cancelled,
                            cancelDetail,
                            SecondsSinceLastProgress(),
                            DomainChangedSinceLastProgress(),
                            DefaultStaleRunSeconds,
                            existingRunGuid);
                    }

                    if (decision.Action == TestRunStartAction.Refuse)
                    {
                        return Response.Error(decision.Reason);
                    }

                    Debug.LogWarning($"[TestRunner] {decision.Reason}");
                    ClearRunState(decision.DisplacedRunStatus);
                }

                var modeParameter = parameters?["testMode"]?.ToString();
                if (string.IsNullOrEmpty(modeParameter))
                {
                    return Response.Error(
                        "testMode is required. Pass 'EditMode', 'PlayMode' or 'EditAndPlayMode' explicitly - " +
                        "PlayMode and EditAndPlayMode enter play mode in the Editor and trigger a domain reload.");
                }

                if (!TryParseTestMode(modeParameter, out ApiTestMode testMode))
                {
                    return Response.Error($"Unknown testMode '{modeParameter}'. Use 'EditMode', 'PlayMode' or 'EditAndPlayMode'.");
                }

                var testNames = parameters?["testNames"]?.ToObject<string[]>();
                var runAll = parameters?["runAll"]?.ToObject<bool>() ?? false;
                var includeCategories = parameters?["includeCategories"]?.ToObject<string[]>();
                var excludeCategories = parameters?["excludeCategories"]?.ToObject<string[]>();
                var filterCategoryNames = includeCategories;
                var filterTestNames = testNames;

                // Unity's Test Runner Filter supports category inclusion, but not exclusion.
                // Resolve excluded categories to explicit test names before creating the filter.
                if (excludeCategories != null && excludeCategories.Length > 0)
                {
                    var discoveredTestNames = DiscoverTests(testMode, null, includeCategories, excludeCategories)
                        .Select(test => test.Name)
                        .ToArray();

                    filterTestNames = testNames != null && testNames.Length > 0
                        ? testNames.Intersect(discoveredTestNames).ToArray()
                        : discoveredTestNames;
                    filterCategoryNames = null;

                    if (filterTestNames.Length == 0)
                    {
                        return new
                        {
                            message = "No tests matched the requested filters",
                            testMode = testMode.ToString(),
                            testCount = 0,
                            runAll = runAll
                        };
                    }
                }

                // Create filter for test execution
                var filter = new Filter()
                {
                    testMode = testMode,
                    testNames = filterTestNames,
                    categoryNames = filterCategoryNames
                };

                // Clear previous results
                ClearResults();

                EnsureCallbacksRegistered();
                MarkRunStarted(null, testMode);

                // Execute tests. Execute() returns the run guid in Unity Test Framework 1.2+, and
                // returns void in older versions, so it is invoked reflectively to keep compiling
                // against both.
                var executionSettings = new ExecutionSettings(filter);
                string runGuid = ExecuteTestRun(executionSettings);
                MarkRunStarted(runGuid, testMode);

                return new
                {
                    message = "Test execution started",
                    testMode = testMode.ToString(),
                    testCount = testNames?.Length ?? 0,
                    runAll = runAll,
                    runGuid = runGuid,
                    timestamp = DateTime.UtcNow.ToString("o")
                };
            }
            catch (Exception ex)
            {
                ClearRunState("failed");
                return Response.Error($"Failed to run tests: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the results of the last test run
        /// </summary>
        public static object GetTestResults(JObject parameters)
        {
            try
            {
                // Strictly read-only: reporting results must never mutate run state. Clearing it here
                // used to let a poll silently abandon a run that was merely slow.
                lastTestResults.EnsureLoaded();

                var includeDetails = parameters?["includeDetails"]?.ToObject<bool>() ?? true;
                var filterStatus = parameters?["filterStatus"]?.ToString();
                var staleAfterSeconds = parameters?["staleAfterSeconds"]?.ToObject<double?>() ?? DefaultStaleRunSeconds;

                bool isRunning = IsRunMarkedRunning();
                bool possiblyStale = IsRunProvablyGone(staleAfterSeconds);

                string runStatus = isRunning
                    ? (possiblyStale ? "possibly-stale" : "running")
                    : SessionState.GetString(RunStatusKey, "idle");

                if (lastTestResults.Count == 0)
                {
                    return new
                    {
                        message = possiblyStale
                            ? "The test run reported no progress and the editor has reloaded since; it may be lost. " +
                              "Call cancel_tests, or run_tests with force: true, to start a new run."
                            : isRunning
                                ? "Tests are still running; no results have been reported yet."
                                : "No test results available. Run tests first.",
                        hasResults = false,
                        isRunning = isRunning,
                        possiblyStale = possiblyStale,
                        runStatus = runStatus,
                        runGuid = SessionState.GetString(RunGuidKey, string.Empty),
                        secondsSinceLastProgress = SecondsSinceLastProgress()
                    };
                }

                var results = new List<object>();

                foreach (var kvp in lastTestResults.Entries)
                {
                    var result = kvp.Value;

                    // Apply status filter if specified
                    if (!string.IsNullOrEmpty(filterStatus))
                    {
                        if (!result.Status.ToString().Equals(filterStatus, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    var testResult = new
                    {
                        name = kvp.Key,
                        status = result.Status.ToString(),
                        duration = result.Duration,
                        startTime = result.StartTime.ToString("o"),
                        endTime = result.EndTime.ToString("o")
                    };

                    if (includeDetails)
                    {
                        var detailedResult = new
                        {
                            name = kvp.Key,
                            status = result.Status.ToString(),
                            duration = result.Duration,
                            startTime = result.StartTime.ToString("o"),
                            endTime = result.EndTime.ToString("o"),
                            message = result.Message,
                            stackTrace = result.StackTrace,
                            output = result.Output
                        };
                        results.Add(detailedResult);
                    }
                    else
                    {
                        results.Add(testResult);
                    }
                }

                var summary = CalculateTestSummary();

                return new
                {
                    results = results.ToArray(),
                    summary = summary,
                    isRunning = isRunning,
                    possiblyStale = possiblyStale,
                    runStatus = runStatus,
                    runGuid = SessionState.GetString(RunGuidKey, string.Empty),
                    secondsSinceLastProgress = SecondsSinceLastProgress(),
                    totalTests = lastTestResults.Count,
                    message = possiblyStale
                        ? "Test results may be partial: the run reported no progress and the editor has reloaded since."
                        : "Test results retrieved successfully"
                };
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to get test results: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancels currently running tests
        /// </summary>
        public static object CancelTests(JObject parameters)
        {
            try
            {
                if (!IsRunMarkedRunning())
                {
                    return new
                    {
                        message = "No tests are currently running",
                        cancelRequested = false,
                        wasCancelled = false
                    };
                }

                string runGuid = SessionState.GetString(RunGuidKey, string.Empty);
                bool apiCancelled = TryCancelTestRun(runGuid, out string cancelDetail);

                if (!apiCancelled)
                {
                    // The run is still going. Clearing state here used to report a cancellation that
                    // never happened and left the real run orphaned, with nothing tracking it.
                    return new
                    {
                        message = $"The Test Runner API did not cancel the run ({cancelDetail}); the run is left untouched. " +
                                  "Stop it from the Test Runner window, or pass force: true to run_tests to discard it.",
                        cancelRequested = false,
                        wasCancelled = false,
                        cancelledViaApi = false,
                        runGuid = runGuid,
                        detail = cancelDetail,
                        timestamp = DateTime.UtcNow.ToString("o")
                    };
                }

                // PlayMode runs also need play mode to end; EditMode runs are cancelled purely through
                // the Test Runner API above. Only done once cancellation was actually accepted.
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                }

                ClearRunState("cancelled");

                return new
                {
                    message = "Test run cancelled through the Unity Test Runner API",
                    cancelRequested = true,
                    wasCancelled = true,
                    cancelledViaApi = true,
                    runGuid = runGuid,
                    detail = cancelDetail,
                    timestamp = DateTime.UtcNow.ToString("o")
                };
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to cancel tests: {ex.Message}");
            }
        }

        #region Helper Methods

        private static ApiTestMode ParseTestMode(string mode)
        {
            return TryParseTestMode(mode, out ApiTestMode result) ? result : AllTestModes;
        }

        private static bool TryParseTestMode(string mode, out ApiTestMode result)
        {
            result = AllTestModes;
            if (string.IsNullOrEmpty(mode))
            {
                return false;
            }

            if (mode.Equals("EditAndPlayMode", StringComparison.OrdinalIgnoreCase) ||
                mode.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                result = AllTestModes;
                return true;
            }

            return Enum.TryParse<ApiTestMode>(mode, true, out result);
        }

        #region Run state (survives domain reloads)

        /// <summary>
        /// Registers this domain's callback exactly once. The Test Framework's CallbacksHolder is not
        /// ours to inspect, so the rule is enforced through <see cref="TestRunCallbackRegistration"/>
        /// and paired with <see cref="UnregisterCallbacks"/> in beforeAssemblyReload.
        /// </summary>
        private static void EnsureCallbacksRegistered()
        {
            if (!callbackRegistration.ShouldRegister())
            {
                return;
            }

            if (testRunnerApi == null)
            {
                testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            }

            currentCallback = new TestRunCallback();
            testRunnerApi.RegisterCallbacks(currentCallback);
        }

        private static void UnregisterCallbacks()
        {
            if (!callbackRegistration.ShouldUnregister())
            {
                return;
            }

            try
            {
                if (testRunnerApi != null && currentCallback != null)
                {
                    testRunnerApi.UnregisterCallbacks(currentCallback);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestRunner] Failed to unregister test callbacks: {ex.Message}");
            }
            finally
            {
                currentCallback = null;
            }
        }

        private static bool IsRunMarkedRunning()
        {
            return SessionState.GetBool(RunningKey, false);
        }

        /// <summary>
        /// True only when a run marked running can be shown to be gone. The rule itself lives in
        /// <see cref="TestRunStartPolicy.IsProvablyGone"/> so run admission and result reporting can
        /// never disagree about what "gone" means.
        /// </summary>
        private static bool IsRunProvablyGone(double staleAfterSeconds)
        {
            return IsRunMarkedRunning() &&
                   TestRunStartPolicy.IsProvablyGone(
                       SecondsSinceLastProgress(),
                       DomainChangedSinceLastProgress(),
                       staleAfterSeconds);
        }

        /// <summary>
        /// True when the editor domain was reloaded after the last progress mark was written.
        /// </summary>
        private static bool DomainChangedSinceLastProgress()
        {
            var progressDomain = SessionState.GetString(RunProgressDomainKey, string.Empty);
            return !string.IsNullOrEmpty(progressDomain) &&
                   !string.Equals(progressDomain, DomainId, StringComparison.Ordinal);
        }

        private static double SecondsSinceLastProgress()
        {
            var raw = SessionState.GetString(RunProgressKey, string.Empty);
            if (string.IsNullOrEmpty(raw) || !long.TryParse(raw, out long ticks) || ticks <= 0)
            {
                return -1;
            }

            double elapsed = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            return elapsed < 0 ? 0 : elapsed;
        }

        private static void MarkRunStarted(string runGuid, ApiTestMode testMode)
        {
            SessionState.SetBool(RunningKey, true);
            SessionState.SetString(RunGuidKey, runGuid ?? string.Empty);
            SessionState.SetString(RunModeKey, testMode.ToString());
            SessionState.SetString(RunStartedKey, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetString(RunStatusKey, "running");
            MarkRunProgress();
        }

        private static void MarkRunProgress()
        {
            SessionState.SetString(RunProgressKey, DateTime.UtcNow.Ticks.ToString());
            SessionState.SetString(RunProgressDomainKey, DomainId);
        }

        private static void ClearRunState(string status)
        {
            SessionState.SetBool(RunningKey, false);
            SessionState.SetString(RunGuidKey, string.Empty);
            SessionState.SetString(RunStatusKey, status ?? "idle");
        }

        /// <summary>
        /// Reads the snapshot persisted before the last domain reload. Entering play mode reloads the
        /// domain and wipes the statics; without this, get_test_results answered "no results" for a run
        /// that had in fact reported plenty. The merge rule itself lives in
        /// <see cref="DomainResultCache{TValue}"/>.
        /// </summary>
        private static IDictionary<string, TestResult> LoadPersistedResults()
        {
            var json = SessionState.GetString(ResultsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, TestResult>>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestRunner] Failed to restore persisted test results: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Persists a bounded snapshot of the results. SessionState is an in-memory editor store, so
        /// both the number of entries and the size of each message are capped.
        /// </summary>
        private static void SaveResults()
        {
            try
            {
                if (lastTestResults.Count == 0)
                {
                    SessionState.SetString(ResultsKey, string.Empty);
                    return;
                }

                var snapshot = new Dictionary<string, TestResult>();
                foreach (var kvp in lastTestResults.Entries)
                {
                    if (snapshot.Count >= MaxPersistedResults)
                    {
                        break;
                    }

                    snapshot[kvp.Key] = Truncate(kvp.Value);
                }

                SessionState.SetString(ResultsKey, JsonConvert.SerializeObject(snapshot));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TestRunner] Failed to persist test results: {ex.Message}");
            }
        }

        private static void ClearResults()
        {
            lastTestResults.Clear();
            SessionState.SetString(ResultsKey, string.Empty);
        }

        private static TestResult Truncate(TestResult result)
        {
            return new TestResult
            {
                Name = result.Name,
                Status = result.Status,
                Duration = result.Duration,
                StartTime = result.StartTime,
                EndTime = result.EndTime,
                Message = TruncateText(result.Message),
                StackTrace = TruncateText(result.StackTrace),
                Output = TruncateText(result.Output)
            };
        }

        private static string TruncateText(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxPersistedTextLength)
            {
                return value;
            }

            return value.Substring(0, MaxPersistedTextLength) + "... [truncated]";
        }

        /// <summary>
        /// Calls TestRunnerApi.Execute reflectively so the run guid is captured on Unity Test Framework
        /// versions that return one, without failing to compile on versions where Execute returns void.
        /// </summary>
        private static string ExecuteTestRun(ExecutionSettings executionSettings)
        {
            var execute = typeof(TestRunnerApi).GetMethod(
                "Execute",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(ExecutionSettings) },
                null);

            // No null branch: the overload is part of the ICallbacks-era API this handler already
            // compiles against, so if reflection cannot find it the environment is broken and the
            // NullReferenceException is the honest outcome.
            return execute.Invoke(testRunnerApi, new object[] { executionSettings }) as string;
        }

        /// <summary>
        /// Cancels the run through TestRunnerApi.CancelTestRun when the installed Unity Test Framework
        /// exposes it (1.2+), degrading gracefully otherwise.
        ///
        /// Signature verified against com.unity.test-framework 1.4.5:
        /// <c>public static bool CancelTestRun(string guid)</c> - static, returning bool - so the
        /// static binding and the bool result handling below are correct for that version.
        /// </summary>
        private static bool TryCancelTestRun(string runGuid, out string detail)
        {
            if (string.IsNullOrEmpty(runGuid))
            {
                detail = "no run guid was recorded for this run";
                return false;
            }

            var cancel = typeof(TestRunnerApi).GetMethod(
                "CancelTestRun",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

            if (cancel == null)
            {
                detail = "TestRunnerApi.CancelTestRun is not available in this Unity Test Framework version";
                return false;
            }

            try
            {
                object result = cancel.Invoke(null, new object[] { runGuid });
                bool cancelled = !(result is bool) || (bool)result;
                detail = cancelled ? "cancelled" : "the Test Runner rejected the cancellation";
                return cancelled;
            }
            catch (Exception ex)
            {
                detail = $"CancelTestRun threw: {ex.InnerException?.Message ?? ex.Message}";
                return false;
            }
        }

        #endregion

        private static List<DiscoveredTest> DiscoverTests(ApiTestMode testMode, string filterPattern, string[] includeCategories, string[] excludeCategories)
        {
            var tests = new List<DiscoveredTest>();
            var includeSet = CreateStringSet(includeCategories);
            var excludeSet = CreateStringSet(excludeCategories);

            var editorAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetReferencedAssemblies().Any(r => r.Name == "nunit.framework"));

            foreach (var assembly in editorAssemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(type => HasAttribute(type, "NUnit.Framework.TestFixtureAttribute") ||
                                       GetTestMethods(type).Any());

                    foreach (var type in types)
                    {
                        var testMethods = GetTestMethods(type).ToArray();
                        if (testMethods.Length == 0)
                            continue;

                        foreach (var method in testMethods)
                        {
                            var isUnityTest = HasAttribute(method, "UnityEngine.TestTools.UnityTestAttribute");
                            var methodTestMode = isUnityTest ? ApiTestMode.PlayMode : ApiTestMode.EditMode;

                            if ((testMode & methodTestMode) == 0)
                                continue;

                            var testName = $"{type.FullName}.{method.Name}";
                            if (!string.IsNullOrEmpty(filterPattern) && !testName.Contains(filterPattern))
                                continue;

                            var categories = GetCategoryNames(type)
                                .Concat(GetCategoryNames(method))
                                .Distinct()
                                .ToArray();

                            if (includeSet != null && !categories.Any(includeSet.Contains))
                                continue;

                            if (excludeSet != null && categories.Any(excludeSet.Contains))
                                continue;

                            tests.Add(new DiscoveredTest
                            {
                                Name = testName,
                                MethodName = method.Name,
                                ClassName = type.FullName,
                                AssemblyName = assembly.GetName().Name,
                                TestMode = methodTestMode,
                                Categories = categories,
                                IsAsync = isUnityTest
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to process assembly {assembly.GetName().Name}: {ex.Message}");
                }
            }

            return tests;
        }

        private static IEnumerable<MethodInfo> GetTestMethods(Type type)
        {
            return type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => HasAttribute(method, "NUnit.Framework.TestAttribute") ||
                                 HasAttribute(method, "UnityEngine.TestTools.UnityTestAttribute"));
        }

        private static string[] GetCategoryNames(MemberInfo member)
        {
            return member.GetCustomAttributes(true)
                .Where(attribute => attribute.GetType().FullName == "NUnit.Framework.CategoryAttribute")
                .Select(GetCategoryName)
                .Where(category => !string.IsNullOrEmpty(category))
                .ToArray();
        }

        private static string GetCategoryName(object categoryAttribute)
        {
            var type = categoryAttribute.GetType();
            return type.GetProperty("Name")?.GetValue(categoryAttribute, null)?.ToString() ??
                   type.GetProperty("Category")?.GetValue(categoryAttribute, null)?.ToString();
        }

        private static bool HasAttribute(MemberInfo member, string attributeFullName)
        {
            return member.GetCustomAttributes(true)
                .Any(attribute => attribute.GetType().FullName == attributeFullName);
        }

        private static HashSet<string> CreateStringSet(string[] values)
        {
            return values != null && values.Length > 0
                ? new HashSet<string>(values, StringComparer.OrdinalIgnoreCase)
                : null;
        }

        private static object CalculateTestSummary()
        {
            int passed = 0;
            int failed = 0;
            int skipped = 0;
            int inconclusive = 0;
            double totalDuration = 0;

            foreach (var result in lastTestResults.Values)
            {
                totalDuration += result.Duration;

                switch (result.Status)
                {
                    case TestStatus.Passed:
                        passed++;
                        break;
                    case TestStatus.Failed:
                        failed++;
                        break;
                    case TestStatus.Skipped:
                        skipped++;
                        break;
                    case TestStatus.Inconclusive:
                        inconclusive++;
                        break;
                }
            }

            return new
            {
                total = lastTestResults.Count,
                passed = passed,
                failed = failed,
                skipped = skipped,
                inconclusive = inconclusive,
                duration = totalDuration,
                successRate = lastTestResults.Count > 0 ? (passed / (double)lastTestResults.Count) * 100 : 0
            };
        }

        #endregion

        /// <summary>
        /// Internal class to handle test execution callbacks
        /// </summary>
        private class TestRunCallback : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log($"[TestRunner] Starting test run");
                ClearResults();
                MarkRunProgress();
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Debug.Log($"[TestRunner] Test run completed");
                ProcessTestResults(result);
                SaveResults();
                ClearRunState("completed");
            }

            public void TestStarted(ITestAdaptor test)
            {
                Debug.Log($"[TestRunner] Test started: {test.FullName}");
                MarkRunProgress();
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                Debug.Log($"[TestRunner] Test finished: {result.Test.FullName} - {result.TestStatus}");
                MarkRunProgress();

                if (result.HasChildren || result.Test == null)
                    return;

                // Store individual test result
                var testResult = new TestResult
                {
                    Name = result.Test.FullName,
                    Status = ConvertTestStatus(result.TestStatus),
                    Duration = result.Duration,
                    StartTime = result.StartTime,
                    EndTime = result.EndTime,
                    Message = result.Message,
                    StackTrace = result.StackTrace,
                    Output = result.Output
                };

                lastTestResults.Set(result.Test.FullName, testResult);
            }

            private void ProcessTestResults(ITestResultAdaptor result)
            {
                // Process all results recursively
                if (result.HasChildren)
                {
                    foreach (var child in result.Children)
                    {
                        ProcessTestResults(child);
                    }
                }
                else if (result.Test != null)
                {
                    // Leaf node - actual test
                    var testResult = new TestResult
                    {
                        Name = result.Test.FullName,
                        Status = ConvertTestStatus(result.TestStatus),
                        Duration = result.Duration,
                        StartTime = result.StartTime,
                        EndTime = result.EndTime,
                        Message = result.Message,
                        StackTrace = result.StackTrace,
                        Output = result.Output
                    };

                    lastTestResults.Set(result.Test.FullName, testResult);
                }
            }

            private TestStatus ConvertTestStatus(UnityEditor.TestTools.TestRunner.Api.TestStatus status)
            {
                switch (status)
                {
                    case UnityEditor.TestTools.TestRunner.Api.TestStatus.Passed:
                        return TestStatus.Passed;
                    case UnityEditor.TestTools.TestRunner.Api.TestStatus.Failed:
                        return TestStatus.Failed;
                    case UnityEditor.TestTools.TestRunner.Api.TestStatus.Skipped:
                        return TestStatus.Skipped;
                    case UnityEditor.TestTools.TestRunner.Api.TestStatus.Inconclusive:
                        return TestStatus.Inconclusive;
                    default:
                        return TestStatus.Inconclusive;
                }
            }
        }

        /// <summary>
        /// Internal class to store test results
        /// </summary>
        private class TestResult
        {
            public string Name { get; set; }
            public TestStatus Status { get; set; }
            public double Duration { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public string Message { get; set; }
            public string StackTrace { get; set; }
            public string Output { get; set; }
        }

        private class DiscoveredTest
        {
            public string Name { get; set; }
            public string MethodName { get; set; }
            public string ClassName { get; set; }
            public string AssemblyName { get; set; }
            public ApiTestMode TestMode { get; set; }
            public string[] Categories { get; set; }
            public bool IsAsync { get; set; }
        }

        private enum TestStatus
        {
            Passed,
            Failed,
            Skipped,
            Inconclusive
        }
    }
}
