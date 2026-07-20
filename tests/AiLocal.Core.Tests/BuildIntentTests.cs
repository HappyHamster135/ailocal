using AiLocal.Node.Roles;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks the chat-to-agent routing trigger: a build request typed in
/// the Host chat must be recognized (so it produces FILES via an agent, not
/// code pasted back as text), while questions and non-build prompts must
/// stay on the ordinary chat pipeline.</summary>
public class BuildIntentTests
{
    [Theory]
    [InlineData("bygg ett 2d plattformsspel")]
    [InlineData("kan du skapa en app som haller koll pa mina rakningar")]
    [InlineData("gör en webbsida om katter")]
    [InlineData("utveckla ett verktyg som konverterar csv till json")]
    [InlineData("build me a snake game")]
    [InlineData("create a small cli tool for renaming files")]
    [InlineData("implementera en api tjanst for vader")]
    public void BuildRequests_AreDetected(string prompt)
    {
        Assert.True(HostRole.IsBuildRequest(prompt));
    }

    [Fact]
    public void WorkspaceIsEmpty_OnlyForMissingOrEmptyDirs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-wsempty-" + Guid.NewGuid().ToString("n"));
        try
        {
            Assert.True(WorkerRole.WorkspaceIsEmpty(dir)); // finns inte
            Directory.CreateDirectory(dir);
            Assert.True(WorkerRole.WorkspaceIsEmpty(dir)); // tom
            File.WriteAllText(Path.Combine(dir, "x.txt"), "hej");
            // Icke-tom: förskaffolden får ALDRIG röra ett befintligt projekt.
            Assert.False(WorkerRole.WorkspaceIsEmpty(dir));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("vad är en app?")]
    [InlineData("vilka spel är populära just nu?")]
    [InlineData("bygg vidare på resonemanget ovan")]
    [InlineData("skriv en dikt om hösten")]
    [InlineData("sammanfatta den här texten")]
    [InlineData("hur fungerar en api-nyckel?")]
    public void NonBuildPrompts_AreNotDetected(string prompt)
    {
        Assert.False(HostRole.IsBuildRequest(prompt));
    }
}
