using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Node.Tests;

/// <summary>C4 (producent + riktiga överlämningar): en SEKVENTIELL rollpipeline
/// (programmerare -> konstnär -> ljuddesigner) på samma arbetsyta. Låser
/// rollordningen, att konstnären kör den starka modellen (multi-modell), att
/// varje roll får förra rollens sammanfattning (överlämning), och - efter
/// granskningen v1.83 - att LEVERANSEN styrs av kärnbygget (inte sista rollen)
/// och att kärnbyggets kapsignal överlever.</summary>
public class ProducerPipelineTests
{
    private static AgentRunResult Res(bool success, string answer = "klar",
        int inTok = 10, int outTok = 20, bool cap = false, bool costCap = false) =>
        new(success, answer, [], 1, [], new TokenUsage(inTok, outTok), cap, costCap);

    [Fact]
    public void Roles_ArIOverlamningsordning_KonstnarKorStarkModell()
    {
        var roles = ProducerPipeline.Roles;
        Assert.Equal(3, roles.Count);
        Assert.Equal("Programmerare", roles[0].Title);
        Assert.Equal("Konstnär", roles[1].Title);
        Assert.Equal("Ljuddesigner", roles[2].Title);
        Assert.False(roles[0].UseStrongModel);
        Assert.True(roles[1].UseStrongModel);   // konstnären får den starka tiern
        Assert.False(roles[2].UseStrongModel);
    }

    [Fact]
    public void Rollprompter_HarRattFokus_OchBararOverlamning()
    {
        var code = ProducerPipeline.CodePrompt("bygg ett spel", null);
        Assert.Contains("PROGRAMMERARE", code);
        Assert.Contains("KÄRNSPELET", code);

        var art = ProducerPipeline.ArtPrompt("bygg ett spel", "programmeraren byggde X");
        Assert.Contains("KONSTNÄR", art);
        Assert.Contains("art-bibel", art);
        Assert.Contains("programmeraren byggde X", art);      // överlämning från förra rollen

        var audio = ProducerPipeline.AudioPrompt("bygg ett spel", "konstnären fixade grafiken");
        Assert.Contains("LJUDDESIGNER", audio);
        Assert.Contains("ljudeffekter", audio, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("konstnären fixade grafiken", audio); // överlämning
    }

    [Fact]
    public async Task RunAsync_KorAllaTreRollerISekvens_KonstnarPaStarkModell()
    {
        var calls = new List<(string Prompt, string? Model)>();
        var steps = new List<AgentStep>();

        var result = await ProducerPipeline.RunAsync(
            "bygg ett spel", "cheap", "strong",
            runRole: (prompt, model) => { calls.Add((prompt, model)); return Task.FromResult(Res(true)); },
            step => { steps.Add(step); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(3, calls.Count);
        Assert.Contains("PROGRAMMERARE", calls[0].Prompt);
        Assert.Contains("KONSTNÄR", calls[1].Prompt);
        Assert.Contains("LJUDDESIGNER", calls[2].Prompt);
        Assert.Equal("cheap", calls[0].Model);
        Assert.Equal("strong", calls[1].Model);   // konstnären -> stark tier (multi-modell)
        Assert.Equal("cheap", calls[2].Model);
        Assert.Contains(steps, s => s.Detail.Contains("Programmerare"));
        Assert.Contains(steps, s => s.Detail.Contains("Ljuddesigner"));
    }

    [Fact]
    public async Task RunAsync_OverlamnarForraRollensSammanfattning()
    {
        var artBrief = "";
        await ProducerPipeline.RunAsync(
            "bygg ett spel", "cheap", "strong",
            runRole: (prompt, _) =>
            {
                if (prompt.Contains("KONSTNÄR")) artBrief = prompt;
                return Task.FromResult(Res(true, prompt.Contains("PROGRAMMERARE") ? "koden byggd av programmeraren" : "klar"));
            },
            _ => Task.CompletedTask, CancellationToken.None);

        Assert.Contains("koden byggd av programmeraren", artBrief);   // programmerarens svar bärs in i konstnärens brief
    }

    [Fact]
    public async Task RunAsync_LeveransStyrsAvKarnbygget_InteSistaRollen()
    {
        // Kärnbygget (programmeraren) lyckas men LJUDET misslyckas -> ett
        // spelbart spel finns ändå på disken -> Success SKA vara true.
        var okCoreBadAudio = await ProducerPipeline.RunAsync(
            "spel", "cheap", "strong",
            (prompt, _) => Task.FromResult(Res(!prompt.Contains("LJUDDESIGNER"))),
            _ => Task.CompletedTask, CancellationToken.None);
        Assert.True(okCoreBadAudio.Success);

        // Kärnbygget misslyckas men ljudet lyckas -> inget spelbart kärnspel ->
        // Success SKA vara false (annars seglar ett trasigt bygge in i grinden).
        var badCoreOkAudio = await ProducerPipeline.RunAsync(
            "spel", "cheap", "strong",
            (prompt, _) => Task.FromResult(Res(!prompt.Contains("PROGRAMMERARE"))),
            _ => Task.CompletedTask, CancellationToken.None);
        Assert.False(badCoreOkAudio.Success);
    }

    [Fact]
    public async Task RunAsync_BevararKarnbyggetsKapsignal_AvenOmSenareRollSlutarRent()
    {
        // Programmeraren slår i iterationstaket; senare roller slutar rent.
        // Kapsignalen SKA överleva till resultatet (inte tappas till sista rollen).
        var result = await ProducerPipeline.RunAsync(
            "spel", "cheap", "strong",
            (prompt, _) => Task.FromResult(Res(true, cap: prompt.Contains("PROGRAMMERARE"))),
            _ => Task.CompletedTask, CancellationToken.None);
        Assert.True(result.HitIterationCap);
    }

    [Fact]
    public async Task RunAsync_SummerarTokensOverAllaRoller()
    {
        var result = await ProducerPipeline.RunAsync(
            "spel", "cheap", "strong",
            (_, _) => Task.FromResult(Res(true, inTok: 100, outTok: 50)),
            _ => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(300, result.TotalUsage.InputTokens);   // 3 roller * 100
        Assert.Equal(150, result.TotalUsage.OutputTokens);
    }
}
