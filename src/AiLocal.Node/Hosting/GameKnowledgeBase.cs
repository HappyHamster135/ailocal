using System;
using System.Collections.Generic;
using System.Linq;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Production-ready knowledge base of common game engine errors and their fixes.
/// Covers Unity, Godot, and HTML5/JavaScript engines with fuzzy-matching lookup.
/// </summary>
public static class GameKnowledgeBase
{
    public record ErrorFix(string ErrorPattern, string Fix, string Explanation);

    private static readonly Dictionary<string, List<ErrorFix>> _knowledge = new()
    {
        // ═════════════════════════════════════════════════════════════════════
        // UNITY (16 entries)
        // ═════════════════════════════════════════════════════════════════════
        ["unity"] = new()
        {
            new("CS0117", "Add a using directive (e.g. 'using UnityEngine;') or reference the missing assembly. If the type is from a package, install it via Package Manager.",
                "The compiler cannot find a type or member — usually a missing using or a missing assembly reference in the .asmdef."),

            new("CS0246", "Install the package or add a Project Reference in the .asmdef file. If it's a standard Unity type, add 'using UnityEngine;' or 'using UnityEditor;'.",
                "The type or namespace could not be found. Check Package Manager for missing dependencies (e.g. InputSystem, Cinemachine)."),

            new("CS1061", "Check the API documentation for the correct member name. Unity 2022+ may have moved or renamed the property. Consider using GetComponent<>().",
                "'X does not contain a definition for Y' — typo, API change, or missing interface. Also common when a script expects a component that isn't attached."),

            new("NullReferenceException", "Identify the null variable with a Debug.Log before the crash. Use 'GetComponent<>()' in Awake or Start, never in Update. Check that the GameObject has the required component.",
                "Object reference not set to an instance of an object. The #1 Unity runtime error — usually a missing assignment in the Inspector or a failed GetComponent."),

            new("MissingReferenceException", "Do not hold references to destroyed objects. Use 'if (obj != null)' before every access. Store reference handles (e.g. instance ID) instead of the object itself in collections.",
                "Attempting to access a Unity object that has been destroyed (e.g. via Destroy() or scene unload). The object reference still exists in C# but the native part is gone."),

            new("IndexOutOfRangeException", "Check array/list bounds with 'if (index >= 0 && index < array.Length)'. Use List.Count, not array.Length for lists. Add Debug.Log to print the index and size.",
                "Accessing an array or list with an index outside its bounds. Often caused by mismatch between expected and actual child count."),

            new("Character falls through floor", "Ensure the Rigidbody has continuous collision detection (CollisionDetectionMode.ContinuousDynamic). Increase the collision solver iteration count in Physics settings. Never use transform.Translate on a Rigidbody.",
                "Fast-moving characters or low physics ticks cause the collider to pass through static colliders. Also check that the floor has a MeshCollider (convex=false) or BoxCollider."),

            new("Collision not detected", "Both objects need colliders, and at least one needs a Rigidbody. Use OnCollisionEnter (not OnTriggerEnter) for physical collisions. Check layer collision matrix in Project Settings.",
                "OnCollisionEnter not firing. Common causes: one object is a trigger only, both objects are triggers, or the layer matrix has collisions disabled."),

            new("OnTriggerEnter not firing", "Enable 'Is Trigger' on the collider. Both objects need colliders, and at least one needs a Rigidbody. Check that the script implements 'void OnTriggerEnter(Collider other)' with the exact signature.",
                "The trigger message method signature must match exactly. Also check that the GameObject with the script actually has the collider attached, not a child."),

            new("Canvas not showing", "Ensure the Canvas has a GraphicRaycaster and the child UI elements have Image/Text components. Check Canvas.sortingOrder and Canvas.renderMode (ScreenSpaceOverlay is safest). Verify the EventSystem exists in the scene.",
                "A Canvas with no GraphicRaycaster is non-interactive. ScreenSpaceCamera requires a camera reference. WorldSpace canvases need correct scale and position."),

            new("Text not showing / TextMeshPro invisible", "Install TextMeshPro via Package Manager if not present. Use a TextMeshProUGUI component (not legacy Text). Ensure the font asset is assigned. Check that the text color alpha > 0.",
                "TMP requires the Essential Resources package. Legacy Text and TMP are incompatible. A white text on white background or alpha=0 also causes invisible text."),

            new("Build error: .NET version mismatch", "Set Api Compatibility Level to .NET Standard 2.1 in Player Settings. Remove packages that require .NET Framework 4.x. Switch to the IL2CPP scripting backend and check for unsupported APIs.",
                "Unity 2022+ defaults to .NET Standard 2.1. Some NuGet packages require .NET 6+ which is not supported in Unity. Use the Unity-compatible version or embed source."),

            new("Build error: Gradle / Android SDK", "Install the correct Android SDK/NDK via Unity Hub. Set 'Custom Keystore' if signing. Check that 'Minimum API Level' matches your device. Delete the 'Library/Bee' folder and rebuild.",
                "Android build failures often come from SDK path mismatches, missing NDK, or Gradle version incompatibility. Unity Hub's Android module install is the safest fix."),

            new("IL2CPP build error", "Switch to Mono backend temporarily to isolate the issue. Check for 'System.Reflection' usage that IL2CPP cannot AOT-compile. Add link.xml with the namespaces you need to preserve.",
                "IL2CPP strips code it thinks is unused. Use link.xml or [Preserve] attribute to keep types loaded via reflection. Also ensure all managed DLLs are IL2CPP-compatible."),

            new("Animation not playing", "Check that the Animator Controller has the correct parameter name and type. Ensure the Animator component has the Controller assigned. Verify the animation clip has the correct loop settings.",
                "Animator parameters must match exactly (case-sensitive). A missing Controller or a misnamed parameter means no animation plays. Also check that the Animator is not disabled."),

            new("Audio not playing", "Check that the AudioSource is not muted and volume > 0. Ensure AudioClip is assigned. Verify the listener is in the scene (AudioListener on MainCamera). Check 3D sound rolloff settings for distance.",
                "Missing AudioListener is the most common cause of silent audio. For 3D sounds, the source may be too far from the listener. Also check the audio mixer groups for muted busses."),
        },

        // ═════════════════════════════════════════════════════════════════════
        // GODOT (16 entries)
        // ═════════════════════════════════════════════════════════════════════
        ["godot"] = new()
        {
            new("Parser Error: Unexpected token", "Check the line indicated in the error. Common causes: missing comma in array/dict, unmatched parentheses, or accidentally using single '=' instead of '==' in if conditions.",
                "GDScript's whitespace-sensitive parser is strict. Indentation changes mid-block or mixing tabs and spaces also causes parse errors."),

            new("Invalid assignment", "Ensure the variable is declared with 'var' or a type hint before assignment. If it's a constant use 'const'. For exported variables check that the assigned value matches the type.",
                "Assigning a value to an undeclared variable or to a constant. Also happens when trying to assign to a property that is read-only."),

            new("Cannot find member", "Verify that the method or property exists on the type. If calling a method on a node, ensure the node has the script attached. Use 'is' or 'as' to cast base types.",
                "GDScript dynamic typing means spelling errors aren't caught until runtime. Use static typing (e.g. ': Node2D') to catch these at parse time."),

            new("Null instance", "Call .instantiate() on a PackedScene before using it. Check that the path to the scene/resource is correct. Add 'if node == null: return' guards.",
                "Trying to access a node that was never created or has been freed. Most common with scenes loaded from disk via load() or preload()."),

            new("Attempt to call function in freed object", "Stop the game before modifying nodes that are in a queue_free() chain. Use 'is_inside_tree()' before accessing. Use Callable with .bind() instead of raw references for deferred calls.",
                "Godot's deferred deletion (queue_free) means the node still exists in memory but is marked as invalid. Any callback referencing the node after it's freed triggers this."),

            new("CharacterBody2D falls through floor", "Add a CollisionShape2D child to the CharacterBody2D. Set MoveAndSlide() parameters correctly — pass the velocity and floor direction. Check that the floor has a StaticBody2D with a CollisionShape2D.",
                "A CharacterBody2D without a CollisionShape2D cannot collide. Floor must be StaticBody2D, not Area2D. Also check that the floor's collision layer matches the character's mask."),

            new("Collision not detected", "Both objects need CollisionShape2D/3D children. For Area2D: connect the 'body_entered' signal in the editor or via code. For PhysicsBody: ensure the script implements _on_body_entered correctly.",
                "Missing CollisionShape is the #1 cause. When using Area2D, you must connect the signal — overriding _on_body_entered is not enough if the signal isn't connected."),

            new("CanvasLayer / Control not visible", "Set the Control node's anchor preset to 'Full Rect' or set size via rect_size. Add a ColorRect or TextureRect as background to make it visible. Check Z-index in 2D.",
                "Control nodes need explicit size to appear. A Control with the default (0, 0) rectangle is invisible. Also check that the CanvasLayer's layer number isn't behind other layers."),

            new("Text not showing in Label", "Check that the Label's text property has a value and that the font color alpha > 0. If using a custom theme, verify the theme resource is assigned. For dynamic fonts, ensure the font file exists.",
                "A Label without font or with transparent color is invisible. Custom themes without Font set will render nothing. Also check that the Label's size is not (0, 0)."),

            new("Export build: missing export templates", "Download export templates from the Godot download page or via the editor's 'Export -> Install Export Templates' menu. Place them in the Godot export templates folder (usually AppData/Roaming/Godot/export_templates/).",
                "Godot cannot build without matching export templates for the target platform. The version must exactly match the editor version (e.g. 4.3-stable templates for 4.3-stable editor)."),

            new("Export build: .NET not found", "Install .NET SDK 8.0+ and the Godot .NET export templates. Set the correct .NET target framework in the .csproj. Run 'dotnet restore' before exporting.",
                "Godot .NET exports require the .NET SDK to be installed separately. The export template path must point to a .NET-enabled template folder."),

            new("Shader compilation error", "Check the shader type (spatial/canvas_item/particles) matches the node. Ensure all varyings used in fragment() are declared in vertex(). Use #ifdef for platform-specific code.",
                "Godot's shader language is GLSL-based but has its own syntax. Common errors: using GLSL functions not supported, mixing CanvasItem and Spatial shaders, or missing 'shader_type' directive."),

            new("_process / _physics_process not called", "Check that the script extends Node (or a subclass) and is attached to a node in the scene tree. Verify the node is not paused (process_mode). Check that the node is not a child of a paused node.",
                "Only nodes inheriting from Node can have _process. The node must be in the scene tree (added via add_child or existing in the scene). Paused nodes don't receive process calls."),

            new("Signal connection not working", "Check the signal name spelling (case-sensitive). Use the editor's 'Node' tab to verify signals. In code, use 'signal_name.connect(callable)' format. Avoid connecting in _ready if the emitting node hasn't been added yet.",
                "Signals are case-sensitive in Godot 4. Mistyping the signal name or connecting before both nodes are in the tree are the most common issues."),

            new("Resource loading fails", "Use 'preload()' for resources that exist at compile time, 'load()' for runtime-loaded resources. Ensure the resource path starts with 'res://'. Check that the file hasn't been moved or renamed.",
                "preload() errors are caught at parse time, load() errors only at runtime. A moved file gives no error at compile time but returns null at runtime."),

            new("AudioStreamPlayer not playing", "Check that the Stream property has an AudioStream assigned. Set 'Playing' to true or call .play(). Verify the Audio Bus is not muted. For 3D audio, check the attenuation range.",
                "An AudioStreamPlayer without a stream assigned will silently do nothing. Also check the 'Autoplay' checkbox or ensure play() is called after the node enters the scene."),
        },

        // ═════════════════════════════════════════════════════════════════════
        // HTML5 / JAVASCRIPT (16 entries)
        // ═════════════════════════════════════════════════════════════════════
        ["html5"] = new()
        {
            new("Cannot read properties of null", "Check that the DOM element exists before access with 'if (el)'. Use optional chaining 'el?.property'. Ensure the script runs after the DOM is ready (DOMContentLoaded event).",
                "Trying to access a property on null or undefined. Usually means querySelector returned null because the element isn't in the DOM yet or the selector is wrong."),

            new("X is not a function", "Verify the variable is actually a function (typeof x === 'function'). Check for typos. If importing, ensure the default vs named import is correct (import x vs import { x }).",
                "Calling something that isn't callable. Often happens with undefined imports, or when a function was overwritten by a non-function assignment."),

            new("Failed to load module", "Check the module path and file extension (must start with './' or '../' for relative). Ensure the server serves the file with the correct MIME type (application/javascript). Verify CORS headers if cross-origin.",
                "ES module loading fails if the path is wrong, the server returns text/html (404 page), or CORS is blocked. Check the Network tab in DevTools for the exact response."),

            new("Canvas context is null", "Call canvas.getContext('2d') only after the canvas is in the DOM. Use requestAnimationFrame or DOMContentLoaded to ensure readiness. Check that the canvas is not display:none.",
                "getContext returns null when the canvas is not visible or the context type is unsupported. WebGL is blocked on some systems — always have a 2D fallback."),

            new("RequestAnimationFrame not running", "Ensure RAF is called recursively — the callback must call requestAnimationFrame again. Stop the loop when the tab is hidden (visibilitychange). Check for JS errors in the callback that silently kill the chain.",
                "RAF stops if the callback throws. Wrap the body in try/catch. Also, some browsers throttle RAF in background tabs to 1 FPS."),

            new("AudioContext not allowed to start", "Call audioContext.resume() inside a user gesture handler (click, keydown, touchstart). Browser policy blocks autoplay audio. Show a 'Click to start' button and resume from there.",
                "Browsers require user interaction before AudioContext becomes active. The context stays in 'suspended' state until resume() is called from a user gesture."),

            new("CORS error loading asset", "Serve the asset from the same origin or set CORS headers on the server (Access-Control-Allow-Origin: *). For local development, use a local HTTP server (npx serve, python -m http.server).",
                "Cross-Origin Resource Sharing blocks loading assets from different origins. file:// URLs cannot use CORS — always use a local server."),

            new("localStorage quota exceeded", "Catch QuotaExceededError and implement an LRU eviction strategy. Store only essential data. Compress JSON strings. Use IndexedDB for larger datasets (>5MB).",
                "localStorage is limited to ~5MB per origin. Once exceeded, setItem throws. IndexedDB supports much larger storage (hundreds of MB)."),

            new("Game loop runs too fast / too slow", "Use delta time from requestAnimationFrame (timestamp argument). Multiply all movement by delta/1000. Use 'frame-rate independent' multiplication rather than relying on fixed FPS.",
                "Without delta time, game speed varies with framerate. requestAnimationFrame passes a timestamp — compare with previous to get dt. Cap dt at 33ms (30 FPS minimum) to prevent teleportation on lag spikes."),

            new("Sprite not rendering on canvas", "Check that drawImage is called with correct source and destination coordinates. Ensure the image is fully loaded (use img.onload or img.decode()). Check that canvas is not cleared after drawing.",
                "drawImage with an unloaded image draws nothing. Use img.complete and load event handlers. Also check that the canvas isn't cleared by another draw call later in the frame."),

            new("Keyboard input not working", "Call preventDefault() on keydown/keyup events to stop browser default behavior. Focus the canvas or document. Ensure event listeners are added (addEventListener, not inline). Check key vs keyCode deprecation.",
                "Browsers intercept many keys (F5, Arrow keys for scrolling). Use event.preventDefault() and focus the game container. Prefer event.key over deprecated event.keyCode."),

            new("Touch input not working", "Add 'touch-action: none' CSS to the canvas to prevent browser scroll. Handle both touchstart/touchmove/touchend and mousedown/mousemove/mouseup for cross-device support. Use event.touches array.",
                "Mobile browsers default to scrolling/pinching on touch. CSS touch-action: none disables this. Always test on a real device — emulators miss gesture nuances."),

            new("WebGL context lost", "Listen for 'webglcontextlost' and 'webglcontextrestored' events. Store critical state and re-create objects on restore. Limit WebGL calls per frame. Reduce texture sizes.",
                "WebGL context loss happens on mobile when switching tabs or on GPU overload. Handle the events gracefully rather than showing a white screen."),

            new("Memory leak — FPS drops over time", "Remove unused event listeners. Debounce animation-based updates. Clear arrays and object maps when scenes change. Use WeakMap/WeakSet for caches. Profile with Chrome DevTools Memory tab.",
                "Accumulating particles, enemies, or DOM elements without cleanup causes gradual slowdown. Detached DOM nodes and uncleared intervals are the most common sources."),

            new("CSS not loading / styles missing", "Check the link tag's href path. Use build tools (Vite, webpack) for correct asset hashing. Ensure CSS is loaded before the script runs (put link in <head>, script at end of <body>).",
                "CSS loading order matters. If the script manipulates styles before CSS loads, there will be a flash of unstyled content. Link href must be relative to the HTML file, not the JS file."),

            new("Error during build: Vite/Webpack bundle fails", "Check the terminal output for the specific error. Common fixes: clear node_modules + package-lock and reinstall, check peer dependency versions, update Node.js to LTS, check for TypeScript errors.",
                "Bundler errors are usually dependency version conflicts, TypeScript compilation failures, or incorrect config in vite.config.ts / webpack.config.js. Always start by clearing cache."),
        }
    };

