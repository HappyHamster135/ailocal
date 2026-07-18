using AiLocal.Core.Contracts;

namespace AiLocal.Core.Agent;

/// <summary>
/// The 'scaffold_game' tool definition. Kept in its own file (built with a
/// C# raw string literal) so its JSON schema has zero backslash escaping
/// and can't drift from what the model sees.
///
/// The executor holds a delegate (wired from the Node layer, which owns
/// GameScaffoldService) so a Worker agent can PRODUCE a real, buildable game
/// project with a single tool call - instead of hand-writing thousands of
/// lines with write_file or guessing a curl command. This closes the gap
/// between "the endpoint exists" and "the agent can actually use it".
///
/// engine is OPTIONAL: the agent is expected to CHOOSE the best engine for
/// the request. Omit engine (or pass 'auto') and the tool picks html5 by
/// default (zero-install, runs anywhere), escalating to unity/godot only
/// when the prompt clearly needs a heavier engine (3D, or the words
/// unity/godot appear). This is what lets the worker decide tech itself.
/// </summary>
public static class ScaffoldGameTool
{
    public static readonly ToolDefinition Definition = new(
        "scaffold_game",
        "Create a complete, buildable game project in one step. Use this FIRST when the user asks for a game - it writes a real, runnable project (not code-as-text) that you then extend with edit_file. CHOOSE the engine yourself: 'html5' (default - self-contained playable 2D platformer in index.html, zero install, runs in any browser), 'unity' (real Unity 2D platformer project that builds to an .exe), or 'godot'. Omit 'engine' (or pass 'auto') to let the tool pick the best fit - it defaults to html5 for 2D and escalates to unity for 3D. Prefer producing a runnable result over a description.",
        """
        {"type":"object","properties":{"engine":{"type":"string","description":"Game engine to use: 'html5' (default, zero-install playable), 'unity', 'godot', or 'auto' (let the tool pick - defaults to html5). Omit to auto-pick."},"prompt":{"type":"string","description":"Short description of the game, e.g. 'en 2d plattformare med hopp och ljud'. Used to choose sensible defaults."},"root":{"type":"string","description":"Optional folder to create the project in (relative to the workspace, or absolute). Defaults to the workspace root."}},"required":["prompt"]}
        """);
}
