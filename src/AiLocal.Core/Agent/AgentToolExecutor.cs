using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiLocal.Core.Contracts;

namespace AiLocal.Core.Agent;

/// <summary>How much of the local machine an agent-mode task may touch. Off by
/// default on every Worker - a Host cannot dispatch an assignment to a Worker
/// whose own operator hasn't explicitly chosen Sandboxed or Full.</summary>
/// <remarks>This app's JSON pipeline serializes enums as their raw int value
/// by default (no global JsonStringEnumConverter - see NodeRole elsewhere,
/// which does the same). This one converts as a readable string ("Off" /
/// "Sandboxed" / "Full") instead, scoped to just this type rather than
/// changing global JSON options and risking every other enum's wire format.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentAccessLevel
{
    Off,
    /// <summary>File read/write/list only, confined to a dedicated workspace
    /// folder under this Worker's own data directory. No command execution.</summary>
    Sandboxed,
    /// <summary>Unrestricted file access and shell command execution, exactly
    /// like Claude Code/Codex have on the machine they run on.</summary>
    Full
}

/// <summary>
/// Executes one agent tool call against the real filesystem/shell, enforcing
/// whichever <see cref="AgentAccessLevel"/> this Worker's operator chose.
/// </summary>
/// <summary>A file-write the agent wants to make, offered to an operator for
/// review before it lands on disk. OldContent is null when the file is new.
/// All strings are already path-resolved to absolute by the executor.</summary>
public sealed record FileChangeProposal(string Path, string? OldContent, string NewContent);

/// <summary>Operator's answer to a <see cref="FileChangeProposal"/>. Approve =
/// false makes the executor return a tool error so the agent can adapt.</summary>
public sealed record FileChangeDecision(bool Approve, string? Reason = null);

public sealed class AgentToolExecutor
{
    private const int MaxOutputChars = 20_000;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    private readonly AgentAccessLevel _level;
    private readonly string _workspaceRoot;
    private readonly bool _allowInternet;
    private readonly Func<FileChangeProposal, CancellationToken, Task<FileChangeDecision>>? _approvalGate;
    private readonly CommandGuard _commandGuard;
    private readonly CodebaseIndex? _codeIndex;
    private readonly ProjectMemory? _memory;
    // Optional self-provisioning delegate (Node layer wires this to the
    // ToolProvisioner). Null in Core tests / when not provisioned.
    private readonly Func<string, string, CancellationToken, Task<(bool Success, string Output)>>? _provisioner;
    // Optional game-scaffold delegate (Node layer wires this to
    // GameScaffoldService). Null in Core tests / when not wired. Args:
    // (engine, prompt, root) -> (success, output). Core can't reference Node,
    // so the concrete scaffolder is injected here, same pattern as _provisioner.
    private readonly Func<string, string, string, CancellationToken, Task<(bool Success, string Output)>>? _gameScaffolder;
    // v1.94: uppdragets/meddelandets ORIGINALTEXT - fallback när modellen
    // anropar scaffold_game/scaffold_app utan prompt (sett live: fotbolls-
    // manager-prompten gav PLATTFORMAR-kittet för att genreväljaren fick tom
    // sträng när en svag modell utelämnade prompt-argumentet).
    private readonly string? _taskHint;

    /// <summary>v1.95: PÅ för team-spårens executors - Full-läge men inhägnat
    /// i worktree-roten, så ett spår aldrig kan skriva förbi sin isolation
    /// med absoluta vägar (sett live: två spår krockade i huvudrotens Main.gd).</summary>
    public bool ConfineToRoot { get; set; }
    // Optional game-BUILDER delegate (Node layer wires this to GameBuilder,
    // same inject pattern as _gameScaffolder). Takes (engine, root) and
    // produces a standalone .exe via the engine's headless build; returns
    // (success, output, exePath). Null => build_game not advertised.
    private readonly Func<string, string, CancellationToken, Task<(bool Success, string Output, string? ExePath)>>? _gameBuilder;
    // Optional app-scaffold delegate (Node layer wires this to AppScaffoldService).
    private readonly Func<string, string, string, CancellationToken, Task<(bool Success, string Output)>>? _appScaffolder;
    // Optional "delegate a sub-task" delegate (Node layer wires this to a
    // local sub-agent run: it runs <prompt> as a fresh AgentLoop pass and
    // returns the result). Lets the lead agent parallelize/break work into
    // sub-tasks without the user micromanaging. Null => tool not advertised.
    // Args: (subPrompt, optionalSystem, ct) -> (success, output).
    private readonly Func<string, string?, CancellationToken, Task<(bool Success, string Output)>>? _taskDelegator;
    // Optional "ask the user" delegate (Session layer wires this to
    // PendingInfoRegistry). Null in Worker/assignment runs and Core tests - a
    // Worker's autonomous assignment writes immediately and never blocks on a
    // human, so the tool is simply not advertised there. Args: (questionsJson,
    // ct) -> the operator's free-text answer.
    private readonly Func<string, CancellationToken, Task<string>>? _askUser;
    // Optional asset-generator delegate (Node layer wires this to AssetGenerator).
    // Args: (type, prompt, width, height, outputPath, ct) -> (success, output, filePath).
    private readonly Func<string, string, int?, int?, string, CancellationToken, Task<(bool Success, string Output, string? FilePath)>>? _assetGenerator;
    // Optional screenshot delegate (Node layer wires this to ScreenshotTool).
    // Args: (windowTitle, outputPath, ct) -> (success, output, filePath).
    private readonly Func<string?, string, CancellationToken, Task<(bool Success, string Output, string? FilePath)>>? _screenshotTool;
    // Optional playtest delegate (Node layer wires this to GamePlaytester).
    // Args: (projectRoot, engine, ct) -> PlaytestResult.
    private readonly Func<string, string, CancellationToken, Task<(bool Success, string Output, double Fps, double PeakMemoryMb, TimeSpan Duration)>>? _playtester;
    // Optional package delegate (Node layer wires this to PackageService).
    // Args: (projectRoot, engine, gameName, outputDir, ct) -> (success, output, packagePath, size).
    private readonly Func<string, string, string, string?, CancellationToken, Task<(bool Success, string Output, string? PackagePath, long SizeBytes)>>? _packager;
    // Optional knowledge lookup delegate (Node layer wires this to GameKnowledgeBase).
    // Args: (engine, errorText) -> (found, fixes, bestPractices).
    private readonly Func<string, string, Task<(bool Found, string Fixes, string BestPractices)>>? _knowledgeBase;
    // Optional game-module delegate (Node layer wires this to GameModuleLibrary):
    // ready-made production systems (inventory, dialog, quest, save, ...) the
    // agent can pull in per engine instead of hand-writing them. Args:
    // (action 'list'|'get', moduleName, engine) -> (success, output).
    private readonly Func<string, string?, string?, Task<(bool Success, string Output)>>? _gameModules;
    // Optional vision-review delegate (Node layer wires this to VisionAnalyzer):
    // sends an image + question to a vision-capable cloud model and returns
    // the analysis with any visual issues found. Args: (imagePath, question,
    // ct) -> (success, output).
    private readonly Func<string, string, CancellationToken, Task<(bool Success, string Output)>>? _visionReviewer;

    public AgentToolExecutor(
        AgentAccessLevel level,
        string workspaceRoot,
        Func<FileChangeProposal, CancellationToken, Task<FileChangeDecision>>? approvalGate = null,
        bool allowInternet = false,
        CommandGuard? commandGuard = null,
        CodebaseIndex? codeIndex = null,
        ProjectMemory? memory = null,
        Func<string, string, CancellationToken, Task<(bool Success, string Output)>>? provisioner = null,
        Func<string, string, string, CancellationToken, Task<(bool Success, string Output)>>? gameScaffolder = null,
        Func<string, string, CancellationToken, Task<(bool Success, string Output, string? ExePath)>>? gameBuilder = null,
        Func<string, string, string, CancellationToken, Task<(bool Success, string Output)>>? appScaffolder = null,
        Func<string, string?, CancellationToken, Task<(bool Success, string Output)>>? taskDelegator = null,
                Func<string, CancellationToken, Task<string>>? askUser = null,
                Func<string, string, int?, int?, string, CancellationToken, Task<(bool Success, string Output, string? FilePath)>>? assetGenerator = null,
                Func<string?, string, CancellationToken, Task<(bool Success, string Output, string? FilePath)>>? screenshotTool = null,
                Func<string, string, CancellationToken, Task<(bool Success, string Output, double Fps, double PeakMemMb, TimeSpan Duration)>>? playtester = null,
                Func<string, string, string, string?, CancellationToken, Task<(bool Success, string Output, string? PackagePath, long SizeBytes)>>? packager = null,
                Func<string, string, Task<(bool Found, string Fixes, string BestPractices)>>? knowledgeBase = null,
                Func<string, string?, string?, Task<(bool Success, string Output)>>? gameModules = null,
                Func<string, string, CancellationToken, Task<(bool Success, string Output)>>? visionReviewer = null,
                string? taskHint = null)
    {
        _level = level;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _taskHint = taskHint;
        _approvalGate = approvalGate;
        _allowInternet = allowInternet;
        _commandGuard = commandGuard ?? new CommandGuard(CommandGuardLevel.Off);
        _codeIndex = codeIndex;
        _memory = memory;
        _provisioner = provisioner;
        _gameScaffolder = gameScaffolder;
        _gameBuilder = gameBuilder;
        _appScaffolder = appScaffolder;
        _taskDelegator = taskDelegator;
                _askUser = askUser;
                _assetGenerator = assetGenerator;
                _screenshotTool = screenshotTool;
                _playtester = playtester;
                _packager = packager;
                _knowledgeBase = knowledgeBase;
                _gameModules = gameModules;
                _visionReviewer = visionReviewer;
        if (_level == AgentAccessLevel.Sandboxed)
            Directory.CreateDirectory(_workspaceRoot);
    }

