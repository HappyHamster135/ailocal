using AiLocal.Node.Hosting;

CrashLog.Install();

try
{
    return await NodeWebHost.RunAsync(args);
}
catch (Exception ex)
{
    CrashLog.Write("Main", ex);
    Console.Error.WriteLine($"AiLocal failed to start: {ex.Message}");
    return 1;
}
