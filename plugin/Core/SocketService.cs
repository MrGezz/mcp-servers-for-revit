using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Models.JsonRPC;
using RevitMCPSDK.API.Interfaces;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;

namespace revit_mcp_plugin.Core
{
    public class SocketService
    {
        private static SocketService _instance;
        // One listener per address family. A single IPv4 listener is invisible to a
        // client that resolves "localhost" to ::1 first — which is what Node 17+ on
        // Windows does, and what issue #29 is.
        private readonly List<TcpListener> _listeners = new List<TcpListener>();
        private readonly List<Thread> _listenerThreads = new List<Thread>();
        private bool _isRunning;
        private int _port = 8080;
        private UIApplication _uiApp;
        private ICommandRegistry _commandRegistry;
        private ILogger _logger;
        private CommandExecutor _commandExecutor;
        private int _mainThreadId;

        public static SocketService Instance
        {
            get
            {
                if(_instance == null)
                    _instance = new SocketService();
                return _instance;
            }
        }

        private SocketService()
        {
            _commandRegistry = new RevitCommandRegistry();
            _logger = new Logger();
        }

        public bool IsRunning => _isRunning;

        public int Port
        {
            get => _port;
            set => _port = value;
        }

        // Initialization.
        public void Initialize(UIApplication uiApp)
        {
            _uiApp = uiApp;

            // Initialize ExternalEventManager
            ExternalEventManager.Instance.Initialize(uiApp, _logger);

            // Get the current Revit version.
            var versionAdapter = new RevitMCPSDK.API.Utils.RevitVersionAdapter(_uiApp.Application);
            string currentVersion = versionAdapter.GetRevitVersion();
            _logger.Info("Current Revit version: {0}", currentVersion);



            // Capture the Revit main thread identity so that CommandExecutor can
            // detect off-thread command execution and fail loudly instead of
            // letting an unguarded Revit API call crash the process.
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            // Create CommandExecutor
            _commandExecutor = new CommandExecutor(_commandRegistry, _logger, _mainThreadId);

            // Load configuration and register commands.
            ConfigurationManager configManager = new ConfigurationManager(_logger);
            configManager.LoadConfiguration();
            

            //// Read the service port from the configuration.
            //if (configManager.Config.Settings.Port > 0)
            //{
            //    _port = configManager.Config.Settings.Port;
            //}
            _port = 8080; // Hard-wired port number.

            // Load command.
            CommandManager commandManager = new CommandManager(
                _commandRegistry, _logger, configManager, _uiApp);
            commandManager.LoadCommands();

            _logger.Info($"Socket service initialized on port {_port}");
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _isRunning = true;
                StartListeners();

                if (_listeners.Count == 0)
                {
                    // Every address family failed to bind. Previously this left
                    // _isRunning true with nothing listening, so the plugin reported
                    // itself started and every client saw ECONNREFUSED.
                    _isRunning = false;
                    _logger.Error("Socket service failed to start: no address could be bound on port {0}", _port);
                    return;
                }
            }
            catch (Exception ex)
            {
                _isRunning = false;
                _logger.Error("Socket service failed to start on port {0}: {1}", _port, ex.Message);
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _isRunning = false;

                foreach (TcpListener listener in _listeners)
                {
                    try { listener.Stop(); }
                    catch (Exception ex) { _logger.Warning("Error stopping listener: {0}", ex.Message); }
                }
                _listeners.Clear();

                foreach (Thread thread in _listenerThreads)
                {
                    if (thread != null && thread.IsAlive)
                    {
                        thread.Join(1000);
                    }
                }
                _listenerThreads.Clear();
            }
            catch (Exception ex)
            {
                _logger.Error("Error during socket service shutdown: {0}", ex.Message);
            }
        }

