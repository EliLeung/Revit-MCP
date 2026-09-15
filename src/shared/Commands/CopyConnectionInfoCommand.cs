using System;
using System.IO;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RvtMcp.Plugin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CopyConnectionInfoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (App.Instance == null) return Result.Failed;

            var transport = App.Instance.Transport;
            var ver = AuthToken.RevitVersion ?? "2022";
            var discoveryFile = Path.Combine(AuthToken.DiscoveryDir(), AuthToken.DiscoveryFileName(ver));

            var info = App.Instance.IsTransportRunning
                ? transport.ConnectionInfo
                : "MCP Server is not running";

            Clipboard.SetText(info);

            var td = new TaskDialog("Connection Info")
            {
                MainInstruction = "Copied to clipboard",
                MainContent = $"{info}\nDiscovery file: {discoveryFile}"
            };
            td.Show();

            return Result.Succeeded;
        }
    }
}
