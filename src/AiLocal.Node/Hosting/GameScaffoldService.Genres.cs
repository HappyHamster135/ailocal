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
    internal static string DetectGenre(string prompt)
    {
        var p = (prompt ?? "").ToLowerInvariant();
        // Word-START matching, not raw substring: "plattfORMsspel" must not
        // trigger the snake keyword "orm", "moBIL" must not trigger "bil".
        // Word-start (not whole-word) so Swedish compounds still hit:
        // "ormspel" -> orm, "bilspel" -> bil. "car" stays whole-word so
        // "card"/"cards" never reads as racing.
        if (WordStart(p, "snake", "orm", "nokia")) return "snake";
        if (WordStart(p, "idle", "clicker", "klicker", "klickspel", "incremental", "cookie")) return "idle";
        if (WordStart(p, "breakout", "arkanoid", "brick", "tegel", "paddle", "pong")) return "breakout";
        if (WordStart(p, "shooter", "bullet", "shmup", "skjut", "shoot")) return "shooter";
        if (WordStart(p, "racing", "racer", "race", "bil", "kart") || WordExact(p, "car", "cars")) return "racing";
        if (WordStart(p, "puzzle", "pussel", "match", "bejeweled", "swap")) return "puzzle";
        if (p.Contains("tower defense") || p.Contains("towerdefense")
            || WordExact(p, "td") || WordStart(p, "torn", "wave")) return "towerdefense";
        if (WordStart(p, "rpg", "adventure", "aventyr", "äventyr", "dungeon")
            || p.Contains("top-down") || p.Contains("topdown")) return "rpg";
        return "platformer";
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
<div id='hud'>Guld <span id='gold'>0</span> &middot; Per klick <span id='cv'>1</span> &middot; Per sekund <span id='ps'>0</span></div>
<button id='mine'>&#9935;</button>
<div class='shop'>
  <button id='buyPick'>Battre hacka (+1/klick)<br>Kostar <span id='pickCost'>10</span></button>
  <button id='buyMiner'>Gruvarbetare (+1/s)<br>Kostar <span id='minerCost'>25</span></button>
  <button id='buyRig'>Borrigg (+8/s)<br>Kostar <span id='rigCost'>200</span></button>
</div>
<canvas id='c' width='420' height='60' style='margin-top:10px'></canvas>
<script>" + ProductionKitJs + @"
const GOAL=10000;
let gold=0,perClick=1,perSec=0,pickCost=10,minerCost=25,rigCost=200,anim=0;
const ctx=document.getElementById('c').getContext('2d');
const el=id=>document.getElementById(id);

el('mine').onclick=()=>{
  if(!PKit.started||PKit.paused||PKit.ended)return;
  gold+=perClick;PKit.sfx.coin();};
el('buyPick').onclick=()=>{if(PKit.ended||gold<pickCost)return;gold-=pickCost;perClick++;pickCost=Math.ceil(pickCost*1.6);PKit.sfx.place();};
el('buyMiner').onclick=()=>{if(PKit.ended||gold<minerCost)return;gold-=minerCost;perSec+=1;minerCost=Math.ceil(minerCost*1.7);PKit.sfx.place();};
el('buyRig').onclick=()=>{if(PKit.ended||gold<rigCost)return;gold-=rigCost;perSec+=8;rigCost=Math.ceil(rigCost*1.8);PKit.sfx.place();};

let tick=0;
function update(){
  if(!PKit.started||PKit.paused||PKit.ended)return;
  tick++;anim=(anim+1)%60;
  if(tick%60===0&&perSec>0){gold+=perSec;}
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
PKit.init('Guldgruvan','Klicka pa gruvan for guld · kop uppgraderingar · na '+GOAL+' guld','idle',null);
loop();
</script>
</body>
</html>";

    internal static string Html5IdleDesignDoc(string prompt) =>
        "# Idle / Clicker (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt klickspel med tre uppgraderingsspar och ett tydligt mal (10 000 guld) sa rundan har ett slut.\n\n" +
        "## Mekanik\n- **Klick:** +guld per klick\n- **Uppgraderingar:** hacka (+1/klick), gruvarbetare (+1/s), borrigg (+8/s); priser vaxer exponentiellt\n" +
        "- **Vinst:** 10 000 guld\n\n## Produktion\n- Titelskarm, paus (Esc/P), vinst-overlay med omstart\n- WebAudio-SFX (klick, kop)\n" +
        "- Skimmer-animation pa progressbaren, tryck-animation pa knappen\n- Highscore i localStorage\n\n## Extension\n- Prestige-system\n- Fler byggnader\n- Offline-produktion\n";

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
}
