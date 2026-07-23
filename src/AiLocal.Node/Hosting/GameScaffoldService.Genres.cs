namespace AiLocal.Node.Hosting;

/// <summary>Genre extensions for GameScaffoldService — 5 playable HTML5 game
/// types beyond the default platformer, each shipped at the production bar:
/// title screen, pause, game over/win overlays with restart, WebAudio SFX,
/// persistent highscore and entity animation. All share one production kit
/// (<see cref="ProductionKitJs"/>) so the polish is identical everywhere and
/// a fix lands in every genre at once.</summary>
public partial class GameScaffoldService
{
    /// <summary>Detect the game genre from prompt keywords. RPG is matched
    /// LAST of the five: its "top-down"/"adventure" keywords describe camera/
    /// flavour that other genres share ("en top-down shooter" is a shooter),
    /// so the more specific genres get first pick.</summary>
    internal static string DetectGenre(string prompt) =>
        MatchGenre((prompt ?? "").ToLowerInvariant()) ?? "platformer";

    /// <summary>Whether a build request is for a GAME rather than a plain
    /// app/tool. The pre-scaffold AND the quality gate both hinge on this:
    /// without it a genre-named prompt with no literal "spel"/"game"
    /// ("Football Manager Tycoon", "en roguelike") fell through to the app
    /// scaffolder and the agent shipped a C# console app that the gate then
    /// waved through - not a playable game at all.</summary>
    internal static bool LooksLikeGame(string prompt)
    {
        var p = (prompt ?? "").ToLowerInvariant();
        if (p.Contains("spel") || p.Contains("game") || p.Contains("godot") || p.Contains("unity"))
            return true;
        // Platformer/metroidvania live only as DetectGenre's fallback, so name
        // them explicitly here (an English "a platformer" has no other signal).
        if (WordStart(p, "platformer", "plattformar", "plattforms", "metroidvania"))
            return true;
        // Any specific genre keyword (tycoon, roguelike, football manager, ...).
        return MatchGenre(p) is not null;
    }

    /// <summary>The specific genre a prompt names, or null when nothing matched
    /// (DetectGenre then defaults to platformer). Split out so LooksLikeGame can
    /// tell "named a game genre" apart from "no game signal at all".</summary>
    private static string? MatchGenre(string p)
    {
        // Word-START matching, not raw substring: "plattfORMsspel" must not
        // trigger the snake keyword "orm", "moBIL" must not trigger "bil".
        // Word-start (not whole-word) so Swedish compounds still hit:
        // "ormspel" -> orm, "bilspel" -> bil. "car" stays whole-word so
        // "card"/"cards" never reads as racing.
        if (WordStart(p, "snake", "orm", "nokia")) return "snake";
        if (WordStart(p, "idle", "clicker", "klicker", "klickspel", "incremental", "cookie")) return "idle";
        if (WordStart(p, "breakout", "arkanoid", "brick", "tegel", "paddle", "pong")) return "breakout";
        // v1.98: artilleri/duell (ShellShock Live/Worms-klassen) - turbaserad
        // ballistik mot AI. "tanks" i plural (WordStart "tank" hade träffat
        // "tanke"); "worms" krockar inte med orm-regeln (WordStart).
        if (WordStart(p, "artilleri", "artillery", "shellshock", "worms", "tanks", "stridsvagn", "ballistik", "kanonad")) return "artillery";
        // v2.1.0: party/bradspel (Mario Party-klassen) - sammansatt spel med
        // flera minispel och bradlage. Anvander bara fras-matchning for
        // "party" (WordStart "party" ar for brett - "party-tema" triggar
        // felaktigt). "minigame"/"minispel" for svenska/engelska varianter.
        if (p.Contains("mario party") || p.Contains("party game") || p.Contains("board game")
            || p.Contains("pummel party") || p.Contains("lego party")
            || WordStart(p, "minigame", "minispel", "bradspel", "brädspel", "boardgame",
                "partyspel", "sallskapsspel", "sällskapsspel", "tarning", "tärning"))
            return "party";
        // v2.5: FPS (first person) - FORE shooter-regeln ("first person
        // shooter" innehaller "shooter") och en egen genre eftersom formen
        // ar 3D-inifran, inte top-down. "fps" med sifferprefix ("60 fps",
        // "120fps") ar ett PRESTANDAKRAV, inte en genre - lookbehind.
        if (p.Contains("first person") || p.Contains("förstaperson") || p.Contains("forstaperson")
            || System.Text.RegularExpressions.Regex.IsMatch(p, @"(?<!\d)(?<!\d\s)\bfps\b")
            || WordStart(p, "doom")) return "fps";
        if (WordStart(p, "minesweeper", "minröj", "minroj", "minor", "mines")) return "minesweeper";
        if (WordStart(p, "quiz", "frågesport", "fragesport", "trivia")) return "quiz";
        if (WordStart(p, "memory", "minnesspel", "kortspel", "card", "pairs")) return "memory";
        if (WordStart(p, "tetris", "tetromino", "block")) return "blockpuzzle";
        if (WordStart(p, "roguelike", "rogue", "dungeon", "grotta", "permadeath")) return "roguelike";
        // Sport FÖRE management/simulator: "fotbollsmanager"/"fotbolls­simulator"
        // ska bli lag-/säsongsstyrning (management-kitet), ALDRIG bondgårds-
        // kitet - rotorsaken bakom "skördespel i stället för fotboll".
        // Vanliga felstavningar (fotball, manegment) täcks medvetet.
        if (WordStart(p, "fotboll", "fotball", "football", "soccer", "hockey", "handboll", "basket", "tennis", "sport"))
            return p.Contains("manag") || p.Contains("maneg") || p.Contains("tycoon") || p.Contains("sim")
                || WordStart(p, "liga", "säsong", "sasong", "trupp", "tränar", "tranar") || WordExact(p, "lag")
                ? "management" : "rpg";
        if (WordStart(p, "tycoon", "manage", "kiosk", "butik", "restaurang", "företag", "foretag", "affär", "affar")) return "management";
        if (WordStart(p, "sim", "farm", "bondgård", "bondgard", "odla", "skörda", "skorda")) return "simulator";
        if (WordStart(p, "shooter", "bullet", "shmup", "skjut", "shoot")) return "shooter";
        if (WordStart(p, "racing", "racer", "race", "bil", "kart") || WordExact(p, "car", "cars")) return "racing";
        if (WordStart(p, "puzzle", "pussel", "match", "bejeweled", "swap")) return "puzzle";
        if (p.Contains("tower defense") || p.Contains("towerdefense")
            || WordExact(p, "td") || WordStart(p, "torn", "wave")) return "towerdefense";
        if (WordStart(p, "rpg", "adventure", "aventyr", "äventyr")
            || p.Contains("top-down") || p.Contains("topdown")) return "rpg";
        return null;
    }

    private static bool WordStart(string text, params string[] prefixes) =>
        prefixes.Any(k => System.Text.RegularExpressions.Regex.IsMatch(
            text, @"\b" + System.Text.RegularExpressions.Regex.Escape(k)));

    private static bool WordExact(string text, params string[] words) =>
        words.Any(k => System.Text.RegularExpressions.Regex.IsMatch(
            text, @"\b" + System.Text.RegularExpressions.Regex.Escape(k) + @"\b"));

    /// <summary>Shared production layer injected into every genre template:
    /// start/pause/game-over overlays (DOM, no alert()), guarded WebAudio SFX,
    /// and a persistent localStorage highscore. Games call
    /// PKit.init(title, hint, storageKey, onStart), gate their update loop on
    /// PKit.started/!PKit.paused/!PKit.ended, fire PKit.sfx.* on events and
    /// finish through PKit.end(win, score) - never alert(), which would block
    /// the game loop.</summary>
    internal const string ProductionKitJs = @"
// ---- Produktionskit: titel/paus/game over-overlay, WebAudio-SFX, highscore ----
const PKit=(()=>{
  const st=document.createElement('style');
  st.textContent='#pk-start,#pk-over,#pk-pause{position:fixed;inset:0;display:flex;align-items:center;justify-content:center;flex-direction:column;background:rgba(8,10,18,.92);color:#fff;font-family:system-ui,sans-serif;text-align:center;z-index:50}'
    +'#pk-over,#pk-pause{display:none}'
    +'#pk-start h1,#pk-over h1,#pk-pause h1{font-size:40px;margin:0 0 8px}'
    +'#pk-start p,#pk-over p,#pk-pause p{margin:4px;opacity:.85}'
    +'.pk-btn{margin-top:18px;padding:11px 32px;font-size:16px;cursor:pointer;background:#4caf50;color:#fff;border:0;border-radius:8px}'
    +'#pk-hs{position:fixed;top:8px;right:12px;color:#fff;font:14px system-ui,sans-serif;text-shadow:1px 1px 0 #000;z-index:40}';
  document.head.appendChild(st);
  let AC=null,audioOK=true,storeKey='pk',paused=false,started=false,ended=false,startCb=null;
  function initAudio(){if(AC||!audioOK)return;try{AC=new (window.AudioContext||window.webkitAudioContext)();}catch(e){audioOK=false;}}
  function mk(f,d,t){if(!AC)return;try{const o=AC.createOscillator(),g=AC.createGain();o.type=t||'square';o.frequency.value=f;
    o.connect(g);g.connect(AC.destination);g.gain.setValueAtTime(.16,AC.currentTime);
    g.gain.exponentialRampToValueAtTime(.001,AC.currentTime+d);o.start();o.stop(AC.currentTime+d);}catch(e){}}
  const sfx={jump:()=>mk(420,.15),coin:()=>mk(880,.12,'triangle'),hit:()=>mk(140,.25,'sawtooth'),
    shoot:()=>mk(620,.07),place:()=>mk(320,.1,'triangle'),
    win:()=>[523,659,784,1047].forEach((f,i)=>setTimeout(()=>mk(f,.16,'triangle'),i*110)),
    lose:()=>[330,262,196].forEach((f,i)=>setTimeout(()=>mk(f,.22,'sawtooth'),i*140))};
  function high(){return +(localStorage.getItem(storeKey+'.high')||0);}
  function el(id){return document.getElementById(id);}
  function init(title,hint,key,onStart){storeKey=key||'pk';startCb=onStart||null;
    const s=document.createElement('div');s.id='pk-start';
    s.innerHTML='<h1></h1><p></p><p>Esc / P = paus</p><button class=pk-btn>Starta spelet</button>';
    s.querySelector('h1').textContent=title;s.querySelector('p').textContent=hint;
    const o=document.createElement('div');o.id='pk-over';
    o.innerHTML='<h1></h1><p id=pk-sc></p><button class=pk-btn>Spela igen</button>';
    o.querySelector('button').onclick=()=>location.reload();
    const p=document.createElement('div');p.id='pk-pause';
    p.innerHTML='<h1>Paus</h1><p>Tryck Esc eller P for att fortsatta</p>';
    const hs=document.createElement('div');hs.id='pk-hs';hs.textContent='Rekord: '+high();
    document.body.appendChild(s);document.body.appendChild(o);document.body.appendChild(p);document.body.appendChild(hs);
    s.querySelector('button').onclick=()=>{s.style.display='none';started=true;initAudio();if(startCb)startCb();};
    addEventListener('keydown',e=>{const k=e.key.toLowerCase();
      if((k==='escape'||k==='p')&&started&&!ended){paused=!paused;el('pk-pause').style.display=paused?'flex':'none';}});
    addEventListener('pointerdown',initAudio);addEventListener('keydown',initAudio);}
  function end(win,score,label){if(ended)return;ended=true;paused=false;el('pk-pause').style.display='none';
    const o=el('pk-over');o.querySelector('h1').textContent=win?'Du vann!':'Game Over';
    const sc=Math.max(0,score|0),h=Math.max(high(),sc);
    try{localStorage.setItem(storeKey+'.high',h);}catch(e){}
    el('pk-sc').textContent=(label||('Poang: '+sc))+' · Rekord: '+h;
    el('pk-hs').textContent='Rekord: '+h;
    o.style.display='flex';(win?sfx.win:sfx.lose)();}
  return {init,sfx,end,initAudio,get paused(){return paused;},get started(){return started;},get ended(){return ended;}};
})();
";