    /// <summary>The tool list THIS executor actually accepts - the single
    /// source of truth AgentLoop advertises to the model. Before this
    /// property existed the loop called the static ToolsFor(level) itself,
    /// which meant the loop and the executor had to agree on the flags out
    /// of band; an instance property can't drift from its own switch.</summary>
    public IReadOnlyList<ToolDefinition> Tools => ToolsFor(_level, _allowInternet, _memory is not null, _gameScaffolder is not null, _appScaffolder is not null, _askUser is not null, _taskDelegator is not null, _gameBuilder is not null, _assetGenerator is not null, _screenshotTool is not null, _playtester is not null, _packager is not null, _knowledgeBase is not null, _gameModules is not null, _visionReviewer is not null);

    public static IReadOnlyList<ToolDefinition> ToolsFor(AgentAccessLevel level, bool allowInternet = false, bool projectMemory = false, bool gameScaffold = false, bool appScaffold = false, bool canAskUser = false, bool canDelegate = false, bool gameBuild = false, bool hasAssetGen = false, bool hasScreenshot = false, bool hasPlaytest = false, bool hasPackage = false, bool hasKnowledge = false, bool hasGameModules = false, bool hasVision = false)
    {
        if (level == AgentAccessLevel.Off)
            return [];

        var tools = new List<ToolDefinition>
        {
            new("read_file", "Read the text contents of a file. Read the WHOLE file by default, or a slice with offset/limit (1-indexed). Prefer a slice for large files to save context.",
                """{"type":"object","properties":{"path":{"type":"string","description":"File path to read."},"offset":{"type":"integer","description":"1-indexed line to start reading from (default 1)."},"limit":{"type":"integer","description":"Max number of lines to return (default: whole file)."}},"required":["path"]}"""),
            new("write_file", "Create or overwrite a text file with the given content, or APPEND to it with append=true. Creates parent directories if needed. LARGE FILES: your output limit truncates very long content - write the first part, then CONTINUE with append=true calls instead of rewriting the whole file. Prefer edit_file for changing existing lines.",
                """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"},"append":{"type":"boolean","description":"true = append content to the end of the file instead of overwriting (for writing large files in parts)."}},"required":["path","content"]}"""),
            new("edit_file", "Make a targeted change to an EXISTING file: replace oldText with newText. Does not rewrite the whole file, so it's safe for large files. By default replaces the first match; set replaceAll=true to replace every match. The change is rejected if oldText is not found or is ambiguous (more than one match without replaceAll).",
                """{"type":"object","properties":{"path":{"type":"string"},"oldText":{"type":"string","description":"The exact text to replace. Must match a unique location unless replaceAll is true."},"newText":{"type":"string","description":"The replacement text."},"replaceAll":{"type":"boolean","description":"Replace every occurrence of oldText instead of just the first (default false)."}},"required":["path","oldText","newText"]}"""),
            new("search", "Search file contents across the workspace for a regex pattern (case-insensitive). Returns matching lines with file path and line number. Use to find where something is defined or used. Respects the same path confinement as other tools.",
                """{"type":"object","properties":{"pattern":{"type":"string","description":"Regular expression to search for."},"path":{"type":"string","description":"Optional directory or file to search within (relative to workspace root). Defaults to the whole workspace."},"maxMatches":{"type":"integer","description":"Cap on results (default 50)."}},"required":["pattern"]}"""),
            new("glob", "List files matching a glob pattern (e.g. \"**/*.cs\", \"src/**/*.json\"). Good for discovering the project layout without reading everything.",
                """{"type":"object","properties":{"pattern":{"type":"string","description":"Glob pattern. '**' matches across directories; a leading '**/' is added automatically so patterns like '*.cs' search recursively."},"path":{"type":"string","description":"Optional directory to scope the search to (relative to workspace root)."}},"required":["pattern"]}"""),
            new("list_files", "List files and directories at a given path (non-recursive).",
                """{"type":"object","properties":{"path":{"type":"string","description":"Directory to list. Defaults to the workspace root."}}}""")
        };

        if (level == AgentAccessLevel.Full)
        {
            tools.Add(new("run_command",
                "Run a shell command on this Worker's machine and return its stdout/stderr. Times out after 5 minutes.",
                """{"type":"object","properties":{"command":{"type":"string"},"workingDirectory":{"type":"string","description":"Optional; defaults to the current directory."}},"required":["command"]}"""));
            tools.Add(new("verify",
                "Verify the project actually builds/tests after your changes. Auto-detects the project type (.NET/Node/Rust/Go/Python) from the workspace, runs the appropriate build/test command, and returns the pass/fail result plus any compiler/test errors to fix. A task is only DONE when verify passes - run it after editing files, then fix what it reports and run again.",
                """{"type":"object","properties":{"workingDirectory":{"type":"string","description":"Optional directory to verify (relative to workspace root). Defaults to the workspace root."}},"required":[]}"""));
        }

        // Internet is a separate operator opt-in, not an access tier: it's
        // network reach, orthogonal to how much of the FILESYSTEM the agent
        // may touch - a Sandboxed agent with internet on can research docs
        // without gaining a single byte of extra disk access.
        if (allowInternet)
        {
            // Self-provisioning: only on Full + internet. The agent passes a
            // TOOL NAME (godot|blender|unity), never a URL - ToolProvisioner
            // fetches only pinned trusted sources and verifies them.
            tools.Add(ProvisionTool.Definition);
            tools.Add(new("fetch_url",
                "Fetch a web page over http/https and return its readable text content (HTML tags stripped). Use for looking things up on the internet.",
                """{"type":"object","properties":{"url":{"type":"string","description":"Absolute http:// or https:// URL to fetch."}},"required":["url"]}"""));
        }

        // Project memory/index: the agent's accumulated, growing knowledge of
        // THIS workspace. Off by default - the operator opts in per Worker.
        if (projectMemory)
        {
            tools.Add(new("recall",
                "Look up this project's accumulated memory and code index for context relevant to a question or task. Returns remembered notes (decisions, gotchas, conventions) plus the files most relevant to your query. Use it before exploring blindly so you build on what's already known.",
                """{"type":"object","properties":{"query":{"type":"string","description":"What you're looking for, e.g. 'how auth works' or 'where is the retry logic'."}},"required":["query"]}"""));
            tools.Add(new("remember",
                "Save a durable note to this project's memory (decisions, gotchas, conventions, non-obvious findings) so future sessions build on it. Keep it concise and reusable. Don't store secrets.",
                """{"type":"object","properties":{"note":{"type":"string","description":"The note to remember."}},"required":["note"]}"""));
        }

        // Game scaffolding: produce a real, buildable game project in one
        // call (Node wires the delegate to GameScaffoldService). This is what
        // lets a Worker agent PRODUCE a game autonomously instead of pasting
        // code-as-text or hand-writing thousands of lines.
        if (gameScaffold)
            tools.Add(ScaffoldGameTool.Definition);

        // App scaffolding: same idea for non-game apps (Node wires the
        // delegate to AppScaffoldService) - python / csharp chosen by the tool.
        if (appScaffold)
            tools.Add(ScaffoldAppTool.Definition);

        // Game BUILD: takes a scaffolded (or hand-written) Godot/Unity
        // project and produces a standalone .exe via the engine's headless
        // build. The agent calls this after scaffold_game to ship a real
        // playable artifact - not just written files.
        if (gameBuild)
            tools.Add(new("build_game",
                "Build a GAME project into a standalone Windows .exe via the engine's headless build (godot --export-release / unity -buildWindows64Player). Auto-detects the engine from the project if 'engine' is omitted. Requires the engine to be installed on this machine (it is NOT downloaded for you - install Godot 4.3 or Unity 6000.x first). Returns the produced .exe path on success.",
                "{\"type\":\"object\",\"properties\":{\"engine\":{\"type\":\"string\",\"description\":\"Optional: 'godot', 'unity', or 'auto' (detect from project files).\"},\"root\":{\"type\":\"string\",\"description\":\"Directory containing the game project (defaults to the workspace root).\"}},\"required\":[]}"));
        // ask_user: the agent can pause mid-run and ask the operator 1-3
        // concrete questions when something is genuinely impossible to guess
        // (e.g. a contradictory or under-specified requirement). Only wired in
        // an interactive session (not on a Worker's autonomous assignment,
        // which has no human to answer). The loop blocks on the answer before
        // continuing - see SessionApi's PendingInfoRegistry wiring.
        if (canAskUser)
            tools.Add(new("ask_user",
                "Ask the operator 1-3 concrete questions when you genuinely cannot proceed without the answer (ambiguous/contradictory requirements, a missing decision that changes the build). Do NOT use this to ask permission or for things you can reasonably assume - make a sensible default and continue. The run pauses until they reply.",
                "{\"type\":\"object\",\"properties\":{\"questions\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"1-3 concrete questions (no yes/no).\"},\"blocking\":{\"type\":\"boolean\",\"description\":\"True if you cannot continue at all without the answers (the prompt is too vague). Default false.\"}},\"required\":[\"questions\"]}"));

        // delegate_task: the lead agent can break a large build into sub-tasks
        // (e.g. "write the enemy AI", "build the settings screen") and have
        // them run as their own agent passes, then fold the results back in.
        // Keeps the main agent's context focused on orchestration instead of
        // drowning in every sub-file. Only wired where a delegator is available.
        if (canDelegate)
                    tools.Add(new("delegate_task",
                        "Hand a self-contained sub-task to a fresh sub-agent run and get its result back. Use to parallelize or isolate a piece of work (e.g. 'implement the save-system module', 'write unit tests for X') so the main conversation stays focused. Give a COMPLETE, self-contained prompt - the sub-agent has no memory of this conversation. Returns the sub-agent's final answer.",
                        """{"type":"object","properties":{"prompt":{"type":"string","description":"A complete, self-contained task description for the sub-agent."},"system":{"type":"string","description":"Optional extra system instructions for the sub-agent (e.g. 'only write tests, do not edit game logic')."}},"required":["prompt"]}"""));

                // ---- P0: Asset pipeline -----------------------------------------------
                if (hasAssetGen)
                    tools.Add(new("generate_asset",
                        "Generate a game/app asset using AI. Creates images (sprites, textures, backgrounds, UI), audio (sound effects, music), or 3D models. Saves directly to the project files. type: 'image'/'sprite'/'texture'/'background'/'ui'/'sfx'/'music'/'model3d'. width/height default to 512x512 for images. prompt describes what to generate. Returns the saved file path.",
                        """{"type":"object","properties":{"type":{"type":"string","description":"Asset type: 'image', 'sprite', 'texture', 'background', 'ui', 'sfx', 'music', 'model3d'."},"prompt":{"type":"string","description":"Description of the asset to generate, e.g. 'a pixel-art hero character, 64x64'."},"width":{"type":"integer","description":"Image width (default 512)."},"height":{"type":"integer","description":"Image height (default 512)."},"output":{"type":"string","description":"Relative path for the output file (default: auto-generated name in assets/)."}},"required":["type","prompt"]}"""));

                // ---- P0: Visual feedback ----------------------------------------------
                if (hasScreenshot)
                    tools.Add(new("screenshot",
                        "Take a screenshot of the screen (or a specific window) and save it as a PNG. Use this to SEE your game/app and check for visual bugs.",
                        """{"type":"object","properties":{"windowTitle":{"type":"string","description":"Optional partial window title to capture a specific window."},"output":{"type":"string","description":"Where to save the PNG (default: screenshots/screenshot-{timestamp}.png)."}},"required":[]}"""));

                // ---- P2: Playtesting --------------------------------------------------
                if (hasPlaytest)
                    tools.Add(new("playtest",
                        "Run the built game and analyze it for issues: performance (FPS, memory), crashes, errors. For HTML5 games, performs static code analysis. For .exe builds, runs the process and monitors it. Use after build_game to verify the game actually works.",
                        """{"type":"object","properties":{"root":{"type":"string","description":"Project root directory. Defaults to workspace root."},"engine":{"type":"string","description":"Engine: 'html5', 'unity', 'godot', or 'auto'."}},"required":[]}"""));

                // ---- P3: Packaging ----------------------------------------------------
                if (hasPackage)
                    tools.Add(new("package",
                        "Package the built game/app into a distributable .zip file with auto-generated README and metadata. Includes all .exe, assets, and dependencies. Use when the game/app is fully built and tested to create a release artifact.",
                        """{"type":"object","properties":{"root":{"type":"string","description":"Project root directory."},"engine":{"type":"string","description":"Engine: 'html5', 'unity', 'godot', 'python', 'csharp'."},"name":{"type":"string","description":"Human-readable name for the release package."},"outputDir":{"type":"string","description":"Output directory for the .zip (default: {root}/release)."}},"required":[]}"""));

                // ---- Vision review ----------------------------------------------------
                if (hasVision)
                    tools.Add(new("vision_review",
                        "SEE and critique an image with a vision-capable AI model: pass the path to a screenshot (from the screenshot tool) or any PNG/JPG in the workspace, plus a question ('does the game look right? any visual bugs?'). Returns the model's analysis and a list of visual issues. Requires a cloud API key (OpenAI/OpenRouter/Anthropic/Gemini) configured on this node. Use after screenshot to visually verify your game/app.",
                        """{"type":"object","properties":{"path":{"type":"string","description":"Path to the image to analyze (PNG/JPG)."},"question":{"type":"string","description":"What to evaluate, e.g. 'Ser spelet korrekt ut? Nagra visuella buggar?'"}},"required":["path"]}"""));

                // ---- Ready-made game modules ------------------------------------------
                if (hasGameModules)
                    tools.Add(new("game_module",
                        "Fetch READY-MADE, production-quality game systems instead of hand-writing them: inventory, dialog, quest, save/load, health/combat, XP/progression, enemy AI, particle effects. Call with action='list' to see what exists, then action='get' with name and engine ('html5'|'godot'|'unity') to receive drop-in code to adapt into the project. Prefer these over writing the same system from scratch.",
                        """{"type":"object","properties":{"action":{"type":"string","description":"'list' to enumerate available modules, 'get' to fetch one module's code."},"name":{"type":"string","description":"Module name for 'get', e.g. 'InventorySystem' or the short alias 'inventory'."},"engine":{"type":"string","description":"Engine for 'get': 'html5', 'godot', or 'unity'."}},"required":["action"]}"""));

                // ---- Knowledge base lookup --------------------------------------------
                if (hasKnowledge)
                    tools.Add(new("lookup_knowledge",
                        "Look up known errors and best practices for a game engine. Pass an error message and the engine name to get matching fixes and best practices. Use when you hit a build/runtime error you don't understand.",
                        """{"type":"object","properties":{"engine":{"type":"string","description":"Engine: 'unity', 'godot', or 'html5'."},"error":{"type":"string","description":"The error message or problem description to look up."}},"required":["engine","error"]}"""));

        return tools;
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        try
        {
            using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            var root = args.RootElement;

            return call.Name switch
            {
                "read_file" => await ReadFileAsync(call, root, ct),
                "write_file" => await WriteFileAsync(call, root, ct),
                "edit_file" => await EditFileAsync(call, root, ct),
                "search" => await SearchAsync(call, root, ct),
                "glob" => await GlobAsync(call, root, ct),
                "list_files" => ListFiles(call, root),
                "run_command" when _level == AgentAccessLevel.Full => await RunCommandAsync(call, root, ct),
                "verify" when _level == AgentAccessLevel.Full => await VerifyAsync(call, root, ct),
                "provision" when _level == AgentAccessLevel.Full && _allowInternet && _provisioner is not null
                    => await ProvisionAsync(call, root, ct),
                "provision" => Error(call, _allowInternet
                    ? "provision requires Full agent access (Inställningar -> Agent & arbetsyta)."
                    : "provision is not available - internet access is disabled on this Worker."),
                "scaffold_game" when _gameScaffolder is not null
                    => await ScaffoldGameAsync(call, root, ct),
                "scaffold_game" => Error(call, "scaffold_game is not available on this Worker (game scaffolder not wired)."),
                "scaffold_app" when _appScaffolder is not null
                    => await ScaffoldAppAsync(call, root, ct),
                "scaffold_app" => Error(call, "scaffold_app is not available on this Worker (app scaffolder not wired)."),
                "build_game" when _gameBuilder is not null
                    => await BuildGameAsync(call, args.RootElement, ct),
                "build_game" => Error(call, "build_game is not available on this Worker (game builder not wired)."),
                "ask_user" when _askUser is not null
                    => await AskUserAsync(call, root, ct),
                "ask_user" => Error(call, "ask_user is not available on this Worker (no interactive operator to answer)."),
                "delegate_task" when _taskDelegator is not null
                    => await DelegateTaskAsync(call, root, ct),
                "delegate_task" => Error(call, "delegate_task is not available on this Worker (no delegator wired)."),
                "recall" when _memory is not null => await RecallAsync(call, root, ct),
                "remember" when _memory is not null => await RememberAsync(call, root, ct),
                "recall" => Error(call, "recall is not enabled on this Worker (Inställningar -> Agent & arbetsyta -> Projektminne)."),
                "remember" => Error(call, "remember is not enabled on this Worker (Inställningar -> Agent & arbetsyta -> Projektminne)."),
                "run_command" => Error(call, "run_command is not available at this Worker's current access level (Sandboxed allows file access only)."),
                "fetch_url" when _allowInternet => await FetchUrlAsync(call, root, ct),
                "fetch_url" => Error(call, "fetch_url is not available - internet access is disabled on this Worker (Inställningar -> Agent & arbetsyta)."),
                "generate_asset" when _assetGenerator is not null => await GenerateAssetAsync(call, root, ct),
                "generate_asset" => Error(call, "generate_asset is not available on this Worker (asset generator not wired)."),
                "screenshot" when _screenshotTool is not null => await ScreenshotAsync(call, root, ct),
                "screenshot" => Error(call, "screenshot is not available on this Worker (screenshot tool not wired)."),
                "playtest" when _playtester is not null => await PlaytestAsync(call, root, ct),
                "playtest" => Error(call, "playtest is not available on this Worker (playtester not wired)."),
                "package" when _packager is not null => await PackageAsync(call, root, ct),
                "package" => Error(call, "package is not available on this Worker (packager not wired)."),
                "lookup_knowledge" when _knowledgeBase is not null => await LookupKnowledgeAsync(call, root, ct),
                "lookup_knowledge" => Error(call, "lookup_knowledge is not available on this Worker (knowledge base not wired)."),
                "game_module" when _gameModules is not null => await GameModuleAsync(call, root, ct),
                "game_module" => Error(call, "game_module is not available on this Worker (module library not wired)."),
                "vision_review" when _visionReviewer is not null => await VisionReviewAsync(call, root, ct),
                "vision_review" => Error(call, "vision_review is not available on this Worker (vision analyzer not wired)."),
                _ => Error(call, $"unknown tool: {call.Name}")
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error(call, ex.Message);
        }
        catch (JsonException ex)
        {
            // Nearly always the model's OUTPUT LIMIT truncating a large
            // write_file mid-string - tell it how to recover instead of
            // leaving a bare parser error it tends to just retry verbatim.
            return Error(call,
                $"tool arguments were not valid JSON ({ex.Message}). Detta beror oftast på att svaret kapades av modellens utdatagräns. " +
                "Skriv INTE om hela filen i ett anrop - dela upp arbetet: skapa filen med en mindre write_file och bygg vidare med flera edit_file-ändringar.");
        }
        catch (Exception ex)
        {
            return Error(call, $"tool execution failed: {ex.Message}");
        }
    }

    private async Task<ToolResult> ReadFileAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var path = ResolvePath(RequireString(args, "path"));
        if (!File.Exists(path))
            return Error(call, $"file not found: {path}");

        // Whole-file by default; a slice when offset/limit are given so the
        // agent can read just the relevant lines of a large file instead of
        // burning its entire context window on the whole thing.
        var offset = args.TryGetProperty("offset", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 1;
        var limit = args.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : int.MaxValue;
        if (offset < 1) offset = 1;

        if (offset == 1 && limit == int.MaxValue)
        {
            var content = await File.ReadAllTextAsync(path, ct);
            return new ToolResult(call.Id, call.Name, Truncate(content));
        }

        var allLines = await File.ReadAllLinesAsync(path, ct);
        var start = offset - 1;
        if (start >= allLines.Length)
            return new ToolResult(call.Id, call.Name, $"(file has {allLines.Length} lines; offset {offset} is past the end)");
        var end = Math.Min(start + limit, allLines.Length);
        var slice = allLines[start..end];
        var header = $"lines {start + 1}-{end} of {allLines.Length}:\n";
        return new ToolResult(call.Id, call.Name, header + Truncate(string.Join('\n', slice)));
    }

    private async Task<ToolResult> WriteFileAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var path = ResolvePath(RequireString(args, "path"));
        var content = RequireString(args, "content");
        // v1.95: maskningsvakt på SKRIVNINGAR. v1.32-vakten stoppade jakt på
        // [ADDRESS] i sök/edit-oldText, men live sågs det VÄRRE fallet: en
        // modell som läst maskade verktygsresultat SKREV OM hela filen och
        // materialiserade "[ADDRESS]" på disk (PackedVector2Array blev
        // [ADDRESS] i Main.gd -> obyggbart projekt). Blockera skrivningen med
        // facit i stället för att låta korruptionen landa.
        if (RedactionArtifactIn(content) is { } writeMarker)
            return Error(call,
                $"content innehåller \"{writeMarker}\" - det är AI-leverantörens integritetsmaskning av det DU LÄST, " +
                "inte filens riktiga innehåll. Skriv ALDRIG platshållaren till disk. Läs de exakta raderna via " +
                "run_command (type/findstr) och skriv innehållet med de riktiga värdena i stället.");
        var append = args.TryGetProperty("append", out var ap) && ap.ValueKind == JsonValueKind.True;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // When a session wires an approval gate, the operator must preview and
        // approve every file write before it lands - the agent never writes
        // to disk blindly. No gate (e.g. a Worker's autonomous assignment) ->
        // write immediately, unchanged behavior.
        string? oldContent = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
        var newContent = append ? (oldContent ?? "") + content : content;
        if (_approvalGate is not null)
        {
            var decision = await _approvalGate(new FileChangeProposal(path, oldContent, newContent), ct);
            if (!decision.Approve)
                return Error(call, decision.Reason ?? "File write was rejected by the operator.");
        }

        await File.WriteAllTextAsync(path, newContent, ct);

        // Truncation tripwire: a weak model whose output limit cut the content
        // mid-file used to see a clean "wrote N characters" and either declare
        // victory on a broken file or loop full rewrites forever (observed:
        // 6+ rewrites of the same 10 KB file until the iteration cap killed
        // the run). Tell it EXACTLY what happened and how to continue.
        var truncationNote = DetectTruncation(path, newContent);

        var verifyNote = await AutoVerifyIfFullAsync(ct);
        return new ToolResult(call.Id, call.Name,
            (append ? $"appended {content.Length} characters to {path} (file is now {newContent.Length} characters)"
                    : $"wrote {content.Length} characters to {path}")
            + (truncationNote is null ? "" : $"\n\n{truncationNote}")
            + (verifyNote is null ? "" : $"\n\n{verifyNote}"));
    }