    /// <summary>
    /// Best-practices guidance strings per engine, assembled from common wisdom.
    /// </summary>
    private static readonly Dictionary<string, string> _bestPractices = new()
    {
        ["unity"] = string.Join("\n",
            "• Use Object pooling instead of Instantiate/Destroy in hot paths",
            "• Store GetComponent<>() results in Awake(), never call it in Update()",
            "• Prefer [SerializeField] private fields over public for Inspector exposure",
            "• Use ScriptableObjects for shared data (items, quests, dialogue)",
            "• Batch SetActive calls; avoid enabling/disabling per-frame",
            "• Use Addressables for large asset bundles instead of Resources folder",
            "• Always check 'if (obj != null)' before accessing a Unity Object",
            "• Use ContinuousDynamic collision detection for fast-moving objects",
            "• Profile with the Profiler window before optimizing",
            "• Keep scene hierarchy flat — deep nesting hurts transform performance"
        ),

        ["godot"] = string.Join("\n",
            "• Use @export var for Inspector-exposed fields instead of public vars",
            "• Prefer signals over direct method calls for decoupled architecture",
            "• Use Resource (.tres) files for shared data; they serialize nicely in Git",
            "• Animate with AnimationTree + StateMachine for complex character motion",
            "• Use Groups (add_to_group) for finding collections of nodes",
            "• Avoid frequent get_node() — cache references in _ready()",
            "• Use @onready var for deferred node initialization",
            "• Static typing (': int', ': Node2D') catches errors at parse time",
            "• Use PackedScene.instantiate() with .add_child() for runtime spawning",
            "• Profile with the built-in Debugger and Frame Profiler tabs"
        ),

        ["html5"] = string.Join("\n",
            "• Always use delta time from requestAnimationFrame for frame-rate independence",
            "• Use Canvas for 2D rendering and WebGL/Three.js for 3D",
            "• Throttle RAF to 30 FPS on mobile for battery savings when full 60 FPS is unnecessary",
            "• Listen for 'visibilitychange' to pause game loop when tab is hidden",
            "• Prefer IndexedDB over localStorage for saves larger than 100KB",
            "• Bundle with Vite for fast HMR and sensible asset hashing defaults",
            "• Use event delegation rather than per-element listeners for UI",
            "• Implement an LRU cache for assets loaded via fetch()",
            "• Use Web Workers for heavy computation to avoid blocking the main thread",
            "• Keep a memory budget: unload and nullify resources when changing scenes"
        )
    };

