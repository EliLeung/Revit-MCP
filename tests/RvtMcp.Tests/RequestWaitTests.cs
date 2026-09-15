using System;
using System.Threading.Tasks;
using RvtMcp.Plugin;
using Xunit;

namespace RvtMcp.Tests
{
    public class RequestWaitTests
    {
        [Fact]
        public async Task WaitOrTimeout_completes_tcs_on_timeout()
        {
            var tcs = new TaskCompletionSource<string>();
            var json = RequestWait.WaitOrTimeout(tcs, "req-1", TimeSpan.FromMilliseconds(40));

            Assert.True(tcs.Task.IsCompleted);
            var completed = await tcs.Task;
            Assert.Equal(json, completed);
            Assert.Contains("req-1", json);
            Assert.Contains("timed out", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("do not retry", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void WaitOrTimeout_returns_result_when_completed_in_time()
        {
            var tcs = new TaskCompletionSource<string>();
            tcs.TrySetResult("{\"id\":\"req-2\",\"success\":true}");
            var json = RequestWait.WaitOrTimeout(tcs, "req-2", TimeSpan.FromSeconds(1));
            Assert.Equal("{\"id\":\"req-2\",\"success\":true}", json);
        }
    }
}
