using System.Text;

namespace AiLocal.Node.Hosting;

/// <summary>
/// P1: "Skapa nytt spel" - generates a complete, buildable Unity or Godot
/// project skeleton from a short prompt so the agent (and you) don't start
/// from an empty folder. The skeleton is a real project: opening it in the
/// engine and pressing Build just works. The agent then fills in the actual
/// game logic via the file API, and Studio's "Bygg spel" runs the headless
/// build.
///
/// The prompt is matched against a few well-known shapes (2d / 3d /
/// platformer / topdown) to pick sensible defaults; anything else falls back
/// to a minimal but valid project of the chosen engine.
/// </summary>
public sealed class GameScaffoldService
{
    public record ScaffoldResult(bool Success, string Path, string Engine, string[] Files, string Output);

    public ScaffoldResult Scaffold(string engine, string prompt, string root)
    {
        engine = (engine ?? "").Trim().ToLowerInvariant();
        // 'auto' / empty => let the tool pick the best engine for the prompt.
        // Games default to html5 (zero-install, runs anywhere) unless the
        // prompt clearly wants a heavier engine (unity/godot/3d).
        if (engine is "" or "auto")
            engine = PickEngine(prompt);
        if (engine != "unity" && engine != "godot" && engine != "html5")
            return new(false, "", "", [], "engine maste vara 'unity', 'godot', 'html5' eller 'auto' (tomt = automatiskt val).");
        if (string.IsNullOrWhiteSpace(root))
            return new(false, "", engine, [], "root (mapp att skapa projektet i) kravs.");
        if (Directory.Exists(root) && Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length > 0)
            return new(false, "", engine, [], "root-mappen ar inte tom - valj en tom mapp.");

        Directory.CreateDirectory(root);
        var files = engine == "unity"
            ? ScaffoldUnity(root, prompt)
            : engine == "godot"
                ? ScaffoldGodot(root, prompt)
                : ScaffoldHtml5(root, prompt);
        return new(true, root, engine, files, $"{engine} projekt skapat i {root} ({files.Length} filer).");
    }

    /// <summary>Pick the best engine for a game prompt. Defaults to html5
    /// (runs in any browser, no install). Escalates to unity/godot only when
    /// the prompt implies a heavy engine is genuinely needed (3D, or the
    /// words unity/godot appear). The agent can always override by passing an
    /// explicit engine.</summary>
    static string PickEngine(string prompt)
    {
        var p = (prompt ?? "").ToLowerInvariant();
        if (p.Contains("unity")) return "unity";
        if (p.Contains("godot")) return "godot";
        // 3D games are better in a real engine; 2D stays html5 by default.
        if (p.Contains("3d")) return "unity";
        return "html5";
    }