        /// <summary>
        /// <para>Start one listener per available loopback address family.</para>
        /// </summary>
        /// <remarks>
        /// Two decisions here are deliberate.
        ///
        /// BOTH FAMILIES. The MCP server dials 127.0.0.1, but any client that hands
        /// "localhost" to the resolver is given ::1 first on Node 17+ / Windows. To
        /// that client a lone IPv4 listener does not exist, and the symptom is
        /// ECONNREFUSED on every tool call (issue #29). One listener per family takes
        /// the resolver out of the argument entirely.
        ///
        /// LOOPBACK, NOT IPAddress.Any. This socket accepts send_code_to_revit, which
        /// compiles and runs arbitrary C# inside the user's Revit session with no
        /// authentication of any kind. Binding the wildcard address offered that to
        /// every host able to route to this machine. Loopback is the right default;
        /// REVIT_MCP_BIND_ANY=1 opts back in for anyone who needs remote access and
        /// understands what it exposes.
        /// </remarks>
        private void StartListeners()
        {
            bool bindAny = string.Equals(
                Environment.GetEnvironmentVariable("REVIT_MCP_BIND_ANY"), "1", StringComparison.Ordinal);

            List<IPAddress> addresses = new List<IPAddress>();
            if (bindAny)
            {
                _logger.Warning("REVIT_MCP_BIND_ANY=1: listening on ALL network interfaces. send_code_to_revit becomes reachable from the network.");
                addresses.Add(IPAddress.Any);
                if (Socket.OSSupportsIPv6) addresses.Add(IPAddress.IPv6Any);
            }
            else
            {
                addresses.Add(IPAddress.Loopback);
                if (Socket.OSSupportsIPv6) addresses.Add(IPAddress.IPv6Loopback);
            }

            foreach (IPAddress address in addresses)
            {
                TcpListener listener = null;
                try
                {
                    listener = new TcpListener(address, _port);
                    listener.Start();
                    _listeners.Add(listener);

                    Thread thread = new Thread(ListenForClients)
                    {
                        IsBackground = true
                    };
                    _listenerThreads.Add(thread);
                    thread.Start(listener);

                    _logger.Info("Listening on {0}:{1}", address, _port);
                }
                catch (Exception ex)
                {
                    // One family failing is survivable so long as the other bound; both
                    // failing is reported by the caller. Either way, name which and why.
                    _logger.Warning("Could not bind {0}:{1}: {2}", address, _port, ex.Message);
                    try { if (listener != null) listener.Stop(); }
                    catch (Exception cleanupEx) { _logger.Warning("Cleanup of failed listener: {0}", cleanupEx.Message); }
                }
            }
        }

        private void ListenForClients(object listenerObj)
        {
            TcpListener listener = (TcpListener)listenerObj;

            try
            {
                while (_isRunning)
                {
                    TcpClient client = listener.AcceptTcpClient();

                    Thread clientThread = new Thread(HandleClientCommunication)
                    {
                        IsBackground = true
                    };
                    clientThread.Start(client);
                }
            }
            catch (SocketException)
            {
                // Expected when Stop() closes the listener socket during shutdown.
                _logger.Info("Listener stopped.");
            }
            catch (Exception ex)
            {
                _logger.Error("Listener thread error: {0}", ex.Message);
            }
        }

