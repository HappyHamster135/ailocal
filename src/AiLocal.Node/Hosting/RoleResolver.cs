using AiLocal.Core.Configuration;
using AiLocal.Core.Roles;

namespace AiLocal.Node.Hosting;

/// <summary>Resolves the node role and CLI overrides at startup.</summary>
public static class RoleResolver
{
    public static bool WantsHelp(string[] args) =>
        args.Any(a => a is "--help" or "-h" or "/?");

    public static void PrintHelp()
    {
        Console.WriteLine("""
        AiLocal

        Usage:
          ailocal --role Host
          ailocal --role Worker --host http://192.168.1.10:5080
          ailocal --role Overseer

        Options:
          --role <Host|Worker|Overseer>  Node role.
          --port <number>                HTTP port. Defaults: Host 5080, Worker 5081, Overseer 5082.
          --host <url>                   Host endpoint for Worker/Overseer when LAN discovery is disabled.
          --name <name>                  Friendly node name.
          --parent-pid <id>              Stop this node when the parent process exits.
          --no-browser                   Do not open the web UI automatically.
          --cluster-token <token>        Pairing token to adopt on first run only.
          --help                         Show this help.

        Environment:
          AILOCAL_ROLE
          ANTHROPIC_API_KEY
          GEMINI_API_KEY
          AILOCAL_CLUSTER_TOKEN
        """);
    }

    public static NodeRole Resolve(string[] args, NodeRole fallback)
    {
        var explicitRole = GetOption(args, "--role")
            ?? FirstBareRole(args)
            ?? Environment.GetEnvironmentVariable("AILOCAL_ROLE");

        if (explicitRole is not null && Enum.TryParse<NodeRole>(explicitRole, ignoreCase: true, out var role))
            return role;

        return fallback;
    }

    public static void ApplyOverrides(string[] args, NodeSettings settings)
    {
        if (GetOption(args, "--port") is { } p && int.TryParse(p, out var port)) settings.Port = port;
        if (GetOption(args, "--host") is { } h) settings.HostEndpoint = h;
        if (GetOption(args, "--name") is { } n) settings.NodeName = n;
        if (GetOption(args, "--parent-pid") is { } pp && int.TryParse(pp, out var parentProcessId))
            settings.ParentProcessId = parentProcessId;
        if (args.Any(a => string.Equals(a, "--no-browser", StringComparison.OrdinalIgnoreCase)))
            settings.Ui.OpenBrowser = false;
        if (GetOption(args, "--cluster-token") is { Length: > 0 } token)
            settings.SeedClusterToken = token;
    }

    private static string? FirstBareRole(string[] args)
    {
        foreach (var a in args)
        {
            if (a.StartsWith('-')) continue;
            if (Enum.TryParse<NodeRole>(a, ignoreCase: true, out _)) return a;
        }
        return null;
    }

    private static string? GetOption(string[] args, string key)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == key && i + 1 < args.Length) return args[i + 1];
            if (args[i].StartsWith(key + "=", StringComparison.Ordinal)) return args[i][(key.Length + 1)..];
        }
        return null;
    }
}
