using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>C11/C12/C13: produktionsbaren i systemprompten guidar VARJE bygge
/// (godot + html5) mot onboarding, tillgänglighet och innehållsdjup - soft
/// guidance i checklistan i stället för att blåsa upp de mätbara
/// kontraktskriterierna (som modellen inte kan fokusera på om de blir för många).</summary>
public class ProductionBarTests
{
    [Fact]
    public void ProduktionsbarTackerOnboardingTillganglighetDjup()
    {
        var prompt = AgentSystemPrompt.Build("C:/proj", AgentAccessLevel.Full, null);

        Assert.Contains("Onboarding", prompt);        // C13
        Assert.Contains("Accessibility", prompt);     // C12
        Assert.Contains("colorblind", prompt);
        Assert.Contains("Depth", prompt);             // C11
        Assert.Contains("replay value", prompt);
        Assert.Contains("Performance", prompt);       // C3 (förstärkt i checklistan)
    }
}
