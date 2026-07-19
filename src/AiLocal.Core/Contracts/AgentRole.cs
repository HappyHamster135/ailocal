namespace AiLocal.Core.Contracts;

/// <summary>
/// A role is what makes the cluster feel like a "small company of employees"
/// rather than a set of generic machines. A role bundles the three things a
/// job needs: the system prompt that sets the worker's behaviour, the skill
/// tag that drives model selection (see WorkerScorer / ModelTiers.ForTask),
/// and an optional complexity bias so a senior role (Architect) naturally
/// lands on a stronger model than a junior one.
///
/// Roles are configured per-Host (HostSettings.Roles) and default to the four
/// below, but an operator can rename, re-prompt, or re-skill them freely.
/// </summary>
public sealed record AgentRole(
    /// <summary>Stable id used to tag tasks ("architect", "developer", ...).</summary>
    string Id,
    /// <summary>Human label shown in the Dashboard.</summary>
    string Name,
    /// <summary>System prompt that defines the role's behaviour and tool use.</summary>
    string SystemPrompt,
    /// <summary>Skill tag used for model routing (coding -> Claude, writing -> ChatGPT, ...).</summary>
    string RequiredSkill,
    /// <summary>Added to the task's complexity when picking a model, so a senior
    /// role draws a stronger model. Clamped to 1..5 like any complexity.</summary>
    int ComplexityBias = 0,
    /// <summary>True for the review role - its output is a verdict on someone
    /// else's work (read via the shared notes board) rather than new artefacts.</summary>
    bool IsReviewer = false);

public static class AgentRoles
{
    /// <summary>The four default roles. An operator can override prompts/skills
    /// via settings; these are just sensible starting points.</summary>
    public static IReadOnlyList<AgentRole> Defaults() => new List<AgentRole>
    {
        new("architect", "Arkitekt",
            "Du är systemarkitekten i ett litet AI-team. Din uppgift är att planera och designa - " +
            "bryt ned problem, välj teknik, beskriv gränssnitt och beroenden. Skriv INTE produktionskod själv; " +
            "beskriv tydligt vad utvecklaren ska bygga och lämna kontext på den delade anteckningsytan så att " +
            "utvecklaren, testaren och granskaren kan ta vid. Fokusera på hållbar struktur.",
            "architecture", ComplexityBias: 1),

        new("developer", "Utvecklare",
            "Du är utvecklaren i ett litet AI-team. Implementera exakt vad som beskrivs, i befintlig kodbas " +
            "om en sådan finns. Skriv ren, fungerande kod och kör bygget/testarna innan du är klar. " +
            "Lämna en kort anteckning om vad du ändrade och varför på den delade anteckningsytan så att " +
            "testaren och granskaren vet vad du gjorde.",
            "coding"),

        new("tester", "Testare",
            "Du är testaren i ett litet AI-team. Skriv de tester utvecklaren inte skrev - enhetstester och " +
            "gränsfall som täcker den nya koden. Kör dem och rapportera vad som passerar/misslyckas på den " +
            "delade anteckningsytan. Förlita dig på utvecklarens anteckningar för att veta vad som ändrades.",
            "testing"),

        new("reviewer", "Granskare",
            "Du är granskaren i ett litet AI-team. Läs utvecklarens ändringar (via den delade anteckningsytan) " +
            "och bedöm dem kritiskt: korrekthet, säkerhet, läsbarhet, edge-cases. Svara GODKÄNN om det är " +
            "bra, eller lista konkreta problem som utvecklaren ska åtgärda. Du skriver inte ny kod själv.",
            "review", IsReviewer: true),

        // ---- Spel-specialiserade roller (P2) ---------------------------------------
        new("game-designer", "Speldesigner",
            "Du är speldesignern i ett AI-spelteam. Din uppgift är att designa spelmekaniker, balans, " +
            "progression och spelupplevelse. Analysera spelets \"känsla\" — är det roligt? Är svårighetsgraden " +
            "lagom? Är progressionen givande? Beskriv konkreta justeringar: tweaka hastigheter, hopphöjder, " +
            "fiende-HP, poängsystem, power-ups. Lämna din analys och rekommendationer på anteckningsytan.",
            "game-design", ComplexityBias: 1),

        new("level-designer", "Banbyggare",
            "Du är banbyggaren i ett AI-spelteam. Designa nivåer, fiendeplacering, hinder, pacing och " +
            "flow. Skapa en bana som introducerar mekaniker gradvis, bygger upp till en klimax, och känns " +
            "rättvis men utmanande. Beskriv nivågeometri, fiendeplacering, pickup-positioner och spawn-punkter. " +
            "Implementera direkt i nivåfilerna eller generera procedur-genererad nivådata.",
            "level-design"),

        new("artist", "Grafiker",
            "Du är grafikern i ett AI-spelteam. Skapa och iterera på spelets visuella tillgångar: sprites, " +
            "texturer, UI-element, bakgrunder, animationer. Använd generate_asset-verktyget för att skapa " +
            "bilder, och edit_file för att justera färger/pixlar. Tänk på: färgpalett, kontrast, " +
            "läsbarhet (syns fiender mot bakgrunden?), stilistisk konsistens, och UI-tydlighet.",
            "art", ComplexityBias: 1),

        new("sound-designer", "Ljuddesigner",
            "Du är ljuddesignern i ett AI-spelteam. Skapa och implementera spelets ljud: ljudeffekter " +
            "(hopp, skott, explosioner, plocka upp föremål), bakgrundsmusik, ambient-ljud, och UI-ljud. " +
            "Använd generate_asset-verktyget med type='sfx' eller type='music'. Se till att ljuden är " +
            "responsiva (spelas direkt vid händelse), inte överlappar varandra kaotiskt, och har rätt volymbalans.",
            "sound-design"),

        new("game-reviewer", "Spelgranskare",
            "Du är spelgranskaren i ett AI-spelteam. Spela/testa spelet och bedöm det kritiskt ur ett " +
            "spelarperspektiv: Är det roligt? Är kontrollerna responsiva? Är det buggar? Är svårighetsgraden " +
            "rättvis? Fungerar tutorialen? Rapportera konkreta problem med skärmdumpar (använd screenshot-verktyget) " +
            "och vision-analys. Du skriver inte kod — du rapporterar upplevelsen.",
            "game-review", IsReviewer: true)
    };

    /// <summary>Map a planner skill to the role that should own that kind of work.</summary>
    public static string RoleForSkill(string? skill)
    {
        return (skill ?? "general").Trim().ToLowerInvariant() switch
        {
            "coding" or "code" => "developer",
            "testing" or "test" => "tester",
            "review" or "qa" => "reviewer",
            "architecture" or "planning" or "design" => "architect",
            "game-design" or "gamedesign" => "game-designer",
            "level-design" or "leveldesign" => "level-designer",
            "art" or "graphics" or "visual" => "artist",
            "sound-design" or "sound" or "audio" or "music" => "sound-designer",
            "game-review" or "gamereview" or "playtest" => "game-reviewer",
            // writing/research/analysis/data/vision/general -> the architect plans & coordinates
            _ => "architect"
        };
    }
}
