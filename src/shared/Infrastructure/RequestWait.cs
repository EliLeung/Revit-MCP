using System;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// Completes the per-request TCS when the 60s wait expires so the UI-thread
    /// handler can skip a stale command instead of writing a late success.
    /// </summary>
    public static class RequestWait
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        public static string WaitOrTimeout(TaskCompletionSource<string> tcs, string id, TimeSpan? timeout = null)
        {
            if (tcs == null) throw new ArgumentNullException(nameof(tcs));

            var wait = timeout ?? DefaultTimeout;
            if (tcs.Task.Wait(wait))
                return tcs.Task.Result;

            var response = JsonConvert.SerializeObject(new
            {
                id,
                success = false,
                error = "Request timed out (60s). Revit may still be running this command. " +
                        "Do not retry clash, export, or other long tools until the current Revit operation finishes."
            });
            tcs.TrySetResult(response);
            return response;
        }
    }
}
