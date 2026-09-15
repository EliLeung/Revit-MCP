using System;
using System.IO;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    [Collection("Sequential")]
    public class SendCodeJournalGateTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _tempConfigPath;

        public SendCodeJournalGateTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"journal-gate-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            SendCodeJournal.LocalAppDataOverride = _tempDir;
            McpLogger.LocalAppDataOverride = _tempDir;
            McpLogger.Initialize();

            _tempConfigPath = Path.Combine(_tempDir, "rvtmcp.config.json");
            
            // Set up active configuration on disk
            RvtMcpConfig.SavePersistSendCodeBodies(true, DateTimeOffset.UtcNow.AddHours(2), _tempConfigPath);
        }

        public void Dispose()
        {
            SendCodeJournal.LocalAppDataOverride = null;
            McpLogger.LocalAppDataOverride = null;
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { }
            }
        }

        [Fact]
        public void OnSendCodeLogged_WhenNonSendCodeTool_DoesNothing()
        {
            SendCodeJournalGate.OnSendCodeLogged(
                "get_current_view_info",
                "{}",
                null,
                true,
                10,
                null,
                "{}");

            Assert.False(File.Exists(SendCodeJournal.JournalPath));
        }

        [Fact]
        public void OnSendCodeLogged_WhenSendCodeTool_AppendsToJournal()
        {
            // We need RvtMcpConfig to load from our temporary config file
            // Let's modify the DefaultConfigFilePath for RvtMcpConfig or pass a custom config path.
            // Wait, in SendCodeJournalGate.cs, it calls RvtMcpConfig.Load(args: null) which uses the DefaultConfigFilePath.
            // Can we temporarily set the DefaultConfigFilePath? No, it's a read-only property:
            // public static string DefaultConfigFilePath => Path.Combine(...)
            // However! RvtMcpConfig.Load uses Environment.SpecialFolder.LocalApplicationData.
            // Wait! Can we mock Environment.SpecialFolder.LocalApplicationData or does it look it up?
            // Actually, RvtMcpConfig.Load uses configFilePath if provided, but SendCodeJournalGate calls it with args: null and configFilePath: null.
            // Wait, in RvtMcpConfig.cs:
            // public static string DefaultConfigFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RvtMcp", "rvtmcp.config.json")
            // So if we write to the ACTUAL LocalApplicationData path, or if we mock/backup it?
            // Let's check: can we just write our active configuration to the ACTUAL LocalApplicationData path, run the test, and then restore or delete it?
            // Yes, that's completely fine, but let's be safe. Let's write to RvtMcpConfig.DefaultConfigFilePath, run the test, and clean it up.
            // Let's check if there is an existing file there.
            var actualConfigPath = RvtMcpConfig.DefaultConfigFilePath;
            string backupContent = null;
            if (File.Exists(actualConfigPath))
            {
                backupContent = File.ReadAllText(actualConfigPath);
            }

            try
            {
                // Write active config to actual path
                RvtMcpConfig.SavePersistSendCodeBodies(true, DateTimeOffset.UtcNow.AddHours(2), actualConfigPath);

                SendCodeJournalGate.OnSendCodeLogged(
                    "send_code_to_revit",
                    "{}",
                    "var x = 1;",
                    true,
                    15,
                    null,
                    "{}");

                Assert.True(File.Exists(SendCodeJournal.JournalPath));
                var lines = File.ReadAllLines(SendCodeJournal.JournalPath);
                Assert.Single(lines);
                Assert.Contains("var x = 1;", lines[0]);
            }
            finally
            {
                // Clean up/restore
                if (backupContent != null)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(actualConfigPath);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(actualConfigPath, backupContent);
                    }
                    catch { }
                }
                else
                {
                    try { File.Delete(actualConfigPath); } catch { }
                }
            }
        }
    }
}
