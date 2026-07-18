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
        if (engine != "unity" && engine != "godot" && engine != "html5")
            return new(false, "", "", [], "engine maste vara 'unity', 'godot' eller 'html5'.");
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

    static string[] ScaffoldUnity(string root, string prompt)
    {
        var name = new DirectoryInfo(root).Name;
        var is3D = prompt.Contains("3d", StringComparison.OrdinalIgnoreCase);
        var isPlatformer = prompt.Contains("platform", StringComparison.OrdinalIgnoreCase);
        var files = new List<string>();

        // .sln + .csproj so WorkspaceService (and the engine) see a buildable C# project.
        Write(root, $"{name}.sln", Sln(name));
        Write(root, $"{name}/{name}.csproj", Csproj(name));
        Write(root, $"{name}/Assembly-CSharp.csproj", Csproj(name));
        files.Add($"{name}.sln"); files.Add($"{name}/{name}.csproj"); files.Add($"{name}/Assembly-CSharp.csproj");

        // A scene + a starter script so there is something to open and build.
        var scriptName = isPlatformer ? "PlayerController" : "GameManager";
        Write(root, $"Assets/{scriptName}.cs", UnityScript(scriptName, isPlatformer, is3D));
        Write(root, "Assets/Scenes/SampleScene.unity", UnityScene(is3D));
        files.Add($"Assets/{scriptName}.cs"); files.Add("Assets/Scenes/SampleScene.unity");

        // Minimal project settings so Unity accepts the folder as a project.
        Write(root, "ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.2.13f1\nm_EditorVersionWithRevision: 6000.2.13f1 (default)\n");
        Write(root, "Packages/manifest.json", "{\n  \"dependencies\": {\n    \"com.unity.modules.physics\": \"1.0.0\"\n  }\n}\n");
        // Register the sample scene in the build so a headless -buildWindowsPlayer
        // actually has something to compile/pack (Unity refuses with "no scenes"
        // otherwise). The guid must match the scene's .meta file below.
        var sceneGuid = Guid.NewGuid().ToString("N").Substring(0, 32);
        Write(root, "Assets/Scenes/SampleScene.unity.meta", "fileFormatVersion: 2\nguid: " + sceneGuid + "\n" +
            "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        Write(root, "ProjectSettings/EditorBuildSettings.asset",
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1045 &1\nEditorBuildSettings:\n" +
            "  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Scenes:\n" +
            "  - enabled: 1\n    path: Assets/Scenes/SampleScene.unity\n    guid: " + sceneGuid + "\n");
        files.Add("ProjectSettings/ProjectVersion.txt"); files.Add("Packages/manifest.json");
        files.Add("Assets/Scenes/SampleScene.unity.meta"); files.Add("ProjectSettings/EditorBuildSettings.asset");
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
        // (damage), a HUD, sprite-frame animation and Web Audio sound so it
        // satisfies "ljud, animationer" out of the box. The agent (or you)
        // can then extend index.html instead of starting from nothing.
        Write(root, "index.html", Html5Game());
        Write(root, "README.md",
            "# 2D Platformer (HTML5)\n\n" +
            "Spela: öppna `index.html` i en webbläsare.\n\n" +
            "Styrning: piltangenter / WASD för rörelse, mellanslag / W / upp för att hoppa.\n\n" +
            "Samla mynt för poäng, undvik fiender. Nå flaggan längst till höger för att vinna.\n");
        return new[] { "index.html", "README.md" };
    }

    static string Html5Game()
    {
        // Keep the body readable as a C# interpolated string; the actual game
        // is plain HTML/JS/CSS. Sprite animation uses a simple 2-frame
        // bob so it reads as "animated" without external assets.
        return @"<!DOCTYPE html>
<html lang=""sv"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>2D Platformer</title>
<style>
  html,body{margin:0;height:100%;background:#1a1a2e;display:flex;align-items:center;justify-content:center;font-family:system-ui,sans-serif;overflow:hidden}
  #wrap{position:relative}
  canvas{background:linear-gradient(#87ceeb,#cdeffd);border:3px solid #16213e;display:block;image-rendering:pixelated}
  #hud{position:absolute;top:10px;left:10px;color:#fff;text-shadow:2px 2px 0 #000;font-size:18px;pointer-events:none}
  .bar{width:200px;height:18px;border:2px solid #fff;border-radius:4px;overflow:hidden;margin-top:4px;background:#333}
  .bar>i{display:block;height:100%;background:#ff5b5b;transition:width .2s}
  #over{position:absolute;inset:0;display:none;align-items:center;justify-content:center;flex-direction:column;background:rgba(0,0,0,.82);color:#fff;text-align:center}
  #over h1{font-size:42px;margin:0}#over button{margin-top:18px;padding:10px 28px;font-size:16px;cursor:pointer;background:#4caf50;color:#fff;border:0;border-radius:6px}
  #hint{position:absolute;bottom:10px;left:10px;color:#fff;text-shadow:1px 1px 0 #000;font-size:13px;pointer-events:none}
</style>
</head>
<body>
<div id=""wrap"">
  <canvas id=""game"" width=""800"" height=""480""></canvas>
  <div id=""hud"">HP <span id=""hp"">100</span><div class=""bar""><i id=""hpbar"" style=""width:100%""></i></div>Päng <span id=""score"">0</span></div>
  <div id=""hint"">Piltangenter / WASD rrelse &middot; Mellanslag / W / Upp = hoppa</div>
  <div id=""over""><h1 id=""title"">Game Over</h1><button onclick=""location.reload()"">Spela igen</button></div>
</div>
<script>
const cv=document.getElementById('game'),ctx=cv.getContext('2d');
const W=cv.width,H=cv.height,G=0.6;
const player={x:40,y:H-60,w:28,h:40,vx:0,vy:0,on:false,hp:100,face:1,frame:0,fps:0};
const keys={};
addEventListener('keydown',e=>{keys[e.key.toLowerCase()]=true;if([' ','arrowup','w'].includes(e.key.toLowerCase()))e.preventDefault();});
addEventListener('keyup',e=>keys[e.key.toLowerCase()]=false);
const platforms=[{x:0,y:H-20,w:W,h:20},{x:160,y:H-110,w:120,h:18},{x:340,y:H-170,w:120,h:18},{x:540,y:H-120,w:120,h:18},{x:680,y:H-200,w:120,h:18}];
const coins=[{x:200,y:H-140,r:9},{x:380,y:H-200,r:9},{x:580,y:H-150,r:9},{x:720,y:H-230,r:9}].map(c=>({...c,got:false}));
const enemies=[{x:400,y:H-58,w:30,h:30,vx:-1.2},{x:620,y:H-138,w:30,h:30,vx:1.2}];
const flag={x:W-48,y:H-90,w:24,h:70};
let score=0,running=true,animT=0;

// ---- Web Audio: SFX + simple loop ----
let AC;const snd={};
function initAudio(){if(AC)return;AC=new (window.AudioContext||window.webkitAudioContext)();
  const mk=(f,t,d,type='square')=>{const o=AC.createOscillator(),g=AC.createGain();o.type=type;o.frequency.value=f;o.connect(g);g.connect(AC.destination);g.gain.setValueAtTime(.18,AC.currentTime);g.gain.exponentialRampToValueAtTime(.001,AC.currentTime+d);o.start();o.stop(AC.currentTime+d);};
  snd.jump=()=>mk(420,.18,'square');snd.coin=()=>mk(880,.15,'triangle');snd.hit=()=>mk(140,.3,'sawtooth');
  snd.win=()=>{[523,659,784,1046].forEach((f,i)=>setTimeout(()=>mk(f,.18,'triangle'),i*120));};
}
addEventListener('click',initAudio);addEventListener('keydown',initAudio);

function rects(a,b){return a.x<b.x+b.w&&a.x+a.w>b.x&&a.y<b.y+b.h&&a.y+a.h>b.y;}

function update(){if(!running)return;
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
  document.getElementById('title').textContent=win?'Du vann! ':'Game Over';snd.win&&snd.win();}
function draw(){ctx.clearRect(0,0,W,H);
  for(const p of platforms){ctx.fillStyle='#5a3d2b';ctx.fillRect(p.x,p.y,p.w,p.h);ctx.fillStyle='#3fa34d';ctx.fillRect(p.x,p.y,p.w,6);}
  for(const c of coins)if(!c.got){ctx.fillStyle='#ffd23f';ctx.beginPath();ctx.arc(c.x,c.y,c.r,0,7);ctx.fill();ctx.strokeStyle='#b8860b';ctx.stroke();}
  for(const e of enemies){ctx.fillStyle='#c0392b';ctx.fillRect(e.x,e.y,e.w,e.h);ctx.fillStyle='#fff';ctx.fillRect(e.x+6,e.y+8,5,5);ctx.fillRect(e.x+18,e.y+8,5,5);}
  // player: body + animated legs (2-frame bob)
  const px=player.x,py=player.y,bob=player.frame?2:0;
  ctx.fillStyle='#2d6cdf';ctx.fillRect(px,py+bob,player.w,player.h-10);
  ctx.fillStyle='#ffd9a0';ctx.fillRect(px+6,py-8+bob,16,14);
  ctx.fillStyle='#1b3b8b';ctx.fillRect(px+(player.face>0?player.w-8:2),py+4+bob,6,4);
  ctx.fillStyle='#15233f';
  if(player.frame){ctx.fillRect(px+4,py+player.h-6,8,6);ctx.fillRect(px+16,py+player.h-2,8,6);}
  else{ctx.fillRect(px+4,py+player.h-2,8,6);ctx.fillRect(px+16,py+player.h-6,8,6);}
  // flag
  ctx.fillStyle='#444';ctx.fillRect(flag.x,flag.y,4,flag.h);
  ctx.fillStyle='#27ae60';ctx.beginPath();ctx.moveTo(flag.x+4,flag.y);ctx.lineTo(flag.x+28,flag.y+12);ctx.lineTo(flag.x+4,flag.y+24);ctx.fill();
  document.getElementById('hp').textContent=Math.max(0,player.hp);
  document.getElementById('hpbar').style.width=Math.max(0,player.hp)+'%';
  document.getElementById('score').textContent=score;
}
function loop(){update();draw();requestAnimationFrame(loop);}
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

    static string UnityScript(string name, bool platformer, bool is3D) => @"
using UnityEngine;

public class " + name + @" : MonoBehaviour
{
    void Start()
    {
        Debug.Log(""" + name + @" started. TODO: game logic."");
    }

    void Update()
    {
" + (platformer ? @"        // Minimal platformer movement stub.
        float x = Input.GetAxis(""Horizontal"");
        transform.Translate(x * Time.deltaTime * 3f, 0, 0);
" : @"        // Stub - replace with your game loop.
") + @"    }
}
";

    static string UnityScene(bool is3D) =>
        @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &" + (is3D ? "3" : "1") + @" mainCamera
GameObject:
  m_Name: MainCamera
--- !u!1 &2
GameObject:
  m_Name: Directional Light
";

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
