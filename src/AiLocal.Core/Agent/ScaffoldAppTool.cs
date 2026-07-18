using AiLocal.Core.Contracts;

namespace AiLocal.Core.Agent;

/// <summary>
/// The 'scaffold_app' tool definition. Mirrors ScaffoldGameTool: the agent
/// produces a real, runnable application project in one call instead of
/// hand-writing boilerplate. tech 'python' (default) or 'csharp' ('auto' /
/// empty lets the tool pick from the prompt). Wired as a Node-layer delegate
/// (same pattern as scaffold_game) because Core can't reference Node.
/// </summary>
public static class ScaffoldAppTool
{
    public static readonly ToolDefinition Definition = new(
        "scaffold_app",
        "Create a complete, runnable application project in one step. Use this when the user asks for an app/script/tool (not a game). tech 'python' (default, runs with `python main.py`) or 'csharp' (`dotnet run`). Pass tech 'auto' or omit it to let the tool pick the best tech for the request. The tool writes the real project files; you then extend them with edit_file. Prefer producing a runnable result over a description.",
        """
        {"type":"object","properties":{"tech":{"type":"string","description":"App tech: 'python' (default), 'csharp', or 'auto' (pick from prompt)."},"prompt":{"type":"string","description":"What the app should do, e.g. 'en CLI som laddar ner och sorterar bilder'."},"root":{"type":"string","description":"Optional folder to create the project in (relative to the workspace, or absolute). Defaults to the workspace root."}},"required":["prompt"]}
        """);
}
