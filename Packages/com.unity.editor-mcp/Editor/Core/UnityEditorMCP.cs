using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEditorMCP.Models;
using UnityEditorMCP.Helpers;
using UnityEditorMCP.Logging;
using UnityEditorMCP.Handlers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityEditorMCP.Core
{
    /// <summary>
    /// Main Unity Editor MCP class that handles TCP communication and command processing
    /// </summary>
    [InitializeOnLoad]
    public static class UnityEditorMCP
    {
        private static TcpListener tcpListener;
        private static readonly Queue<QueuedCommand> commandQueue = new Queue<QueuedCommand>();
        private static readonly object queueLock = new object();
        private static CancellationTokenSource cancellationTokenSource;
        private static Task listenerTask;
        private static Task watchdogTask;
        private const double RegistryHeartbeatIntervalSeconds = 5.0;
        private static double lastRegistryWriteTime;
        private static int mainThreadId;
        private static volatile bool registryWritePending;

        /// <summary>
        /// Unity's main-thread synchronization context, captured while the static constructor runs on the
        /// main thread. Posting to it is legal from any thread and is the only supported way to ask the
        /// editor loop to run work from a socket thread (EditorApplication.QueuePlayerLoopUpdate,
        /// EditorApplication.delayCall and every other EditorApplication member are main-thread only).
        /// </summary>
        private static SynchronizationContext unitySyncContext;

        private const string ListenerPortSessionKey = "UnityEditorMCP.ListenerPort";
        private const double MainThreadStallSeconds = MainThreadStallPolicy.DefaultStallSeconds;
        private const double MainThreadHandlerCeilingSeconds = MainThreadStallPolicy.DefaultHandlerCeilingSeconds;

        /// <summary>
        /// How long a single framed response may take to reach a client before that client is dropped.
        /// </summary>
        private const int ResponseWriteTimeoutMs = 10000;

        /// <summary>
        /// Token that every background send observes, so responses stop being written once the listener
        /// has been torn down (domain reload, editor quit).
        /// </summary>
        private static CancellationToken backgroundToken = CancellationToken.None;

        /// <summary>
        /// Set before the listener is torn down. Background code (accept loop, watchdog, response
        /// senders) must check it before touching statics or logging: during beforeAssemblyReload those
        /// tasks are still running old-assembly code while the domain is being unloaded underneath them,
        /// which is a crash source.
        /// </summary>
        private static volatile bool isShuttingDown;

        /// <summary>Every live client, so a shutdown can close their sockets instead of waiting on them.</summary>
        private static readonly HashSet<ClientConnection> activeConnections = new HashSet<ClientConnection>();
        private static readonly object connectionsLock = new object();

        /// <summary>Total time the main thread may spend joining background tasks during shutdown.</summary>
        private const int ShutdownJoinTimeoutMs = 200;

        private static long lastMainThreadTickTicks;

        /// <summary>
        /// Ticks at which the main thread entered the currently running handler, or 0 when no handler
        /// is in flight. A healthy main thread that is busy inside one of our handlers stops ticking
        /// exactly like a frozen one does, so the watchdog needs this to tell the two apart.
        /// </summary>
        private static long handlerInFlightSinceTicks;
        private static volatile string handlerInFlightCommandType;

        /// <summary>
        /// Watchdog thread only: the in-flight handler whose ceiling breach was already reported, so a
        /// handler that holds the main thread for hours fails the queue once rather than on every tick.
        /// </summary>
        private static DateTime? ceilingReportedForHandlerStartedUtc;

        private static volatile EditorSnapshot editorSnapshot;

        /// <summary>Main-thread only: guards against re-entering the command drain.</summary>
        private static bool isProcessingQueue;

        /// <summary>
        /// Upper bound on how stale an unchanged snapshot may get before it is rebuilt anyway. Without
        /// it the snapshot would be refreshed (and allocated) on every single editor tick.
        /// </summary>
        private const double EditorSnapshotMaxAgeSeconds = 0.5;

        // Values that are only legal to read on the main thread, captured once so the socket thread can
        // answer ping/get_editor_state without touching any Unity API.
        private static string cachedUnityVersion = string.Empty;
        private static string cachedProjectPath = string.Empty;
        private static string cachedPackageVersion = "unknown";
        private static string cachedApplicationPath = string.Empty;
        private static string cachedApplicationContentsPath = string.Empty;

        private delegate object CommandHandlerDelegate(Command command);
        private static readonly Dictionary<string, CommandHandlerDelegate> CommandHandlers =
            new Dictionary<string, CommandHandlerDelegate>(StringComparer.OrdinalIgnoreCase)
            {
                { "ping", HandlePing },
                { "get_project_info", command => UnityInstanceRegistry.GetProjectInfo(currentPort, Status) },
                { "read_logs", command => HandleReadLogs(command.Parameters) },
                { "clear_logs", command => HandleClearLogs() },
                { "refresh_assets", command => HandleRefreshAssets() },
                { "create_gameobject", command => GameObjectHandler.CreateGameObject(command.Parameters) },
                { "find_gameobject", command => GameObjectHandler.FindGameObjects(command.Parameters) },
                { "modify_gameobject", command => GameObjectHandler.ModifyGameObject(command.Parameters) },
                { "delete_gameobject", command => GameObjectHandler.DeleteGameObject(command.Parameters) },
                { "get_hierarchy", command => GameObjectHandler.GetHierarchy(command.Parameters) },
                { "create_scene", command => SceneHandler.CreateScene(command.Parameters) },
                { "load_scene", command => SceneHandler.LoadScene(command.Parameters) },
                { "save_scene", command => SceneHandler.SaveScene(command.Parameters) },
                { "list_scenes", command => SceneHandler.ListScenes(command.Parameters) },
                { "get_scene_info", command => SceneHandler.GetSceneInfo(command.Parameters) },
                { "get_gameobject_details", command => SceneAnalysisHandler.GetGameObjectDetails(command.Parameters) },
                { "analyze_scene_contents", command => SceneAnalysisHandler.AnalyzeSceneContents(command.Parameters) },
                { "get_component_values", command => SceneAnalysisHandler.GetComponentValues(command.Parameters) },
                { "find_by_component", command => SceneAnalysisHandler.FindByComponent(command.Parameters) },
                { "get_object_references", command => SceneAnalysisHandler.GetObjectReferences(command.Parameters) },
                { "play_game", command => PlayModeHandler.HandleCommand("play_game", command.Parameters) },
                { "pause_game", command => PlayModeHandler.HandleCommand("pause_game", command.Parameters) },
                { "stop_game", command => PlayModeHandler.HandleCommand("stop_game", command.Parameters) },
                { "get_editor_state", command => PlayModeHandler.HandleCommand("get_editor_state", command.Parameters) },
                { "find_ui_elements", command => UIInteractionHandler.FindUIElements(command.Parameters) },
                { "click_ui_element", command => UIInteractionHandler.ClickUIElement(command.Parameters) },
                { "get_ui_element_state", command => UIInteractionHandler.GetUIElementState(command.Parameters) },
                { "set_ui_element_value", command => UIInteractionHandler.SetUIElementValue(command.Parameters) },
                { "simulate_ui_input", command => UIInteractionHandler.SimulateUIInput(command.Parameters) },
                { "create_prefab", command => AssetManagementHandler.CreatePrefab(command.Parameters) },
                { "modify_prefab", command => AssetManagementHandler.ModifyPrefab(command.Parameters) },
                { "instantiate_prefab", command => AssetManagementHandler.InstantiatePrefab(command.Parameters) },
                { "create_material", command => AssetManagementHandler.CreateMaterial(command.Parameters) },
                { "modify_material", command => AssetManagementHandler.ModifyMaterial(command.Parameters) },
                { "open_prefab", command => AssetManagementHandler.OpenPrefab(command.Parameters) },
                { "exit_prefab_mode", command => AssetManagementHandler.ExitPrefabMode(command.Parameters) },
                { "save_prefab", command => AssetManagementHandler.SavePrefab(command.Parameters) },
                { "create_script", command => ScriptHandler.CreateScript(command.Parameters) },
                { "read_script", command => ScriptHandler.ReadScript(command.Parameters) },
                { "update_script", command => ScriptHandler.UpdateScript(command.Parameters) },
                { "delete_script", command => ScriptHandler.DeleteScript(command.Parameters) },
                { "list_scripts", command => ScriptHandler.ListScripts(command.Parameters) },
                { "validate_script", command => ScriptHandler.ValidateScript(command.Parameters) },
                { "execute_menu_item", command => MenuHandler.ExecuteMenuItem(command.Parameters) },
                { "clear_console", command => ConsoleHandler.ClearConsole(command.Parameters) },
                { "enhanced_read_logs", command => ConsoleHandler.EnhancedReadLogs(command.Parameters) },
                { "capture_screenshot", command => ScreenshotHandler.CaptureScreenshot(command.Parameters) },
                { "analyze_screenshot", command => ScreenshotHandler.AnalyzeScreenshot(command.Parameters) },
                { "add_component", command => ComponentHandler.AddComponent(command.Parameters) },
                { "remove_component", command => ComponentHandler.RemoveComponent(command.Parameters) },
                { "modify_component", command => ComponentHandler.ModifyComponent(command.Parameters) },
                { "list_components", command => ComponentHandler.ListComponents(command.Parameters) },
                { "start_compilation_monitoring", command => CompilationHandler.StartCompilationMonitoring(command.Parameters) },
                { "stop_compilation_monitoring", command => CompilationHandler.StopCompilationMonitoring(command.Parameters) },
                { "get_compilation_state", command => CompilationHandler.GetCompilationState(command.Parameters) },
                { "manage_tags", command => TagManagementHandler.HandleCommand(GetAction(command), command.Parameters) },
                { "manage_layers", command => LayerManagementHandler.HandleCommand(GetAction(command), command.Parameters) },
                { "manage_selection", command => SelectionHandler.HandleCommand(GetAction(command), command.Parameters) },
                { "manage_windows", command => WindowManagementHandler.HandleCommand(GetAction(command), command.Parameters) },
                { "manage_tools", command => ToolManagementHandler.HandleCommand(GetAction(command), command.Parameters) },
                { "manage_asset_import_settings", command => AssetImportSettingsHandler.HandleCommand(GetAction(command), command.Parameters) },
                { "manage_asset_database", command => AssetDatabaseHandler.HandleCommand(GetAction(command), command.Parameters) },
                { "analyze_asset_dependencies", command => AssetDependencyHandler.HandleCommand(GetAction(command), command.Parameters) },
                { "list_tests", command => TestRunnerHandler.ListTests(command.Parameters) },
                { "run_tests", command => TestRunnerHandler.RunTests(command.Parameters) },
                { "get_test_results", command => TestRunnerHandler.GetTestResults(command.Parameters) },
                { "cancel_tests", command => TestRunnerHandler.CancelTests(command.Parameters) }
            };
        
        private static McpStatus _status = McpStatus.NotConfigured;
        public static McpStatus Status
        {
            get => _status;
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    Debug.Log($"[Unity Editor MCP] Status changed to: {value}");
                    RequestInstanceRegistryWrite();
                }
            }
        }
        
        public const int DEFAULT_PORT = 6400;
        private static int currentPort = DEFAULT_PORT;

        /// <summary>
        /// Port this instance's listener is actually bound to, which is DEFAULT_PORT only when it was
        /// free. Exposed to the editor test assembly so integration tests dial this editor rather than
        /// whatever process happens to own 6400.
        /// </summary>
        internal static int ListenerPort
        {
            get { return currentPort; }
        }
        
        /// <summary>
        /// Static constructor - called when Unity loads
        /// </summary>
        static UnityEditorMCP()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            unitySyncContext = SynchronizationContext.Current;
            Interlocked.Exchange(ref lastMainThreadTickTicks, DateTime.UtcNow.Ticks);
            CacheMainThreadValues();
            currentPort = SessionState.GetInt(ListenerPortSessionKey, DEFAULT_PORT);
            Debug.Log("[Unity Editor MCP] Initializing...");
            // The heartbeat is registered *ahead of* the drain so the tick is recorded even when the
            // drain itself runs a long handler, and even if a handler throws.
            EditorApplication.update += MainThreadHeartbeat;
            EditorApplication.update += ProcessCommandQueue;
            EditorApplication.quitting += Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            
            // Start the TCP listener
            StartTcpListener();
        }
        
        /// <summary>
        /// Starts the TCP listener on the configured port
        /// </summary>
        private static void StartTcpListener()
        {
            try
            {
                if (tcpListener != null)
                {
                    StopTcpListener();
                }
                
                isShuttingDown = false;
                cancellationTokenSource = new CancellationTokenSource();
                backgroundToken = cancellationTokenSource.Token;
                TcpListener newListener;
                int boundPort;
                int requestedPort = currentPort;
                int rememberedPort = SessionState.GetInt(ListenerPortSessionKey, DEFAULT_PORT);

                // Exactly one synchronous attempt, then an immediate fallback. Blocking the main thread
                // waiting for the pre-reload socket to leave TIME_WAIT froze the editor on every single
                // domain reload, and nothing needs the port to be stable: the Node server follows this
                // editor through the instance registry (pid + project path), which is rewritten below
                // with whatever port was actually bound. A fixed port only matters when the user opts
                // out of discovery with UNITY_PORT / --port, and that is their own choice of port.
                if (!TryStartTcpListener(requestedPort, out newListener, out boundPort, out SocketException primaryException))
                {
                    bool mayFallBack = requestedPort == DEFAULT_PORT || requestedPort == rememberedPort;
                    if (mayFallBack && primaryException.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        Debug.LogFormat(LogType.Warning, LogOption.NoStacktrace, null, "[Unity Editor MCP] Port {0} is already in use (another Unity Editor?). Falling back to an available loopback port.", requestedPort);

                        if (!TryStartTcpListener(0, out newListener, out boundPort, out SocketException fallbackException))
                        {
                            throw fallbackException;
                        }
                    }
                    else
                    {
                        throw primaryException;
                    }
                }

                tcpListener = newListener;
                currentPort = boundPort;
                SessionState.SetInt(ListenerPortSessionKey, currentPort);

                Status = McpStatus.Disconnected;
                Debug.Log($"[Unity Editor MCP] TCP listener started on port {currentPort}");

                // Republish the registry entry immediately so clients can re-resolve this pid's new port
                // instead of dialling the pre-reload one.
                WriteInstanceRegistry();

                // Start accepting connections asynchronously
                listenerTask = Task.Run(() => AcceptConnectionsAsync(cancellationTokenSource.Token));
                watchdogTask = Task.Run(() => RunQueueWatchdogAsync(cancellationTokenSource.Token));
            }
            catch (SocketException ex)
            {
                Status = McpStatus.Error;
                Debug.LogError($"[Unity Editor MCP] Failed to start TCP listener on port {currentPort}: {ex.Message}");
                
                if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    Debug.LogError($"[Unity Editor MCP] Port {currentPort} is already in use. Please ensure no other instance is running.");
                }
            }
            catch (Exception ex)
            {
                Status = McpStatus.Error;
                Debug.LogError($"[Unity Editor MCP] Unexpected error starting TCP listener: {ex}");
            }
        }

        private static bool TryStartTcpListener(int requestedPort, out TcpListener listener, out int boundPort, out SocketException socketException)
        {
            listener = null;
            boundPort = requestedPort;
            socketException = null;

            try
            {
                listener = new TcpListener(IPAddress.Loopback, requestedPort);
                if (Path.DirectorySeparatorChar != '\\')
                {
                    // On Unix SO_REUSEADDR only relaxes TIME_WAIT; it cannot steal a port from a live
                    // listener (that would need SO_REUSEPORT). On Windows it *can* hijack another
                    // process's socket, so it stays off there.
                    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                }
                listener.Start();
                boundPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                return true;
            }
            catch (SocketException ex)
            {
                listener = null;
                socketException = ex;
                return false;
            }
        }
        
        /// <summary>
        /// Stops the TCP listener
        /// </summary>
        private static void StopTcpListener()
        {
            try
            {
                // Order matters: the flag stops background code from logging or touching statics, the
                // token unblocks the awaiting tasks, and closing the sockets unblocks any in-flight
                // read/write.
                isShuttingDown = true;
                cancellationTokenSource?.Cancel();
                tcpListener?.Stop();
                CloseAllConnections();

                // A short, bounded join so the socket tasks have a chance to leave old-assembly code
                // before beforeAssemblyReload unloads it. It cannot deadlock with the main thread: the
                // tasks only await socket I/O and Task.Delay, never the editor loop. Anything still
                // running past the timeout is left observed but abandoned.
                JoinBackgroundTasks(listenerTask, watchdogTask);
                ObserveBackgroundTask(listenerTask);
                ObserveBackgroundTask(watchdogTask);

                tcpListener = null;
                cancellationTokenSource = null;
                listenerTask = null;
                watchdogTask = null;

                Status = McpStatus.Disconnected;
                Debug.Log("[Unity Editor MCP] TCP listener stopped");
                UnityInstanceRegistry.Delete();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Unity Editor MCP] Error stopping TCP listener: {ex}");
            }
        }
        
        private static void CloseAllConnections()
        {
            ClientConnection[] connections;
            lock (connectionsLock)
            {
                connections = new ClientConnection[activeConnections.Count];
                activeConnections.CopyTo(connections);
                activeConnections.Clear();
            }

            foreach (var connection in connections)
            {
                connection.Close();
            }
        }

        /// <summary>
        /// Waits up to <see cref="ShutdownJoinTimeoutMs"/> in total for the background tasks to exit.
        /// </summary>
        private static void JoinBackgroundTasks(params Task[] tasks)
        {
            var pending = new List<Task>();
            foreach (var task in tasks)
            {
                if (task != null && !task.IsCompleted)
                {
                    pending.Add(task);
                }
            }

            if (pending.Count == 0)
            {
                return;
            }

            try
            {
                Task.WaitAll(pending.ToArray(), ShutdownJoinTimeoutMs);
            }
            catch (AggregateException)
            {
                // Faulted background tasks are expected while the sockets are being torn down.
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Unity Editor MCP] Error joining background tasks: {ex.Message}");
            }
        }

        /// <summary>
        /// Accepts incoming TCP connections asynchronously
        /// </summary>
        private static async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && !isShuttingDown)
            {
                try
                {
                    var tcpClient = await AcceptClientAsync(tcpListener, cancellationToken);
                    if (tcpClient != null)
                    {
                        if (isShuttingDown)
                        {
                            tcpClient.Close();
                            break;
                        }

                        Status = McpStatus.Connected;
                        Debug.Log($"[Unity Editor MCP] Client connected from {tcpClient.Client.RemoteEndPoint}");

                        // Handle client in a separate task
                        _ = Task.Run(() => HandleClientAsync(tcpClient, cancellationToken));
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested && !isShuttingDown)
                    {
                        Debug.LogError($"[Unity Editor MCP] Error accepting connection: {ex}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Accepts a client with cancellation support
        /// </summary>
        private static async Task<TcpClient> AcceptClientAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(() => listener.Stop()))
            {
                try
                {
                    return await listener.AcceptTcpClientAsync();
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
            }
        }
        
        /// <summary>
        /// Handles communication with a connected client
        /// </summary>
        private static async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            ClientConnection connection = null;
            try
            {
                client.ReceiveTimeout = 30000; // 30 second timeout
                client.SendTimeout = 30000;    // ignored by NetworkStream.WriteAsync; see ClientConnection

                var buffer = new byte[4096];
                connection = new ClientConnection(client);
                RegisterConnection(connection);
                var stream = connection.Stream;
                var messageBuffer = new List<byte>();

                while (!cancellationToken.IsCancellationRequested && !isShuttingDown && client.Connected)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                    if (bytesRead == 0)
                    {
                        // Client disconnected
                        break;
                    }
                    
                    // Add received bytes to message buffer
                    for (int i = 0; i < bytesRead; i++)
                    {
                        messageBuffer.Add(buffer[i]);
                    }
                    
                    // Process complete messages
                    while (messageBuffer.Count >= 4)
                    {
                        // Read message length (first 4 bytes, big-endian)
                        var lengthBytes = messageBuffer.GetRange(0, 4).ToArray();
                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(lengthBytes);
                        }
                        var messageLength = BitConverter.ToInt32(lengthBytes, 0);
                        
                        // Check if we have the complete message
                        if (messageBuffer.Count >= 4 + messageLength)
                        {
                            // Extract message
                            var messageBytes = messageBuffer.GetRange(4, messageLength).ToArray();
                            messageBuffer.RemoveRange(0, 4 + messageLength);
                            
                            var json = Encoding.UTF8.GetString(messageBytes);
                            Debug.Log($"[Unity Editor MCP] Received command (length={messageLength}): {RedactCommandJson(json)}");
                            
                            try
                            {
                                // Handle special ping command
                                if (json.Trim().ToLower() == "ping")
                                {
                                    var authError = Response.ErrorResult(
                                        "Plain ping requires an authenticated JSON command envelope",
                                        "AUTH_FAILED",
                                        null
                                    );
                                    await connection.SendAsync(authError, cancellationToken);
                                    continue;
                                }
                                
                                // Parse command
                                var command = JsonConvert.DeserializeObject<Command>(json);
                                if (command != null)
                                {
                                    // Commands that can be answered from thread-safe cached state never
                                    // touch the editor loop, so the bridge stays responsive while the main
                                    // thread is stalled (modal dialog, backgrounded editor, long import).
                                    if (TryHandleWithoutMainThread(command, out string immediateResponse))
                                    {
                                        await connection.SendAsync(immediateResponse, cancellationToken);
                                    }
                                    else
                                    {
                                        // Queue command for processing on main thread
                                        lock (queueLock)
                                        {
                                            commandQueue.Enqueue(new QueuedCommand(command, connection));
                                        }

                                        RequestMainThreadDrain();
                                    }
                                }
                                else
                                {
                                    var errorResponse = Response.ErrorResult("Invalid command format", "PARSE_ERROR", null);
                                    await connection.SendAsync(errorResponse, cancellationToken);
                                }
                            }
                            catch (JsonException ex)
                            {
                                var errorResponse = Response.ErrorResult($"JSON parsing error: {ex.Message}", "JSON_ERROR", null);
                                await connection.SendAsync(errorResponse, cancellationToken);
                            }
                        }
                        else
                        {
                            // Not enough data yet, wait for more
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested || isShuttingDown)
                {
                    // Teardown; nothing to report.
                }
                else if (IsExpectedDisconnectException(ex, connection))
                {
                    // A response that timed out closes the socket deliberately, and the ReadAsync
                    // running concurrently on this loop then fails with ObjectDisposedException. That is
                    // the disconnect we just caused, not an error worth a stack trace in the console.
                    Debug.LogWarning($"[Unity Editor MCP] Client connection closed: {ex.GetType().Name}");
                }
                else
                {
                    Debug.LogError($"[Unity Editor MCP] Client handler error: {ex}");
                }
            }
            finally
            {
                if (connection != null)
                {
                    UnregisterConnection(connection);
                    connection.Close();
                }
                else
                {
                    client?.Close();
                }

                if (!isShuttingDown)
                {
                    if (Status == McpStatus.Connected)
                    {
                        Status = McpStatus.Disconnected;
                    }

                    Debug.Log("[Unity Editor MCP] Client disconnected");
                }
            }
        }

        /// <summary>
        /// True for the socket exceptions that a connection we closed ourselves is expected to produce.
        /// </summary>
        private static bool IsExpectedDisconnectException(Exception ex, ClientConnection connection)
        {
            if (connection == null || !connection.IsClosed)
            {
                return false;
            }

            while (ex != null)
            {
                if (ex is ObjectDisposedException ||
                    ex is IOException ||
                    ex is SocketException ||
                    ex is OperationCanceledException)
                {
                    return true;
                }

                ex = ex.InnerException;
            }

            return false;
        }

        private static void RegisterConnection(ClientConnection connection)
        {
            lock (connectionsLock)
            {
                activeConnections.Add(connection);
            }
        }

        private static void UnregisterConnection(ClientConnection connection)
        {
            lock (connectionsLock)
            {
                activeConnections.Remove(connection);
            }
        }
        
        /// <summary>
        /// One connected client. It owns its own stream and its own write lock, so a client that stops
        /// reading (full TCP window) can only ever block responses addressed to itself - a single shared
        /// lock used to stall every other client, including the socket-thread ping fast path.
        /// </summary>
        private sealed class ClientConnection
        {
            private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
            private readonly TcpClient client;
            private readonly NetworkStream stream;
            private int closed;

            public ClientConnection(TcpClient client)
            {
                this.client = client;
                stream = client.GetStream();
            }

            public NetworkStream Stream
            {
                get { return stream; }
            }

            public bool IsConnected
            {
                get
                {
                    try
                    {
                        return Volatile.Read(ref closed) == 0 && client != null && client.Connected;
                    }
                    catch (ObjectDisposedException)
                    {
                        return false;
                    }
                }
            }

            /// <summary>True once this connection was closed deliberately (write timeout, shutdown).</summary>
            public bool IsClosed
            {
                get { return Volatile.Read(ref closed) != 0; }
            }

            /// <summary>
            /// Writes one length-prefixed frame, giving up (and dropping the client) after
            /// <see cref="ResponseWriteTimeoutMs"/>.
            /// </summary>
            public async Task SendAsync(string message, CancellationToken cancellationToken)
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);
                var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

                // Convert to big-endian
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthBytes);
                }

                if (isShuttingDown || IsClosed)
                {
                    return;
                }

                Debug.Log($"[Unity Editor MCP] Sending response (length={messageBytes.Length}): {message}");

                // Responses are produced from several threads (socket fast path, main-thread dispatch,
                // stall watchdog), so writes to *this* client are serialized to keep the frames intact.
                try
                {
                    await writeLock.WaitAsync(cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    // The connection was closed and its lock disposed while we were queuing for it.
                    return;
                }

                try
                {
                    using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        var writeTask = WriteFrameAsync(lengthBytes, messageBytes, cancellationToken);
                        var timeoutTask = Task.Delay(ResponseWriteTimeoutMs, timeoutSource.Token);
                        var completed = await Task.WhenAny(writeTask, timeoutTask);

                        if (completed != writeTask)
                        {
                            // Closing the socket is what actually unblocks a write parked on a full TCP
                            // window: NetworkStream.WriteAsync honours neither Socket.SendTimeout nor the
                            // cancellation token once the write is in flight.
                            Close();
                            ObserveBackgroundTask(writeTask);
                            throw new TimeoutException(
                                $"Timed out after {ResponseWriteTimeoutMs}ms writing a {messageBytes.Length} byte response; the client was closed.");
                        }

                        timeoutSource.Cancel();
                        ObserveBackgroundTask(timeoutTask);
                        await writeTask;
                    }
                }
                finally
                {
                    try
                    {
                        writeLock.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Closed underneath this write; nothing to release.
                    }

                    // Close() cannot dispose the lock while this write holds it, so the last writer out
                    // of a closed connection does it instead.
                    if (IsClosed)
                    {
                        TryDisposeWriteLock();
                    }
                }
            }

            private async Task WriteFrameAsync(byte[] lengthBytes, byte[] messageBytes, CancellationToken cancellationToken)
            {
                await stream.WriteAsync(lengthBytes, 0, 4, cancellationToken);
                await stream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            public void Close()
            {
                if (Interlocked.Exchange(ref closed, 1) != 0)
                {
                    return;
                }

                try
                {
                    client.Close();
                }
                catch (Exception ex)
                {
                    if (!isShuttingDown)
                    {
                        Debug.LogWarning($"[Unity Editor MCP] Failed to close client: {ex.Message}");
                    }
                }

                TryDisposeWriteLock();
            }

            /// <summary>
            /// Disposes the per-client write lock with the connection, but only while it is free.
            /// SemaphoreSlim.Dispose does not release waiters, so disposing one that a send is holding
            /// (or queued on) would hang that send; in that case the send disposes it on its way out.
            /// </summary>
            private void TryDisposeWriteLock()
            {
                try
                {
                    if (writeLock.Wait(0))
                    {
                        writeLock.Dispose();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed by the other side of the race.
                }
            }
        }

        /// <summary>
        /// Processes queued commands on the Unity main thread
        /// </summary>
        private static void ProcessCommandQueue()
        {
            MainThreadHeartbeat();

            // Re-entrancy guard: a handler that pumps the editor loop (play-mode transitions, modal
            // progress bars, AssetDatabase.Refresh) can re-enter this drain, which would run a second
            // handler nested inside the first and corrupt the in-flight markers.
            if (isProcessingQueue)
            {
                return;
            }

            isProcessingQueue = true;
            try
            {
                DrainCommandQueue();
            }
            finally
            {
                isProcessingQueue = false;
            }
        }

        private static void DrainCommandQueue()
        {
            RefreshEditorSnapshot();

            while (true)
            {
                QueuedCommand queued;
                lock (queueLock)
                {
                    if (commandQueue.Count == 0)
                    {
                        break;
                    }

                    queued = commandQueue.Dequeue();
                }

                // Deliberately outside the lock: handlers can run for seconds (asset import, play mode),
                // and holding queueLock would block every socket thread trying to enqueue.
                // The in-flight marker tells the watchdog that a silent main thread is working, not stuck.
                handlerInFlightCommandType = queued.Command != null ? queued.Command.Type : null;
                Interlocked.Exchange(ref handlerInFlightSinceTicks, DateTime.UtcNow.Ticks);
                try
                {
                    ProcessCommand(queued.Command, queued.Connection);
                }
                finally
                {
                    Interlocked.Exchange(ref handlerInFlightSinceTicks, 0);
                    handlerInFlightCommandType = null;
                    MainThreadHeartbeat();
                }
            }

            if (tcpListener != null &&
                (registryWritePending || EditorApplication.timeSinceStartup - lastRegistryWriteTime >= RegistryHeartbeatIntervalSeconds))
            {
                WriteInstanceRegistry();
            }
        }
        
        /// <summary>
        /// Processes a single command
        /// </summary>
        private static void ProcessCommand(Command command, ClientConnection connection)
        {
            Debug.Log($"[Unity Editor MCP] Processing command: {command}");

            // Handler failures must always become a response: an unobserved exception used to escape the
            // old `async void` dispatch and leave the client waiting for its timeout.
            string response = BuildCommandResponse(command);
            SendResponseInBackground(connection, response);
        }

        /// <summary>
        /// Runs the registered handler for a command and converts any failure into an error response.
        /// </summary>
        private static string BuildCommandResponse(Command command)
        {
            try
            {
                if (!IsAuthorized(command))
                {
                    return Response.ErrorResult(
                        command.Id,
                        "Invalid or missing Unity Editor MCP auth token",
                        "AUTH_FAILED",
                        new { commandType = command.Type }
                    );
                }

                if (TryExecuteRegisteredCommand(command, out object commandResult))
                {
                    return Response.SuccessResult(command.Id, commandResult);
                }

                return Response.ErrorResult(
                    command.Id,
                    $"Unknown command type: {command.Type}",
                    "UNKNOWN_COMMAND",
                    new { commandType = command.Type }
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Unity Editor MCP] Error processing command {command}: {ex}");

                try
                {
                    return Response.ErrorResult(
                        command.Id,
                        $"Internal error: {ex.Message}",
                        "INTERNAL_ERROR",
                        new
                        {
                            commandType = command.Type,
                            stackTrace = ex.StackTrace
                        }
                    );
                }
                catch (Exception serializationError)
                {
                    Debug.LogError($"[Unity Editor MCP] Failed to serialize error response: {serializationError.Message}");
                    return Response.ErrorResult(command.Id, "Internal error", "INTERNAL_ERROR", null);
                }
            }
        }

        private static void SendResponseInBackground(ClientConnection connection, string response)
        {
            // The listener's token, not CancellationToken.None: a send that is still parked when the
            // domain reloads must stop instead of writing from an unloading assembly.
            var token = backgroundToken;
            ObserveBackgroundTask(Task.Run(async () =>
            {
                try
                {
                    if (!isShuttingDown && connection != null && connection.IsConnected)
                    {
                        await connection.SendAsync(response, token);
                    }
                }
                catch (Exception ex)
                {
                    if (!isShuttingDown)
                    {
                        Debug.LogWarning($"[Unity Editor MCP] Failed to send response: {ex.Message}");
                    }
                }
            }));
        }

        private static void ObserveBackgroundTask(Task task)
        {
            if (task == null)
            {
                return;
            }

            task.ContinueWith(
                completed => { var ignored = completed.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Shuts down the MCP system
        /// </summary>
        private static void Shutdown()
        {
            Debug.Log("[Unity Editor MCP] Shutting down...");
            StopTcpListener();
            UnityInstanceRegistry.Delete();
            EditorApplication.update -= MainThreadHeartbeat;
            EditorApplication.update -= ProcessCommandQueue;
            EditorApplication.quitting -= Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
        }
        
        /// <summary>
        /// Restarts the TCP listener
        /// </summary>
        public static void Restart()
        {
            Debug.Log("[Unity Editor MCP] Restarting...");
            StopTcpListener();
            StartTcpListener();
        }
        
        /// <summary>
        /// Changes the listening port and restarts
        /// </summary>
        public static void ChangePort(int newPort)
        {
            if (newPort < 1024 || newPort > 65535)
            {
                Debug.LogError($"[Unity Editor MCP] Invalid port number: {newPort}. Must be between 1024 and 65535.");
                return;
            }
            
            currentPort = newPort;
            Restart();
        }

        private static void WriteInstanceRegistry()
        {
            if (tcpListener == null)
            {
                return;
            }

            registryWritePending = false;
            UnityInstanceRegistry.Write(currentPort, Status);
            lastRegistryWriteTime = EditorApplication.timeSinceStartup;
        }

        private static void RequestInstanceRegistryWrite()
        {
            if (tcpListener == null)
            {
                return;
            }

            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            {
                WriteInstanceRegistry();
            }
            else
            {
                registryWritePending = true;
            }
        }

        private static bool IsAuthorized(Command command)
        {
            return command != null &&
                   !string.IsNullOrEmpty(command.AuthToken) &&
                   string.Equals(command.AuthToken, UnityInstanceRegistry.AuthToken, StringComparison.Ordinal);
        }

        private static bool TryExecuteRegisteredCommand(Command command, out object result)
        {
            result = null;
            if (command == null || string.IsNullOrEmpty(command.Type))
            {
                return false;
            }

            if (!CommandHandlers.TryGetValue(command.Type, out CommandHandlerDelegate handler))
            {
                return false;
            }

            result = handler(command);
            return true;
        }

        /// <summary>
        /// Builds the ping payload. Only thread-safe cached state is used so this can also answer directly
        /// on the socket thread while the editor main thread is stalled.
        /// </summary>
        private static object HandlePing(Command command)
        {
            return new
            {
                message = "pong",
                echo = command.Parameters?["message"]?.ToString(),
                timestamp = DateTime.UtcNow.ToString("o"),
                unityVersion = cachedUnityVersion,
                projectPath = cachedProjectPath,
                workspaceId = UnityInstanceRegistry.WorkspaceId,
                workspaceIdSource = UnityInstanceRegistry.WorkspaceIdSource,
                git = UnityInstanceRegistry.GitInfo,
                port = currentPort,
                packageVersion = cachedPackageVersion,
                mainThreadLastTickSecondsAgo = MainThreadSecondsSinceTick(),
                mainThreadResponsive = IsMainThreadResponsive(),
                mainThreadInFlightCommand = InFlightCommandType(),
                mainThreadInFlightSeconds = InFlightSeconds()
            };
        }

        /// <summary>
        /// Command type the main thread is currently executing, or null. Read from the thread-safe
        /// markers so a stalled or busy editor can still tell a client what it is stuck on.
        /// </summary>
        private static string InFlightCommandType()
        {
            return Interlocked.Read(ref handlerInFlightSinceTicks) > 0 ? handlerInFlightCommandType : null;
        }

        /// <summary>Seconds the in-flight handler has been running, or -1 when none is running.</summary>
        private static double InFlightSeconds()
        {
            long ticks = Interlocked.Read(ref handlerInFlightSinceTicks);
            if (ticks <= 0)
            {
                return -1;
            }

            double elapsed = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            return elapsed < 0 ? 0 : elapsed;
        }

        /// <summary>
        /// Captures values that may only be read on the Unity main thread.
        /// </summary>
        private static void CacheMainThreadValues()
        {
            try
            {
                cachedUnityVersion = Application.unityVersion;
                cachedApplicationPath = EditorApplication.applicationPath;
                cachedApplicationContentsPath = EditorApplication.applicationContentsPath;
                // Touching these on the main thread forces UnityInstanceRegistry's static initialization
                // (which reads Application.dataPath) to happen here rather than on a socket thread.
                cachedProjectPath = UnityInstanceRegistry.ProjectPath;
                cachedPackageVersion = UnityInstanceRegistry.PackageVersion;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Unity Editor MCP] Failed to cache editor metadata: {ex.Message}");
            }
        }

        /// <summary>
        /// Refreshes the immutable editor snapshot that off-thread get_editor_state replies are built from.
        /// Called on every main-thread tick.
        /// </summary>
        private static void RefreshEditorSnapshot()
        {
            try
            {
                bool isPlaying = EditorApplication.isPlaying;
                bool isPaused = EditorApplication.isPaused;
                bool isCompiling = EditorApplication.isCompiling;
                bool isUpdating = EditorApplication.isUpdating;
                int frameCount = Time.frameCount;
                float time = Time.time;
                float timeScale = Time.timeScale;

                // Skip the allocation when nothing an off-thread reader cares about has moved. The
                // editor ticks many times a second and this used to allocate a snapshot every time.
                var previous = editorSnapshot;
                if (previous != null &&
                    previous.IsPlaying == isPlaying &&
                    previous.IsPaused == isPaused &&
                    previous.IsCompiling == isCompiling &&
                    previous.IsUpdating == isUpdating &&
                    previous.FrameCount == frameCount &&
                    previous.Time == time &&
                    previous.TimeScale == timeScale &&
                    (DateTime.UtcNow - previous.CapturedAtUtc).TotalSeconds < EditorSnapshotMaxAgeSeconds)
                {
                    return;
                }

                editorSnapshot = new EditorSnapshot
                {
                    IsPlaying = isPlaying,
                    IsPaused = isPaused,
                    IsCompiling = isCompiling,
                    IsUpdating = isUpdating,
                    TimeSinceStartup = EditorApplication.timeSinceStartup,
                    FrameCount = frameCount,
                    Time = time,
                    RealtimeSinceStartup = Time.realtimeSinceStartup,
                    TimeScale = timeScale,
                    CapturedAtUtc = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Unity Editor MCP] Failed to refresh editor snapshot: {ex.Message}");
            }
        }

        /// <summary>
        /// Trims latency by queueing a drain onto Unity's main-thread synchronization context, which the
        /// editor loop services in the same tick as EditorApplication.update.
        ///
        /// This does NOT wake a stopped editor loop. A backgrounded editor (macOS App Nap), a modal
        /// dialog or a blocked main thread simply never services the post, exactly as it never runs
        /// EditorApplication.update - the queued command then waits for the loop to resume, or is failed
        /// by the stall watchdog. The mitigations that actually work are the socket-thread fast paths
        /// (ping / get_editor_state) and the MAIN_THREAD_STALLED error; see the README troubleshooting
        /// section.
        /// </summary>
        private static void RequestMainThreadDrain()
        {
            var context = unitySyncContext;
            if (context == null || isShuttingDown)
            {
                return;
            }

            try
            {
                context.Post(_ => ProcessCommandQueue(), null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Unity Editor MCP] Failed to post main thread drain: {ex.Message}");
            }
        }

        /// <summary>
        /// Answers commands that need no Unity API access directly on the socket thread.
        /// </summary>
        private static bool TryHandleWithoutMainThread(Command command, out string response)
        {
            response = null;
            if (command == null || string.IsNullOrEmpty(command.Type))
            {
                return false;
            }

            bool isPing = string.Equals(command.Type, "ping", StringComparison.OrdinalIgnoreCase);
            bool isEditorState = string.Equals(command.Type, "get_editor_state", StringComparison.OrdinalIgnoreCase);
            if (!isPing && !isEditorState)
            {
                return false;
            }

            if (!IsAuthorized(command))
            {
                response = Response.ErrorResult(
                    command.Id,
                    "Invalid or missing Unity Editor MCP auth token",
                    "AUTH_FAILED",
                    new { commandType = command.Type }
                );
                return true;
            }

            try
            {
                if (isPing)
                {
                    response = Response.SuccessResult(command.Id, HandlePing(command));
                    return true;
                }

                var snapshot = editorSnapshot;
                if (snapshot == null)
                {
                    // No main-thread tick has happened yet; let the normal queue path answer it.
                    return false;
                }

                response = Response.SuccessResult(command.Id, BuildCachedEditorState(snapshot));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Unity Editor MCP] Off-thread handling of {command.Type} failed: {ex.Message}");
                response = null;
                return false;
            }
        }

        private static JObject BuildCachedEditorState(EditorSnapshot snapshot)
        {
            var state = new JObject
            {
                ["isPlaying"] = snapshot.IsPlaying,
                ["isPaused"] = snapshot.IsPaused,
                ["isCompiling"] = snapshot.IsCompiling,
                ["isUpdating"] = snapshot.IsUpdating,
                ["applicationPath"] = cachedApplicationPath,
                ["applicationContentsPath"] = cachedApplicationContentsPath,
                ["timeSinceStartup"] = snapshot.TimeSinceStartup,
                ["frameCount"] = snapshot.FrameCount,
                ["time"] = snapshot.Time,
                ["realtimeSinceStartup"] = snapshot.RealtimeSinceStartup,
                ["timeScale"] = snapshot.TimeScale,
                ["isPlayerLoopAdvancing"] = snapshot.IsPlaying && !snapshot.IsPaused
                    ? snapshot.FrameCount > 1 && snapshot.Time > 0f
                    : false,
                ["fromCache"] = true,
                ["capturedAt"] = snapshot.CapturedAtUtc.ToString("o"),
                ["mainThreadLastTickSecondsAgo"] = MainThreadSecondsSinceTick(),
                ["mainThreadResponsive"] = IsMainThreadResponsive(),
                ["mainThreadInFlightCommand"] = InFlightCommandType(),
                ["mainThreadInFlightSeconds"] = InFlightSeconds()
            };

            return new JObject
            {
                ["status"] = "success",
                ["state"] = state
            };
        }

        /// <summary>
        /// Records that the editor loop is alive. Registered as its own EditorApplication.update
        /// callback ahead of the drain, so the heartbeat is independent of whether the drain runs (or
        /// throws) and of how long any single handler takes.
        /// </summary>
        private static void MainThreadHeartbeat()
        {
            Interlocked.Exchange(ref lastMainThreadTickTicks, DateTime.UtcNow.Ticks);
        }

        private static DateTime? TicksToUtc(long ticks)
        {
            return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : (DateTime?)null;
        }

        /// <summary>
        /// Seconds since the Unity main thread last ran the command pump, or -1 if it never has.
        /// </summary>
        private static double MainThreadSecondsSinceTick()
        {
            long ticks = Interlocked.Read(ref lastMainThreadTickTicks);
            if (ticks <= 0)
            {
                return -1;
            }

            var elapsed = (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            return elapsed < 0 ? 0 : elapsed;
        }

        /// <summary>
        /// Evaluates the current main-thread state from the thread-safe tick / handler markers.
        /// </summary>
        private static MainThreadStallDecision EvaluateMainThread(DateTime? queueHeadEnqueuedAt)
        {
            return MainThreadStallPolicy.Evaluate(
                DateTime.UtcNow,
                TicksToUtc(Interlocked.Read(ref lastMainThreadTickTicks)),
                TicksToUtc(Interlocked.Read(ref handlerInFlightSinceTicks)),
                handlerInFlightCommandType,
                queueHeadEnqueuedAt,
                MainThreadStallSeconds,
                MainThreadHandlerCeilingSeconds);
        }

        /// <summary>
        /// True while the editor loop is alive. A main thread that is silent because it is running one
        /// of our own handlers still counts as responsive - only a genuine stall does not.
        /// </summary>
        private static bool IsMainThreadResponsive()
        {
            return EvaluateMainThread(null).State != MainThreadState.Stalled;
        }

        /// <summary>
        /// Fails commands that are stuck in the queue because the editor loop stopped running, so clients
        /// get an actionable error instead of an opaque timeout. Commands queued behind a long-running
        /// handler of ours are deliberately left waiting: the main thread is working, not stuck.
        /// </summary>
        private static async Task RunQueueWatchdogAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && !isShuttingDown)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                DateTime? queueHeadEnqueuedAt = null;
                lock (queueLock)
                {
                    if (commandQueue.Count > 0)
                    {
                        queueHeadEnqueuedAt = commandQueue.Peek().EnqueuedAtUtc;
                    }
                }

                var handlerStartedAt = TicksToUtc(Interlocked.Read(ref handlerInFlightSinceTicks));
                var decision = MainThreadStallPolicy.Evaluate(
                    now,
                    TicksToUtc(Interlocked.Read(ref lastMainThreadTickTicks)),
                    handlerStartedAt,
                    handlerInFlightCommandType,
                    queueHeadEnqueuedAt,
                    MainThreadStallSeconds,
                    MainThreadHandlerCeilingSeconds,
                    ceilingReportedForHandlerStartedUtc);

                if (!decision.ShouldFailQueuedCommands)
                {
                    continue;
                }

                if (decision.CeilingExceeded)
                {
                    // Report this stuck handler once. Commands queued afterwards wait for it instead of
                    // being failed on every tick; ping / get_editor_state keep answering on the socket
                    // thread and say what is in flight and for how long.
                    ceilingReportedForHandlerStartedUtc = handlerStartedAt;
                }

                List<QueuedCommand> stalled = null;
                lock (queueLock)
                {
                    while (commandQueue.Count > 0 &&
                           (now - commandQueue.Peek().EnqueuedAtUtc).TotalSeconds >= MainThreadStallSeconds)
                    {
                        if (stalled == null)
                        {
                            stalled = new List<QueuedCommand>();
                        }

                        stalled.Add(commandQueue.Dequeue());
                    }
                }

                if (stalled == null)
                {
                    continue;
                }

                Debug.LogWarning($"[Unity Editor MCP] {decision.Reason}; failing {stalled.Count} queued command(s).");

                foreach (var queued in stalled)
                {
                    var errorResponse = Response.ErrorResult(
                        queued.Command.Id,
                        decision.Reason,
                        "MAIN_THREAD_STALLED",
                        new
                        {
                            commandType = queued.Command.Type,
                            mainThreadState = decision.State.ToString(),
                            mainThreadLastTickSecondsAgo = decision.SecondsSinceTick,
                            busyCommandType = decision.BusyCommandType,
                            busyForSeconds = decision.HandlerRunningSeconds,
                            queuedForSeconds = (now - queued.EnqueuedAtUtc).TotalSeconds
                        }
                    );

                    SendResponseInBackground(queued.Connection, errorResponse);
                }
            }
        }

        private sealed class QueuedCommand
        {
            public QueuedCommand(Command command, ClientConnection connection)
            {
                Command = command;
                Connection = connection;
                EnqueuedAtUtc = DateTime.UtcNow;
            }

            public Command Command { get; private set; }
            public ClientConnection Connection { get; private set; }
            public DateTime EnqueuedAtUtc { get; private set; }
        }

        private sealed class EditorSnapshot
        {
            public bool IsPlaying;
            public bool IsPaused;
            public bool IsCompiling;
            public bool IsUpdating;
            public double TimeSinceStartup;
            public int FrameCount;
            public float Time;
            public float RealtimeSinceStartup;
            public float TimeScale;
            public DateTime CapturedAtUtc;
        }

        private static object HandleReadLogs(JObject parameters)
        {
            int count = 100;
            string logTypeFilter = null;

            if (parameters != null)
            {
                if (parameters.ContainsKey("count") &&
                    int.TryParse(parameters["count"].ToString(), out int parsedCount))
                {
                    count = Math.Min(Math.Max(parsedCount, 1), 1000);
                }

                if (parameters.ContainsKey("logType"))
                {
                    logTypeFilter = parameters["logType"].ToString();
                }
            }

            LogType? filterType = null;
            if (!string.IsNullOrEmpty(logTypeFilter) &&
                Enum.TryParse(logTypeFilter, true, out LogType parsed))
            {
                filterType = parsed;
            }

            var logs = LogCapture.GetLogs(count, filterType);
            var logData = new List<object>();

            foreach (var log in logs)
            {
                logData.Add(new
                {
                    message = log.message,
                    stackTrace = log.stackTrace,
                    logType = log.logType.ToString(),
                    timestamp = log.timestamp.ToString("o")
                });
            }

            return new
            {
                logs = logData,
                count = logData.Count,
                totalCaptured = logs.Count
            };
        }

        private static object HandleClearLogs()
        {
            LogCapture.ClearLogs();
            return new
            {
                message = "Logs cleared successfully",
                timestamp = DateTime.UtcNow.ToString("o")
            };
        }

        private static object HandleRefreshAssets()
        {
            AssetDatabase.Refresh();
            return new
            {
                message = "Asset refresh triggered",
                isCompiling = EditorApplication.isCompiling,
                timestamp = DateTime.UtcNow.ToString("o")
            };
        }

        private static string GetAction(Command command)
        {
            return command.Parameters?["action"]?.ToString();
        }

        private static string RedactCommandJson(string json)
        {
            try
            {
                var token = JObject.Parse(json);
                if (token["authToken"] != null)
                {
                    token["authToken"] = "[redacted]";
                }

                return token.ToString(Formatting.None);
            }
            catch
            {
                return json;
            }
        }
    }
}
