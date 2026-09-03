using System;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using System.Reflection;
using System.Windows.Media.Imaging;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;

namespace revit_mcp_plugin.Core
{
    public class Application : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            // Nothing in here may be allowed to escape.
            //
            // OnStartup runs while Revit is building the ribbon, on the ribbon thread,
            // in the same AppDomain as every other add-in. An exception thrown from
            // here is not merely "this add-in failed to load" — it aborts ribbon
            // construction partway through, and add-ins that had not yet had their turn
            // can lose their tabs. Issue #31 reports exactly that shape against
            // pyRevit. Contain it, log it, and let Revit carry on.
            try
            {
                RibbonPanel mcpPanel = application.CreateRibbonPanel("Revit MCP Plugin");

                PushButtonData pushButtonData = new PushButtonData("ID_EXCMD_TOGGLE_REVIT_MCP", "Revit MCP\r\n Switch",
                    Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.MCPServiceConnection");
                pushButtonData.ToolTip = "Open / Close mcp server";
                pushButtonData.Image = LoadRibbonImage("icon-16.png");
                pushButtonData.LargeImage = LoadRibbonImage("icon-32.png");
                mcpPanel.AddItem(pushButtonData);

                PushButtonData mcp_settings_pushButtonData = new PushButtonData("ID_EXCMD_MCP_SETTINGS", "Settings",
                    Assembly.GetExecutingAssembly().Location, "revit_mcp_plugin.Core.Settings");
                mcp_settings_pushButtonData.ToolTip = "MCP Settings";
                mcp_settings_pushButtonData.Image = LoadRibbonImage("settings-16.png");
                mcp_settings_pushButtonData.LargeImage = LoadRibbonImage("settings-32.png");
                mcpPanel.AddItem(mcp_settings_pushButtonData);

                // AUTO-START. Until now the socket only came up after a human clicked
                // "Revit MCP Switch", so an MCP client on a freshly started Revit
                // always failed its first call and the AI had to ask the user to press
                // a button. ApplicationInitialized is the earliest point at which a
                // UIApplication exists and Revit is in a valid API context — which
                // ExternalEvent.Create (called for every command as it loads)
                // requires — so the service is started there, not here.
                application.ControlledApplication.ApplicationInitialized += OnApplicationInitialized;
            }
            catch (Exception ex)
            {
                try
                {
                    new Logger().Error("OnStartup failed: {0}\n{1}",
                        ex.Message, ex.StackTrace);
                }
                catch
                {
                    // Logging must never itself be the reason OnStartup throws.
                }
            }

            return Result.Succeeded;
        }

        private static void OnApplicationInitialized(object sender, ApplicationInitializedEventArgs e)
        {
            Logger logger = null;
            try
            {
                logger = new Logger();

                if (!AutoStartEnabled(logger))
                {
                    logger.Info("Auto-start is disabled (settings.autoStart=false or REVIT_MCP_AUTOSTART=0); use the ribbon switch.");
                    return;
                }

                var app = sender as Autodesk.Revit.ApplicationServices.Application;
                if (app == null)
                {
                    logger.Warning("ApplicationInitialized sender is not an Application; auto-start skipped.");
                    return;
                }

                SocketService service = SocketService.Instance;
                if (service.IsRunning) return;

                service.Initialize(new UIApplication(app));
                service.Start();

                if (service.IsRunning)
                    logger.Info("Socket service auto-started on port {0} at Revit start-up.", service.Port);
                else
                    logger.Error("Socket service auto-start failed; use the ribbon switch after checking the log above.");
            }
            catch (Exception ex)
            {
                try { (logger ?? new Logger()).Error("Auto-start failed: {0}\n{1}", ex.Message, ex.StackTrace); }
                catch { }
            }
        }

        /// <summary>
        /// REVIT_MCP_AUTOSTART=0 wins; otherwise the "settings.autoStart" value in
        /// commandRegistry.json, which defaults to true when absent.
        /// </summary>
        private static bool AutoStartEnabled(Logger logger)
        {
            string env = Environment.GetEnvironmentVariable("REVIT_MCP_AUTOSTART");
            if (string.Equals(env, "0", StringComparison.Ordinal) ||
                string.Equals(env, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var configManager = new ConfigurationManager(logger);
                configManager.LoadConfiguration();
                return configManager.Config?.Settings?.AutoStart ?? true;
            }
            catch (Exception ex)
            {
                logger.Warning("Could not read autoStart from the configuration ({0}); defaulting to on.", ex.Message);
                return true;
            }
        }

        /// <summary>
        /// Load a ribbon icon, returning null rather than throwing when it cannot be found.
        /// </summary>
        /// <remarks>
        /// These icons were addressed with the RELATIVE pack path
        /// "/RevitMCPPlugin;component/...". A relative pack URI resolves against the
        /// ambient WPF application context, which inside Revit belongs to whichever
        /// component happened to create it — so the identical string can resolve for one
        /// add-in and throw for another purely on load order. The absolute
        /// "pack://application:,,,/" form does not depend on that context.
        ///
        /// A missing glyph is also not a reason to fail startup: the button still works.
        /// </remarks>
        private static BitmapImage LoadRibbonImage(string fileName)
        {
            try
            {
                // Touching PackUriHelper registers the "pack" URI scheme. Without it the
                // first absolute pack URI in a process throws UriFormatException.
                if (!UriParser.IsKnownScheme("pack"))
                {
                    _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
                }

                return new BitmapImage(new Uri(
                    "pack://application:,,,/RevitMCPPlugin;component/Core/Ressources/" + fileName,
                    UriKind.Absolute));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                application.ControlledApplication.ApplicationInitialized -= OnApplicationInitialized;
            }
            catch (Exception)
            {
                // Unsubscribing is best effort.
            }

            try
            {
                if (SocketService.Instance.IsRunning)
                {
                    SocketService.Instance.Stop();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("MCP plugin OnShutdown error: " + ex.Message);
            }

            return Result.Succeeded;
        }
    }
}
