using System.Text.Json;
using System.Text.RegularExpressions;
using Jint;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Headless RUNTIME playtest for HTML5 games: actually executes the game's
/// inline scripts in Jint against a stubbed DOM/canvas/audio environment and
/// pumps the requestAnimationFrame loop for a couple of seconds of simulated
/// frames. Catches the failure class parsing can't see - ReferenceErrors,
/// null derefs and crashes inside the game loop that otherwise ship as a
/// game that loads and then freezes. Also flags getElementById calls whose
/// id appears nowhere in the document (classic id-drift) and alert() usage
/// (which blocks the loop). No browser, no Node.js - runs on any Worker.
/// </summary>
public sealed class GameRuntimeSmokeTester
{
    public sealed record SmokeResult(
        bool LoadedCleanly,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings,
        int FramesPumped);

    private static readonly Regex IdAttribute = new(
        "id\\s*=\\s*['\"]?([A-Za-z0-9_-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Explicit stubs rather than Proxy magic: predictable, debuggable, and a
    // missing API shows up as a clear TypeError naming the member to add here.
    private const string DomShim = """
        (function(){
        var g = (typeof globalThis !== 'undefined') ? globalThis : this;
        g.__errors = []; g.__warnings = []; g.__requestedIds = []; g.__raf = [];
        var NOOP = function(){};
        function ctx2d(){ return {
          fillStyle:'', strokeStyle:'', lineWidth:1, font:'', globalAlpha:1,
          textAlign:'', textBaseline:'', imageSmoothingEnabled:true,
          shadowBlur:0, shadowColor:'', lineCap:'', lineJoin:'',
          fillRect:NOOP, clearRect:NOOP, strokeRect:NOOP, rect:NOOP,
          beginPath:NOOP, closePath:NOOP, arc:NOOP, ellipse:NOOP,
          fill:NOOP, stroke:NOOP, moveTo:NOOP, lineTo:NOOP,
          quadraticCurveTo:NOOP, bezierCurveTo:NOOP,
          save:NOOP, restore:NOOP, translate:NOOP, rotate:NOOP, scale:NOOP,
          setTransform:NOOP, resetTransform:NOOP, clip:NOOP,
          fillText:NOOP, strokeText:NOOP, drawImage:NOOP, putImageData:NOOP,
          getImageData:function(){ return { data: [] }; },
          createImageData:function(){ return { data: [] }; },
          measureText:function(){ return { width: 0 }; },
          createLinearGradient:function(){ return { addColorStop: NOOP }; },
          createRadialGradient:function(){ return { addColorStop: NOOP }; },
          createPattern:function(){ return {}; }
        }; }
        function makeEl(id){
          var el = {
            id: id || '', tagName: 'DIV', textContent: '', innerHTML: '', value: '',
            width: 0, height: 0, offsetWidth: 800, offsetHeight: 600,
            checked: false, disabled: false, hidden: false, dataset: {},
            style: {}, parentElement: null, children: [],
            classList: { add:NOOP, remove:NOOP, toggle:NOOP, contains:function(){ return false; } },
            appendChild: function(x){ if (x) x.parentElement = el; el.children.push(x); return x; },
            removeChild: function(x){ return x; }, remove: NOOP,
            addEventListener: NOOP, removeEventListener: NOOP,
            setAttribute: NOOP, getAttribute: function(){ return null; },
            focus: NOOP, blur: NOOP, click: NOOP,
            getContext: function(){ return ctx2d(); },
            querySelector: function(){ return makeEl(''); },
            querySelectorAll: function(){ return []; },
            getBoundingClientRect: function(){ return { left:0, top:0, right:800, bottom:600, width:800, height:600, x:0, y:0 }; }
          };
          return el;
        }
        var byId = {};
        g.document = {
          head: makeEl('head'), body: makeEl('body'), title: '',
          documentElement: makeEl('html'),
          createElement: function(){ return makeEl(''); },
          createTextNode: function(){ return makeEl(''); },
          getElementById: function(id){
            id = String(id); g.__requestedIds.push(id);
            if (!byId[id]) byId[id] = makeEl(id);
            return byId[id]; },
          querySelector: function(){ return makeEl(''); },
          querySelectorAll: function(){ return []; },
          addEventListener: NOOP, removeEventListener: NOOP
        };
        g.window = g;
        g.self = g;
        g.addEventListener = NOOP; g.removeEventListener = NOOP;
        g.location = { reload: NOOP, href: '', search: '', hash: '' };
        g.navigator = { userAgent: 'ailocal-smoke', language: 'sv' };
        g.performance = { now: function(){ return 0; } };
        g.innerWidth = 800; g.innerHeight = 600; g.devicePixelRatio = 1;
        var store = {};
        g.localStorage = {
          getItem: function(k){ return (k in store) ? store[k] : null; },
          setItem: function(k, v){ store[k] = String(v); },
          removeItem: function(k){ delete store[k]; },
          clear: function(){ store = {}; } };
        g.sessionStorage = g.localStorage;
        function osc(){ return { type:'', frequency:{ value:0, setValueAtTime:NOOP }, connect:NOOP, disconnect:NOOP, start:NOOP, stop:NOOP }; }
        function gain(){ return { gain:{ value:0, setValueAtTime:NOOP, exponentialRampToValueAtTime:NOOP, linearRampToValueAtTime:NOOP }, connect:NOOP, disconnect:NOOP }; }
        g.AudioContext = function(){ return {
          currentTime: 0, state: 'running', destination: {},
          createOscillator: osc, createGain: gain,
          createBuffer: function(){ return { getChannelData: function(){ return []; } }; },
          createBufferSource: function(){ return { buffer:null, connect:NOOP, start:NOOP, stop:NOOP }; },
          resume: function(){ return { then: function(f){ if (f) f(); return this; }, catch: function(){ return this; } }; },
          suspend: NOOP, close: NOOP }; };
        g.webkitAudioContext = g.AudioContext;
        g.Audio = function(){ return { play: function(){ return { then:function(){return this;}, catch:function(){return this;} }; }, pause: NOOP, volume: 1, currentTime: 0, loop: false, src: '' }; };
        g.Image = function(){ var i = makeEl(''); i.complete = true; i.onload = null; return i; };
        g.requestAnimationFrame = function(cb){ g.__raf.push(cb); return g.__raf.length; };
        g.cancelAnimationFrame = NOOP;
        g.setTimeout = function(cb){ try { if (typeof cb === 'function') cb(); } catch(e){ g.__errors.push('setTimeout: ' + (e && e.message ? e.message : String(e))); } return 0; };
        g.clearTimeout = NOOP;
        g.setInterval = function(){ return 0; };
        g.clearInterval = NOOP;
        g.alert = function(m){ g.__warnings.push('alert() anropades (blockerar spel-loopen - anvand en overlay i stallet): ' + m); };
        g.confirm = function(){ g.__warnings.push('confirm() anropades (blockerar spel-loopen)'); return true; };
        g.prompt = function(){ return ''; };
        g.console = { log:NOOP, info:NOOP, warn:NOOP, debug:NOOP,
          error: function(m){ g.__warnings.push('console.error: ' + m); } };
        g.fetch = function(){ return { then: function(){ return this; }, catch: function(){ return this; } }; };
        g.__time = 0;
        g.__pump = function(){
          var q = g.__raf; g.__raf = []; g.__time += 16;
          for (var i = 0; i < q.length; i++){
            try { q[i](g.__time); }
            catch(e){ g.__errors.push('frame: ' + (e && e.message ? e.message : String(e))); } } };
        })();
        """;

    /// <summary>Executes every inline script block against the stub DOM, then
    /// pumps the RAF loop <paramref name="frames"/> times (16 ms each).</summary>
    public SmokeResult Run(string html, int frames = 120)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var pumped = 0;

        var engine = new Engine(options => options
            .TimeoutInterval(TimeSpan.FromSeconds(3))
            .LimitRecursion(512)
            .MaxStatements(20_000_000));

        try
        {
            engine.Execute(DomShim);
        }
        catch (Exception ex)
        {
            // Shim failure is OUR bug, never the game's - report and bail
            // without failing the game.
            return new SmokeResult(true, [], [$"(smoke-testern kunde inte initieras: {ex.Message})"], 0);
        }

        var loadedCleanly = true;
        var index = 0;
        foreach (var script in AiLocal.Core.Agent.JsSyntaxChecker.ExtractInlineScripts(html))
        {
            index++;
            try
            {
                engine.Execute(script);
            }
            catch (Exception ex)
            {
                loadedCleanly = false;
                errors.Add($"script-block {index} kraschade vid laddning: {Trim(ex.Message)}");
            }
        }

        for (var i = 0; i < frames; i++)
        {
            try
            {
                engine.Evaluate("__pump()");
                pumped++;
            }
            catch (Exception ex)
            {
                errors.Add($"frame-pump stannade: {Trim(ex.Message)}");
                break;
            }
        }

        CollectJsList(engine, "__errors", errors);
        CollectJsList(engine, "__warnings", warnings);

        // Id-drift: getElementById('typo') where the id is declared nowhere.
        // The requested id ALWAYS appears once in the html (its own call
        // site sits in an inline script), so "not found anywhere" can't
        // work. Instead: declared = id= attributes in the markup with the
        // scripts stripped; dynamically-created ids live in script string
        // literals and therefore occur >= 2 times (creation + lookup). One
        // single occurrence and no declaration = a typo'd lookup.
        try
        {
            var markupOnly = Regex.Replace(html, "<script[^>]*>.*?</script>", "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in IdAttribute.Matches(markupOnly))
                declared.Add(m.Groups[1].Value);
            var requestedJson = engine.Evaluate("JSON.stringify(__requestedIds)").AsString();
            var requested = JsonSerializer.Deserialize<List<string>>(requestedJson) ?? [];
            foreach (var id in requested.Distinct())
            {
                if (declared.Contains(id)) continue;
                var occurrences = CountOccurrences(html, id);
                if (occurrences <= 1)
                    errors.Add($"getElementById('{id}') - id:t finns inte i dokumentet (troligen stavfel)");
            }
        }
        catch { /* id analysis is best-effort */ }

        return new SmokeResult(loadedCleanly, Dedupe(errors), Dedupe(warnings), pumped);
    }

    private static void CollectJsList(Engine engine, string name, List<string> into)
    {
        try
        {
            var json = engine.Evaluate($"JSON.stringify({name})").AsString();
            foreach (var item in JsonSerializer.Deserialize<List<string>>(json) ?? [])
                into.Add(Trim(item));
        }
        catch { /* best effort */ }
    }

    private static IReadOnlyList<string> Dedupe(List<string> items) =>
        items.Distinct().Take(8).ToList();

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
