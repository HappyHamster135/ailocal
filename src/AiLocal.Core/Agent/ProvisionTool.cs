using AiLocal.Core.Contracts;

namespace AiLocal.Core.Agent;

/// <summary>
/// The 'provision' tool definition. Kept in its own file (built with a
/// C# raw string literal) so its JSON schema has zero backslash escaping
/// and can't drift from what the model sees.
/// </summary>
public static class ProvisionTool
{
    // Raw string literal: the JSON schema below contains literal " chars,
    // no escaping. ToolProvisioner (Node layer) is the only thing that
    // acts on this - it accepts ONLY a tool NAME, never a URL.
    public static readonly ToolDefinition Definition = new(
        "provision",
        "Provision a build tool this Worker is missing so a task can complete: python, node (npm), git, java, dotnet, godot, godot-templates, blender or unity. Pass the tool NAME only - never a URL. The Worker downloads it from a pinned trusted official source and installs it, and returns the absolute executable path. Use whenever a build/verify/command fails because the tool is missing (e.g. exit 9009 'command not found' on Windows) - do NOT skip the step, provision and retry instead. For Godot games that must export a runnable exe: provision 'godot' AND 'godot-templates' (the export templates), then run build_game.",
        """
        {"type":"object","properties":{"tool":{"type":"string","description":"Tool name to provision: 'python', 'node', 'git', 'java', 'dotnet', 'godot', 'godot-templates', 'blender' or 'unity'. No URLs."},"destination":{"type":"string","description":"Optional folder to install into (defaults to the Worker's local app-data tools dir; godot-templates always installs to Godot's own template folder)."}},"required":["tool"]}
        """);
}