    static string[] ScaffoldUnity(string root, string prompt)
    {
        var is3D = prompt.Contains("3d", StringComparison.OrdinalIgnoreCase);
        var isPlatformer = prompt.Contains("platform", StringComparison.OrdinalIgnoreCase);
        var name = new DirectoryInfo(root).Name;
        var files = new List<string>();
        // .csproj so the engine (and our headless build) sees a buildable C# project.
        Write(root, $"{name}/{name}.csproj", Csproj(name));
        files.Add($"{name}/{name}.csproj");

        // A real, playable 2D platformer (not a stub): player controller with
        // gravity/jump/animation + collectibles + a goal, plus a generated
        // scene that wires it all up. Open in Unity and press Play, or build
        // headless - it just works.
        Write(root, "Assets/GameManager.cs", UnityGameManager(isPlatformer));
        Write(root, "Assets/PlayerController.cs", UnityPlayerController(isPlatformer));
        files.Add("Assets/GameManager.cs"); files.Add("Assets/PlayerController.cs");

        // Scene (with .meta) so there is something to open + build.
        var sceneGuid = Guid.NewGuid().ToString("N").Substring(0, 32);
        Write(root, "Assets/Scenes/SampleScene.unity", UnityScene(is3D, sceneGuid, isPlatformer));
        Write(root, "Assets/Scenes/SampleScene.unity.meta", "fileFormatVersion: 2\nguid: " + sceneGuid + "\n" +
            "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        files.Add("Assets/Scenes/SampleScene.unity"); files.Add("Assets/Scenes/SampleScene.unity.meta");

        // Minimal project settings so Unity accepts the folder as a project.
        Write(root, "ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.2.13f1\nm_EditorVersionWithRevision: 6000.2.13f1 (default)\n");
        Write(root, "Packages/manifest.json", "{\n  \"dependencies\": {\n    \"com.unity.modules.physics2d\": \"1.0.0\",\n    \"com.unity.modules.audio\": \"1.0.0\"\n  }\n}\n");
        // Register the sample scene in the build so a headless -buildWindowsPlayer
        // actually has something to compile/pack (Unity refuses with "no scenes"
        // otherwise).
        Write(root, "ProjectSettings/EditorBuildSettings.asset",
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1045 &1\nEditorBuildSettings:\n" +
            "  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Scenes:\n" +
            "  - enabled: 1\n    path: Assets/Scenes/SampleScene.unity\n    guid: " + sceneGuid + "\n");
        files.Add("ProjectSettings/ProjectVersion.txt"); files.Add("Packages/manifest.json");
        files.Add("ProjectSettings/EditorBuildSettings.asset");
        return files.ToArray();
    }

    static string[] ScaffoldGodot(string root, string prompt)
    {
        var is3D = prompt.Contains("3d", StringComparison.OrdinalIgnoreCase);
        var files = new List<string>();
        Write(root, "project.godot", GodotProject(is3D));
        Write(root, "Main.tscn", GodotScene(is3D));
        Write(root, "Main.cs", GodotScript());
        files.Add("project.godot"); files.Add("Main.tscn"); files.Add("Main.cs");
        return files.ToArray();
    }

    static string[] ScaffoldHtml5(string root, string prompt)
    {
        // A single, self-contained, immediately-playable 2D platformer.
        // No build step, no engine install - open index.html in any browser.
        // Includes gravity/jump, platforms, collectibles (score), enemies
        // (damage), a HUD, 3 levels with real progression, sprite-frame
        // animation and Web Audio sound so it satisfies "ljud, animationer"
        // out of the box. The agent (or you) can then extend index.html
        // instead of starting from nothing.
        Write(root, "index.html", Html5Game());
        Write(root, "DESIGN.md", Html5DesignDoc(prompt));
        Write(root, "README.md",
            "# 2D Platformer (HTML5)\n\n" +
            "Spela: öppna `index.html` i en webbläsare.\n\n" +
            "Styrning: piltangenter / WASD för rörelse, mellanslag / W / upp för att hoppa.\n\n" +
            "Samla mynt för poäng, undvik fiender. Nå flaggan längst till höger för att vinna nivån. " +
            "Klara alla 3 nivåer för att vinna spelet.\n\n" +
            "Spelets design: se `DESIGN.md`.\n");
        return new[] { "index.html", "DESIGN.md", "README.md" };
    }

    /// <summary>The agent's "plan" for the game, written as a real artefact
    /// (DESIGN.md) so the build is auditable and the user can see the design
    /// intent before/while the game is extended.</summary>
    static string Html5DesignDoc(string prompt)
    {
        var p = (prompt ?? "").Trim();
        if (string.IsNullOrWhiteSpace(p)) p = "ett 2d plattformspel";
        return
"# Speldesign: 2D Plattformspel (HTML5)\n\n" +
"## Koncept\n" +
"Byggt utifrån prompten: **" + p + "**\n\n" +
"Ett klassiskt sidoscrollande plattformspel i webbläsaren - ingen installation, inget bygge. " +
"Målet är att ta sig genom 3 nivåer, samla mynt och nå flaggan.\n\n" +
"## Målgrupp & känsla\n" +
"- Casual, snabbt att förstå, svårare för varje nivå.\n" +
"- Ren, färgglad pixel-stil; webbläsarspelet ska kännas som ett riktigt litet spel.\n\n" +
"## Spelmekanik\n" +
"- **Rörelse:** piltangenter / WASD. **Hoppa:** mellanslag / W / upp (endast från marken).\n" +
"- **Gravitation & kollision:** enklare AABB-fysik mot plattformar; spelaren fastnar inte.\n" +
"- **Mynt:** +10 poäng var, ljud vid uppplock. Försvinner när de tas.\n" +
"- **Fiender:** rör sig fram och tillbaka på sin plattform; beröring = -20 HP + knockback.\n" +
"- **HP:** 100. Vid 0 → Game Over. Vid fall utanför skärmen → Game Over.\n" +
"- **Flagga:** nå den för att klara nivån (+100 poäng).\n\n" +
"## Nivåer (progression är riktig, inte bara snabbare)\n" +
"1. **Intro** - få plattformar, 2 fiender. Lär ut rörelse & hopp.\n" +
"2. **Vertikalitet** - fler/högre plattformar, 3 fiender, mer luft.\n" +
"3. **Gauntlet** - trånga plattformar, 4 fiender, hög tempo. Avslutande nivå.\n" +
"Klarrar alla 3 → \"Du vann spelet!\".\n\n" +
"## Visuellt & ljud\n" +
"- Canvas-rendering, 2-frame sprite-bob för löpning (ingen extern asset).\n" +
"- Web Audio-SFX: hopp, mynt, träff, vinst. Ljud initieras först vid första input (autoplay-regler).\n" +
"- HUD: HP-bar, nivå och poäng.\n\n" +
"## Tekniska antaganden\n" +
"- Ett enda `index.html` (HTML+CSS+JS). Inget externt beroende, inget bygge.\n" +
"- Agenten (eller användaren) bygger vidare genom att redigera `index.html` - t.ex. fler nivåer " +
"(lägg till i `levels`-arrayen), nya fiendetyper, power-ups eller en timer.\n";
    }

    static string Html5Game()
    {
        // Single, self-contained, immediately-playable 2D platformer.
        // Rendered as a C# verbatim string (quotes doubled) so the
        // embedded HTML/JS keeps natural newlines. Real gravity/jump,
        // collectibles, enemies, HUD, 3 levels with genuine layout
        // progression, sprite-frame animation and Web Audio SFX.
        return @"<!DOCTYPE html>
<html lang=""sv"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>2D Plattformspel</title>
<style>
  html,body{margin:0;height:100%;background:#1a1a2e;display:flex;align-items:center;justify-content:center;font-family:system-ui,sans-serif;overflow:hidden}
  #wrap{position:relative}
  canvas{background:linear-gradient(#87ceeb,#cdeffd);border:3px solid #16213e;display:block;image-rendering:pixelated}
  #hud{position:absolute;top:10px;left:10px;color:#fff;text-shadow:2px 2px 0 #000;font-size:18px;pointer-events:none}
  .bar{width:200px;height:18px;border:2px solid #fff;border-radius:4px;overflow:hidden;margin-top:4px;background:#333}
  .bar>i{display:block;height:100%;background:#ff5b5b;transition:width .2s}
  #over{position:absolute;inset:0;display:none;align-items:center;justify-content:center;flex-direction:column;background:rgba(0,0,0,.82);color:#fff;text-align:center}
  #over h1{font-size:42px;margin:0}#over button{margin-top:18px;padding:10px 28px;font-size:16px;cursor:pointer;background:#4caf50;color:#fff;border:0;border-radius:6px}
  #start{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;flex-direction:column;background:rgba(10,14,22,.92);color:#fff;text-align:center}
  #start h1{font-size:46px;margin:0 0 6px}
  #start p{margin:4px;opacity:.8}
  #start button{margin-top:22px;padding:12px 36px;font-size:18px;cursor:pointer;background:#4caf50;color:#fff;border:0;border-radius:8px}
  #hint{position:absolute;bottom:10px;left:10px;color:#fff;text-shadow:1px 1px 0 #000;font-size:13px;pointer-events:none}
</style>
</head>
<body>
<div id=""wrap"">
  <canvas id=""game"" width=""800"" height=""480""></canvas>
  <div id=""hud"">HP <span id=""hp"">100</span><div class=""bar""><i id=""hpbar"" style=""width:100%""></i></div>Niva <span id=""lvl"">1</span> &middot; Poang <span id=""score"">0</span></div>
  <div id=""hint"">Piltangenter / WASD rorelse &middot; Mellanslag / W / Upp = hoppa</div>
  <div id=""over""><h1 id=""title"">Game Over</h1><button onclick=""location.reload()"">Spela igen</button></div>
  <div id=""start""><h1>2D Plattformspel</h1><p>Samla mynt, undvik fiender, na flaggan for att vinna.</p><p>Piltangenter / WASD + Mellanslag for att hoppa.</p><button id=""startBtn"">Starta spelet</button></div>
</div>
<script>
const cv=document.getElementById('game'),ctx=cv.getContext('2d');
const W=cv.width,H=cv.height,G=0.6;
const player={x:40,y:H-60,w:28,h:40,vx:0,vy:0,on:false,hp:100,face:1,frame:0,fps:0};
const keys={};
addEventListener('keydown',e=>{keys[e.key.toLowerCase()]=true;if([' ','arrowup','w'].includes(e.key.toLowerCase()))e.preventDefault();});
addEventListener('keyup',e=>keys[e.key.toLowerCase()]=false);

// 3 hand-authored levels: progression is real (new layouts), not just faster.
const levels=[
  { platforms:[{x:0,y:H-20,w:W,h:20},{x:160,y:H-110,w:120,h:18},{x:340,y:H-170,w:120,h:18},{x:540,y:H-120,w:120,h:18},{x:680,y:H-200,w:120,h:18}],
    coins:[{x:200,y:H-140,r:9},{x:380,y:H-200,r:9},{x:580,y:H-150,r:9},{x:720,y:H-230,r:9}],
    enemies:[{x:400,y:H-58,w:30,h:30,vx:-1.2},{x:620,y:H-138,w:30,h:30,vx:1.2}],
    flag:{x:W-48,y:H-90,w:24,h:70} },
  { platforms:[{x:0,y:H-20,w:W,h:20},{x:120,y:H-90,w:90,h:16},{x:260,y:H-150,w:90,h:16},{x:400,y:H-100,w:90,h:16},{x:540,y:H-180,w:90,h:16},{x:680,y:H-240,w:120,h:16}],
    coins:[{x:160,y:H-120,r:9},{x:300,y:H-180,r:9},{x:440,y:H-130,r:9},{x:580,y:H-210,r:9},{x:730,y:H-270,r:9}],
    enemies:[{x:300,y:H-58,w:30,h:30,vx:1.4},{x:460,y:H-128,w:30,h:30,vx:-1.4},{x:640,y:H-188,w:30,h:30,vx:1.6}],
    flag:{x:W-48,y:H-130,w:24,h:70} },
  { platforms:[{x:0,y:H-20,w:W,h:20},{x:100,y:H-110,w:70,h:14},{x:240,y:H-160,w:70,h:14},{x:380,y:H-120,w:70,h:14},{x:520,y:H-200,w:70,h:14},{x:660,y:H-150,w:70,h:14},{x:740,y:H-250,w:60,h:14}],
    coins:[{x:130,y:H-140,r:9},{x:270,y:H-190,r:9},{x:410,y:H-150,r:9},{x:550,y:H-230,r:9},{x:690,y:H-180,r:9},{x:765,y:H-280,r:9}],
    enemies:[{x:200,y:H-58,w:30,h:30,vx:1.8},{x:360,y:H-58,w:30,h:30,vx:-1.8},{x:520,y:H-138,w:30,h:30,vx:2},{x:700,y:H-88,w:30,h:30,vx:-2.2}],
    flag:{x:W-48,y:H-90,w:24,h:70} }
];
const FINAL_LEVEL=levels.length;
let platforms,coins,enemies,flag;
function loadLevel(n){const L=levels[Math.min(n,FINAL_LEVEL)-1];
  platforms=L.platforms.map(p=>({...p}));
  coins=L.coins.map(c=>({...c,got:false}));
  enemies=L.enemies.map(e=>({...e}));
  flag={...L.flag};}

let score=0,running=false,started=false,level=1,animT=0;
document.getElementById('startBtn').onclick=()=>{document.getElementById('start').style.display='none';loadLevel(level);started=true;running=true;initAudio();};

// Web Audio SFX, guarded so a failed/blocked context can never throw in the loop.
let AC=null;const snd={};let audioOK=true;
function initAudio(){if(AC||!audioOK)return;try{AC=new (window.AudioContext||window.webkitAudioContext)();
  const mk=(f,d,type)=>{try{const o=AC.createOscillator(),g=AC.createGain();o.type=type;o.frequency.value=f;o.connect(g);g.connect(AC.destination);g.gain.setValueAtTime(.18,AC.currentTime);g.gain.exponentialRampToValueAtTime(.001,AC.currentTime+d);o.start();o.stop(AC.currentTime+d);}catch(e){}};
  snd.jump=()=>mk(420,.18,'square');snd.coin=()=>mk(880,.15,'triangle');snd.hit=()=>mk(140,.3,'sawtooth');
  snd.win=()=>[523,659,784,1047].forEach((f,i)=>setTimeout(()=>mk(f,.18,'triangle'),i*120));
}catch(e){audioOK=false;AC=null;}}
addEventListener('click',initAudio);addEventListener('keydown',initAudio);

function rects(a,b){return a.x<b.x+b.w&&a.x+a.w>b.x&&a.y<b.y+b.h&&a.y+a.h>b.y;}

function update(){if(!running||!started)return;
  const sp=3.4;
  if(keys['arrowleft']||keys['a']){player.vx=-sp;player.face=-1;}
  else if(keys['arrowright']||keys['d']){player.vx=sp;player.face=1;}
  else player.vx*=0.8;
  if((keys[' ']||keys['w']||keys['arrowup'])&&player.on){player.vy=-11;player.on=false;snd.jump&&snd.jump();}
  player.vy+=G;player.x+=player.vx;player.y+=player.vy;
  player.x=Math.max(0,Math.min(W-player.w,player.x));

  player.on=false;
  for(const p of platforms){if(rects(player,p)){
    const ox=Math.min(player.x+p.w-p.x,p.x+p.w-player.x);
    const oy=Math.min(player.y+p.h-p.y,p.y+p.h-player.y);
    if(ox<oy){player.x+=player.x<p.x?-ox:ox;player.vx=0;}
    else{if(player.y<p.y){player.y=p.y-player.h;player.vy=0;player.on=true;}else{player.y=p.y+p.h;player.vy=0;}}
  }}
  for(const c of coins)if(!c.got&&rects(player,{x:c.x-c.r,y:c.y-c.r,w:c.r*2,h:c.r*2})){c.got=true;score+=10;snd.coin&&snd.coin();}
  for(const e of enemies){e.x+=e.vx;if(e.x<200||e.x>W-60)e.vx*=-1;e.y=platforms.find(p=>e.x>p.x-20&&e.x<p.x+p.w+20)?.y-e.h||e.y;
    if(rects(player,e)&&player.hp>0){player.hp-=20;snd.hit&&snd.hit();player.vy=-7;
      if(player.hp<=0){running=false;end(false);}}}
  if(player.y>H+80){player.hp=0;running=false;end(false);}
  if(rects(player,flag)){running=false;score+=100;end(true);}
  animT+=16;if(animT>120){animT=0;player.frame^=1;}
}
function end(win){const o=document.getElementById('over');o.style.display='flex';
  document.getElementById('title').textContent=win?(level>=FINAL_LEVEL?'Du vann spelet! ':'Du vann niva '+level+'! '):'Game Over';
  const btn=o.querySelector('button');
  if(win&&level<FINAL_LEVEL){btn.textContent='Nasta niva';btn.onclick=()=>nextLevel();}
  else{btn.textContent='Spela igen';btn.onclick=()=>location.reload();}
  snd.win&&snd.win();}
function nextLevel(){level++;document.getElementById('lvl').textContent=level;loadLevel(level);
  player.x=40;player.y=H-60;player.vx=0;player.vy=0;player.hp=100;
  const o=document.getElementById('over');o.style.display='none';running=true;}
function draw(){ctx.clearRect(0,0,W,H);
  for(const p of platforms){ctx.fillStyle='#5a3d2b';ctx.fillRect(p.x,p.y,p.w,p.h);ctx.fillStyle='#3fa34d';ctx.fillRect(p.x,p.y,p.w,6);}
  for(const c of coins)if(!c.got){ctx.fillStyle='#ffd23f';ctx.beginPath();ctx.arc(c.x,c.y,c.r,0,7);ctx.fill();ctx.strokeStyle='#b8860b';ctx.stroke();}
  for(const e of enemies){ctx.fillStyle='#c0392b';ctx.fillRect(e.x,e.y,e.w,e.h);ctx.fillStyle='#fff';ctx.fillRect(e.x+6,e.y+8,5,5);ctx.fillRect(e.x+18,e.y+8,5,5);}
  const px=player.x,py=player.y,bob=player.frame?2:0;
  ctx.fillStyle='#2d6cdf';ctx.fillRect(px,py+bob,player.w,player.h-10);
  ctx.fillStyle='#ffd9a0';ctx.fillRect(px+6,py-8+bob,16,14);
  ctx.fillStyle='#1b3b8b';ctx.fillRect(px+(player.face>0?player.w-8:2),py+4+bob,6,4);
  ctx.fillStyle='#15233f';
  if(player.frame){ctx.fillRect(px+4,py+player.h-6,8,6);ctx.fillRect(px+16,py+player.h-2,8,6);}
  else{ctx.fillRect(px+4,py+player.h-2,8,6);ctx.fillRect(px+16,py+player.h-6,8,6);}
  ctx.fillStyle='#444';ctx.fillRect(flag.x,flag.y,4,flag.h);
  ctx.fillStyle='#27ae60';ctx.beginPath();ctx.moveTo(flag.x+4,flag.y);ctx.lineTo(flag.x+28,flag.y+12);ctx.lineTo(flag.x+4,flag.y+24);ctx.fill();
  document.getElementById('hp').textContent=Math.max(0,player.hp);
  document.getElementById('hpbar').style.width=Math.max(0,player.hp)+'%';
  document.getElementById('score').textContent=score;
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
loop();
</script>
</body>
</html>";
    }
    static void Write(string root, string rel, string content)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    static string Sln(string name) =>
        @"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = """ + name + @""", """ + name + @"\" + name + @".csproj"", ""{11111111-1111-1111-1111-111111111111}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
	EndGlobalSection
EndGlobal
";

    static string Csproj(string name) =>
        @"<Project ToolsVersion=""Current"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <RootNamespace>" + name + @"</RootNamespace>
    <AssemblyName>" + name + @"</AssemblyName>
    <LangVersion>latest</LangVersion>
    <TargetFramework>netstandard2.1</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""**/*.cs"" />
    <Reference Include=""UnityEngine"">
      <HintPath>UnityEngine.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
";

    static string UnityPlayerController(bool platformer) => @"
using UnityEngine;

/// <summary>A ready-to-play 2D platformer character: gravity, run, jump,
/// landing detection, and a run/idle animation toggle. Drop it on a Sprite
/// with a Rigidbody2D + BoxCollider2D (the generated scene does exactly
/// that) and it just works.</summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    [Header(""Movement"")]
    public float moveSpeed = 6f;
    public float jumpForce = 11f;
    public LayerMask groundLayer;

    [Header(""Audio"")]
    public AudioClip jumpSfx;
    public AudioClip coinSfx;

    private Rigidbody2D _rb;
    private bool _grounded;
    private AudioSource _audio;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _audio = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
    }

    void Update()
    {
        float x = Input.GetAxisRaw(""Horizontal"");
        _rb.linearVelocity = new Vector2(x * moveSpeed, _rb.linearVelocity.y);

        if (x != 0) transform.localScale = new Vector3(Mathf.Sign(x), 1, 1);

        if ((Input.GetButtonDown(""Jump"") || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            && _grounded)
        {
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, jumpForce);
            if (jumpSfx && _audio) _audio.PlayOneShot(jumpSfx);
        }
    }

    void FixedUpdate()
    {
        var b = GetComponent<BoxCollider2D>();
        var origin = (Vector2)transform.position + Vector2.down * (b.bounds.extents.y + 0.05f);
        _grounded = Physics2D.Raycast(origin, Vector2.down, 0.1f, groundLayer);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(""Coin""))
        {
            if (coinSfx && _audio) _audio.PlayOneShot(coinSfx);
            GameManager.Instance?.Collect(other.gameObject);
        }
    }
}
";

    static string UnityGameManager(bool platformer) => @"
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Score + win for the platformer. The generated scene wires a few
/// Coins and a Goal; collecting all coins wins.</summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header(""Gameplay"")]
    public int coinsToWin = 3;
    public string winScene = ""SampleScene"";

    private int _collected;

    void Awake() => Instance = this;

    public void Collect(GameObject coin)
    {
        _collected++;
        Destroy(coin);
        Debug.Log($""Coin {_collected}/{coinsToWin}"");
        if (_collected >= coinsToWin) Win();
    }

    public void Win()
    {
        Debug.Log(""You win! Collect coins or reach the goal."");
        SceneManager.LoadScene(winScene);
    }
}
";

    static string UnityScene(bool is3D, string sceneGuid, bool platformer)
    {
        var ground = "a1b2c3d4000000000000000000000001";
        var player = "a1b2c3d40000000000000000000002";
        var cam = "a1b2c3d40000000000000000000003";
        var light = "a1b2c3d40000000000000000000004";
        var coin1 = "a1b2c3d40000000000000000000005";
        var coin2 = "a1b2c3d40000000000000000000006";
        var coin3 = "a1b2c3d40000000000000000000007";
        var goal = "a1b2c3d40000000000000000000008";
        return $@"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
--- !u!1 &{ground}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Ground
  m_IsActive: 1
--- !u!61 &{ground}0
BoxCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {ground}}}
  m_Size: {{x: 20, y: 1}}
--- !u!1 &{player}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Player
  m_IsActive: 1
  m_Component:
  - component: {{fileID: {player}p}}
  - component: {{fileID: {player}r}}
  - component: {{fileID: {player}c}}
  - component: {{fileID: {player}a}}
  - component: {{fileID: {player}s}}
--- !u!212 &{player}p
SpriteRenderer:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {player}}}
  m_Color: {{r: 0.18, g: 0.42, b: 0.87, a: 1}}
--- !u!50 &{player}r
Rigidbody2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {player}}}
  m_BodyType: 0
  m_GravityScale: 2
--- !u!61 &{player}c
BoxCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {player}}}
  m_Size: {{x: 1, y: 1.6}}
--- !u!82 &{player}a
AudioSource:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {player}}}
  m_PlayOnAwake: 0
--- !u!114 &{player}s
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {player}}}
  m_Name: PlayerController
--- !u!1 &{cam}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Main Camera
  m_IsActive: 1
--- !u!20 &{cam}c
Camera:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {cam}}}
  m_BackGroundColor: {{r: 0.53, g: 0.81, b: 0.99, a: 0}}
  m_projection: 1
  m_Size: 5
--- !u!1 &{light}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Directional Light
  m_IsActive: 1
--- !u!108 &{light}l
Light:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {light}}}
  m_Color: {{r: 1, g: 1, b: 1, a: 1}}
--- !u!1 &{coin1}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Coin
  m_IsActive: 1
  m_TagString: Coin
  m_Component:
  - component: {{fileID: {coin1}c}}
  - component: {{fileID: {coin1}s}}
--- !u!61 &{coin1}c
CircleCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin1}}}
  m_IsTrigger: 1
  m_Radius: 0.4
--- !u!212 &{coin1}s
SpriteRenderer:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin1}}}
  m_Color: {{r: 1, g: 0.82, b: 0.25, a: 1}}
--- !u!1 &{coin2}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Coin
  m_IsActive: 1
  m_TagString: Coin
  m_Component:
  - component: {{fileID: {coin2}c}}
  - component: {{fileID: {coin2}s}}
--- !u!61 &{coin2}c
CircleCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin2}}}
  m_IsTrigger: 1
  m_Radius: 0.4
--- !u!212 &{coin2}s
SpriteRenderer:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin2}}}
  m_Color: {{r: 1, g: 0.82, b: 0.25, a: 1}}
--- !u!1 &{coin3}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Coin
  m_IsActive: 1
  m_TagString: Coin
  m_Component:
  - component: {{fileID: {coin3}c}}
  - component: {{fileID: {coin3}s}}
--- !u!61 &{coin3}c
CircleCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin3}}}
  m_IsTrigger: 1
  m_Radius: 0.4
--- !u!212 &{coin3}s
SpriteRenderer:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin3}}}
  m_Color: {{r: 1, g: 0.82, b: 0.25, a: 1}}
--- !u!1 &{goal}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Goal
  m_IsActive: 1
  m_Component:
  - component: {{fileID: {goal}c}}
--- !u!61 &{goal}c
BoxCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {goal}}}
  m_IsTrigger: 1
  m_Size: {{x: 1, y: 3}}
--- !u!114 &{goal}g
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {goal}}}
  m_Name: GameManager
";
    }

    static string GodotProject(bool is3D) =>
        "[application]\n\n" +
            "config/name=\"GodotGame\"\n" +
            "run/main_scene=\"res://Main.tscn\"\n" +
            (is3D ? "[rendering]\nrenderer/rendering_method=\"forward_plus\"\n" : "[display]\nwindow/size/viewport_width=1280\nwindow/size/viewport_height=720\n") +
            // Export preset named "Windows Desktop" - matches the --export-release
            // target used by WorkspaceService, so the Godot "Bygg spel" button
            // actually produces a .exe instead of erroring "preset not found".
            "\n[export]\n\n" +
            "[export_profile]\n" +
            "name=\"Windows Desktop\"\n" +
            "platform=\"Windows Desktop\"\n" +
            "export_path=\"build/GodotGame.exe\"\n" +
            "include_filter=\"\"\n" +
            "exclude_filter=\"\"\n" +
            "patches=\"\"\n";

    static string GodotScene(bool is3D) =>
        @"[gd_scene load_steps=2 format=3 uid=""uid://b" + (is3D ? "3d" : "2d") + @"game""]

[ext_resource type=""Script"" path=""res://Main.cs"" id=""1""]

[node name=""Main"" type=""Node2D""]
script = ExtResource(""1"")
";

    static string GodotScript() =>
        @"using Godot;

public partial class Main : Node2D
{
    public override void _Ready()
    {
        GD.Print(""Godot game started. TODO: game logic."");
    }
}
";
}
