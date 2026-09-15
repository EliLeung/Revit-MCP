// Modified for Revit-MCP, 2026-09-15: multi-client connections, connection management, and/or product branding.
// Based on bimwright/rvt-mcp; original licensing and attribution retained.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RvtMcp.Plugin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ManageConnectionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
#if REVIT2026
            App.Instance?.ShowConnectionManager(data.Application.MainWindowHandle);
#endif
            return Result.Succeeded;
        }
    }
}
