using System;
using Autodesk.Revit.UI;
using System.Reflection;
using System.Windows.Media.Imaging;
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
                if (SocketService.Instance.IsRunning)
                {
                    SocketService.Instance.Stop();
                }
            }
            catch { }

            return Result.Succeeded;
        }
    }
}