        private void HandleClientCommunication(object clientObj)
        {
            TcpClient tcpClient = (TcpClient)clientObj;
            NetworkStream stream = tcpClient.GetStream();

            try
            {
                byte[] buffer = new byte[8192];

                // A single 8 KB read used to be treated as one whole JSON-RPC request.
                // Any request larger than the buffer — send_code_to_revit routinely is —
                // arrived split across reads, and every fragment failed to parse.
                // Accumulate instead, and dispatch only once the text forms a complete
                // JSON value. A Decoder rather than GetString-per-chunk, because a
                // multi-byte UTF-8 character can straddle a read boundary.
                Decoder decoder = Encoding.UTF8.GetDecoder();
                StringBuilder pending = new StringBuilder();

                while (_isRunning && tcpClient.Connected)
                {
                    // Read client messages.
                    int bytesRead = 0;

                    try
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException)
                    {
                        // Client disconnected.
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        // Client disconnected.
                        break;
                    }

                    char[] chars = new char[decoder.GetCharCount(buffer, 0, bytesRead)];
                    int charCount = decoder.GetChars(buffer, 0, bytesRead, chars, 0);
                    pending.Append(chars, 0, charCount);

                    if (pending.Length > MaxRequestChars)
                    {
                        // Refuse to grow without bound on a client that never sends a
                        // parseable request. Loud, not silent.
                        string oversize = CreateErrorResponse(null, JsonRPCErrorCodes.InvalidRequest,
                            $"Request exceeded {MaxRequestChars} characters without forming valid JSON");
                        byte[] oversizeData = Encoding.UTF8.GetBytes(oversize);
                        stream.Write(oversizeData, 0, oversizeData.Length);
                        break;
                    }

                    string message = pending.ToString();
                    if (!IsCompleteJson(message))
                    {
                        // Partial request. Keep reading rather than trying to parse half
                        // a message and reporting it as malformed.
                        continue;
                    }
                    pending.Clear();

                    System.Diagnostics.Trace.WriteLine($"Received message: {message}");

                    string response = ProcessJsonRPCRequest(message);

                    // Send response.
                    byte[] responseData = Encoding.UTF8.GetBytes(response);
                    stream.Write(responseData, 0, responseData.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Client communication error: {0}", ex.Message);
            }
            finally
            {
                tcpClient.Close();
            }
        }

        // Large enough for any realistic send_code_to_revit payload, small enough that
        // a client which never completes a request cannot exhaust memory.
        private const int MaxRequestChars = 16 * 1024 * 1024;

        /// <summary>
        /// True when <paramref name="text"/> is a complete JSON value — every brace and
        /// bracket opened outside a string literal has been closed again.
        /// </summary>
        /// <remarks>
        /// This is a FRAMING check, not a validator; ProcessJsonRPCRequest still does the
        /// real parse and still rejects malformed input. It exists because the wire
        /// protocol carries no length prefix and no delimiter, so "is a whole message in
        /// the buffer yet" has to be answered from the bytes themselves.
        /// </remarks>
        private static bool IsCompleteJson(string text)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            bool sawStructure = false;

            foreach (char c in text)
            {
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') { inString = true; continue; }
                if (c == '{' || c == '[') { depth++; sawStructure = true; continue; }
                if (c == '}' || c == ']') { depth--; continue; }
            }

            return sawStructure && depth == 0 && !inString;
        }

        private string ProcessJsonRPCRequest(string requestJson)
        {
            JsonRPCRequest request;

            try
            {
                // Parse JSON-RPC requests.
                request = JsonConvert.DeserializeObject<JsonRPCRequest>(requestJson);

                // Verify that the request format is valid.
                if (request == null || !request.IsValid())
                {
                    return CreateErrorResponse(
                        null,
                        JsonRPCErrorCodes.InvalidRequest,
                        "Invalid JSON-RPC request"
                    );
                }

                // Delegate to CommandExecutor, which owns the registry lookup,
                // the thread-safety guard, and structured error handling.
                return _commandExecutor.ExecuteCommand(request);
            }
            catch (JsonException)
            {
                // JSON parsing error.
                return CreateErrorResponse(
                    null,
                    JsonRPCErrorCodes.ParseError,
                    "Invalid JSON"
                );
            }
            catch (Exception ex)
            {
                // Catch other errors produced when processing requests.
                return CreateErrorResponse(
                    null,
                    JsonRPCErrorCodes.InternalError,
                    $"Internal error: {ex.Message}"
                );
            }
        }

        private string CreateErrorResponse(string id, int code, string message, object data = null)
        {
            var response = new JsonRPCErrorResponse
            {
                Id = id,
                Error = new JsonRPCError
                {
                    Code = code,
                    Message = message,
                    Data = data != null ? JToken.FromObject(data) : null
                }
            };

            return response.ToJson();
        }
    }
}
