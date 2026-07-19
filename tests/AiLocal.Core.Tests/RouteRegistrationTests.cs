using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// Guards against the class of bug that produced an AmbiguousMatchException on
/// GET/PUT /api/providers for a Launcher node (v1.19.15 and earlier): a route
/// registered BOTH in NodeWebHost.MapSharedEndpoints (which runs for every role
/// except where explicitly guarded) AND again in a per-role MapEndpoints file.
/// ASP.NET only throws at request time, so a plain build/test wouldn't catch it;
/// this codifies the invariant at the source level so it can't silently return.
/// </summary>
public sealed class RouteRegistrationTests
{
    private static readonly Regex MapCall =
        new(@"app\.Map(Get|Post|Put|Delete|Patch)\(\s*""([^""]+)""", RegexOptions.Compiled);

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        // tests/AiLocal.Core.Tests/RouteRegistrationTests.cs -> up 3 to repo root
        var dir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(dir, "..", ".."));
    }

    private static HashSet<(string Verb, string Path)> RoutesIn(string file, int start = 0, int end = int.MaxValue)
    {
        var set = new HashSet<(string, string)>();
        var lines = File.ReadAllLines(file);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i + 1 < start || i + 1 > end) continue;
            var m = MapCall.Match(lines[i]);
            if (m.Success) set.Add((m.Groups[1].Value, m.Groups[2].Value));
        }
        return set;
    }

    private static (int Start, int End) MethodRange(string file, string signatureFragment)
    {
        var lines = File.ReadAllLines(file);
        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            // Match the METHOD DEFINITION, not a call site: the line must both
            // name the method and be a static declaration. (The shared block is
            // invoked by name earlier in the file, which would otherwise anchor
            // us to the call site and yield an empty range.)
            if (lines[i].Contains(signatureFragment) &&
                (lines[i].Contains("static void") || lines[i].Contains("static WebApplication") ||
                 lines[i].Contains("static Task") || lines[i].Contains("static async")))
            { start = i + 1; break; }
        }
        if (start < 0) return (0, int.MaxValue);
        // End at the next top-level "static" method declaration after start.
        for (var i = start; i < lines.Length; i++)
        {
            if ((lines[i].Contains("private static") || lines[i].Contains("public static") ||
                 lines[i].Contains("internal static")) && (i + 1) > start)
                return (start, i); // exclusive of next method's declaration line
        }
        return (start, lines.Length);
    }

    [Fact]
    public void NoRouteIsMappedBothInSharedAndInARoleFile()
    {
        var root = RepoRoot();
        var nodeRoot = Path.Combine(root, "src", "AiLocal.Node");
        var nwh = Path.Combine(nodeRoot, "Hosting", "NodeWebHost.cs");
        Assert.True(File.Exists(nwh), $"NodeWebHost.cs not found at {nwh}");

        var (sStart, sEnd) = MethodRange(nwh, "MapSharedEndpoints");
        var shared = RoutesIn(nwh, sStart, sEnd);
        Assert.NotEmpty(shared); // sanity: we actually located the shared block

        // Overseer is explicitly guarded OUT of some shared routes, so a
        // matching registration in OverseerRole is legitimate. Every other
        // role receives the full shared set, so any overlap is a real dup.
        var roles = new[]
        {
            ("HostRole.cs", false),
            ("WorkerRole.cs", false),
            ("LauncherRole.cs", false),
            ("OverseerRole.cs", true),
        };

        var problems = new List<string>();
        foreach (var (roleFile, isOverseer) in roles)
        {
            var path = Path.Combine(nodeRoot, "Roles", roleFile);
            if (!File.Exists(path)) continue;
            var roleRoutes = RoutesIn(path);
            foreach (var route in shared)
            {
                if (!roleRoutes.Contains(route)) continue;
                // Routes the shared block guards away from Overseer are allowed
                // to be re-mapped by OverseerRole.
                if (isOverseer && route.Path is "/api/providers") continue;
                problems.Add($"{roleFile} re-maps shared route {route.Verb} {route.Path}");
            }
        }

        Assert.True(problems.Count == 0,
            "Duplicate route registrations (would throw AmbiguousMatchException at runtime):\n" +
            string.Join("\n", problems));
    }

    /// <summary>Guards the ask_user endpoint fix: pending-info/answer-info were
    /// once registered INSIDE the /run handler's success branch (a merge
    /// accident), which 404'd every answer until some earlier run had succeeded
    /// - and then double-registered the route on the next success. The /run
    /// registration is the last Map* call in SessionApi.MapEndpoints and spans
    /// to the end of the method, so "registered before /run in the source" is
    /// exactly "registered at startup".</summary>
    [Fact]
    public void SessionInfoEndpoints_AreRegisteredAtStartup_NotInsideTheRunHandler()
    {
        var sessionApi = Path.Combine(RepoRoot(), "src", "AiLocal.Node", "Roles", "SessionApi.cs");
        Assert.True(File.Exists(sessionApi), $"SessionApi.cs not found at {sessionApi}");
        var lines = File.ReadAllLines(sessionApi);

        int LineOf(string fragment) => Array.FindIndex(lines, l => l.Contains(fragment)) + 1;
        var pendingInfo = LineOf("\"/api/sessions/{id}/pending-info\"");
        var answerInfo = LineOf("\"/api/sessions/{id}/answer-info\"");
        var run = LineOf("\"/api/sessions/{id}/run\"");

        Assert.True(pendingInfo > 0, "pending-info endpoint is not registered at all");
        Assert.True(answerInfo > 0, "answer-info endpoint is not registered at all");
        Assert.True(run > 0, "run endpoint not found (test anchor broke - update this test)");
        Assert.True(pendingInfo < run,
            "pending-info is registered after /run begins - it has been re-nested inside the run handler");
        Assert.True(answerInfo < run,
            "answer-info is registered after /run begins - it has been re-nested inside the run handler");
    }
}