    /// <summary>
    /// Look up fixes for an error message by engine. Uses case-insensitive
    /// fuzzy matching — returns any entry whose pattern is a substring of
    /// <paramref name="errorText"/> or vice versa.
    /// </summary>
    public static List<ErrorFix> Lookup(string engine, string errorText)
    {
        var result = new List<ErrorFix>();
        if (string.IsNullOrEmpty(errorText)) return result;

        if (!_knowledge.TryGetValue(engine.ToLowerInvariant(), out var entries))
            return result;

        var lower = errorText.ToLowerInvariant();

        foreach (var entry in entries)
        {
            var pattern = entry.ErrorPattern.ToLowerInvariant();
            // Exact match → highest priority first
            if (lower.Contains(pattern) || pattern.Contains(lower))
                result.Add(entry);
        }

        return result;
    }

    /// <summary>
    /// Returns best-practices guidance for a given engine.
    /// </summary>
    public static string GetBestPractices(string engine)
    {
        return _bestPractices.TryGetValue(engine.ToLowerInvariant(), out var text)
            ? text
            : "No best practices available for '" + engine + "'. Supported engines: unity, godot, html5.";
    }

    /// <summary>
    /// Returns all error-fix entries for a given engine.
    /// </summary>
    public static IReadOnlyList<ErrorFix> GetAll(string engine)
    {
        return _knowledge.TryGetValue(engine.ToLowerInvariant(), out var entries)
            ? entries.AsReadOnly()
            : Array.Empty<ErrorFix>();
    }

    /// <summary>
    /// Returns the total count of error entries across all engines.
    /// </summary>
    public static int TotalEntryCount =>
        _knowledge.Values.Sum(list => list.Count);
}