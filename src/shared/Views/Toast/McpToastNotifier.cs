using System;

namespace RvtMcp.Plugin.Views.Toast
{
    public sealed class McpToastNotifier
    {
        private readonly McpToastHost _host;
        private readonly Func<bool> _isEnabled;

        public McpToastNotifier(McpToastHost host, Func<bool> isEnabled)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        }

        public void SetOwnerHandle(IntPtr hwnd) => _host.SetOwnerHandle(hwnd);

        public void SetHostDispatcher(System.Windows.Threading.Dispatcher dispatcher) =>
            _host.SetHostDispatcher(dispatcher);

        public void OnCompleted(
            string toolName,
            string paramsJson,
            string resultJson,
            bool success,
            string errorMessage,
            long durationMs,
            string toolDescription)
        {
            if (!_isEnabled())
                return;

            var vm = ToastContentBuilder.BuildCompleted(
                toolName,
                paramsJson,
                resultJson,
                success,
                errorMessage,
                durationMs,
                toolDescription);

            _host.Post(manager => manager.Complete(vm));
        }

        public void DismissAll()
        {
            _host.DismissAll(synchronous: false);
        }

        public void Shutdown()
        {
            _host.Shutdown();
        }
    }
}
