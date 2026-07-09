using System.Net;
using System.Net.Sockets;
using AiLocal.Node.Hosting;

namespace AiLocal.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        CrashLog.Install();
        Application.ThreadException += (_, e) => CrashLog.Write("UI thread", e.Exception);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        try
        {
            RunMain(args);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Main", ex);
            MessageBox.Show(ex.Message, "AiLocal failed to start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void RunMain(string[] args)
    {
        if (IsServerMode(args))
        {
            NodeWebHost.RunAsync(args).GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();

        var port = GetFreeTcpPort();
        var serverArgs = new[]
        {
            "--role", "Launcher",
            "--port", port.ToString(),
            "--no-browser",
            "--name", Environment.MachineName
        };

        var node = NodeWebHost.StartAsync(serverArgs).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("AiLocal launcher failed to start.");

        try
        {
            using var form = new MainForm(node);
            Application.Run(form);
        }
        finally
        {
            node.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static bool IsServerMode(string[] args)
    {
        return args.Any(a =>
            string.Equals(a, "--role", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase));
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