    /// <summary>Cheap end-of-file sanity for the file types agents write
    /// most: an .html without a closing tag or a .js/.html whose script does
    /// not parse is almost always output-limit truncation. Returns an
    /// actionable Swedish warning, or null when the file looks complete.</summary>
    public static string? DetectTruncation(string path, string content)
    {
        const string Advice = "Skriv INTE om hela filen igen (samma gräns kapar den igen) - " +
            "fortsätt i stället där den slutar med write_file append:true, eller reparera med edit_file.";
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".html" or ".htm")
        {
            if (!content.Contains("</html>", StringComparison.OrdinalIgnoreCase))
                return "VARNING: filen saknar </html> - innehållet ser TRUNKERAT ut (modellens utdatagräns). " + Advice;
            var jsErrors = JsSyntaxChecker.CheckHtml(content);
            if (jsErrors.Count > 0)
                return "VARNING: filens JavaScript parsas inte (" + jsErrors[0] + ") - troligen trunkerad. " + Advice;
        }
        else if (ext is ".js" or ".mjs")
        {
            if (JsSyntaxChecker.CheckScript(content) is { } err &&
                JsSyntaxChecker.CheckScript(content, asModule: true) is not null)
                return $"VARNING: filen parsas inte som JavaScript ({err.Message}, rad {err.Line}) - troligen trunkerad. " + Advice;
        }
        else if (ext is ".gd")
        {
            // v1.94: JS-ism-tripwire för GDScript. Svaga modeller skriver
            // JavaScript-vanor ("// kommentar", function, tomma kroppar) som
            // Godot vägrar parsa - sett live ("// Godot Script" + func utan
            // kropp). Fånga vid SKRIVNINGEN med facit, inte först i verify.
            if (GdScriptLint.Check(content) is { } gdErr)
                return "VARNING: " + gdErr + " Rätta filen med edit_file innan du går vidare - Godot vägrar parsa den som den är.";
        }
        return null;
    }

    private async Task<ToolResult> EditFileAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var path = ResolvePath(RequireString(args, "path"));
        var oldText = RequireString(args, "oldText");
        var newText = RequireString(args, "newText");
        var replaceAll = args.TryGetProperty("replaceAll", out var r) && r.ValueKind == JsonValueKind.True;

        if (!File.Exists(path))
            return Error(call, $"file not found: {path}");

        var content = await File.ReadAllTextAsync(path, ct);
        var occurrences = content.Split(oldText).Length - 1;

        if (occurrences == 0)
        {
            // Leverantörsartefakt, inte filkorruption: vissa modellrutter
            // (PII-filter hos t.ex. gratis OpenRouter-varianter) maskerar
            // sökvägar m.m. i VERKTYGSRESULTATEN till platshållare som
            // "[ADDRESS]". Modellen "ser" dem i read_file men de finns aldrig
            // i filen på disk - och har setts jaga dem i evighetsloopar tills
            // iterationstaket slog i (search + findstr tomma hela tiden).
            if (RedactionArtifactIn(oldText) is { } marker)
                return Error(call,
                    $"edit_file: oldText innehåller \"{marker}\" - det är en maskningsartefakt från AI-leverantörens integritetsfilter, INTE något som finns i filen. " +
                    "Filen på disk är oskadad. Sluta leta efter eller \"laga\" sådana markörer - gå vidare med nästa riktiga uppgift.");
            return Error(call, "edit_file failed: oldText was not found in the file. Re-read the file and copy the exact text to replace (whitespace and punctuation must match).");
        }
        if (!replaceAll && occurrences > 1)
            return Error(call, $"edit_file failed: oldText matched {occurrences} locations and replaceAll is false. Make oldText more specific, or set replaceAll=true to replace all of them.");

        // v1.95: samma maskningsvakt på NYA texten - en modell som skriver in
        // "[ADDRESS]" materialiserar leverantörsmaskningen på disk (sett live).
        if (RedactionArtifactIn(newText) is { } newMarker)
            return Error(call,
                $"edit_file: newText innehåller \"{newMarker}\" - det är AI-leverantörens integritetsmaskning av det du läst, " +
                "inte riktig kod. Läs de exakta raderna via run_command (type/findstr) och gör om ändringen med de riktiga värdena.");

        var updated = replaceAll ? content.Replace(oldText, newText) : content.Replace(oldText, newText);

        // Apply the same approval gate as write_file so the operator (or the
        // Host's ChangeReviewer) still previews an edit before it lands.
        if (_approvalGate is not null)
        {
            var decision = await _approvalGate(new FileChangeProposal(path, content, updated), ct);
            if (!decision.Approve)
                return Error(call, decision.Reason ?? "File edit was rejected by the operator.");
        }

        await File.WriteAllTextAsync(path, updated, ct);
        var verifyNote = await AutoVerifyIfFullAsync(ct);
        return new ToolResult(call.Id, call.Name,
            $"edited {path}: replaced {occurrences} occurrence(s) of {oldText.Length} chars with {newText.Length} chars."
            + (verifyNote is null ? "" : $"\n\n{verifyNote}"));
    }

    private ToolResult ListFiles(ToolCall call, JsonElement args)
    {
        var requested = args.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()!
            : ".";
        var path = ResolvePath(requested);
        if (!Directory.Exists(path))
            return Error(call, $"directory not found: {path}");

        var entries = Directory.EnumerateFileSystemEntries(path)
            .Select(e => Directory.Exists(e) ? $"{Path.GetFileName(e)}/" : Path.GetFileName(e))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ToolResult(call.Id, call.Name, entries.Count > 0 ? string.Join('\n', entries) : "(empty directory)");
    }

    // Directories we never descend into when searching/globbing - keeps the
    // agent from drowning in build output, deps, and VCS internals.
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs", "packages", "out", "dist", ".idea"
    };

    private async Task<ToolResult> SearchAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var pattern = RequireString(args, "pattern");
        var maxMatches = args.TryGetProperty("maxMatches", out var m) && m.ValueKind == JsonValueKind.Number
            ? Math.Clamp(m.GetInt32(), 1, 500) : 50;

        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline); }
        catch (ArgumentException ex) { return Error(call, $"invalid search pattern: {ex.Message}"); }

        var baseDir = args.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } pp
            ? ResolveDir(pp)
            : _level == AgentAccessLevel.Sandboxed ? _workspaceRoot : ".";
        if (!Directory.Exists(baseDir))
            return Error(call, $"search path not found: {baseDir}");

        var matches = new List<string>();
        foreach (var file in EnumerateFiles(baseDir))
        {
            if (matches.Count >= maxMatches) break;
            if (!IsTextFile(file)) continue;
            string[] lines;
            try { lines = await File.ReadAllLinesAsync(file, ct); }
            catch { continue; }
            for (var i = 0; i < lines.Length; i++)
            {
                if (matches.Count >= maxMatches) break;
                if (regex.IsMatch(lines[i]))
                {
                    var rel = _level == AgentAccessLevel.Sandboxed
                        ? Path.GetRelativePath(_workspaceRoot, file).Replace('\\', '/')
                        : file;
                    matches.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        if (matches.Count == 0)
        {
            var artifactNote = RedactionArtifactIn(pattern) is { } marker
                ? $"\nOBS: \"{marker}\" är en maskningsartefakt från AI-leverantörens integritetsfilter - den finns aldrig i filer på disk, bara i vad modellen visas. Jaga den inte; filerna är oskadade."
                : "";
            return new ToolResult(call.Id, call.Name, $"no matches for /{pattern}/ under {baseDir}{artifactNote}");
        }
        var truncated = matches.Count >= maxMatches;
        return new ToolResult(call.Id, call.Name,
            string.Join('\n', matches) + (truncated ? $"\n...({maxMatches} match cap reached)" : ""));
    }

    private async Task<ToolResult> GlobAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var pattern = RequireString(args, "pattern");
        // Accept a leading "**/" automatically so a bare "*.cs" still searches
        // recursively, matching the glob behaviour agents expect.
        if (!pattern.StartsWith("**") && !pattern.Contains('/'))
            pattern = "**/" + pattern;
        if (pattern.StartsWith("**/"))
            pattern = pattern[3..];

        var baseDir = args.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 } pp
            ? ResolveDir(pp)
            : _level == AgentAccessLevel.Sandboxed ? _workspaceRoot : ".";
        if (!Directory.Exists(baseDir))
            return Error(call, $"glob path not found: {baseDir}");

        var results = new List<string>();
        foreach (var file in EnumerateFiles(baseDir))
        {
            if (GlobMatch(Path.GetRelativePath(baseDir, file).Replace('\\', '/'), pattern)
                || GlobMatch(Path.GetFileName(file), pattern))
                results.Add(_level == AgentAccessLevel.Sandboxed
                    ? Path.GetRelativePath(_workspaceRoot, file).Replace('\\', '/')
                    : file);
        }
        results.Sort(StringComparer.OrdinalIgnoreCase);

        if (results.Count == 0)
            return new ToolResult(call.Id, call.Name, $"no files match '{pattern}' under {baseDir}");
        return new ToolResult(call.Id, call.Name, string.Join('\n', results));
    }

    /// <summary>Enumerate files under a directory, skipping VCS/build/dep dirs.</summary>
    private static IEnumerable<string> EnumerateFiles(string baseDir)
    {
        var stack = new Stack<string>();
        stack.Push(baseDir);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch { continue; }
            foreach (var f in files) yield return f;

            IEnumerable<string> subdirs;
            try { subdirs = Directory.EnumerateDirectories(dir); }
            catch { continue; }
            foreach (var d in subdirs)
                if (!SkipDirs.Contains(Path.GetFileName(d)))
                    stack.Push(d);
        }
    }

    private static bool IsTextFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".txt" or ".cs" or ".json" or ".md" or ".js" or ".ts" or ".tsx" or ".jsx"
            or ".py" or ".java" or ".c" or ".cpp" or ".h" or ".hpp" or ".css" or ".scss"
            or ".html" or ".xml" or ".yaml" or ".yml" or ".toml" or ".ini" or ".sh" or ".ps1"
            or ".go" or ".rs" or ".rb" or ".php" or ".sql" or ".gitignore" or ".cfg" or ".conf"
            or ".csproj" or ".fsproj" or ".sln" or ".props" or ".targets" or ".razor" or ".cshtml"
            or ".vue" or ".lock" or ".graphql" or ".proto" or ".ipynb" or ".r" or ".kt" or ".swift"
            or ".dart" or ".lua" or ".pl" or ".asm" or ".s" or ".bat" or ".cmd" or ".csv";
    }

    /// <summary>Lightweight glob match supporting '*' and '**'. Not a full
    /// glob engine - covers the patterns an agent actually uses.</summary>
    private static bool GlobMatch(string text, string pattern)
    {
        // Convert the glob to a regex: ** -> .*, * -> [^/]*, ? -> ., escape rest.
        var sb = new System.Text.StringBuilder();
        sb.Append('^');
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    sb.Append(".*");
                    i++; // consume second *
                    // Skip a following slash so "**/*.cs" matches "a/b.cs" too.
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/') i++;
                }
                else sb.Append("[^/]*");
            }
            else if (c == '?') sb.Append('.');
            else sb.Append(Regex.Escape(c.ToString()));
        }
        sb.Append('$');
        return Regex.IsMatch(text, sb.ToString(), RegexOptions.IgnoreCase);
    }

    /// <summary>Resolve a directory argument the same way file paths are
    /// resolved (sandbox confinement for Sandboxed, workspace-relative for
    /// Full), returning the absolute directory or throwing on escape.</summary>
    private string ResolveDir(string requestedPath)
    {
        // Mirror ResolvePath's contract for directories.
        if (_level == AgentAccessLevel.Full)
        {
            var full = Path.GetFullPath(requestedPath, _workspaceRoot);
            if (ConfineToRoot)
            {
                var rootSep = _workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
                    ? _workspaceRoot : _workspaceRoot + Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootSep, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(full, _workspaceRoot, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException(
                        $"ditt teamspår arbetar ISOLERAT i worktreen '{_workspaceRoot}' - '{requestedPath}' pekar utanför den. Använd RELATIVA vägar.");
            }
            return full;
        }
        if (Path.IsPathRooted(requestedPath))
            throw new UnauthorizedAccessException(
                $"absolute paths are not allowed in sandboxed mode: '{requestedPath}'");
        return Path.GetFullPath(Path.Combine(_workspaceRoot, requestedPath));
    }

    private async Task<ToolResult> RunCommandAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var command = RequireString(args, "command");
        var screen = _commandGuard.Screen(command);
        if (screen is not null && _commandGuard.IsBlocked(command))
            return Error(call, screen);
        var requestedDirectory = args.TryGetProperty("workingDirectory", out var wd) && wd.ValueKind == JsonValueKind.String
            ? wd.GetString()!
            : ".";
        var workingDirectory = Path.GetFullPath(requestedDirectory, _workspaceRoot);

        var (exitCode, output) = await RunCommandCoreAsync(command, workingDirectory, ct);
        var warn = screen is not null ? screen + "\n" : "";
        return new ToolResult(call.Id, call.Name, warn + output, IsError: exitCode != 0);
    }

    /// <summary>Runs a shell command in <paramref name="workingDirectory"/>
    /// with the standard 5-minute timeout, returning (exitCode, combined
    /// output). Shared by run_command AND the verify tool so both use the
    /// exact same process plumbing; ProjectVerifier calls it via a delegate
    /// to keep the filesystem/process logic in one place.</summary>
    public async Task<(int ExitCode, string Output)> RunCommandCoreAsync(string command, string workingDirectory, CancellationToken ct)
    {
        // Full stays unrestricted, this only fixes what a RELATIVE path/the
        // omitted-argument default means (see RunCommandAsync above).
        var psi = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            // Wrappad /c "{cmd}": cmd.exe strippar första+sista citatet när
            // kommandot har >2 citattecken - agentens run_command med citerad
            // exe-väg OCH citerade argument gick sönder före körning (v1.90).
            ? new ProcessStartInfo("cmd.exe", $"/c \"{command}\"")
            : new ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"");
        // A Full-mode executor's _workspaceRoot is never auto-created (see the
        // constructor) - Process.Start throws on a working directory that
        // doesn't exist, so fall all the way back to this process's own cwd
        // (guaranteed to exist) rather than risk that.
        psi.WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory
            : Directory.Exists(_workspaceRoot) ? _workspaceRoot
            : Environment.CurrentDirectory;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(CommandTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            return (137, $"command timed out after {CommandTimeout.TotalMinutes:0}m and was killed. Partial output:\n{Truncate(stdout.ToString())}");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var output = $"exit code: {process.ExitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}";
        return (process.ExitCode, Truncate(output));
    }

    private async Task<ToolResult> VerifyAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        // No explicit dir -> verify the project the agent is ACTUALLY working
        // in, not blindly the workspace root. Workspaces accumulate projects
        // over time, and verify used to grade a stale HTML5 game at the root
        // while the agent built a Python app in a subfolder.
        var requested = args.TryGetProperty("workingDirectory", out var wd) && wd.ValueKind == JsonValueKind.String && wd.GetString() is { Length: > 0 } p
            ? ResolveDir(p)
            : ProjectRootDetector.Detect(_workspaceRoot) ?? _workspaceRoot;

        var verifier = new ProjectVerifier();
        var result = await verifier.VerifyAsync(requested,
            (cmd, dir, c) => RunCommandCoreAsync(cmd, dir, c), ct);
        return new ToolResult(call.Id, call.Name, result.Report, IsError: !result.Success);
    }

    /// <summary>Returns the first provider-redaction placeholder found in
    /// <paramref name="text"/> (e.g. "[ADDRESS]"), or null. Some model routes
    /// run PII filters over what the MODEL is shown, masking Windows paths
    /// and similar as bracketed placeholders - they exist only in the model's
    /// view, never in files on disk. Public so tests can lock the list.</summary>
    public static string? RedactionArtifactIn(string text)
    {
        string[] markers = ["[ADDRESS]", "[NAME]", "[EMAIL]", "[PHONE]", "[SSN]", "[REDACTED]", "[PII]"];
        var raw = text ?? "";
        // Modeller söker ofta med regex-escapade mönster ("\[ADDRESS\]") -
        // avescapa innan matchning så även de fångas.
        var unescaped = raw.Replace("\\", "");
        return markers.FirstOrDefault(m =>
            raw.Contains(m, StringComparison.OrdinalIgnoreCase)
            || unescaped.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>After a file write/edit in Full mode, re-verify the project so
    /// the model gets build/test errors back immediately rather than declaring
    /// victory on a broken change. Returns null when there's nothing meaningful
    /// to report (no project detected) so we don't spam the context with
    /// "couldn't find a project" on every single-file edit in a non-code dir.</summary>
    private async Task<string?> AutoVerifyIfFullAsync(CancellationToken ct)
    {
        if (_level != AgentAccessLevel.Full) return null;
        // Same most-recently-active-project rule as the verify tool itself.
        var target = ProjectRootDetector.Detect(_workspaceRoot);
        if (target is null) return null;

        try
        {
            var result = await new ProjectVerifier().VerifyAsync(target,
                (cmd, dir, c) => RunCommandCoreAsync(cmd, dir, c), ct);
            return result.Success
                ? "VERIFY AFTER EDIT: project still builds/tests (PASS)."
                : $"VERIFY AFTER EDIT (FAIL - fix before declaring done):\n{result.Report}";
        }
        catch (Exception ex)
        {
            // Verification is a bonus safety net, never a hard failure of the
            // write itself - if the build toolchain isn't installed or errors
            // out internally, the edit already landed; just don't crash.
            return $"VERIFY AFTER EDIT: could not run verification ({ex.GetType().Name}).";
        }
    }

    // Shared across calls/executors: sockets are pooled per handler, and a
    // per-call HttpClient would exhaust ports under an agent that reads many
    // pages. Redirects are capped by the default handler (50); the 10s
    // timeout is per request.
    private static readonly HttpClient FetchClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const int MaxFetchBytes = 1_000_000;

    private async Task<ToolResult> FetchUrlAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var rawUrl = RequireString(args, "url");
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Error(call, $"fetch_url only accepts absolute http:// or https:// URLs, got: {rawUrl}");

        using var response = await FetchClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return Error(call, $"fetch failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {uri}");

        // Read at most MaxFetchBytes no matter what Content-Length claims -
        // the model asked for "the page", not an unbounded download.
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var buffer = new char[MaxFetchBytes];
        var read = await reader.ReadBlockAsync(buffer, ct);
        var raw = new string(buffer, 0, read);

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var text = contentType.Contains("html", StringComparison.OrdinalIgnoreCase) || raw.TrimStart().StartsWith('<')
            ? HtmlToText(raw)
            : raw;
        return new ToolResult(call.Id, call.Name, Truncate($"[{uri}]\n{text}"));
    }

    private async Task<ToolResult> ProvisionAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        if (_provisioner is null)
            return Error(call, "provision is not available on this Worker (ToolProvisioner not wired).");
        var tool = RequireString(args, "tool");
        // Default: tom sträng => provisionerns egen verktygsmapp
        // (%LOCALAPPDATA%\AiLocal\tools) - SAMMA plats ToolLocator letar på,
        // så verify/run hittar verktyget direkt. Arbetsytan som default
        // gömde verktyget för locatorn.
        var destination = args.TryGetProperty("destination", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()! : "";
        var (success, output) = await _provisioner(tool, destination, ct);
        return success
            ? new ToolResult(call.Id, call.Name, output)
            : Error(call, output);
    }

    private async Task<ToolResult> ScaffoldGameAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        if (_gameScaffolder is null)
            return Error(call, "scaffold_game is not available on this Worker (scaffolder not wired).");
        // engine is OPTIONAL: the agent chooses, or 'auto'/omitted => the
        // tool picks (html5 default, unity for 3D). Never required.
        var engine = args.TryGetProperty("engine", out var e) && e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())
            ? e.GetString()! : "auto";
        var prompt = args.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()! : "";
        // v1.94: GENREVALET lever på prompten - en svag modell som utelämnar
        // den fick standardkittet (plattformare) oavsett vad användaren bad om
        // (live: "fotbolls manager" -> Pixel Rush). Uppdragets originaltext är
        // alltid ett bättre genreunderlag än tom sträng.
        if (string.IsNullOrWhiteSpace(prompt) && !string.IsNullOrWhiteSpace(_taskHint))
            prompt = _taskHint;
        var root = args.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(r.GetString())
            ? ResolvePath(r.GetString()!) : _workspaceRoot;
        var (success, output) = await _gameScaffolder(engine, prompt, root, ct);
        if (!success)
            return Error(call, output);
        // A: after scaffolding, verify the produced project actually builds /
        // runs (Full mode only). For html5 this confirms the file is present
        // and well-formed; for engine projects it compiles. The build status
        // is surfaced to the agent so it (and the user) sees a finished,
        // verified artifact rather than just "files written".
        var verifyNote = await AutoVerifyIfFullAsync(ct);
        return new ToolResult(call.Id, call.Name,
            output + (verifyNote is null ? "" : "\n\n" + verifyNote));
    }

    private async Task<ToolResult> ScaffoldAppAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        if (_appScaffolder is null)
            return Error(call, "scaffold_app is not available on this Worker (app scaffolder not wired).");
        // tech is OPTIONAL: 'auto'/omitted => the tool picks (python default,
        // csharp when the prompt asks for it). The agent is free to choose.
        var tech = args.TryGetProperty("tech", out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString())
            ? t.GetString()! : "auto";
        var prompt = RequireString(args, "prompt");
        var root = args.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(r.GetString())
            ? ResolvePath(r.GetString()!) : _workspaceRoot;
        var (success, output) = await _appScaffolder(tech, prompt, root, ct);
        if (!success)
            return Error(call, output);
        // A: verify the produced app actually builds/runs (Full mode), so the
        // studio ships a verified artifact rather than just written files.
        var verifyNote = await AutoVerifyIfFullAsync(ct);
        return new ToolResult(call.Id, call.Name,
            output + (verifyNote is null ? "" : "\n\n" + verifyNote));
    }

    private async Task<ToolResult> BuildGameAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        if (_gameBuilder is null)
            return Error(call, "build_game is not available on this Worker (game builder not wired).");
        var engine = args.TryGetProperty("engine", out var e) && e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())
            ? e.GetString()! : "auto";
        var reqRoot = args.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(r.GetString())
            ? ResolvePath(r.GetString()!) : _workspaceRoot;
        var (success, output, exePath) = await _gameBuilder(engine, reqRoot, ct);
        if (!success)
            return Error(call, output);
        return new ToolResult(call.Id, call.Name,
            output + (exePath is null ? "" : $"\nexe: {exePath}"), IsError: false);
    }

    private async Task<ToolResult> AskUserAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        if (_askUser is null)
            return Error(call, "ask_user is not available on this Worker (no interactive operator to answer).");

        var questions = new List<string>();
        if (args.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in q.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    questions.Add(item.GetString()!);
        }
        if (questions.Count == 0)
            return Error(call, "ask_user requires a non-empty 'questions' array of 1-3 concrete strings.");
        if (questions.Count > 3)
            questions = questions.Take(3).ToList();

        var blocking = args.TryGetProperty("blocking", out var b) && b.ValueKind == JsonValueKind.True;

        // Serialize the request so the Session layer can render it as an
        // "awaiting_info" SSE step, then block on the operator's answer.
        var requestJson = JsonSerializer.Serialize(new
        {
            questions,
            blocking
        });
        var answer = await _askUser(requestJson, ct);

        if (string.IsNullOrWhiteSpace(answer))
            return Error(call, "The operator did not provide an answer (run may have been cancelled).");

        return new ToolResult(call.Id, call.Name,
            "Operator svarade:\n" + answer, IsError: false);
    }

    private async Task<ToolResult> DelegateTaskAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        if (_taskDelegator is null)
            return Error(call, "delegate_task is not available on this Worker (no delegator wired).");

        var prompt = RequireString(args, "prompt");
        if (string.IsNullOrWhiteSpace(prompt))
            return Error(call, "delegate_task requires a non-empty 'prompt'.");
        // Optional extra system context for the sub-agent (e.g. "only write
        // tests"). The sub-agent starts fresh with no memory of this run, so
        // the prompt must be fully self-contained.
        var system = args.TryGetProperty("system", out var s) && s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString())
            ? s.GetString()! : null;

        try
        {
            var (success, output) = await _taskDelegator(prompt, system, ct);
            return success
                ? new ToolResult(call.Id, call.Name,
                    "SUB-TASK RESULT:\n" + Truncate(output),
                    IsError: false)
                : Error(call, "Sub-task failed:\n" + output);
        }
        catch (Exception ex)
        {
            return Error(call, $"delegate_task failed: {ex.Message}");
        }
    }

    /// <summary>Crude, dependency-free readable-text extraction: good enough
    /// for an agent to read documentation/articles, deliberately not a
    /// browser. Scripts/styles removed entirely, remaining tags stripped,
    /// the handful of entities that dominate real pages decoded, whitespace
    /// collapsed to at most one blank line.</summary>
    public static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<br\\s*/?>|</p>|</div>|</li>|</h[1-6]>|</tr>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = text.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<")
            .Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#39;", "'");
        text = Regex.Replace(text, "[ \\t]+", " ");
        text = Regex.Replace(text, "( ?\\n ?)+", "\n");
        return text.Trim();
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }

    /// <summary>Vägar som uppenbart är exempel-platshållare ur verktygs-
    /// beskrivningar ("/path/to/...", "your/project", vinkelparenteser).</summary>
    internal static bool LooksLikePlaceholderPath(string p) =>
        p.Contains("/path/to", StringComparison.OrdinalIgnoreCase)
        || p.Contains("\\path\\to", StringComparison.OrdinalIgnoreCase)
        || p.Contains("your/project", StringComparison.OrdinalIgnoreCase)
        || p.Contains("your\\project", StringComparison.OrdinalIgnoreCase)
        || p.Contains('<') || p.Contains('>');

    private string ResolvePath(string requestedPath)
    {
        // v1.94: platshållar-vakt. Svaga modeller kopierar exempelvägen ur
        // verktygsbeskrivningen rakt av (sett live: build_game med
        // "/path/to/your/project/root") - fånga det FÖRE filsystemet och
        // säg exakt vad som ska stå i stället.
        if (LooksLikePlaceholderPath(requestedPath))
            throw new InvalidOperationException(
                $"'{requestedPath}' är en PLATSHÅLLARE från ett exempel, inte en riktig väg. " +
                "Använd \".\" för projektroten eller en verklig relativ väg (t.ex. \"spel/Main.gd\").");

        // Full is deliberately unconfined - not a security boundary, that's
        // the whole point of Full ("exactly like Claude Code/Codex have on
        // the machine they run on") - an absolute path still passes straight
        // through here untouched. What changes: a RELATIVE path used to
        // resolve against Environment.CurrentDirectory (wherever this node's
        // own exe happened to launch from), which has nothing to do with the
        // folder the agent is actually working in. Path.GetFullPath's two-arg
        // overload resolves a relative path against _workspaceRoot instead,
        // while still returning an absolute path as-is when one is given.
        if (_level == AgentAccessLevel.Full)
        {
            var full = Path.GetFullPath(requestedPath, _workspaceRoot);
            // v1.95: worktree-inhägnad för TEAM-spår. I team-läget SKA spåren
            // vara isolerade i varsin worktree - live sågs spår skriva med
            // ABSOLUTA vägar rakt i huvudroten (förbi sin worktree) så två
            // spår krockade i samma Main.gd. Inhägnaden ger facit i felet.
            if (ConfineToRoot)
            {
                var rootSep = _workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
                    ? _workspaceRoot : _workspaceRoot + Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootSep, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(full, _workspaceRoot, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException(
                        $"ditt teamspår arbetar ISOLERAT i worktreen '{_workspaceRoot}' - '{requestedPath}' pekar utanför den. " +
                        "Använd RELATIVA vägar (t.ex. \"Main.gd\"); huvudroten och andra spår får inte röras (mergen sker efteråt).");
            }
            return full;
        }

        // Sandboxed: reject absolute paths outright rather than trying to
        // "combine" them - Path.Combine(root, absolutePath) on Windows
        // silently DISCARDS root and returns the absolute path as-is, which
        // would otherwise be a trivial sandbox escape (write_file path:
        // "C:\Windows\System32\whatever" resolving right past the workspace).
        if (Path.IsPathRooted(requestedPath))
            throw new UnauthorizedAccessException(
                $"absolute paths are not allowed in sandboxed mode: '{requestedPath}' - use a path relative to the workspace root");

        var combined = Path.GetFullPath(Path.Combine(_workspaceRoot, requestedPath));
        var rootWithSeparator = _workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _workspaceRoot
            : _workspaceRoot + Path.DirectorySeparatorChar;

        // Path.GetFullPath already collapses ".." segments, so this single
        // prefix check also catches relative traversal attempts like
        // "../../../etc/passwd" - nothing under _workspaceRoot survives
        // GetFullPath and still fails this check.
        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, _workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"path '{requestedPath}' resolves outside this Worker's sandboxed workspace");

        return combined;
    }

    private async Task<ToolResult> RecallAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var query = RequireString(args, "query");
        _codeIndex?.Build(_workspaceRoot);
        var memory = _memory?.Read() ?? "";
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(memory))
        {
            sb.AppendLine("## Projektminne");
            sb.AppendLine(memory);
            sb.AppendLine();
        }
        if (_codeIndex is not null)
        {
            var hits = _codeIndex.Recall(query, limit: 8);
            if (hits.Count > 0)
            {
                sb.AppendLine("## Mest relevanta filer");
                foreach (var (path, score) in hits)
                    sb.AppendLine($"- ({score}) {path}");
            }
            else
            {
                sb.AppendLine("## Mest relevanta filer\n(inget träffade indexet för den frågan)");
            }
        }
        return new ToolResult(call.Id, call.Name, sb.ToString().Trim(), IsError: false);
    }

    private async Task<ToolResult> RememberAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var note = RequireString(args, "note");
        _memory?.Remember(note);
        return new ToolResult(call.Id, call.Name, "Sparat i projektminnet.", IsError: false);
    }

    private static string RequireString(JsonElement args, string property) =>
        args.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ArgumentException($"missing required argument: {property}");

    private static string Truncate(string s) =>
        s.Length > MaxOutputChars ? s[..MaxOutputChars] + $"\n...(truncated, {s.Length} characters total)" : s;

    private static ToolResult Error(ToolCall call, string message) => new(call.Id, call.Name, message, IsError: true);

        // ---- New tool PeakMemoryMb (P0-P3) ------------------------------------------

        private async Task<ToolResult> GenerateAssetAsync(ToolCall call, JsonElement args, CancellationToken ct)
        {
            if (_assetGenerator is null)
                return Error(call, "generate_asset is not wired.");
            var type = RequireString(args, "type");
            var prompt = RequireString(args, "prompt");
            var width = args.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number ? (int?)w.GetInt32() : null;
            var height = args.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number ? (int?)h.GetInt32() : null;
            var output = args.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString()! : "";
            // The tool promises "default: auto-generated name in assets/" -
            // resolve it HERE so the generated file lands inside the agent's
            // workspace (and respects sandbox confinement), not wherever this
            // node's process happens to have its current directory. An empty
            // path passed straight through used to throw in Path.GetFullPath.
            var extension = type.Trim().ToLowerInvariant() switch
            {
                "sfx" or "music" or "audio" => ".wav",
                "model3d" => ".glb",
                _ => ".png"
            };
            output = string.IsNullOrWhiteSpace(output)
                ? ResolvePath(Path.Combine("assets", $"{type.Trim().ToLowerInvariant()}-{Guid.NewGuid().ToString("n")[..8]}{extension}"))
                : ResolvePath(output);
            // Stilkonsekvens: alla bildpromptar i ett projekt får SAMMA
            // stilsuffix (från DESIGN.md:s art direction när den finns) så
            // spelet ser ut att vara ritat av en hand, inte ett collage.
            prompt = AssetStyle.Apply(_workspaceRoot, type, prompt);
            var (success, result, filePath) = await _assetGenerator(type, prompt, width, height, output, ct);
            return success
                ? new ToolResult(call.Id, call.Name, filePath is not null ? $"Asset created: {filePath}\n{result}" : result)
                : Error(call, result);
        }

        private async Task<ToolResult> ScreenshotAsync(ToolCall call, JsonElement args, CancellationToken ct)
        {
            if (_screenshotTool is null)
                return Error(call, "screenshot is not wired.");
            var windowTitle = args.TryGetProperty("windowTitle", out var wt) && wt.ValueKind == JsonValueKind.String ? wt.GetString() : null;
            var output = args.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString()! : "";
            // Same default-path contract as generate_asset: an omitted output
            // must land inside the workspace, not be passed through as "" (the
            // tool would try Directory.CreateDirectory(GetDirectoryName(""))).
            output = string.IsNullOrWhiteSpace(output)
                ? ResolvePath(Path.Combine("screenshots", $"screenshot-{Guid.NewGuid().ToString("n")[..8]}.png"))
                : ResolvePath(output);
            var (success, result, filePath) = await _screenshotTool(windowTitle, output, ct);
            return success
                ? new ToolResult(call.Id, call.Name, filePath is not null ? $"Screenshot saved: {filePath}\n{result}" : result)
                : Error(call, result);
        }

        private async Task<ToolResult> PlaytestAsync(ToolCall call, JsonElement args, CancellationToken ct)
        {
            if (_playtester is null)
                return Error(call, "playtest is not wired.");
            var root = args.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } rp
                ? rp : _workspaceRoot;
            var engine = args.TryGetProperty("engine", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString()! : "auto";
            var (success, output, fps, peakMem, duration) = await _playtester(root, engine, ct);
            return success
                ? new ToolResult(call.Id, call.Name, output)
                : Error(call, output);
        }

        private async Task<ToolResult> PackageAsync(ToolCall call, JsonElement args, CancellationToken ct)
        {
            if (_packager is null)
                return Error(call, "package is not wired.");
            var root = args.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } rp
                ? rp : _workspaceRoot;
            var engine = args.TryGetProperty("engine", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString()! : "auto";
            var name = args.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "Game";
            var outputDir = args.TryGetProperty("outputDir", out var od) && od.ValueKind == JsonValueKind.String ? od.GetString() : null;
            var (success, output, packagePath, size) = await _packager(root, engine, name, outputDir, ct);
            return success
                ? new ToolResult(call.Id, call.Name, output)
                : Error(call, output);
        }

        private async Task<ToolResult> VisionReviewAsync(ToolCall call, JsonElement args, CancellationToken ct)
        {
            if (_visionReviewer is null)
                return Error(call, "vision_review is not wired.");
            var path = ResolvePath(RequireString(args, "path"));
            if (!File.Exists(path))
                return Error(call, $"image not found: {path}");
            var question = args.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(q.GetString())
                ? q.GetString()!
                : "Beskriv vad du ser. Ser spelet/appen korrekt ut? Lista alla visuella buggar, renderingsfel eller saker som ser ofardiga ut.";
            var (success, output) = await _visionReviewer(path, question, ct);
            return success
                ? new ToolResult(call.Id, call.Name, Truncate(output))
                : Error(call, output);
        }

        private async Task<ToolResult> GameModuleAsync(ToolCall call, JsonElement args, CancellationToken ct)
        {
            if (_gameModules is null)
                return Error(call, "game_module is not wired.");
            var action = RequireString(args, "action");
            var name = args.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var engine = args.TryGetProperty("engine", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var (success, output) = await _gameModules(action, name, engine);
            return success
                ? new ToolResult(call.Id, call.Name, Truncate(output))
                : Error(call, output);
        }

        private async Task<ToolResult> LookupKnowledgeAsync(ToolCall call, JsonElement args, CancellationToken ct)
        {
            if (_knowledgeBase is null)
                return Error(call, "lookup_knowledge is not wired.");
            var engine = RequireString(args, "engine");
            var error = RequireString(args, "error");
            var (found, fixes, bestPractices) = await _knowledgeBase(engine, error);
            var result = found
                ? $"## Hittade lösningar för: {error}\n\n{fixes}\n\n## Best Practices ({engine})\n{bestPractices}"
                : $"Inga matchande fel hittades för '{error}'.\n\n## Best Practices ({engine})\n{bestPractices}";
            return new ToolResult(call.Id, call.Name, result);
        }
    }
