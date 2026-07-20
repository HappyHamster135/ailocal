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
        "Provision a build tool this Worker is missing so a task can complete: godot, blender, unity or python. Pass the tool NAME only - never a URL. The Worker downloads it from a pinned trusted official source and installs it. Use when a build/verify fails because the tool is missing (e.g. exit 9009 'python' not found on Windows) - do NOT skip the step, provision and retry instead.",
        """
        {"type":"object","properties":{"tool":{"type":"string","description":"Tool name to provision: 'godot', 'blender', 'unity' or 'python'. No URLs."},"destination":{"type":"string","description":"Optional folder to install into (defaults to the Worker's local app-data tools dir)."}},"required":["tool"]}
        """);
}
