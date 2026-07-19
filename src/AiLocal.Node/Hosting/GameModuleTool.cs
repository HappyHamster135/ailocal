using AiLocal.Node.Hosting.GameModules;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Node-side handler for the agent's <c>game_module</c> tool: exposes
/// <see cref="GameModuleLibrary"/>'s ready-made production systems (inventory,
/// dialog, quest, save/load, combat, progression, AI, particles) as
/// list/get actions. Shared by WorkerRole and SessionApi so both wire the
/// exact same behaviour into their executors.
/// </summary>
public static class GameModuleTool
{
    public static Task<(bool Success, string Output)> Handle(string action, string? name, string? engine)
    {
        if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
        {
            var list = string.Join(Environment.NewLine, GameModuleLibrary.List()
                .Select(m => $"- {m.Name}: {m.Description}"
                    + (m.Dependencies.Length > 0 ? $" (kräver: {string.Join(", ", m.Dependencies)})" : "")));
            return Task.FromResult((true,
                "Färdiga moduler (hämta kod med action='get', name, engine='html5'|'godot'|'unity'):\n" + list));
        }

        if (string.Equals(action, "get", StringComparison.OrdinalIgnoreCase))
        {
            var code = GameModuleLibrary.GetCode(name ?? "", engine ?? "");
            return Task.FromResult(code is null
                ? (false, $"Okänd modul '{name}' eller motor '{engine}'. Kör action='list' för alternativ; engine måste vara html5, godot eller unity.")
                : (true, $"// Modul: {name} ({engine}) - anpassa namn/värden och integrera i projektet.\n{code}"));
        }

        return Task.FromResult((false, "action måste vara 'list' eller 'get'."));
    }
}