    /// <summary>Genre-specific README so every scaffold documents its own
    /// controls instead of the platformer's.</summary>
    internal static string Html5GenreReadme(string genre) => genre switch
    {
        "rpg" =>
            "# Adventure - Top-Down RPG (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Styrning: piltangenter/WASD rörelse, E = prata med NPC, I = inventory, mellanslag = attack, Esc/P = paus.\n\n" +
            "Hitta Golden Amulet i världen för att vinna. Du har 3 liv. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "racing" =>
            "# Racer - Top-Down Racing (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Styrning: piltangenter/WASD, R = ställ om bilen, Esc/P = paus.\n\n" +
            "Kör 3 varv så snabbt du kan - snabbare tid ger högre poäng. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "puzzle" =>
            "# Match-3 (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Klicka två intilliggande rutor för att byta plats - matcha 3+ i rad. Du har 25 drag; nå 500 poäng för att vinna. Esc/P = paus.\n\n" +
            "Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "towerdefense" =>
            "# Tower Defense (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Styrning: 1/2/3 väljer torntyp, klicka på rutnätet för att placera. Esc/P = paus.\n\n" +
            "Överlev 12 vågor för att vinna. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "shooter" =>
            "# Shooter - Top-Down Arena (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Styrning: WASD rörelse, sikta med musen, håll vänster musknapp för att skjuta. Esc/P = paus.\n\n" +
            "Klara 10 vågor för att vinna. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "snake" =>
            "# Snake (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Styrning: piltangenter/WASD. Ät mat, väx, krocka inte med vägg eller dig själv. Esc/P = paus.\n\n" +
            "Farten ökar var 50:e poäng. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "idle" =>
            "# Guldgruvan - Idle/Clicker (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Klicka på gruvan för guld, köp uppgraderingar (hacka, gruvarbetare, borrigg) och nå 10 000 guld. Esc/P = paus.\n\n" +
            "Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "breakout" =>
            "# Breakout (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Styrning: piltangenter/A-D eller mus; mellanslag släpper bollen. Esc/P = paus.\n\n" +
            "3 nivåer, tegel med 1-3 HP, vinkelstyrd studs. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "management" =>
            "# Kiosken - Management/Tycoon (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Köp lager, sätt pris, anställ personal och starta dagen - efterfrågan styrs av pris och rykte. " +
            "Nå 5000 kr på 14 dagar. Esc/P = paus. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "simulator" =>
            "# Bondgården - Simulator (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Klicka för att plantera (5 kr) och skörda (+14 kr). Regn ger dubbel växtfart. " +
            "Nå 300 kr före dag 15. Esc/P = paus. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "roguelike" =>
            "# Grottan - Roguelike (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Turordningsbaserat: piltangenter/WASD flyttar, gå in i fiender för att slåss. " +
            "Procedurgenererade våningar - nå trappan på våning 5. Esc/P = paus. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "memory" =>
            "# Memory (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Vänd två kort per försök och hitta alla 8 par på 30 försök. Esc/P = paus. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "minesweeper" =>
            "# Minröj (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Vänsterklick öppnar, högerklick flaggar. Första klicket är alltid säkert. " +
            "Öppna alla säkra rutor för att vinna. Esc/P = paus. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "quiz" =>
            "# Frågesport (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "10 frågor, 15 sekunder per fråga, 3 liv - snabba rätta svar ger bonus. Esc/P = paus. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        "blockpuzzle" =>
            "# Blockpussel (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Piltangenter/WASD flyttar, upp roterar, mellanslag hårdfäller. Rensa 15 rader för att vinna. " +
            "Esc/P = paus. Highscore sparas lokalt.\n\nSpelets design: se `DESIGN.md`.\n",
        _ =>
            "# 2D Platformer (HTML5)\n\nSpela: öppna `index.html` i en webbläsare.\n\n" +
            "Styrning: piltangenter / WASD för rörelse, mellanslag / W / upp för att hoppa, Esc/P = paus.\n\n" +
            "Samla mynt för poäng, undvik fiender. Nå flaggan för att vinna nivån - klara alla 3 nivåer för att vinna spelet. Highscore sparas lokalt.\n\n" +
            "Spelets design: se `DESIGN.md`.\n"
    };

