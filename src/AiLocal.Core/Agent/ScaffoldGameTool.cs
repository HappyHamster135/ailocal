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
/// </summary>
public static class ScaffoldGameTool
{
    public static readonly ToolDefinition Definition = new(
        "scaffold_game",
        "Create a complete, buildable game project in one step. Use this FIRST when the user asks for a game - it writes a real, runnable project (not code-as-text) that you then extend with edit_file. engine 'html5' writes a self-contained playable 2D platformer (index.html, zero install) and is the safest default. engine 'unity' writes a real Unity 2D platformer project (PlayerController, GameManager, scene, settings) that builds to an .exe. engine 'godot' writes a Godot project. Prefer this over writing game files by hand.",
        """
        {"type":"object","properties":{"engine":{"type":"string","description":"Game engine: 'html5' (default, zero-install playable), 'unity', or 'godot'."},"prompt":{"type":"string","description":"Short description of the game, e.g. 'en 2d plattformare med hopp och ljud'. Used to pick sensible defaults."},"root":{"type":"string","description":"Optional folder to create the project in (relative to the workspace, or absolute). Defaults to the workspace root."}},"required":["engine","prompt"]}
        """);
}
