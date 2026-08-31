import { RevitClientConnection } from "./SocketClient.js";

// Node 17+ hands back DNS results in the resolver's preferred order rather than
// re-sorting them, which on Windows puts ::1 ahead of 127.0.0.1 for "localhost".
// The Revit plugin used to listen on IPv4 only, so "localhost" resolved to an
// address with nothing behind it and every tool call failed with ECONNREFUSED
// (issue #29). Dialling a literal IPv4 address takes the resolver out of the
// path entirely; the plugin now also listens on ::1, so either would work, but
// this side stays explicit so an older plugin build keeps working too.
//
// REVIT_MCP_HOST / REVIT_MCP_PORT override it when the plugin is not local.
const REVIT_HOST = process.env.REVIT_MCP_HOST || "127.0.0.1";
const REVIT_PORT = Number(process.env.REVIT_MCP_PORT) || 8080;

// Mutex to serialize all Revit connections - prevents race conditions
// when multiple requests are made in parallel
let connectionMutex: Promise<void> = Promise.resolve();

/**
 * Connect to the Revit client and run an operation.
 * @param operation Callback to execute once the connection is established.
 * @returns The result of the operation.
 */
export async function withRevitConnection<T>(
  operation: (client: RevitClientConnection) => Promise<T>
): Promise<T> {
  // Wait for any pending connection to complete before starting a new one
  const previousMutex = connectionMutex;
  let releaseMutex: () => void;
  connectionMutex = new Promise<void>((resolve) => {
    releaseMutex = resolve;
  });
  await previousMutex;

  const revitClient = new RevitClientConnection(REVIT_HOST, REVIT_PORT);

  try {
    // Connect to the Revit client
    if (!revitClient.isConnected) {
      await new Promise<void>((resolve, reject) => {
        let timer: ReturnType<typeof setTimeout> | undefined;

        const cleanup = () => {
          if (timer !== undefined) clearTimeout(timer);
          revitClient.socket.removeListener("connect", onConnect);
          revitClient.socket.removeListener("error", onError);
        };

        const onConnect = () => {
          cleanup();
          resolve();
        };

        const onError = (error: any) => {
          cleanup();
          // The old message named neither the address nor the reason, which is
          // most of why this failure generated so many support threads.
          reject(
            new Error(
              `Could not reach the Revit plugin at ${REVIT_HOST}:${REVIT_PORT} ` +
                `(${error?.code ?? "unknown error"}). Check that Revit is running, ` +
                `that the mcp-servers-for-revit add-in loaded, and that the ` +
                `"Revit MCP Switch" button on the Add-Ins ribbon is switched on.`
            )
          );
        };

        revitClient.socket.on("connect", onConnect);
        revitClient.socket.on("error", onError);

        revitClient.connect();

        timer = setTimeout(() => {
          cleanup();
          reject(
            new Error(
              `Timed out after 5s connecting to the Revit plugin at ${REVIT_HOST}:${REVIT_PORT}.`
            )
          );
        }, 5000);
      });
    }

    // Run the operation
    return await operation(revitClient);
  } finally {
    // Disconnect
    revitClient.disconnect();
    // Release the mutex so the next request can proceed
    releaseMutex!();
  }
}
