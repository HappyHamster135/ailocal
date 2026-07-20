using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks v1.35.0: follow-up prompts on a non-empty workspace get the
/// existing project's context (continue, never restart), and the benchmark
/// suite's scoring/history plumbing is deterministic.</summary>
public class ContinuityAndBenchmarkTests : IDisposable
{
    private readonly string _dir;

    public ContinuityAndBenchmarkTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-cont-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- Projektkontinuitet ------------------------------------------------

    [Fact]
    public void ProjectContext_FollowUpOnExistingGame_GetsBriefWithDesign()
    {
        File.WriteAllText(Path.Combine(_dir, "project.godot"), "[application]");
        File.WriteAllText(Path.Combine(_dir, "DESIGN.md"), "# Rymdspelet\nEn shooter med vågor.");
        File.WriteAllText(Path.Combine(_dir, "Main.gd"), "extends Node2D");

        var brief = ProjectContext.Build(_dir, "gör spelet svårare");
        Assert.NotNull(brief);
        Assert.Contains("BEFINTLIGT PROJEKT", brief);
        Assert.Contains("godot", brief);
        Assert.Contains("Rymdspelet", brief);
        Assert.Contains("Main.gd", brief);
        Assert.Contains("Skapa INTE ett nytt projekt", brief);
    }

    [Fact]
    public void ProjectContext_EmptyWorkspace_ReturnsNull()
    {
        Assert.Null(ProjectContext.Build(_dir, "gör spelet svårare"));
    }

    [Fact]
    public void ProjectContext_UnrelatedQuestion_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), "<!DOCTYPE html><html></html>");
        // Varken byggverb eller bakreferens - projektkontext vore bara brus.
        Assert.Null(ProjectContext.Build(_dir, "vad är klockan?"));
    }

    // ---- Benchmark ---------------------------------------------------------

    [Fact]
    public void BenchmarkScore_RangesAreSane()
    {
        var clean = new QualityFindings(true, false, "ok", _dir, "html5");
        var soft = new QualityFindings(false, false, "Playtest: ljud saknas", _dir, "html5");
        var hard = new QualityFindings(false, true, "VERIFY FAILED", _dir, "html5");

        Assert.Equal(100, BenchmarkService.Score(success: true, clean, files: 5));
        Assert.Equal(85, BenchmarkService.Score(success: true, soft, files: 5));   // -15 för anmärkningar
        Assert.Equal(45, BenchmarkService.Score(success: true, hard, files: 5));   // -40 hårt fel, -15 ej ren
        Assert.Equal(0, BenchmarkService.Score(success: false, hard, files: 0));
    }

    [Fact]
    public void BenchmarkPrompts_AreFixedAndCoverBothPaths()
    {
        Assert.Equal(5, BenchmarkService.StandardPrompts.Length);
        Assert.Contains(BenchmarkService.StandardPrompts, p => p.Contains("webbspel"));
        Assert.Contains(BenchmarkService.StandardPrompts, p => p.Contains("python"));
    }
}
