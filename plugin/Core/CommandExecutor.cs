using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Interfaces;
using RevitMCPSDK.API.Models.JsonRPC;
using RevitMCPSDK.Exceptions;
using System;
using System.Threading;

namespace revit_mcp_plugin.Core
{
    public class CommandExecutor
    {
        private readonly ICommandRegistry _commandRegistry;
        private readonly ILogger _logger;
        private readonly int _mainThreadId;

        public CommandExecutor(ICommandRegistry commandRegistry, ILogger logger, int mainThreadId)
        {
            _commandRegistry = commandRegistry;
            _logger = logger;
            _mainThreadId = mainThreadId;
        }

        /// <summary>
        /// Returns true when the calling code is running on the Revit API main thread.
        /// Commands that touch the Revit API directly (without marshalling through
        /// <see cref="ExternalEventManager"/>) must run on this thread.
        /// </summary>
        public bool IsOnMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        /// <summary>
        /// The Revit API main thread id, captured at add-in startup.
        /// </summary>
        public int MainThreadId => _mainThreadId;

        /// <summary>
        /// <para>The structural guard P0-2 asks for.</para>
        /// <para>
        /// Call this at the top of any code path that touches the Revit API WITHOUT
        /// going through an ExternalEvent. Off the API thread the Revit API does not
        /// reliably raise a managed exception - it can take the whole process down
        /// with an access violation, which Revit then reports to the user as a crash
        /// with no add-in named. This converts that into an immediate, attributable
        /// InvalidOperationException at the offending call site.
        /// </para>
        /// <para>
        /// It is deliberately NOT called on the normal command path: commands marshal
        /// through ExternalEvent and are SUPPOSED to run here on the socket thread.
        /// </para>
        /// </summary>
        /// <param name="context">What was about to be done, named for the log.</param>
        public void RequireRevitApiThread(string context)
        {
            if (IsOnMainThread) return;

            string message =
                $"'{context}' touched the Revit API on thread " +
                $"{Thread.CurrentThread.ManagedThreadId}, but the API may only be used on the " +
                $"Revit main thread ({_mainThreadId}). Marshal the work through an " +
                "IWaitableExternalEventHandler / ExternalEvent instead.";

            _logger.Error("{0}", message);
            throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Executes a Revit command declared inside a JSON-RPC request.
        /// </summary>
        /// <param name="request">A JSON-RPC request.</param>
        /// <returns></returns>
        public string ExecuteCommand(JsonRPCRequest request)
        {
            try
            {
                // Find command
                if (!_commandRegistry.TryGetCommand(request.Method, out var command))
                {
                    _logger.Warning("Command not found: {0}", request.Method);
                    return CreateErrorResponse(request.Id,
                        JsonRPCErrorCodes.MethodNotFound,
                        $"Method not found: '{request.Method}'");
                }

                _logger.Info("Executing command: {0}", request.Method);

                // NOTE ON THREADING - P0-2. This runs on the socket thread, and that is
                // CORRECT for this architecture, not a defect to be logged. Every
                // command marshals its own Revit work by raising an ExternalEvent and
                // waiting (ExternalEventCommandBase.RaiseAndWaitForCompletion), so the
                // socket thread is the thread that is supposed to be blocked.
                //
                // Warning here on every request would print a failure-shaped line on
                // every SUCCESS - the same defect as issues #47/#48, where the healthy
                // registration path had been given the catch block's message.
                //
                // The backlog's "Fix A" - wrapping this Execute call in an ExternalEvent
                // so it runs on the API thread - MUST NOT be applied: the command would
                // then call RaiseAndWaitForCompletion FROM the API thread, waiting for a
                // queue that cannot drain until the current API operation returns, which
                // is the wait itself. That is a guaranteed deadlock, not a fix.
                //
                // What P0-2 actually asks for is that a command touching the Revit API
                // directly, without marshalling, fails LOUDLY and diagnosably instead of
                // taking the host down. That is the catch below, plus
                // RequireRevitApiThread() for command authors who need the assertion.

                // Execute command
                try
                {
                    object result = command.Execute(request.GetParamsObject(), request.Id);
                    _logger.Info("Command {0} executed successfully.", request.Method);

                    return CreateSuccessResponse(request.Id, result);
                }
                catch (CommandExecutionException ex)
                {
                    _logger.Error("Command {0} failed to execute: {1}", request.Method, ex.Message);
                    return CreateErrorResponse(request.Id,
                        ex.ErrorCode,
                        ex.Message,
                        ex.ErrorData);
                }
                catch (Exception ex)
                {
                    // Every command runs on the socket thread, so IsOnMainThread is false
                    // for ALL of them; the thread hint is only right when the exception came
                    // from the Revit API itself. A command's own deliberate error (unknown
                    // category, timeout, bad argument) was being reported as an API
                    // marshalling bug, which sent readers to the wrong place.
                    if (!IsOnMainThread && ex is Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        _logger.Error(
                            "Command {0} threw on a background thread (thread {1}, not the Revit " +
                            "main thread {2}). This typically means the command accesses the Revit " +
                            "API without marshalling through ExternalEvent: {3}",
                            request.Method, Thread.CurrentThread.ManagedThreadId, _mainThreadId, ex.Message);
                        return CreateErrorResponse(request.Id,
                            JsonRPCErrorCodes.InternalError,
                            $"Command '{request.Method}' failed on a background thread. " +
                            "This typically means it accesses the Revit API without marshalling " +
                            "through IWaitableExternalEventHandler / ExternalEvent. " +
                            $"Original error: {ex.Message}");
                    }

                    _logger.Error("An exception occurred while executing command {0}: {1}", request.Method, ex.Message);
                    return CreateErrorResponse(request.Id,
                        JsonRPCErrorCodes.InternalError,
                        ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("An exception has occurred during command execution: {0}", ex.Message);
                return CreateErrorResponse(request.Id,
                    JsonRPCErrorCodes.InternalError,
                    $"Internal error: {ex.Message}");
            }
        }

        private string CreateSuccessResponse(string id, object result)
        {
            var response = new JsonRPCSuccessResponse
            {
                Id = id,
                Result = result is JToken jToken ? jToken : JToken.FromObject(result)
            };

            return response.ToJson();
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