    // ---- RPG / Top-Down Adventure (HTML5) ---------------------------------------
    internal static string Html5Rpg(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Adventure</title>
<style>
  body{margin:0;background:#111;display:flex;justify-content:center;align-items:center;height:100vh;overflow:hidden}
  canvas{border:2px solid #333}
  #ui{position:absolute;top:10px;left:10px;color:#fff;font:14px monospace;pointer-events:none;text-shadow:1px 1px 0 #000}
</style>
</head>
<body>
<canvas id='c'></canvas>
<div id='ui'>HP <span id='hp'>100</span> &middot; XP <span id='xp'>0</span> &middot; Lv <span id='lv'>1</span> &middot; Guld <span id='gold'>0</span> &middot; Liv <span id='lives'>3</span></div>
<script>" + ProductionKitJs + @"
const c=document.getElementById('c'),ctx=c.getContext('2d'),W=800,H=600;
c.width=W;c.height=H;
const keys={};
document.onkeydown=e=>{keys[e.key]=true;
  if(!PKit.started||PKit.paused||PKit.ended)return;
  if(e.key==='e'||e.key==='E')interact();
  if(e.key==='i'||e.key==='I')invOpen=!invOpen;};
document.onkeyup=e=>keys[e.key]=false;

let player={x:400,y:300,w:24,h:24,speed:3,hp:100,maxHp:100,xp:0,level:1,gold:0,lives:3,frame:0};
let inventory=[],invOpen=false,questDone=false,animT=0,msg='',msgT=0;
let npcs=[{x:200,y:250,t:'Valkommen! Hitta den gyllene amuletten i grottan osterut.'},
  {x:600,y:350,t:'Grottan ar farlig - kom tillbaka starkare!'},
  {x:150,y:450,t:'Jag saljer brygder... om du hittar en.'}];
let items=[{x:500,y:200,n:'Sword',d:'+10 dmg'},{x:650,y:450,n:'Potion',d:'+25 HP'},
  {x:100,y:180,n:'Golden Amulet',d:'QUEST'}];
let enemies=[{x:350,y:150,hp:20,n:'Slime'},{x:550,y:500,hp:35,n:'Goblin'},{x:700,y:280,hp:15,n:'Bat'}];

function hasItem(n){return inventory.some(i=>i.n===n);}
function say(t){msg=t;msgT=240;}
function interact(){for(const n of npcs)if(Math.hypot(player.x-n.x,player.y-n.y)<50){say(n.t);return;}}

function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  let dx=0,dy=0;
  if(keys['ArrowLeft']||keys['a'])dx=-1;
  if(keys['ArrowRight']||keys['d'])dx=1;
  if(keys['ArrowUp']||keys['w'])dy=-1;
  if(keys['ArrowDown']||keys['s'])dy=1;
  if(dx&&dy){dx*=0.7;dy*=0.7;}
  player.x+=dx*player.speed;player.y+=dy*player.speed;
  player.x=Math.max(12,Math.min(W-12,player.x));
  player.y=Math.max(12,Math.min(H-12,player.y));

  for(const e of enemies)if(e.hp>0){
    const d=Math.hypot(player.x-e.x,player.y-e.y);
    if(d<30)player.hp-=0.3;
    if(d<40&&keys[' ']){e.hp-=hasItem('Sword')?10:5;
      if(e.hp<=0){player.xp+=10+(Math.random()*5|0);player.gold+=(Math.random()*5|0)+1;PKit.sfx.coin();}}}
  enemies=enemies.filter(e=>e.hp>0);

  if(player.xp>=player.level*30){player.level++;player.maxHp+=10;player.hp=player.maxHp;player.xp=0;PKit.sfx.jump();}

  if(player.hp<=0){
    player.lives--;PKit.sfx.hit();
    if(player.lives<=0){PKit.end(false,player.gold*5+player.level*100+player.xp);return;}
    player.hp=player.maxHp;player.x=400;player.y=300;player.gold=Math.max(0,player.gold-5);
    say('Du foll... '+player.lives+' liv kvar.');}

  for(const it of items){
    const d=Math.hypot(player.x-it.x,player.y-it.y);
    if(d<25){inventory.push(it);it.x=-999;PKit.sfx.coin();
      if(it.n==='Potion'){player.hp=Math.min(player.maxHp,player.hp+25);inventory.pop();say('Brygd: +25 HP');}
      if(it.n==='Golden Amulet'){questDone=true;PKit.end(true,player.gold*5+player.level*100+player.xp+500);}}}
  items=items.filter(it=>it.x>-900);

  if(msgT>0)msgT--;
  animT++;if(animT>18){animT=0;player.frame^=1;}
}

function draw(){
  ctx.fillStyle='#0a0';ctx.fillRect(0,0,W,H);
  const bob=player.frame?1:0; // 2-frame anim: spelare + fiender pulserar
  for(const n of npcs){ctx.fillStyle='#f80';ctx.fillRect(n.x-10,n.y-10,20,20);}
  for(const e of enemies)if(e.hp>0){ctx.fillStyle='#f44';ctx.fillRect(e.x-10,e.y-10+(player.frame?1:0),20,20);}
  for(const it of items)if(it.x>-900){ctx.fillStyle=it.n==='Golden Amulet'?'#ff0':'#0ff';ctx.fillRect(it.x-8,it.y-8,16,16);}
  ctx.fillStyle=questDone?'#ff0':'#4af';ctx.fillRect(player.x-12,player.y-12+bob,24,24);
  if(msgT>0){ctx.fillStyle='rgba(0,0,0,.8)';ctx.fillRect(40,H-70,W-80,50);
    ctx.fillStyle='#fff';ctx.font='15px monospace';ctx.fillText(msg,56,H-40);}
  if(invOpen){ctx.fillStyle='rgba(0,0,0,0.85)';ctx.fillRect(50,50,W-100,H-100);
    ctx.fillStyle='#fff';ctx.font='16px monospace';ctx.fillText('INVENTORY (I stanger)',80,80);
    for(let i=0;i<inventory.length;i++)ctx.fillText(inventory[i].n+': '+inventory[i].d,80,110+i*25);}
  document.getElementById('hp').textContent=player.hp|0;
  document.getElementById('xp').textContent=player.xp;
  document.getElementById('lv').textContent=player.level;
  document.getElementById('gold').textContent=player.gold;
  document.getElementById('lives').textContent=player.lives;
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Adventure','Piltangenter/WASD rorelse · E prata · I inventory · Mellanslag attack','rpg',null);
loop();
</script>
</body>
</html>";

    internal static string Html5RpgDesignDoc(string prompt) =>
        "# RPG / Top-Down Adventure (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt top-down aventyr i webblasaren med NPCs, fiender, items, inventory, quest och liv-system.\n\n" +
        "## Mekanik\n- **Rorelse:** piltangenter/WASD\n- **Interagera:** E (prata med NPCs, dialogruta - inga blockerande popups)\n" +
        "- **Inventory:** I\n- **Combat:** mellanslag\n- **XP/Level:** fiender ger XP, level ger max-HP\n" +
        "- **Liv:** 3 liv; alla slut = Game Over\n- **Quest:** hitta Golden Amulet for att vinna\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), game over/vinst-overlay med omstart\n- WebAudio-SFX (plock, traff, level up, vinst/forlust)\n" +
        "- 2-frame-animation pa spelare/fiender\n- Highscore i localStorage\n\n## Extension\n- Fler NPCs med dialogtrad\n- Fler quests\n- Dungeon-nivaer\n";

    // ---- Racing (HTML5) ----------------------------------------------------------
    internal static string Html5Racing(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Racer</title>
<style>
  body{margin:0;background:#222;display:flex;justify-content:center;align-items:center;height:100vh}
  canvas{border:2px solid #444}
  #hud{position:absolute;top:10px;left:10px;color:#fff;font:14px monospace;text-shadow:1px 1px 0 #000}
</style>
</head>
<body>
<canvas id='c'></canvas>
<div id='hud'>Fart <span id='spd'>0</span> &middot; Varv <span id='lap'>1/3</span> &middot; Tid <span id='time'>0.0s</span></div>
<script>" + ProductionKitJs + @"
const c=document.getElementById('c'),ctx=c.getContext('2d'),W=800,H=500;
c.width=W;c.height=H;
const keys={};
document.onkeydown=e=>{keys[e.key]=true;if((e.key==='r'||e.key==='R')&&PKit.started&&!PKit.ended)resetCar();};
document.onkeyup=e=>keys[e.key]=false;

let car={x:120,y:250,a:0,s:0,frame:0},lap=1,cp=[false,false,false],t=0,hitCd=0;
const walls=[{x:100,y:100,w:600,h:10},{x:100,y:390,w:600,h:10},
  {x:100,y:100,w:10,h:300},{x:690,y:100,w:10,h:300},
  {x:300,y:200,w:200,h:10},{x:300,y:290,w:200,h:10}];

function resetCar(){car.x=120;car.y=250;car.a=0;car.s=0;}

function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  t+=1/60;hitCd=Math.max(0,hitCd-1);
  const accel=keys['ArrowUp']||keys['w'];
  if(accel)car.s=Math.min(8,car.s+0.2);
  else if(keys['ArrowDown']||keys['s'])car.s=Math.max(-3,car.s-0.3);
  else car.s*=0.96;
  if(keys['ArrowLeft']||keys['a'])car.a-=0.04*Math.sign(car.s||1);
  if(keys['ArrowRight']||keys['d'])car.a+=0.04*Math.sign(car.s||1);
  car.x+=Math.cos(car.a)*car.s;car.y+=Math.sin(car.a)*car.s;
  car.x=Math.max(10,Math.min(W-10,car.x));car.y=Math.max(10,Math.min(H-10,car.y));
  for(const w of walls)if(car.x>w.x&&car.x<w.x+w.w&&car.y>w.y&&car.y<w.y+w.h){
    if(Math.abs(car.s)>1&&hitCd<=0){PKit.sfx.hit();hitCd=20;}
    car.s*=-0.5;car.x-=Math.cos(car.a)*car.s*2;car.y-=Math.sin(car.a)*car.s*2;}
  if(car.x>650&&car.y>80&&car.y<120&&!cp[0]){cp[0]=true;PKit.sfx.coin();}
  if(car.x>650&&car.y>380&&car.y<420&&cp[0]&&!cp[1]){cp[1]=true;PKit.sfx.coin();}
  if(car.x<150&&car.y>80&&car.y<120&&cp[0]&&cp[1]&&!cp[2]){cp=[false,false,false];lap++;PKit.sfx.jump();}
  if(lap>3){const secs=t.toFixed(1);PKit.end(true,Math.max(0,(180-t)*10)|0,'Tid: '+secs+'s');}
  car.frame^=accel?1:0; // avgas-anim nar man gasar
}

function draw(){
  ctx.fillStyle='#333';ctx.fillRect(0,0,W,H);
  for(const w of walls){ctx.fillStyle='#555';ctx.fillRect(w.x,w.y,w.w,w.h);}
  ctx.save();ctx.translate(car.x,car.y);ctx.rotate(car.a);
  if(car.frame&&Math.abs(car.s)>2){ctx.fillStyle='#f80';ctx.fillRect(-21,-4,6,8);} // avgas-sprite
  ctx.fillStyle='#e33';ctx.fillRect(-15,-8,30,16);
  ctx.fillStyle='#fff';ctx.fillRect(-3,-8,6,16);
  ctx.restore();
  document.getElementById('spd').textContent=Math.abs(car.s*10)|0;
  document.getElementById('lap').textContent=Math.min(lap,3)+'/3';
  document.getElementById('time').textContent=t.toFixed(1)+'s';
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Racer','Piltangenter/WASD styr · R = stall om bilen','racing',null);
loop();
</script>
</body>
</html>";

    internal static string Html5RacingDesignDoc(string prompt) =>
        "# Racing (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt top-down racing-spel med varvbaserad bana, checkpoints och tidspoang.\n\n" +
        "## Mekanik\n- **Styrning:** piltangenter/WASD\n- **Varv:** 3 (checkpoints i ordning)\n- **Omstall:** R\n" +
        "- **Poang:** snabbare tid = fler poang\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst-overlay med tid + omstart\n- WebAudio-SFX (checkpoint, varv, vaggtraff)\n" +
        "- Avgas-animation vid gas\n- Highscore i localStorage\n\n## Extension\n- AI-motstandare\n- Fler banor\n- Power-ups\n";

    // ---- Puzzle / Match-3 (HTML5) ------------------------------------------------
    internal static string Html5Puzzle(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Match-3</title>
<style>
  body{margin:0;background:#1a1a2e;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column}
  canvas{border:2px solid #444}
  #hud{color:#fff;font:18px monospace;margin-bottom:10px}
</style>
</head>
<body>
<div id='hud'>Poang <span id='sc'>0</span> &middot; Drag kvar <span id='mv'>25</span> &middot; Mal: 500</div>
<canvas id='c'></canvas>
<script>" + ProductionKitJs + @"
const COLS=8,ROWS=8,T=60,clrs=['#e44','#4e4','#44e','#ee4','#e4e','#4ee'];
const c=document.getElementById('c'),ctx=c.getContext('2d');
c.width=COLS*T;c.height=ROWS*T;
let grid=[],sel=null,score=0,moves=25,anim=0;

function init(){grid=[];for(let y=0;y<ROWS;y++){grid[y]=[];for(let x=0;x<COLS;x++)grid[y][x]=Math.random()*clrs.length|0;}
  // startbradet far inte innehalla fardiga matchningar
  while(chk(false)){col(false);}}

c.onclick=e=>{
  if(!PKit.started||PKit.paused||PKit.ended)return;
  const x=e.offsetX/T|0,y=e.offsetY/T|0;
  if(x<0||x>=COLS||y<0||y>=ROWS)return;
  if(!sel){sel={x,y};return;}
  const dx=Math.abs(sel.x-x),dy=Math.abs(sel.y-y);
  if((dx===1&&dy===0)||(dx===0&&dy===1)){
    [grid[sel.y][sel.x],grid[y][x]]=[grid[y][x],grid[sel.y][sel.x]];
    if(chk(true)){moves--;checkEnd();}
    else{[grid[sel.y][sel.x],grid[y][x]]=[grid[y][x],grid[sel.y][sel.x]];PKit.sfx.hit();}}
  sel=null;};

function chk(scoreIt){let m=false;
  for(let y=0;y<ROWS;y++)for(let x=0;x<COLS-2;x++)
    if(grid[y][x]>=0&&grid[y][x]===grid[y][x+1]&&grid[y][x]===grid[y][x+2]){grid[y][x]=grid[y][x+1]=grid[y][x+2]=-1;m=true;if(scoreIt)score+=30;}
  for(let x=0;x<COLS;x++)for(let y=0;y<ROWS-2;y++)
    if(grid[y][x]>=0&&grid[y][x]===grid[y+1][x]&&grid[y][x]===grid[y+2][x]){grid[y][x]=grid[y+1][x]=grid[y+2][x]=-1;m=true;if(scoreIt)score+=30;}
  if(m){if(scoreIt)PKit.sfx.coin();col(scoreIt);return true;}
  return false;}

function col(scoreIt){for(let x=0;x<COLS;x++){const a=[];
    for(let y=ROWS-1;y>=0;y--)if(grid[y][x]!==-1)a.push(grid[y][x]);
    while(a.length<ROWS)a.push(Math.random()*clrs.length|0);
    a.reverse();for(let y=0;y<ROWS;y++)grid[y][x]=a[y];}
  chk(scoreIt);}

function checkEnd(){if(moves<=0)PKit.end(score>=500,score);}

function draw(){
  ctx.fillStyle='#222';ctx.fillRect(0,0,c.width,c.height);
  for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){
    ctx.fillStyle=clrs[grid[y][x]]||'#222';
    ctx.fillRect(x*T+2,y*T+2,T-4,T-4);
    ctx.strokeStyle='#111';ctx.strokeRect(x*T+2,y*T+2,T-4,T-4);}
  if(sel){ctx.strokeStyle='#fff';ctx.lineWidth=anim<15?3:5;ctx.strokeRect(sel.x*T,sel.y*T,T,T);ctx.lineWidth=1;} // puls-anim pa vald ruta
  anim=(anim+1)%30;
  document.getElementById('sc').textContent=score;
  document.getElementById('mv').textContent=moves;
}
function loop(){try{draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Match-3','Klicka tva intilliggande rutor for att byta plats · 25 drag, na 500 poang','puzzle',init);
loop();
</script>
</body>
</html>";

    internal static string Html5PuzzleDesignDoc(string prompt) =>
        "# Puzzle / Match-3 (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt klassiskt match-3-pussel med dragbudget och poangmal - vinst eller forlust, inte oandligt.\n\n" +
        "## Mekanik\n- **Klicka:** valj tva intilliggande rutor for att byta plats\n- **Match:** 3+ i rad/kolumn (+30 poang)\n" +
        "- **Collapse:** matchade rutor forsvinner, nya faller ner (kedjor raknas)\n" +
        "- **Mal:** 500 poang pa 25 drag; ogiltigt byte kostar inget drag\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay med omstart\n- WebAudio-SFX (match, ogiltigt byte)\n" +
        "- Puls-animation pa vald ruta\n- Highscore i localStorage\n\n## Extension\n- Special tiles\n- Tidsbegransning\n- Combo-multiplikator\n";

    // ---- Tower Defense (HTML5) ---------------------------------------------------
    internal static string Html5TowerDefense(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Tower Defense</title>
<style>
  body{margin:0;background:#1a2a1a;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column}
  canvas{border:2px solid #444}
  #hud{color:#fff;font:14px monospace;margin-bottom:8px}
</style>
</head>
<body>
<div id='hud'>Guld <span id='gld'>200</span> &middot; Vag <span id='wv'>1</span>/12 &middot; Liv <span id='lvs'>20</span> &middot; 1/2/3 = torn</div>
<canvas id='c'></canvas>
<script>" + ProductionKitJs + @"
const W=800,H=500,T=40;
const c=document.getElementById('c'),ctx=c.getContext('2d');
c.width=W;c.height=H;
const FINAL_WAVE=12;
let gold=200,lives=20,wave=1,en=[],tw=[],pj=[],sp=0,st=0,anim=0,score=0;
const path=[{x:0,y:5},{x:5,y:5},{x:5,y:2},{x:10,y:2},{x:10,y:8},{x:15,y:8},{x:15,y:4},{x:19,y:4}];
const types=[{c:50,d:15,r:3,cl:'#48f'},{c:100,d:30,r:3.5,cl:'#f84'},{c:75,d:8,r:2.5,cl:'#8f4'}];

document.addEventListener('keydown',e=>{
  if(e.key==='1')st=0;if(e.key==='2')st=1;if(e.key==='3')st=2;});
c.onclick=e=>{
  if(!PKit.started||PKit.paused||PKit.ended)return;
  const tx=e.offsetX/T|0,ty=e.offsetY/T|0,t=types[st];
  if(gold>=t.c&&!tw.some(w=>w.x===tx&&w.y===ty)&&!path.some(p=>p.x===tx&&p.y===ty)){
    tw.push({x:tx,y:ty,tp:st,cd:0,flash:0});gold-=t.c;PKit.sfx.place();}};

function spawn(){const n=5+wave*2;
  for(let i=0;i<n;i++)en.push({x:-i*30,y:5*T+T/2,pi:0,hp:10+wave*5,mhp:10+wave*5,sp:1+wave*.1,rw:5+wave,frame:0});}

function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  anim=(anim+1)%20;
  for(const e of en){
    if(e.pi>=path.length-1){lives--;e.hp=0;PKit.sfx.hit();continue;}
    const tgt=path[e.pi+1];
    const txp=tgt.x*T+T/2,typ=tgt.y*T+T/2;
    const dx=txp-e.x,dy=typ-e.y,d=Math.hypot(dx,dy)||1;
    e.x+=dx/d*e.sp;e.y+=dy/d*e.sp;
    if(d<4)e.pi++;
    if(anim===0)e.frame^=1;}
  en=en.filter(e=>e.hp>0);
  for(const t of tw){t.cd=Math.max(0,t.cd-0.016);t.flash=Math.max(0,t.flash-1);
    if(t.cd<=0){const tp=types[t.tp],r=tp.r*T;
      const tgt=en.find(e=>Math.hypot(e.x-(t.x*T+T/2),e.y-(t.y*T+T/2))<r);
      if(tgt){pj.push({x:t.x*T+T/2,y:t.y*T+T/2,tg:tgt,d:tp.d,cl:tp.cl});t.cd=1.2;t.flash=6;PKit.sfx.shoot();}}}
  for(const p of pj){
    if(p.tg.hp<=0){p.hit=true;continue;}
    const dx=p.tg.x-p.x,dy=p.tg.y-p.y,d=Math.hypot(dx,dy);
    if(d<8){p.tg.hp-=p.d;if(p.tg.hp<=0){gold+=p.tg.rw;score+=p.tg.rw;PKit.sfx.coin();}p.hit=true;continue;}
    p.x+=dx/d*6;p.y+=dy/d*6;}
  pj=pj.filter(p=>!p.hit);
  sp--;
  if(sp<=0&&en.length===0){
    if(wave>=FINAL_WAVE){PKit.end(true,score+wave*100+gold);return;}
    wave++;spawn();sp=60;}
  if(lives<=0)PKit.end(false,score+wave*100+gold);
}

function draw(){
  ctx.fillStyle='#2a3';ctx.fillRect(0,0,W,H);
  for(let i=1;i<path.length;i++){ctx.fillStyle='#863';
    ctx.fillRect(Math.min(path[i-1].x,path[i].x)*T,Math.min(path[i-1].y,path[i].y)*T,
      (Math.abs(path[i].x-path[i-1].x)+1)*T,(Math.abs(path[i].y-path[i-1].y)+1)*T);}
  for(const t of tw){const tp=types[t.tp];
    ctx.fillStyle=t.flash>0?'#fff':tp.cl; // mynningsflash-anim
    ctx.fillRect(t.x*T+4,t.y*T+4,T-8,T-8);
    ctx.strokeStyle='#111';ctx.strokeRect(t.x*T+4,t.y*T+4,T-8,T-8);}
  for(const e of en){const bob=e.frame?1:0;
    ctx.fillStyle='#f44';ctx.fillRect(e.x-8,e.y-8+bob,16,16);
    ctx.fillStyle='#500';ctx.fillRect(e.x-8,e.y-13,16,3);
    ctx.fillStyle='#0f0';ctx.fillRect(e.x-8,e.y-13,16*Math.max(0,e.hp)/e.mhp,3);}
  for(const p of pj){ctx.fillStyle=p.cl;ctx.beginPath();ctx.arc(p.x,p.y,3,0,Math.PI*2);ctx.fill();}
  document.getElementById('gld').textContent=gold;
  document.getElementById('wv').textContent=Math.min(wave,FINAL_WAVE);
  document.getElementById('lvs').textContent=Math.max(0,lives);
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Tower Defense','1/2/3 valjer torn · klicka pa gras for att placera · overlev '+FINAL_WAVE+' vagor','td',spawn);
loop();
</script>
</body>
</html>";

    internal static string Html5TdDesignDoc(string prompt) =>
        "# Tower Defense (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nPlacera torn langs banan och overlev 12 vagor - vinst nar sista vagen ar rensad.\n\n" +
        "## Mekanik\n- **Placera torn:** klicka (kostar guld); 1/2/3 valjer typ (snabb/tung/billig)\n" +
        "- **Guld:** fran dodade fiender\n- **Liv:** fiender som nar slutet tar liv; 0 liv = Game Over\n- **Vinst:** vag 12 rensad\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay med omstart\n- WebAudio-SFX (placering, skott, doda fiender, lackage)\n" +
        "- Animation: fiende-bob + mynningsflash, HP-bars\n- Highscore i localStorage\n\n## Extension\n- Uppgraderingar\n- Boss-fiender\n- Fler banor\n";

    // ---- Top-Down Shooter (HTML5) ------------------------------------------------
    internal static string Html5Shooter(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Shooter</title>
<style>
  body{margin:0;background:#000;display:flex;justify-content:center;align-items:center;height:100vh;overflow:hidden}
  canvas{border:2px solid #333;cursor:none}
  #hud{position:absolute;top:10px;left:10px;color:#fff;font:14px monospace;pointer-events:none;text-shadow:1px 1px 0 #000}
</style>
</head>
<body>
<canvas id='c'></canvas>
<div id='hud'>Poang <span id='sc'>0</span> &middot; HP <span id='hp'>100</span> &middot; Vag <span id='wv'>1</span>/10</div>
<script>" + ProductionKitJs + @"
const c=document.getElementById('c'),ctx=c.getContext('2d'),W=800,H=600;
c.width=W;c.height=H;
const FINAL_WAVE=10;
const keys={},mouse={x:W/2,y:H/2,down:false};
document.onkeydown=e=>keys[e.key]=true;
document.onkeyup=e=>keys[e.key]=false;
c.onmousemove=e=>{mouse.x=e.offsetX;mouse.y=e.offsetY;};
c.onmousedown=()=>mouse.down=true;
c.onmouseup=()=>mouse.down=false;

let player={x:W/2,y:H/2,speed:4,hp:100,mhp:100,inv:0};
let bullets=[],enemies=[],particles=[],score=0,wave=1,scd=0;

function spawn(){const n=3+wave*2;
  for(let i=0;i<n;i++){const s=Math.random()*4|0;let ex,ey;
    if(s===0){ex=Math.random()*W;ey=-20;}
    else if(s===1){ex=W+20;ey=Math.random()*H;}
    else if(s===2){ex=Math.random()*W;ey=H+20;}
    else{ex=-20;ey=Math.random()*H;}
    enemies.push({x:ex,y:ey,hp:5+wave*2,sp:1+wave*.3});}}

function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  let dx=0,dy=0;
  if(keys['a']||keys['ArrowLeft'])dx=-1;
  if(keys['d']||keys['ArrowRight'])dx=1;
  if(keys['w']||keys['ArrowUp'])dy=-1;
  if(keys['s']||keys['ArrowDown'])dy=1;
  if(dx&&dy){dx*=0.7;dy*=0.7;}
  player.x+=dx*player.speed;player.y+=dy*player.speed;
  player.x=Math.max(10,Math.min(W-10,player.x));
  player.y=Math.max(10,Math.min(H-10,player.y));
  player.inv=Math.max(0,player.inv-1);
  scd=Math.max(0,scd-0.016);
  if(mouse.down&&scd<=0){
    const a=Math.atan2(mouse.y-player.y,mouse.x-player.x);
    bullets.push({x:player.x,y:player.y,vx:Math.cos(a)*8,vy:Math.sin(a)*8});
    scd=0.15;PKit.sfx.shoot();}
  for(const b of bullets){b.x+=b.vx;b.y+=b.vy;if(b.x<0||b.x>W||b.y<0||b.y>H)b.dead=true;}
  bullets=bullets.filter(b=>!b.dead);
  for(const e of enemies){
    const a=Math.atan2(player.y-e.y,player.x-e.x);
    e.x+=Math.cos(a)*e.sp;e.y+=Math.sin(a)*e.sp;
    if(Math.hypot(player.x-e.x,player.y-e.y)<20&&player.inv<=0){
      player.hp-=10;player.inv=30;PKit.sfx.hit();}
    for(const b of bullets)if(Math.hypot(b.x-e.x,b.y-e.y)<15){e.hp-=10;b.dead=true;
      for(let i=0;i<5;i++)particles.push({x:e.x,y:e.y,vx:(Math.random()-.5)*4,vy:(Math.random()-.5)*4,life:20});}}
  const before=enemies.length;
  enemies=enemies.filter(e=>e.hp>0);
  if(enemies.length<before)PKit.sfx.coin();
  score+=(before-enemies.length)*10;
  if(enemies.length===0){
    if(wave>=FINAL_WAVE){PKit.end(true,score+1000);return;}
    wave++;spawn();score+=wave*50;}
  particles=particles.filter(p=>{p.life--;p.x+=p.vx;p.y+=p.vy;return p.life>0;});
  if(player.hp<=0)PKit.end(false,score);
}

function draw(){
  ctx.fillStyle='#111';ctx.fillRect(0,0,W,H);
  // spelaren blinkar under osarbarhets-frames (traff-anim)
  if(player.inv%6<3){ctx.fillStyle='#0af';ctx.fillRect(player.x-10,player.y-10,20,20);}
  for(const b of bullets){ctx.fillStyle='#ff0';ctx.fillRect(b.x-2,b.y-2,4,4);}
  for(const e of enemies){ctx.fillStyle='#f44';ctx.fillRect(e.x-9,e.y-9,18,18);}
  for(const p of particles){ctx.globalAlpha=p.life/20;ctx.fillStyle='#f80';ctx.fillRect(p.x-2,p.y-2,4,4);}
  ctx.globalAlpha=1;
  ctx.strokeStyle='#fff';ctx.beginPath();ctx.arc(mouse.x,mouse.y,8,0,Math.PI*2);
  ctx.moveTo(mouse.x-12,mouse.y);ctx.lineTo(mouse.x+12,mouse.y);
  ctx.moveTo(mouse.x,mouse.y-12);ctx.lineTo(mouse.x,mouse.y+12);ctx.stroke();
  document.getElementById('sc').textContent=score;
  document.getElementById('hp').textContent=Math.max(0,player.hp|0);
  document.getElementById('wv').textContent=Math.min(wave,FINAL_WAVE);
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Shooter','WASD rorelse · sikta med musen · hall vanster musknapp for att skjuta','shooter',spawn);
loop();
</script>
</body>
</html>";

    // ---- Snake (HTML5) -----------------------------------------------------------
    internal static string Html5Snake(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Snake</title>
<style>
  body{margin:0;background:#101418;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column}
  canvas{border:2px solid #2a3a2a}
  #hud{color:#fff;font:16px monospace;margin-bottom:8px}
</style>
</head>
<body>
<div id='hud'>Poang <span id='sc'>0</span> &middot; Langd <span id='len'>3</span> &middot; Fart <span id='spd'>1</span></div>
<canvas id='c'></canvas>
<script>" + ProductionKitJs + @"
const T=24,COLS=28,ROWS=22;
const c=document.getElementById('c'),ctx=c.getContext('2d');
c.width=COLS*T;c.height=ROWS*T;
let snake=[{x:14,y:11},{x:13,y:11},{x:12,y:11}];
let dir={x:1,y:0},nextDir={x:1,y:0},food=null,score=0,tick=0,speed=8,anim=0;

document.addEventListener('keydown',e=>{
  if(!PKit.started||PKit.paused||PKit.ended)return;
  const k=e.key;
  if((k==='ArrowUp'||k==='w')&&dir.y!==1)nextDir={x:0,y:-1};
  if((k==='ArrowDown'||k==='s')&&dir.y!==-1)nextDir={x:0,y:1};
  if((k==='ArrowLeft'||k==='a')&&dir.x!==1)nextDir={x:-1,y:0};
  if((k==='ArrowRight'||k==='d')&&dir.x!==-1)nextDir={x:1,y:0};
  if(k.startsWith('Arrow'))e.preventDefault();});

function placeFood(){
  while(true){const f={x:Math.random()*COLS|0,y:Math.random()*ROWS|0};
    if(!snake.some(s=>s.x===f.x&&s.y===f.y)){food=f;return;}}}

function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  tick++;anim=(anim+1)%20;
  if(tick%Math.max(2,(12-speed))!==0)return;
  dir=nextDir;
  const head={x:snake[0].x+dir.x,y:snake[0].y+dir.y};
  if(head.x<0||head.x>=COLS||head.y<0||head.y>=ROWS||snake.some(s=>s.x===head.x&&s.y===head.y)){
    PKit.sfx.hit();PKit.end(false,score);return;}
  snake.unshift(head);
  if(food&&head.x===food.x&&head.y===food.y){
    score+=10;speed=Math.min(10,1+(score/50|0));PKit.sfx.coin();placeFood();
    if(snake.length>=COLS*ROWS-1){PKit.end(true,score+1000);return;}}
  else snake.pop();
}

function draw(){
  ctx.fillStyle='#182018';ctx.fillRect(0,0,c.width,c.height);
  if(food){ctx.fillStyle=anim<10?'#f5c542':'#ffd76a'; // puls-anim pa maten
    ctx.fillRect(food.x*T+3,food.y*T+3,T-6,T-6);}
  for(let i=0;i<snake.length;i++){const s=snake[i];
    ctx.fillStyle=i===0?'#7ef07e':'#3fa34d';
    ctx.fillRect(s.x*T+1,s.y*T+1,T-2,T-2);}
  document.getElementById('sc').textContent=score;
  document.getElementById('len').textContent=snake.length;
  document.getElementById('spd').textContent=speed;
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Snake','Piltangenter/WASD styr ormen · at mat, vax, krocka inte','snake',placeFood);
loop();
</script>
</body>
</html>";

    internal static string Html5SnakeDesignDoc(string prompt) =>
        "# Snake (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nKlassisk snake med okande fart och vinst nar planen ar full.\n\n" +
        "## Mekanik\n- **Styrning:** piltangenter/WASD (180-graders svangar blockeras)\n" +
        "- **Mat:** +10 poang, ormen vaxer, farten okar var 50:e poang\n- **Forlust:** vagg eller egen kropp\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), game over-overlay med omstart\n- WebAudio-SFX (mat, krock)\n" +
        "- Puls-animation pa maten\n- Highscore i localStorage\n\n## Extension\n- Hinder\n- Speciamat (bonus/gift)\n- Tva spelare\n";

    // ---- Idle / Clicker (HTML5) --------------------------------------------------
    internal static string Html5Idle(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Guldgruvan</title>
<style>
  body{margin:0;background:#141018;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column;font-family:system-ui,sans-serif;color:#fff}
  #mine{width:180px;height:180px;border-radius:50%;background:radial-gradient(#f5c542,#8a6d1a);border:6px solid #5a4712;font-size:64px;cursor:pointer;transition:transform .06s}
  #mine:active{transform:scale(.94)}
  #hud{font:18px monospace;margin:14px}
  .shop{display:flex;gap:10px;margin-top:14px}
  .shop button{padding:10px 14px;background:#242030;color:#fff;border:1px solid #443c5a;border-radius:8px;cursor:pointer;font:13px monospace}
  .shop button:disabled{opacity:.4;cursor:default}
</style>
</head>
<body>
<div id='hud'>Guld <span id='gold'>0</span> &middot; Per klick <span id='cv'>1</span> &middot; Per sekund <span id='ps'>0</span> &middot; Prestige <span id='pr'>x1.0</span></div>
<button id='mine'>&#9935;</button>
<div class='shop'>
  <button id='buyPick'>Battre hacka (+1/klick)<br>Kostar <span id='pickCost'>10</span></button>
  <button id='buyMiner'>Gruvarbetare (+1/s)<br>Kostar <span id='minerCost'>25</span></button>
  <button id='buyRig'>Borrigg (+8/s)<br>Kostar <span id='rigCost'>200</span></button>
  <button id='prestige'>Prestige (+50%/niva)<br>Krav <span id='prReq'>2500</span> guld</button>
</div>
<canvas id='c' width='420' height='60' style='margin-top:10px'></canvas>
<script>" + ProductionKitJs + @"
const GOAL=10000;
let gold=0,perClick=1,perSec=0,pickCost=10,minerCost=25,rigCost=200,anim=0,prLevel=0,mult=1;
const ctx=document.getElementById('c').getContext('2d');
const el=id=>document.getElementById(id);

el('mine').onclick=()=>{
  if(!PKit.started||PKit.paused||PKit.ended)return;
  gold+=perClick*mult;PKit.sfx.coin();};
el('buyPick').onclick=()=>{if(PKit.ended||gold<pickCost)return;gold-=pickCost;perClick++;pickCost=Math.ceil(pickCost*1.6);PKit.sfx.place();};
el('buyMiner').onclick=()=>{if(PKit.ended||gold<minerCost)return;gold-=minerCost;perSec+=1;minerCost=Math.ceil(minerCost*1.7);PKit.sfx.place();};
el('buyRig').onclick=()=>{if(PKit.ended||gold<rigCost)return;gold-=rigCost;perSec+=8;rigCost=Math.ceil(rigCost*1.8);PKit.sfx.place();};
// Prestige: borja om fran noll mot +50% produktion per niva - det klassiska
// idle-vagvalet (kortsiktigt guld mot langsiktig multiplikator).
el('prestige').onclick=()=>{if(PKit.ended||gold<2500)return;prLevel++;mult=1+prLevel*0.5;gold=0;perClick=1;perSec=0;pickCost=10;minerCost=25;rigCost=200;PKit.sfx.place();};

let tick=0;
function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  tick++;anim=(anim+1)%60;
  if(tick%60===0&&perSec>0){gold+=perSec*mult;}
  if(gold>=GOAL)PKit.end(true,gold);
}

function draw(){
  el('gold').textContent=gold|0;
  el('cv').textContent=perClick;
  el('ps').textContent=perSec;
  el('pickCost').textContent=pickCost;
  el('minerCost').textContent=minerCost;
  el('rigCost').textContent=rigCost;
  el('buyPick').disabled=gold<pickCost;
  el('buyMiner').disabled=gold<minerCost;
  el('buyRig').disabled=gold<rigCost;
  el('pr').textContent='x'+mult.toFixed(1);
  el('prestige').disabled=gold<2500;
  // progress-bar mot malet med skimmer-anim
  ctx.fillStyle='#242030';ctx.fillRect(0,0,420,60);
  const w=Math.min(1,gold/GOAL)*412;
  ctx.fillStyle='#f5c542';ctx.fillRect(4,4,w,52);
  ctx.fillStyle='rgba(255,255,255,'+(anim<30?anim/60:(60-anim)/60)+')';
  ctx.fillRect(Math.max(0,w-30),4,26,52);
  ctx.fillStyle='#fff';ctx.font='14px monospace';
  ctx.fillText('Mal: '+GOAL+' guld',150,34);
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Guldgruvan','Klicka pa gruvan for guld · kop uppgraderingar · prestige ger +50%/niva · na '+GOAL+' guld','idle',null);
loop();
</script>
</body>
</html>";

    internal static string Html5IdleDesignDoc(string prompt) =>
        "# Idle / Clicker (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt klickspel med tre uppgraderingsspar och ett tydligt mal (10 000 guld) sa rundan har ett slut.\n\n" +
        "## Mekanik\n- **Klick:** +guld per klick\n- **Uppgraderingar:** hacka (+1/klick), gruvarbetare (+1/s), borrigg (+8/s); priser vaxer exponentiellt\n" +
        "- **Prestige:** aterstall allt for +50% produktion per niva (krav 2 500 guld) - kortsiktigt guld mot langsiktig multiplikator\n" +
        "- **Vinst:** 10 000 guld\n\n## Produktion\n- Titelskarm, paus (Esc/P), vinst-overlay med omstart\n- WebAudio-SFX (klick, kop)\n" +
        "- Skimmer-animation pa progressbaren, tryck-animation pa knappen\n- Highscore i localStorage\n\n## Extension\n- Fler byggnader\n- Offline-produktion\n- Achievements\n";

    // ---- Breakout (HTML5) --------------------------------------------------------
    internal static string Html5Breakout(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Breakout</title>
<style>
  body{margin:0;background:#0e1220;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column}
  canvas{border:2px solid #2a3350}
  #hud{color:#fff;font:16px monospace;margin-bottom:8px}
</style>
</head>
<body>
<div id='hud'>Poang <span id='sc'>0</span> &middot; Liv <span id='lv'>3</span> &middot; Niva <span id='ni'>1</span>/3</div>
<canvas id='c'></canvas>
<script>" + ProductionKitJs + @"
const W=720,H=520,ROWS=5,COLS=10,BW=64,BH=20;
const c=document.getElementById('c'),ctx=c.getContext('2d');
c.width=W;c.height=H;
const clrs=['#e05561','#e0a355','#e0d955','#7ee055','#55c8e0'];
let paddle={x:W/2-50,w:100,h:12},ball={x:W/2,y:H-80,vx:3,vy:-4,r:7,stuck:true};
let bricks=[],score=0,lives=3,level=1,anim=0;
const keys={};
document.addEventListener('keydown',e=>{keys[e.key]=true;
  if(e.key===' '&&ball.stuck&&PKit.started&&!PKit.paused&&!PKit.ended){ball.stuck=false;PKit.sfx.jump();}
  if(e.key.startsWith('Arrow'))e.preventDefault();});
document.addEventListener('keyup',e=>keys[e.key]=false);
c.onmousemove=e=>{if(PKit.started&&!PKit.paused)paddle.x=Math.max(0,Math.min(W-paddle.w,e.offsetX-paddle.w/2));};

function buildLevel(n){bricks=[];
  for(let r=0;r<ROWS;r++)for(let col=0;col<COLS;col++){
    const hp=Math.min(3,1+((r+n)%3));
    bricks.push({x:col*(BW+6)+15,y:r*(BH+6)+50,hp,max:hp});}}

function resetBall(){ball.x=paddle.x+paddle.w/2;ball.y=H-80;ball.vx=3*(Math.random()<0.5?-1:1);ball.vy=-4;ball.stuck=true;}

function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  anim=(anim+1)%30;
  const sp=7;
  if(keys['ArrowLeft']||keys['a'])paddle.x=Math.max(0,paddle.x-sp);
  if(keys['ArrowRight']||keys['d'])paddle.x=Math.min(W-paddle.w,paddle.x+sp);
  if(ball.stuck){ball.x=paddle.x+paddle.w/2;ball.y=H-paddle.h-30;return;}
  ball.x+=ball.vx;ball.y+=ball.vy;
  if(ball.x<ball.r||ball.x>W-ball.r){ball.vx*=-1;ball.x=Math.max(ball.r,Math.min(W-ball.r,ball.x));}
  if(ball.y<ball.r){ball.vy*=-1;ball.y=ball.r;}
  if(ball.y>H+20){lives--;PKit.sfx.hit();
    if(lives<=0){PKit.end(false,score);return;}
    resetBall();return;}
  const py=H-30;
  if(ball.vy>0&&ball.y+ball.r>=py&&ball.y+ball.r<=py+paddle.h+8&&ball.x>=paddle.x-ball.r&&ball.x<=paddle.x+paddle.w+ball.r){
    const hit=(ball.x-(paddle.x+paddle.w/2))/(paddle.w/2);
    ball.vx=hit*5;ball.vy=-Math.abs(ball.vy);PKit.sfx.place();}
  for(const b of bricks){if(b.hp<=0)continue;
    if(ball.x>b.x-ball.r&&ball.x<b.x+BW+ball.r&&ball.y>b.y-ball.r&&ball.y<b.y+BH+ball.r){
      b.hp--;score+=10;PKit.sfx.coin();
      const fromSide=ball.x<b.x||ball.x>b.x+BW;
      if(fromSide)ball.vx*=-1;else ball.vy*=-1;
      break;}}
  if(bricks.every(b=>b.hp<=0)){
    if(level>=3){PKit.end(true,score+lives*100);return;}
    level++;score+=50;buildLevel(level);resetBall();PKit.sfx.win();}
}

function draw(){
  ctx.fillStyle='#101528';ctx.fillRect(0,0,W,H);
  for(const b of bricks){if(b.hp<=0)continue;
    ctx.fillStyle=clrs[(b.max-b.hp+b.y/26|0)%clrs.length];
    ctx.globalAlpha=0.5+0.5*(b.hp/b.max);
    ctx.fillRect(b.x,b.y,BW,BH);ctx.globalAlpha=1;
    ctx.strokeStyle='#0e1220';ctx.strokeRect(b.x,b.y,BW,BH);}
  ctx.fillStyle='#7ea2ff';ctx.fillRect(paddle.x,H-30,paddle.w,paddle.h);
  ctx.fillStyle=anim<15?'#fff':'#ffe9a0'; // glimt-anim pa bollen
  ctx.beginPath();ctx.arc(ball.x,ball.y,ball.r,0,Math.PI*2);ctx.fill();
  document.getElementById('sc').textContent=score;
  document.getElementById('lv').textContent=lives;
  document.getElementById('ni').textContent=level;
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Breakout','Piltangenter/A-D eller mus styr plattan · mellanslag slapper bollen','breakout',()=>{buildLevel(1);resetBall();});
loop();
</script>
</body>
</html>";

    internal static string Html5BreakoutDesignDoc(string prompt) =>
        "# Breakout (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nKlassisk breakout med 3 nivaer, flertaligt tegel (1-3 HP) och vinkelstyrd studs pa plattan.\n\n" +
        "## Mekanik\n- **Styrning:** piltangenter/A-D eller mus; mellanslag slapper bollen\n" +
        "- **Tegel:** 10 poang per traff, tal 1-3 traffar (genomskinlighet visar skada)\n" +
        "- **Studs:** traffpunkt pa plattan styr vinkeln\n- **Liv:** 3; vinst efter niva 3 (+100/liv kvar)\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay med omstart\n- WebAudio-SFX (studs, tegel, miss, niva)\n" +
        "- Glimt-animation pa bollen, skade-genomskinlighet pa tegel\n- Highscore i localStorage\n\n## Extension\n- Power-ups (bredare platta, multiboll)\n- Fler nivalayouter\n- Okande bollfart\n";

    internal static string Html5ShooterDesignDoc(string prompt) =>
        "# Top-Down Shooter (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEn top-down arena-shooter: overlev 10 vagor med okande svarighetsgrad for att vinna.\n\n" +
        "## Mekanik\n- **Rorelse:** WASD\n- **Sikta:** muspekare\n- **Skjuta:** hall vanster musknapp\n" +
        "- **Vagor:** rensa alla fiender for nasta vag; vag 10 rensad = vinst\n- **Osarbarhet:** korta i-frames efter traff\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay med omstart\n- WebAudio-SFX (skott, traff, doda fiender)\n" +
        "- Animation: partiklar vid traff, spelaren blinkar under i-frames\n- Highscore i localStorage\n\n## Extension\n- Power-ups\n- Boss-fiender\n- Olika vapen\n";

    // ---- Management / Tycoon (HTML5) ---------------------------------------------
    internal static string Html5Management(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Kiosken</title>
<style>
  body{margin:0;background:#151a22;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column;font-family:system-ui,sans-serif;color:#fff}
  canvas{border:2px solid #2a3350}
  #hud{font:15px monospace;margin-bottom:8px}
  .row{display:flex;gap:8px;margin-top:10px}
  .row button{padding:9px 12px;background:#242c3c;color:#fff;border:1px solid #3a4a66;border-radius:8px;cursor:pointer;font:12px monospace}
  .row button:disabled{opacity:.4;cursor:default}
  #startDay{background:#2f6b3a}
</style>
</head>
<body>
<div id='hud'>Kassa <span id='cash'>100</span> kr &middot; Lager <span id='stock'>0</span> &middot; Pris <span id='price'>15</span> kr &middot; Personal <span id='staff'>1</span> &middot; Rykte <span id='rep'>50</span> &middot; Dag <span id='day'>1</span>/14</div>
<canvas id='c' width='720' height='300'></canvas>
<div class='row'>
  <button id='buyStock'>Kop lager (10 st, 80 kr)</button>
  <button id='priceDown'>Pris -1</button>
  <button id='priceUp'>Pris +1</button>
  <button id='hire'>Anstall (200 kr)</button>
  <button id='startDay'>Starta dagen</button>
</div>
<script>" + ProductionKitJs + @"
const GOAL=5000,MAXDAY=14;
const c=document.getElementById('c'),ctx=c.getContext('2d'),W=720,H=300;
let cash=100,stock=0,price=15,staff=1,rep=50,day=1,dayActive=false,customers=[],anim=0;
const el=id=>document.getElementById(id);
function guard(){return PKit.started&&!PKit.paused&&!PKit.ended;}

el('buyStock').onclick=()=>{if(!guard()||dayActive||cash<80)return;cash-=80;stock+=10;PKit.sfx.place();};
el('priceDown').onclick=()=>{if(!guard()||price<=5)return;price--;};
el('priceUp').onclick=()=>{if(!guard()||price>=40)return;price++;};
el('hire').onclick=()=>{if(!guard()||dayActive||cash<200||staff>=5)return;cash-=200;staff++;PKit.sfx.place();};
el('startDay').onclick=()=>{if(!guard()||dayActive)return;startDay();};

function startDay(){
  dayActive=true;
  const demand=Math.max(2,Math.min(25,Math.round(8+rep/10-(price-12)*0.8)));
  customers=[];
  for(let i=0;i<demand;i++)customers.push({x:-30-i*46,y:210+(i%3)*14,bought:false,frame:0});
}

function update(){
  if(!guard())return;
  anim=(anim+1)%24;
  if(!dayActive)return;
  const serveCap=staff; // personal begransar hur manga som hinner betjanas samtidigt
  let serving=0;
  for(const cu of customers){
    cu.x+=2.2;
    if(anim%12===0)cu.frame^=1;
    if(!cu.bought&&cu.x>330&&cu.x<390&&serving<serveCap){
      serving++;
      if(stock>0&&Math.random()<Math.max(0.25,1.1-price/38)){
        cu.bought=true;stock--;cash+=price;rep=Math.min(99,rep+0.4);PKit.sfx.coin();
      }else if(stock<=0){rep=Math.max(1,rep-0.5);}
    }
  }
  if(customers.every(cu=>cu.x>W+30)){
    dayActive=false;day++;
    if(cash>=GOAL){PKit.end(true,cash);return;}
    if(day>MAXDAY){PKit.end(cash>=GOAL,cash);return;}
    if(cash<80&&stock<=0){PKit.sfx.hit();PKit.end(false,cash);return;} // konkurs
  }
}

function draw(){
  ctx.fillStyle='#1a2130';ctx.fillRect(0,0,W,H);
  ctx.fillStyle='#3a4a66';ctx.fillRect(0,240,W,60); // trottoar
  ctx.fillStyle='#8a5a2a';ctx.fillRect(320,90,80,150); // kiosk
  ctx.fillStyle='#f5c542';ctx.fillRect(320,90,80,22);
  ctx.fillStyle='#111';ctx.font='12px monospace';ctx.fillText('KIOSK',338,106);
  ctx.fillStyle='#242c3c';ctx.fillRect(10,10,W-20,16);
  ctx.fillStyle='#4caf50';ctx.fillRect(10,10,(W-20)*Math.min(1,cash/GOAL),16);
  ctx.fillStyle='#fff';ctx.fillText('Mal: '+GOAL+' kr',W/2-40,22);
  for(const cu of customers){
    if(cu.x<-20||cu.x>W+20)continue;
    const bob=cu.frame?2:0;
    ctx.fillStyle=cu.bought?'#4caf50':'#c8d0e0';
    ctx.fillRect(cu.x,cu.y+bob,14,26);
    ctx.fillStyle='#ffd9a0';ctx.fillRect(cu.x+2,cu.y-8+bob,10,10);
  }
  el('cash').textContent=cash|0;el('stock').textContent=stock;el('price').textContent=price;
  el('staff').textContent=staff;el('rep').textContent=rep|0;el('day').textContent=Math.min(day,MAXDAY);
  el('buyStock').disabled=dayActive||cash<80;
  el('hire').disabled=dayActive||cash<200||staff>=5;
  el('startDay').disabled=dayActive;
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Kiosken','Kop lager, satt pris, anstall - na '+GOAL+' kr pa '+MAXDAY+' dagar','management',null);
loop();
</script>
</body>
</html>";

    internal static string Html5ManagementDesignDoc(string prompt) =>
        "# Management / Tycoon (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nDriv en kiosk: kop lager, satt pris, anstall personal och overlev 14 dagar med vinstmal.\n\n" +
        "## Mekanik\n- **Efterfragan:** styrs av pris och rykte (lagt pris + gott rykte = fler kunder)\n" +
        "- **Personal:** begransar hur manga kunder som hinner betjanas per dag\n" +
        "- **Rykte:** upp vid kop, ner nar lagret ar tomt\n- **Vinst:** 5000 kr; **konkurs:** utan pengar och lager\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay\n- WebAudio-SFX (forsaljning, kop)\n" +
        "- Kund-animation (gang-bob), progressbar mot malet\n- Highscore i localStorage\n\n## Extension\n- Fler varutyper\n- Marknadsforing\n- Konkurrenter\n";

    // ---- Simulator / Farm (HTML5) ------------------------------------------------
    internal static string Html5Simulator(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Bondgarden</title>
<style>
  body{margin:0;background:#12200f;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column;font-family:system-ui,sans-serif;color:#fff}
  canvas{border:2px solid #2a3a1e;cursor:pointer}
  #hud{font:15px monospace;margin-bottom:8px}
</style>
</head>
<body>
<div id='hud'>Pengar <span id='cash'>20</span> kr &middot; Dag <span id='day'>1</span>/15 &middot; Vader <span id='wx'>Sol</span> &middot; Klicka: plantera (5 kr) / skorda (+14 kr)</div>
<canvas id='c'></canvas>
<script>" + ProductionKitJs + @"
const COLS=6,ROWS=4,T=90,GOAL=300,MAXDAY=15;
const c=document.getElementById('c'),ctx=c.getContext('2d');
c.width=COLS*T;c.height=ROWS*T+40;
let cash=20,frameN=0,day=1,rain=false,anim=0;
const plots=[];
for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++)plots.push({x,y,stage:-1,t:0});

c.onclick=e=>{
  if(!PKit.started||PKit.paused||PKit.ended)return;
  const gx=e.offsetX/T|0,gy=(e.offsetY-40)/T|0;
  const p=plots.find(q=>q.x===gx&&q.y===gy);
  if(!p)return;
  if(p.stage===-1&&cash>=5){cash-=5;p.stage=0;p.t=0;PKit.sfx.place();}
  else if(p.stage>=3){cash+=14;p.stage=-1;p.t=0;PKit.sfx.coin();}
};

function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  frameN++;anim=(anim+1)%40;
  if(frameN%420===0){day++;rain=Math.random()<0.35;
    if(day>MAXDAY){PKit.end(cash>=GOAL,cash);return;}}
  const growth=rain?2:1; // regn = dubbel vaxtfart
  for(const p of plots)if(p.stage>=0&&p.stage<3){p.t+=growth;if(p.t>=160){p.t=0;p.stage++;}}
}

function draw(){
  ctx.fillStyle=rain?'#39424e':'#7ec8e8';ctx.fillRect(0,0,c.width,40); // himmel
  if(rain){ctx.fillStyle='#9fc2e8';for(let i=0;i<12;i++)ctx.fillRect((i*61+anim*4)%c.width,8+(i*13+anim*3)%26,2,7);}
  else{ctx.fillStyle='#ffd94a';ctx.beginPath();ctx.arc(40,20,14+(anim<20?1:0),0,Math.PI*2);ctx.fill();}
  for(const p of plots){
    const px=p.x*T,py=p.y*T+40;
    ctx.fillStyle='#5a3d1e';ctx.fillRect(px+2,py+2,T-4,T-4);
    if(p.stage>=0){
      const sway=p.stage>0&&anim<20?1:0; // vind-anim pa plantan
      ctx.fillStyle=['#8afc8a','#4fd04f','#2fa32f','#ffd94a'][p.stage];
      const h=[10,26,44,58][p.stage];
      ctx.fillRect(px+T/2-5+sway,py+T-8-h,10,h);
      if(p.stage>=3){ctx.fillStyle='#ffb02e';ctx.beginPath();ctx.arc(px+T/2+sway,py+T-14-h,10,0,Math.PI*2);ctx.fill();}
    }
    ctx.strokeStyle='#1c2a12';ctx.strokeRect(px+2,py+2,T-4,T-4);
  }
  document.getElementById('cash').textContent=cash;
  document.getElementById('day').textContent=Math.min(day,MAXDAY);
  document.getElementById('wx').textContent=rain?'Regn':'Sol';
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Bondgarden','Plantera, vanta pa skord (regn vaxer snabbare) - na '+GOAL+' kr pa '+MAXDAY+' dagar','simulator',null);
loop();
</script>
</body>
</html>";

    internal static string Html5SimulatorDesignDoc(string prompt) =>
        "# Simulator / Bondgard (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEn odlingssimulator: plantera, vanta genom vaxtstadier, skorda och sal for att na det ekonomiska malet fore sasongens slut.\n\n" +
        "## Mekanik\n- **Plantera:** 5 kr per ruta; **skorda:** +14 kr vid stadie 3\n- **Vaxtstadier:** 4 (planta till mogen groda)\n" +
        "- **Vader:** regn ger dubbel vaxtfart, byts per dag\n- **Mal:** 300 kr fore dag 15\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay\n- WebAudio-SFX (plantering, skord)\n" +
        "- Animation: plantvind, sol-puls, regndroppar\n- Highscore i localStorage\n\n## Extension\n- Fler grodor med olika varden\n- Bevattning\n- Djur\n";

    // ---- Roguelike (HTML5) -------------------------------------------------------
    internal static string Html5Roguelike(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Grottan</title>
<style>
  body{margin:0;background:#0c0a10;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column}
  canvas{border:2px solid #2a2333}
  #hud{color:#fff;font:15px monospace;margin-bottom:8px}
</style>
</head>
<body>
<div id='hud'>HP <span id='hp'>20</span>/20 &middot; Guld <span id='gold'>0</span> &middot; Vaning <span id='fl'>1</span>/5 &middot; Ga pa fiender for att attackera</div>
<canvas id='c'></canvas>
<script>" + ProductionKitJs + @"
const COLS=15,ROWS=11,T=40,FINAL=5;
const c=document.getElementById('c'),ctx=c.getContext('2d');
c.width=COLS*T;c.height=ROWS*T;
let grid=[],player={x:1,y:1,hp:20,maxHp:20,gold:0},floor=1,enemies=[],stairs={x:0,y:0},anim=0,bossMsg=0;

function genFloor(){
  grid=[];enemies=[];
  for(let y=0;y<ROWS;y++){grid[y]=[];for(let x=0;x<COLS;x++)
    grid[y][x]=(x===0||y===0||x===COLS-1||y===ROWS-1||Math.random()<0.18)?1:0;}
  player.x=1;player.y=1;grid[1][1]=0;
  stairs={x:COLS-2,y:ROWS-2};grid[stairs.y][stairs.x]=0;
  // Grav en garanterad vag fran start till trappan sa vaningen alltid gar att klara.
  let cx=1,cy=1;
  while(cx!==stairs.x||cy!==stairs.y){
    if(cx<stairs.x&&Math.random()<0.6)cx++;else if(cy<stairs.y)cy++;else if(cx<stairs.x)cx++;
    grid[cy][cx]=0;}
  for(let i=0;i<3+floor;i++)placeThing(t=>enemies.push({x:t.x,y:t.y,hp:5+floor*2,frame:0}));
  // Sista vaningen vaktas av en boss - trappan ar last tills den ar besegrad.
  if(floor===FINAL)placeThing(t=>enemies.push({x:t.x,y:t.y,hp:24,frame:0,boss:true}));
  for(let i=0;i<4;i++)placeThing(t=>grid[t.y][t.x]=2); // guld
  for(let i=0;i<2;i++)placeThing(t=>grid[t.y][t.x]=3); // brygd
}
function placeThing(fn){
  for(let tries=0;tries<80;tries++){
    const x=1+(Math.random()*(COLS-2)|0),y=1+(Math.random()*(ROWS-2)|0);
    if(grid[y][x]===0&&!(x===player.x&&y===player.y)&&!(x===stairs.x&&y===stairs.y)
      &&!enemies.some(e=>e.x===x&&e.y===y)){fn({x,y});return;}}}

document.addEventListener('keydown',e=>{
  if(!PKit.started||PKit.paused||PKit.ended)return;
  let dx=0,dy=0;const k=e.key;
  if(k==='ArrowUp'||k==='w')dy=-1;else if(k==='ArrowDown'||k==='s')dy=1;
  else if(k==='ArrowLeft'||k==='a')dx=-1;else if(k==='ArrowRight'||k==='d')dx=1;else return;
  e.preventDefault();
  turn(dx,dy);});

function turn(dx,dy){
  const nx=player.x+dx,ny=player.y+dy;
  if(nx<0||ny<0||nx>=COLS||ny>=ROWS||grid[ny][nx]===1)return;
  const foe=enemies.find(e=>e.x===nx&&e.y===ny);
  if(foe){foe.hp-=4;PKit.sfx.hit();
    if(foe.hp<=0){enemies=enemies.filter(e=>e!==foe);player.gold+=foe.boss?50:5;PKit.sfx.coin();}}
  else{
    player.x=nx;player.y=ny;
    if(grid[ny][nx]===2){grid[ny][nx]=0;player.gold+=15;PKit.sfx.coin();}
    if(grid[ny][nx]===3){grid[ny][nx]=0;player.hp=Math.min(player.maxHp,player.hp+8);PKit.sfx.jump();}
    if(nx===stairs.x&&ny===stairs.y){
      if(floor>=FINAL){
        // Trappan ut ar last sa lange bossen lever.
        if(enemies.some(e=>e.boss)){bossMsg=90;PKit.sfx.hit();return;}
        PKit.end(true,player.gold+floor*100);return;}
      floor++;genFloor();return;}}
  // Fiendernas tur: ga mot spelaren, sla om intill.
  for(const en of enemies){
    const ex=Math.sign(player.x-en.x),ey=Math.sign(player.y-en.y);
    const tx=en.x+(Math.abs(player.x-en.x)>=Math.abs(player.y-en.y)?ex:0);
    const ty=en.y+(Math.abs(player.x-en.x)>=Math.abs(player.y-en.y)?0:ey);
    if(Math.abs(player.x-en.x)+Math.abs(player.y-en.y)===1){
      player.hp-=en.boss?5:2+(floor>>1);PKit.sfx.hit();
      if(player.hp<=0){PKit.end(false,player.gold+floor*50);return;}}
    else if(grid[ty]&&grid[ty][tx]===0&&!(tx===player.x&&ty===player.y)
      &&!enemies.some(o=>o!==en&&o.x===tx&&o.y===ty)){en.x=tx;en.y=ty;}}
}

function draw(){
  anim=(anim+1)%30;
  const torch=anim<15?0:8; // fackel-flimmer-anim
  for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){
    const v=grid[y][x];
    ctx.fillStyle=v===1?'rgb('+(42+torch)+','+(35+torch)+',51)':'#191420';
    ctx.fillRect(x*T,y*T,T,T);
    if(v===2){ctx.fillStyle='#f5c542';ctx.fillRect(x*T+14,y*T+14,12,12);}
    if(v===3){ctx.fillStyle='#e05561';ctx.fillRect(x*T+13,y*T+11,14,18);}
    ctx.strokeStyle='#0c0a10';ctx.strokeRect(x*T,y*T,T,T);}
  ctx.fillStyle='#9a7db8';ctx.fillRect(stairs.x*T+8,stairs.y*T+8,T-16,T-16);
  for(const en of enemies){if(anim%15===0)en.frame^=1;
    const pad=en.boss?4:8;
    ctx.fillStyle=en.boss?'#e0762e':'#d04848';
    ctx.fillRect(en.x*T+pad,en.y*T+pad+(en.frame?2:0),T-2*pad,T-2*pad);}
  ctx.fillStyle='#5ab8f0';ctx.fillRect(player.x*T+7,player.y*T+7,T-14,T-14);
  if(bossMsg>0){bossMsg--;ctx.fillStyle='#fff';ctx.font='16px monospace';
    ctx.fillText('Besegra bossen forst!',c.width/2-95,24);}
  document.getElementById('hp').textContent=Math.max(0,player.hp);
  document.getElementById('gold').textContent=player.gold;
  document.getElementById('fl').textContent=floor;
}
function loop(){try{draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Grottan','Piltangenter/WASD - turordning: du gar, fienderna gar · besegra bossen och na trappan pa vaning '+FINAL,'roguelike',genFloor);
loop();
</script>
</body>
</html>";

    internal static string Html5RoguelikeDesignDoc(string prompt) =>
        "# Roguelike (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nTurordningsbaserad grott-roguelike: procedurgenererade vaningar, permadeath, besegra bossen och na trappan pa vaning 5.\n\n" +
        "## Mekanik\n- **Turordning:** spelaren gar, sedan gar alla fiender ett steg mot spelaren\n" +
        "- **Strid:** ga in i en fiende for att sla (4 skada); intilliggande fiender slar tillbaka\n" +
        "- **Boss:** vaning 5 vaktas av en boss (24 HP, 5 skada, +50 guld) - trappan ut ar last tills den ar besegrad\n" +
        "- **Plock:** guld (+15), brygder (+8 HP)\n- **Procedur:** slumpade vaggar med garanterad grav vag till trappan\n" +
        "- **Svarighet:** fler och starkare fiender per vaning\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay\n- WebAudio-SFX (strid, plock, brygd)\n" +
        "- Animation: fackel-flimmer pa vaggar, fiende-bob\n- Highscore i localStorage\n\n## Extension\n- Utrustning\n- Fiendetyper\n- Fler bossar/miniboss per vaning\n";

    // ---- Memory / Card (HTML5) ---------------------------------------------------
    internal static string Html5Memory(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Memory</title>
<style>
  body{margin:0;background:#1a1424;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column}
  canvas{border:2px solid #3a2f50;cursor:pointer}
  #hud{color:#fff;font:16px monospace;margin-bottom:8px}
</style>
</head>
<body>
<div id='hud'>Par <span id='pr'>0</span>/8 &middot; Forsok kvar <span id='mv'>30</span></div>
<canvas id='c'></canvas>
<script>" + ProductionKitJs + @"
const COLS=4,ROWS=4,T=110,MOVES=30;
const c=document.getElementById('c'),ctx=c.getContext('2d');
c.width=COLS*T;c.height=ROWS*T;
const clrs=['#e05561','#e0a355','#e0d955','#7ee055','#55c8e0','#7e7ee0','#d055c8','#8a5a2a'];
let cards=[],first=null,second=null,flipback=0,moves=MOVES,pairs=0,anim=0;

function deal(){
  const vals=[];for(let i=0;i<8;i++){vals.push(i);vals.push(i);}
  for(let i=vals.length-1;i>0;i--){const j=Math.random()*(i+1)|0;[vals[i],vals[j]]=[vals[j],vals[i]];}
  cards=vals.map((v,i)=>({v,x:i%COLS,y:i/COLS|0,up:false,done:false,flip:0}));
}

c.onclick=e=>{
  if(!PKit.started||PKit.paused||PKit.ended||flipback>0)return;
  const gx=e.offsetX/T|0,gy=e.offsetY/T|0;
  const card=cards.find(k=>k.x===gx&&k.y===gy&&!k.done&&!k.up);
  if(!card)return;
  card.up=true;card.flip=6;PKit.sfx.place();
  if(!first){first=card;return;}
  second=card;moves--;
  if(first.v===second.v){first.done=second.done=true;pairs++;PKit.sfx.coin();first=second=null;
    if(pairs>=8){PKit.end(true,moves*20+200);return;}}
  else{flipback=30;PKit.sfx.hit();}
  if(moves<=0&&pairs<8)PKit.end(false,pairs*25);
};

function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  anim=(anim+1)%40;
  for(const k of cards)if(k.flip>0)k.flip--;
  if(flipback>0){flipback--;
    if(flipback===0){if(first)first.up=false;if(second)second.up=false;first=second=null;}}
}

function draw(){
  ctx.fillStyle='#221a30';ctx.fillRect(0,0,c.width,c.height);
  for(const k of cards){
    const px=k.x*T,py=k.y*T;
    const w=(T-14)*(k.flip>0?Math.abs(3-k.flip)/3:1); // flip-anim: kortet smalnar och vaxer
    const off=(T-14-w)/2;
    if(k.up||k.done){ctx.fillStyle=clrs[k.v];ctx.fillRect(px+7+off,py+7,w,T-14);
      ctx.fillStyle='#fff';ctx.font='34px monospace';
      if(k.flip===0)ctx.fillText(String.fromCharCode(65+k.v),px+T/2-12,py+T/2+12);}
    else{ctx.fillStyle='#3a2f50';ctx.fillRect(px+7+off,py+7,w,T-14);
      ctx.fillStyle='#55486e';ctx.fillRect(px+T/2-8,py+T/2-8,16,16);}
    if(k.done){ctx.globalAlpha=0.35+(anim<20?0.05:0);ctx.fillStyle='#000';
      ctx.fillRect(px+7,py+7,T-14,T-14);ctx.globalAlpha=1;}}
  document.getElementById('pr').textContent=pairs;
  document.getElementById('mv').textContent=moves;
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Memory','Vand tva kort - hitta alla 8 par pa '+MOVES+' forsok','memory',deal);
loop();
</script>
</body>
</html>";

    internal static string Html5MemoryDesignDoc(string prompt) =>
        "# Memory / Kortspel (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nKlassiskt memory med 8 par, forsoksbudget och flip-animation.\n\n" +
        "## Mekanik\n- **Vand:** tva kort per forsok; par ligger kvar\n- **Budget:** 30 forsok; farre anvanda forsok = hogre poang\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay\n- WebAudio-SFX (vand, par, miss)\n" +
        "- Flip-animation, dampning pa klara par\n- Highscore i localStorage\n\n## Extension\n- Storre brador\n- Tidsrekord\n- Tva spelare\n";

    // ---- Minesweeper (HTML5) -----------------------------------------------------
    internal static string Html5Minesweeper(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Minroj</title>
<style>
  body{margin:0;background:#171c17;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column}
  canvas{border:2px solid #2e3a2e;cursor:pointer}
  #hud{color:#fff;font:15px monospace;margin-bottom:8px}
</style>
</head>
<body>
<div id='hud'>Minor <span id='mn'>12</span> &middot; Flaggor <span id='fg'>0</span> &middot; Vansterklick: oppna &middot; Hogerklick: flagga</div>
<canvas id='c'></canvas>
<script>" + ProductionKitJs + @"
const N=10,T=44,MINES=12;
const c=document.getElementById('c'),ctx=c.getContext('2d');
c.width=N*T;c.height=N*T;
let cells=[],placed=false,flags=0,revealedCount=0,boom=0,anim=0;
const numClrs=['#000','#5ab8f0','#7ee055','#e05561','#9a7db8','#e0a355','#55c8e0','#fff','#888'];

function reset(){cells=[];placed=false;flags=0;revealedCount=0;boom=0;
  for(let y=0;y<N;y++)for(let x=0;x<N;x++)cells.push({x,y,mine:false,open:false,flag:false,n:0});}
function at(x,y){return(x<0||y<0||x>=N||y>=N)?null:cells[y*N+x];}
function neighbors(cl){const r=[];for(let dy=-1;dy<=1;dy++)for(let dx=-1;dx<=1;dx++){
  if(!dx&&!dy)continue;const q=at(cl.x+dx,cl.y+dy);if(q)r.push(q);}return r;}
function placeMines(safe){
  let m=0;
  while(m<MINES){const q=cells[Math.random()*cells.length|0];
    if(q.mine||q===safe||neighbors(safe).includes(q))continue;q.mine=true;m++;}
  for(const q of cells)q.n=neighbors(q).filter(o=>o.mine).length;
  placed=true;}
function reveal(cl){
  if(cl.open||cl.flag)return;
  cl.open=true;revealedCount++;
  if(cl.mine){boom=20;PKit.sfx.hit();for(const q of cells)if(q.mine)q.open=true;
    PKit.end(false,(revealedCount-1)*5);return;}
  PKit.sfx.place();
  if(cl.n===0){const stack=[cl];
    while(stack.length){const cur=stack.pop();
      for(const q of neighbors(cur))if(!q.open&&!q.flag&&!q.mine){
        q.open=true;revealedCount++;if(q.n===0)stack.push(q);}}}
  if(revealedCount>=N*N-MINES){PKit.sfx.win();PKit.end(true,1000+flags*10);}}

c.onclick=e=>{
  if(!PKit.started||PKit.paused||PKit.ended)return;
  const cl=at(e.offsetX/T|0,e.offsetY/T|0);
  if(!cl)return;
  if(!placed)placeMines(cl);
  reveal(cl);};
c.oncontextmenu=e=>{e.preventDefault();
  if(!PKit.started||PKit.paused||PKit.ended)return;
  const cl=at(e.offsetX/T|0,e.offsetY/T|0);
  if(!cl||cl.open)return;
  cl.flag=!cl.flag;flags+=cl.flag?1:-1;PKit.sfx.place();};

function draw(){
  anim=(anim+1)%30;
  if(boom>0)boom--;
  ctx.fillStyle=boom>0&&boom%4<2?'#3a1c1c':'#1d241d';
  ctx.fillRect(0,0,c.width,c.height);
  for(const cl of cells){
    const px=cl.x*T,py=cl.y*T;
    if(cl.open){ctx.fillStyle=cl.mine?'#5a1c1c':'#2a332a';ctx.fillRect(px+1,py+1,T-2,T-2);
      if(cl.mine){ctx.fillStyle='#e05561';ctx.beginPath();ctx.arc(px+T/2,py+T/2,9,0,Math.PI*2);ctx.fill();}
      else if(cl.n>0){ctx.fillStyle=numClrs[cl.n];ctx.font='20px monospace';ctx.fillText(cl.n,px+T/2-6,py+T/2+7);}}
    else{ctx.fillStyle='#37452f';ctx.fillRect(px+1,py+1,T-2,T-2);
      ctx.fillStyle='#43543a';ctx.fillRect(px+1,py+1,T-2,4);
      if(cl.flag){const wave=anim<15?0:2; // flagg-vaj-anim
        ctx.fillStyle='#e05561';ctx.fillRect(px+T/2-2,py+10,4,T-20);
        ctx.fillRect(px+T/2+2,py+10+wave,12,9);}}
    }
  document.getElementById('mn').textContent=MINES;
  document.getElementById('fg').textContent=flags;
}
function loop(){try{draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Minroj','Oppna alla sakra rutor - forsta klicket ar alltid sakert','minesweeper',reset);
loop();
</script>
</body>
</html>";

    internal static string Html5MinesweeperDesignDoc(string prompt) =>
        "# Minroj (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nKlassisk minroj 10x10 med 12 minor, flood-fill och garanterat sakert forsta klick.\n\n" +
        "## Mekanik\n- **Vansterklick:** oppna (nollor oppnar grannomrade)\n- **Hogerklick:** flagga\n" +
        "- **Forsta klicket:** minor placeras EFTER klicket, aldrig pa eller intill det\n- **Vinst:** alla sakra rutor oppna\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay\n- WebAudio-SFX (oppna, flagga, small)\n" +
        "- Animation: flagg-vaj, explosion-blink\n- Highscore i localStorage\n\n## Extension\n- Svarighetsgrader\n- Tidsrekord\n- Chord-klick\n";

    // ---- Quiz (HTML5) ------------------------------------------------------------
    internal static string Html5Quiz(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Fragesport</title>
<style>
  body{margin:0;background:#101828;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column;font-family:system-ui,sans-serif;color:#fff}
  #q{font-size:22px;margin:16px;max-width:640px;text-align:center;min-height:56px}
  canvas{margin-bottom:6px}
  .answers{display:grid;grid-template-columns:1fr 1fr;gap:10px;width:640px}
  .answers button{padding:16px;background:#243048;color:#fff;border:1px solid #3a4a6e;border-radius:10px;cursor:pointer;font-size:15px}
  #hud{font:15px monospace;margin-bottom:6px}
</style>
</head>
<body>
<div id='hud'>Poang <span id='sc'>0</span> &middot; Liv <span id='lv'>3</span> &middot; Fraga <span id='qn'>1</span>/<span id='qt'>20</span></div>
<canvas id='c' width='640' height='14'></canvas>
<div id='q'>...</div>
<div class='answers'>
  <button id='a0'></button><button id='a1'></button>
  <button id='a2'></button><button id='a3'></button>
</div>
<script>" + ProductionKitJs + @"
const QS=[
 {q:'Vilken planet ar narmast solen?',a:['Merkurius','Venus','Mars','Jupiter'],r:0},
 {q:'Vad ar Sveriges huvudstad?',a:['Goteborg','Uppsala','Stockholm','Malmo'],r:2},
 {q:'Hur manga ben har en spindel?',a:['6','8','10','12'],r:1},
 {q:'Vilket ar det storsta havet?',a:['Atlanten','Indiska oceanen','Norra ishavet','Stilla havet'],r:3},
 {q:'Vem malade Mona Lisa?',a:['Da Vinci','Picasso','Rembrandt','Monet'],r:0},
 {q:'Vad ar H2O?',a:['Syre','Vatten','Vate','Salt'],r:1},
 {q:'Vilket land har flest invanare?',a:['USA','Indien','Kina','Ryssland'],r:1},
 {q:'Hur manga minuter ar en fotbollsmatch?',a:['80','90','100','120'],r:1},
 {q:'Vilken ar varldens langsta flod?',a:['Amazonfloden','Nilen','Yangtze','Mississippi'],r:1},
 {q:'Vad heter var galax?',a:['Andromeda','Orion','Vintergatan','Centaurus'],r:2},
 {q:'Hur manga ben har en spindel?',a:['6','8','10','12'],r:1},
 {q:'Vilket grundamne har symbolen O?',a:['Guld','Syre','Silver','Osmium'],r:1},
 {q:'Vilket ar Sveriges hogsta berg?',a:['Kebnekaise','Sarektjakka','Helags','Akka'],r:0},
 {q:'Vem malade Mona Lisa?',a:['Rembrandt','Picasso','Leonardo da Vinci','Monet'],r:2},
 {q:'Vilket land har flest invanare?',a:['Indien','USA','Kina','Indonesien'],r:0},
 {q:'Vad ar 12 x 12?',a:['124','132','144','156'],r:2},
 {q:'Vilken planet ligger narmast solen?',a:['Venus','Merkurius','Mars','Jorden'],r:1},
 {q:'Hur manga strangar har en klassisk gitarr?',a:['4','5','6','7'],r:2},
 {q:'Vilket ar det snabbaste landdjuret?',a:['Gepard','Lejon','Struts','Antilop'],r:0},
 {q:'I vilken stad ligger Colosseum?',a:['Aten','Rom','Istanbul','Kairo'],r:1}];
const TIME=900; // 15 s i frames
const ctx=document.getElementById('c').getContext('2d');
let qi=0,score=0,lives=3,timer=TIME,answered=false,advanceT=0,flash=0,anim=0;
const el=id=>document.getElementById(id);

function show(){const Q=QS[qi];el('q').textContent=Q.q;
  for(let i=0;i<4;i++)el('a'+i).textContent=Q.a[i];
  timer=TIME;answered=false;}

for(let i=0;i<4;i++)el('a'+i).onclick=()=>{
  if(!PKit.started||PKit.paused||PKit.ended||answered)return;
  answered=true;advanceT=45;
  if(i===QS[qi].r){const bonus=Math.round(timer/60);score+=100+bonus;flash=1;PKit.sfx.coin();}
  else{lives--;flash=2;PKit.sfx.hit();
    if(lives<=0){PKit.end(false,score);return;}}};

function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  anim=(anim+1)%30;
  if(flash>0&&anim%5===0)flash=0;
  if(answered){advanceT--;
    if(advanceT<=0){qi++;
      if(qi>=QS.length){PKit.end(lives>0,score+lives*100);return;}
      show();}
    return;}
  timer--;
  if(timer<=0){answered=true;advanceT=45;lives--;PKit.sfx.hit();
    if(lives<=0)PKit.end(false,score);}
}

function draw(){
  ctx.fillStyle='#243048';ctx.fillRect(0,0,640,14);
  const frac=Math.max(0,timer/TIME);
  ctx.fillStyle=frac>0.4?'#4caf50':frac>0.2?'#e0a355':'#e05561'; // timer-anim: farg + krympande bar
  ctx.fillRect(0,0,640*frac,14);
  if(flash===1){ctx.fillStyle='rgba(76,175,80,.5)';ctx.fillRect(0,0,640,14);}
  if(flash===2){ctx.fillStyle='rgba(224,85,97,.5)';ctx.fillRect(0,0,640,14);}
  el('sc').textContent=score;el('lv').textContent=Math.max(0,lives);
  el('qn').textContent=Math.min(qi+1,QS.length);
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
document.getElementById('qt').textContent=QS.length;
PKit.init('Fragesport',QS.length+' fragor · 15 sekunder per fraga · 3 liv · snabba svar ger bonus','quiz',show);
loop();
</script>
</body>
</html>";

    internal static string Html5QuizDesignDoc(string prompt) =>
        "# Fragesport / Quiz (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEn fragesport med 20 inbyggda fragor (blandade amnen), tidspress och liv-system. HUD och intro laser antalet fran QS.length - fler fragor kraver bara nya rader i arrayen.\n\n" +
        "## Mekanik\n- **Timer:** 15 s per fraga; snabbt ratt svar ger tidsbonus\n- **Liv:** 3; fel svar eller timeout kostar ett\n" +
        "- **Vinst:** alla fragor med liv kvar (+100/liv)\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay\n- WebAudio-SFX (ratt, fel)\n" +
        "- Animation: timer-bar med fargskifte, ratt/fel-blink\n- Highscore i localStorage\n\n## Extension\n- Fler fragor/kategorier\n- Svarighetsniva\n- Blandade svarsordningar\n";

    // ---- Blockpussel / Tetris-typ (HTML5) ----------------------------------------
    internal static string Html5BlockPuzzle(string prompt) => @"<!DOCTYPE html>
<html lang='sv'>
<head>
<meta charset='UTF-8'>
<title>Blockpussel</title>
<style>
  body{margin:0;background:#0e1018;display:flex;justify-content:center;align-items:center;height:100vh;flex-direction:column}
  canvas{border:2px solid #2a3040}
  #hud{color:#fff;font:15px monospace;margin-bottom:8px}
</style>
</head>
<body>
<div id='hud'>Poang <span id='sc'>0</span> &middot; Rader <span id='ln'>0</span>/15 &middot; Niva <span id='lv'>1</span></div>
<canvas id='c'></canvas>
<script>" + ProductionKitJs + @"
const COLS=10,ROWS=18,T=26,GOAL=15;
const c=document.getElementById('c'),ctx=c.getContext('2d');
c.width=COLS*T+120;c.height=ROWS*T;
const SHAPES=[
 [[0,0],[1,0],[0,1],[1,1]],      // O
 [[-1,0],[0,0],[1,0],[2,0]],     // I
 [[-1,0],[0,0],[1,0],[0,1]],     // T
 [[-1,0],[0,0],[0,1],[1,1]],     // S
 [[0,0],[1,0],[-1,1],[0,1]],     // Z
 [[-1,0],[0,0],[1,0],[1,1]],     // J
 [[-1,0],[0,0],[1,0],[-1,1]]];   // L
const shapeClrs=['#e0d955','#55c8e0','#9a7db8','#7ee055','#e05561','#5a78e0','#e0a355'];
let grid=[],piece=null,next=null,fallT=0,score=0,lines=0,flashRows=[],flashT=0,anim=0;

function newGrid(){grid=[];for(let y=0;y<ROWS;y++){grid[y]=[];for(let x=0;x<COLS;x++)grid[y][x]=-1;}}
function spawn(){
  piece=next||{s:Math.random()*7|0,cells:null,x:4,y:1,rot:0};
  next={s:Math.random()*7|0,cells:null,x:4,y:1,rot:0};
  piece.x=4;piece.y=1;piece.rot=0;
  if(collides(piece,0,0,piece.rot)){PKit.end(false,score);}}
function cellsOf(p,rot){
  return SHAPES[p.s].map(([x,y])=>{let cx=x,cy=y;
    for(let r=0;r<rot;r++){const t=cx;cx=-cy;cy=t;}
    return [cx,cy];});}
function collides(p,dx,dy,rot){
  return cellsOf(p,rot).some(([cx,cy])=>{
    const gx=p.x+cx+dx,gy=p.y+cy+dy;
    return gx<0||gx>=COLS||gy>=ROWS||(gy>=0&&grid[gy][gx]>=0);});}
function lockPiece(){
  for(const [cx,cy] of cellsOf(piece,piece.rot)){
    const gy=piece.y+cy,gx=piece.x+cx;
    if(gy>=0&&gy<ROWS)grid[gy][gx]=piece.s;}
  PKit.sfx.place();
  const full=[];
  for(let y=0;y<ROWS;y++)if(grid[y].every(v=>v>=0))full.push(y);
  if(full.length){flashRows=full;flashT=14;
    score+=[0,100,300,500,800][full.length];lines+=full.length;PKit.sfx.coin();}
  else spawn();}
function clearFlashed(){
  for(const y of flashRows){grid.splice(y,1);grid.unshift(Array(COLS).fill(-1));}
  flashRows=[];
  if(lines>=GOAL){PKit.sfx.win();PKit.end(true,score+400);return;}
  spawn();}

document.addEventListener('keydown',e=>{
  if(!PKit.started||PKit.paused||PKit.ended||!piece||flashRows.length)return;
  const k=e.key;
  if(k==='ArrowLeft'||k==='a'){if(!collides(piece,-1,0,piece.rot))piece.x--;}
  else if(k==='ArrowRight'||k==='d'){if(!collides(piece,1,0,piece.rot))piece.x++;}
  else if(k==='ArrowUp'||k==='w'){const nr=(piece.rot+1)%4;if(!collides(piece,0,0,nr))piece.rot=nr;}
  else if(k==='ArrowDown'||k==='s'){if(!collides(piece,0,1,piece.rot))piece.y++;}
  else if(k===' '){while(!collides(piece,0,1,piece.rot))piece.y++;lockPiece();}
  if(k.startsWith('Arrow')||k===' ')e.preventDefault();});

function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  anim=(anim+1)%30;
  if(flashRows.length){flashT--;if(flashT<=0)clearFlashed();return;}
  if(!piece)return;
  fallT++;
  const speed=Math.max(6,34-(lines*2|0));
  if(fallT>=speed){fallT=0;
    if(!collides(piece,0,1,piece.rot))piece.y++;
    else lockPiece();}}

function drawCell(gx,gy,s){
  ctx.fillStyle=shapeClrs[s];ctx.fillRect(gx*T+1,gy*T+1,T-2,T-2);
  ctx.fillStyle='rgba(255,255,255,.25)';ctx.fillRect(gx*T+1,gy*T+1,T-2,4);}
function draw(){
  ctx.fillStyle='#131624';ctx.fillRect(0,0,c.width,c.height);
  ctx.fillStyle='#0b0d16';ctx.fillRect(0,0,COLS*T,ROWS*T);
  for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++)if(grid[y][x]>=0)drawCell(x,y,grid[y][x]);
  for(const y of flashRows){ctx.fillStyle=flashT%4<2?'#fff':'#888'; // rad-rensnings-anim
    ctx.fillRect(0,y*T,COLS*T,T);}
  if(piece&&!flashRows.length)
    for(const [cx,cy] of cellsOf(piece,piece.rot))
      if(piece.y+cy>=0)drawCell(piece.x+cx,piece.y+cy,piece.s);
  ctx.fillStyle='#fff';ctx.font='13px monospace';ctx.fillText('Nasta:',COLS*T+18,30);
  if(next)for(const [cx,cy] of cellsOf(next,0)){
    ctx.fillStyle=shapeClrs[next.s];ctx.fillRect(COLS*T+52+cx*16,60+cy*16,14,14);}
  document.getElementById('sc').textContent=score;
  document.getElementById('ln').textContent=lines;
  document.getElementById('lv').textContent=1+(lines/5|0);
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
PKit.init('Blockpussel','Piltangenter/WASD · upp roterar · mellanslag slapper · rensa '+GOAL+' rader','blockpuzzle',()=>{newGrid();spawn();});
loop();
</script>
</body>
</html>";

    internal static string Html5BlockPuzzleDesignDoc(string prompt) =>
        "# Blockpussel (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt fallande-block-pussel med 7 pjaser, rotation, nasta-pjas-forhandsvisning och okande fart.\n\n" +
        "## Mekanik\n- **Styrning:** vanster/hoger flyttar, upp roterar, ner mjukfaller, mellanslag hardfaller\n" +
        "- **Rader:** fulla rader rensas (100/300/500/800 poang); farten okar med rensade rader\n" +
        "- **Vinst:** 15 rader; **forlust:** stapeln nar toppen\n\n" +
        "## Produktion\n- Titelskarm, paus (Esc/P), vinst/forlust-overlay\n- WebAudio-SFX (las, rensning)\n" +
        "- Animation: rad-rensnings-blink, glansen pa block\n- Highscore i localStorage\n\n## Extension\n- Hold-funktion\n- Ghost piece\n- Kombo-poang\n";
}
