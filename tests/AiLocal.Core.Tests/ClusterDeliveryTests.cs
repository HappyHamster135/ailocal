using AiLocal.Core.Hardware;
using AiLocal.Core.Nodes;
using AiLocal.Core.Roles;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v1.40.0: klusterbred leverans (Spela/Ladda ner via Hostens proxy) och
/// flottuppdateringens målval. Rapporterna bakom: "filerna hamnade på den
/// andra worker-datorn och jag fick ingen spelbar fil".
/// </summary>
public class ClusterDeliveryTests
{
    // ---- Final-frame-omskrivningen -----------------------------------------

    [Fact]
    public void RewriteFinalFrame_SkriverOmBadaVagarnaTillHostensProxy()
    {
        var frame = """{"final":{"Success":true,"FinalAnswer":"Klar"},"previewPath":"/api/preview/spel/index.html","artifactPath":"/api/artifact?path=spel%2Fbuild%2Fspel.exe"}""";

        var (json, preview, artifact) = ClusterDelivery.RewriteFinalFrame(frame, "w1");

        Assert.Equal("/api/nodes/w1/preview/spel/index.html", preview);
        Assert.Equal("/api/nodes/w1/artifact?path=spel%2Fbuild%2Fspel.exe", artifact);
        Assert.NotNull(json);
        // final-objektet ska passera orört (PascalCase-formen är kontraktet
        // mot dashboardens stegrendering).
        Assert.Contains("\"Success\":true", json);
        Assert.Contains("\"FinalAnswer\":\"Klar\"", json);
        Assert.Contains("/api/nodes/w1/preview/spel/index.html", json);
    }

    [Fact]
    public void RewriteFinalFrame_StegframesNullvagarOchSkrap_RorsAldrig()
    {
        Assert.Null(ClusterDelivery.RewriteFinalFrame(
            """{"step":{"Kind":"thinking","Detail":"x"}}""", "w1").Json);
        Assert.Null(ClusterDelivery.RewriteFinalFrame(
            """{"final":{"Success":false},"previewPath":null,"artifactPath":null}""", "w1").Json);
        Assert.Null(ClusterDelivery.RewriteFinalFrame("inte json alls", "w1").Json);
    }

    [Fact]
    public void RewriteFinalFrame_EscaparWorkerIdIVagen()
    {
        var frame = """{"final":{"Success":true},"previewPath":"/api/preview/index.html"}""";
        var (_, preview, _) = ClusterDelivery.RewriteFinalFrame(frame, "a b/c");
        Assert.Equal("/api/nodes/a%20b%2Fc/preview/index.html", preview);
    }

    [Fact]
    public void RewriteFinalFrame_ReplayPath_SkrivsOmAvenUtanPreview()
    {
        // Motorspel har ingen index.html (previewPath=null) men kan ha en
        // repris (B3). Den måste ändå gå via Host-proxyn - annars pekar
        // reprisen på fel nod i klustret. Reprisen ensam ska räknas som
        // "behövdes rewrite" (annars skippas hela framen).
        var frame = """{"final":{"Success":true},"previewPath":null,"artifactPath":null,"replayPath":"/api/preview/spel/screenshots/replay.png"}""";

        var (json, preview, artifact) = ClusterDelivery.RewriteFinalFrame(frame, "w1");

        Assert.NotNull(json);
        Assert.Null(preview);
        Assert.Null(artifact);
        Assert.Contains("/api/nodes/w1/preview/spel/screenshots/replay.png", json);
        Assert.DoesNotContain("\"replayPath\":\"/api/preview/", json);
    }

    // ---- Artefaktupplösningen ----------------------------------------------

    [Fact]
    public void ResolveArtifactFile_VaktarTraversalOchFiltyp()
    {
        var root = Path.Combine(Path.GetTempPath(), "ailocal-artifact-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(root, "spel", "build"));
        var exe = Path.Combine(root, "spel", "build", "spel.exe");
        File.WriteAllText(exe, "MZ");
        File.WriteAllText(Path.Combine(root, "spel", "hemlig.txt"), "aldrig serverad");
        try
        {
            Assert.Equal(exe, ClusterDelivery.ResolveArtifactFile(root, "spel/build/spel.exe"));
            // Fel filtyp: artefakt-endpointen får aldrig bli en generisk filläsare.
            Assert.Null(ClusterDelivery.ResolveArtifactFile(root, "spel/hemlig.txt"));
            // Traversal ut ur arbetsytan.
            Assert.Null(ClusterDelivery.ResolveArtifactFile(root, "..\\utanfor.exe"));
            Assert.Null(ClusterDelivery.ResolveArtifactFile(root, "spel/../../utanfor.exe"));
            // Saknad fil och tom väg.
            Assert.Null(ClusterDelivery.ResolveArtifactFile(root, "spel/build/finns-inte.exe"));
            Assert.Null(ClusterDelivery.ResolveArtifactFile(root, null));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* städning */ }
        }
    }

    // ---- Flottuppdateringens målval ----------------------------------------

    private static NodeInfo Node(string id, NodeRole role, NodeStatus status,
        int activeTasks = 0, int selfReported = 0) => new()
    {
        Id = id,
        Name = id,
        Endpoint = "http://10.0.0.1:5081",
        Role = role,
        Status = status,
        ActiveTasks = activeTasks,
        SelfReportedActive = selfReported
    };

    [Fact]
    public void UpdateTargets_ValjerLedigaWorkers_SkipparUpptagnaOchOffline()
    {
        var nodes = new[]
        {
            Node("ledig", NodeRole.Worker, NodeStatus.Idle),
            Node("bygger", NodeRole.Worker, NodeStatus.Busy, activeTasks: 1),
            Node("bygger-lokalt", NodeRole.Worker, NodeStatus.Busy, selfReported: 1),
            Node("borta", NodeRole.Worker, NodeStatus.Offline),
            Node("hosten", NodeRole.Host, NodeStatus.Idle),
            Node("cloud:openrouter", NodeRole.Worker, NodeStatus.Idle),
        };

        var (targets, busy, offline) = ClusterDelivery.UpdateTargets(nodes, force: false);

        Assert.Equal(["ledig"], targets.Select(n => n.Id).ToArray());
        Assert.Equal(new[] { "bygger", "bygger-lokalt" }, busy.Select(n => n.Id).Order().ToArray());
        Assert.Equal(["borta"], offline.Select(n => n.Id).ToArray());
    }

    [Fact]
    public void UpdateTargets_ForceTarMedUpptagna()
    {
        var nodes = new[] { Node("bygger", NodeRole.Worker, NodeStatus.Busy, activeTasks: 1) };
        var (targets, busy, _) = ClusterDelivery.UpdateTargets(nodes, force: true);
        Assert.Single(targets);
        Assert.Empty(busy);
    }

    // ---- Artefakten följer med uppdragshistoriken --------------------------

    [Fact]
    public void AssignmentLog_ArtifactPathOverleverIHistoriken()
    {
        var path = Path.Combine(Path.GetTempPath(), "ailocal-log-" + Guid.NewGuid().ToString("n"), "log.json");
        var log = new AssignmentLog(path);
        var entry = log.Begin("bygg ett fotbollsspel", "Worker-1");
        log.Complete(entry, success: true, "Klar", "/api/preview/spel/index.html", "/api/artifact?path=spel.exe");

        var snapshot = log.Snapshot();
        Assert.Equal("/api/artifact?path=spel.exe", snapshot[0].ArtifactPath);
        Assert.Equal("/api/preview/spel/index.html", snapshot[0].PreviewPath);

        try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { /* städning */ }
    }
}
