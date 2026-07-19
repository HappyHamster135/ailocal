namespace AiLocal.Node.Hosting;

/// <summary>Genre extensions for GameScaffoldService — 5 new playable HTML5 game types.</summary>
public partial class GameScaffoldService
{
    /// <summary>Detect the game genre from prompt keywords.</summary>
    internal static string DetectGenre(string prompt)
    {
        var p = (prompt ?? "").ToLowerInvariant();
        if (ContainsAny(p, "rpg", "adventure", "aventyr", "top-down", "topdown", "dungeon")) return "rpg";
        if (ContainsAny(p, "racing", "racer", "race", "car", "bil", "kart")) return "racing";
        if (ContainsAny(p, "puzzle", "pussel", "match", "match3", "bejeweled", "swap")) return "puzzle";
        if (ContainsAny(p, "tower defense", "towerdefense", "td", "torn", "wave")) return "towerdefense";
        if (ContainsAny(p, "shooter", "bullet", "shmup", "skjut", "shoot", "hell")) return "shooter";
        return "platformer";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(v => text.Contains(v));

    // ---- RPG / Top-Down Adventure (HTML5) ---------------------------------------
    internal static string Html5Rpg(string prompt) =>
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><title>Adventure</title>" +
        "<style>body{margin:0;background:#111;display:flex;justify-content:center;align-items:center;" +
        "height:100vh;overflow:hidden}canvas{border:2px solid #333}" +
        "#ui{position:absolute;top:10px;left:10px;color:#fff;font:14px monospace;pointer-events:none}</style>" +
        "</head><body><canvas id=\"c\"></canvas><div id=\"ui\">HP:<span id=\"hp\">100</span> " +
        "XP:<span id=\"xp\">0</span> Lv:<span id=\"lv\">1</span> Gold:<span id=\"gold\">0</span></div>" +
        "<script>const c=document.getElementById('c'),ctx=c.getContext('2d'),W=800,H=600;" +
        "c.width=W;c.height=H;const keys={};document.onkeydown=e=>{keys[e.key]=true;" +
        "if(e.key==='e')interact();if(e.key==='i')invOpen=!invOpen};document.onkeyup=e=>keys[e.key]=false;" +
        "let player={x:400,y:300,w:24,h:24,speed:3,hp:100,maxHp:100,xp:0,level:1,gold:0};" +
        "let inventory=[],invOpen=false,questDone=false;" +
        "let npcs=[{x:200,y:250,t:'Welcome! Find the golden amulet in the cave east of here.'}," +
        "{x:600,y:350,t:'The cave is dangerous — come back stronger!'},{x:150,y:450,t:'I sell potions.'}];" +
        "let items=[{x:500,y:200,n:'Sword',d:'+10 dmg'},{x:650,y:450,n:'Potion',d:'+25 HP'}," +
        "{x:100,y:180,n:'Golden Amulet',d:'QUEST'}];" +
        "let enemies=[{x:350,y:150,hp:20,n:'Slime'},{x:550,y:500,hp:35,n:'Goblin'},{x:700,y:280,hp:15,n:'Bat'}];" +
        "function update(){let dx=0,dy=0;" +
        "if(keys['ArrowLeft']||keys['a'])dx=-1;if(keys['ArrowRight']||keys['d'])dx=1;" +
        "if(keys['ArrowUp']||keys['w'])dy=-1;if(keys['ArrowDown']||keys['s'])dy=1;" +
        "if(dx&&dy){dx*=0.7;dy*=0.7;}player.x+=dx*player.speed;player.y+=dy*player.speed;" +
        "player.x=Math.max(12,Math.min(W-12,player.x));player.y=Math.max(12,Math.min(H-12,player.y));" +
        "for(let e of enemies)if(e.hp>0){let d=Math.hypot(player.x-e.x,player.y-e.y);" +
        "if(d<30)player.hp-=0.3;if(d<40&&keys[' ']){e.hp-=hasItem('Sword')?10:5;" +
        "if(e.hp<=0){player.xp+=10+Math.random()*5|0;player.gold+=Math.random()*5+1|0;}}}" +
        "enemies=enemies.filter(e=>e.hp>0);" +
        "if(player.xp>=player.level*30){player.level++;player.maxHp+=10;player.hp=player.maxHp;player.xp=0;}" +
        "if(player.hp<=0){player.hp=player.maxHp;player.x=400;player.y=300;player.gold=Math.max(0,player.gold-5);}" +
        "for(let it of items)if(it.x>-900){let d=Math.hypot(player.x-it.x,player.y-it.y);" +
        "if(d<25){inventory.push(it);it.x=-999;if(it.n==='Potion'){player.hp=Math.min(player.maxHp,player.hp+25);inventory.pop();}" +
        "if(it.n==='Golden Amulet')questDone=true;}}items=items.filter(it=>it.x>-900);}" +
        "function hasItem(n){return inventory.some(i=>i.n===n);}" +
        "function interact(){for(let n of npcs)if(Math.hypot(player.x-n.x,player.y-n.y)<50){alert(n.t);return;}}" +
        "function draw(){ctx.fillStyle='#0a0';ctx.fillRect(0,0,W,H);" +
        "ctx.fillStyle=questDone?'#ff0':'#4af';ctx.fillRect(player.x-12,player.y-12,24,24);" +
        "for(let n of npcs){ctx.fillStyle='#f80';ctx.fillRect(n.x-10,n.y-10,20,20);}" +
        "for(let e of enemies)if(e.hp>0){ctx.fillStyle='#f44';ctx.fillRect(e.x-10,e.y-10,20,20);}" +
        "for(let it of items)if(it.x>-900){ctx.fillStyle=it.n==='Golden Amulet'?'#ff0':'#0ff';ctx.fillRect(it.x-8,it.y-8,16,16);}" +
        "document.getElementById('hp').textContent=player.hp|0;document.getElementById('xp').textContent=player.xp;" +
        "document.getElementById('lv').textContent=player.level;document.getElementById('gold').textContent=player.gold;" +
        "if(questDone){ctx.fillStyle='rgba(0,0,0,0.7)';ctx.fillRect(0,0,W,H);" +
        "ctx.fillStyle='#ff0';ctx.font='36px monospace';ctx.fillText('QUEST COMPLETE!',200,300);}" +
        "if(invOpen){ctx.fillStyle='rgba(0,0,0,0.85)';ctx.fillRect(50,50,W-100,H-100);" +
        "ctx.fillStyle='#fff';ctx.font='16px monospace';ctx.fillText('INVENTORY (I to close)',80,80);" +
        "for(let i=0;i<inventory.length;i++)ctx.fillText(inventory[i].n+': '+inventory[i].d,80,110+i*25);}}" +
        "function loop(){update();draw();requestAnimationFrame(loop);}loop();</script></body></html>";

    internal static string Html5RpgDesignDoc(string prompt) =>
        "# RPG / Top-Down Adventure (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt top-down aventyr i webblasaren med NPCs, fiender, items, inventory och quest-system.\n\n" +
        "## Mekanik\n- **Rorelse:** piltangenter/WASD\n- **Interagera:** E (prata med NPCs)\n" +
        "- **Inventory:** I (oppna/stang)\n- **Combat:** mellanslag attack\n- **XP/Level:** fa XP fran fiender\n" +
        "- **Quest:** hitta Golden Amulet for att vinna\n\n## Extension\n- Fler NPCs med dialogtrad\n- Fler quests\n- Dungeon-nivaer\n";

    // ---- Racing (HTML5) ----------------------------------------------------------
    internal static string Html5Racing(string prompt) =>
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><title>Racer</title>" +
        "<style>body{margin:0;background:#222;display:flex;justify-content:center;align-items:center;" +
        "height:100vh}canvas{border:2px solid#444}" +
        "#hud{position:absolute;top:10px;left:10px;color:#fff;font:14px monospace}</style></head>" +
        "<body><canvas id=\"c\"></canvas><div id=\"hud\">Speed:<span id=\"spd\">0</span> " +
        "Lap:<span id=\"lap\">1/3</span> Time:<span id=\"time\">0s</span></div>" +
        "<script>const c=document.getElementById('c'),ctx=c.getContext('2d'),W=800,H=500;" +
        "c.width=W;c.height=H;const keys={};document.onkeydown=e=>{keys[e.key]=true;if(e.key==='r')reset();};" +
        "document.onkeyup=e=>keys[e.key]=false;" +
        "let car={x:120,y:250,a:0,s:0},lap=1,cp=[!1,!1,!1],st=Date.now();" +
        "let walls=[{x:100,y:100,w:600,h:10},{x:100,y:390,w:600,h:10}," +
        "{x:100,y:100,w:10,h:300},{x:690,y:100,w:10,h:300}," +
        "{x:300,y:200,w:200,h:10},{x:300,y:290,w:200,h:10}];" +
        "function up(){if(keys['ArrowUp']||keys['w'])car.s=Math.min(8,car.s+0.2);" +
        "else if(keys['ArrowDown']||keys['s'])car.s=Math.max(-3,car.s-0.3);else car.s*=0.96;" +
        "if(keys['ArrowLeft']||keys['a'])car.a-=0.04*Math.sign(car.s||1);" +
        "if(keys['ArrowRight']||keys['d'])car.a+=0.04*Math.sign(car.s||1);" +
        "car.x+=Math.cos(car.a)*car.s;car.y+=Math.sin(car.a)*car.s;" +
        "car.x=Math.max(10,Math.min(W-10,car.x));car.y=Math.max(10,Math.min(H-10,car.y));" +
        "for(let w of walls)if(car.x>w.x&&car.x<w.x+w.w&&car.y>w.y&&car.y<w.y+w.h){car.s*=-0.5;" +
        "car.x-=Math.cos(car.a)*car.s*2;car.y-=Math.sin(car.a)*car.s*2;}" +
        "if(car.x>650&&car.y>80&&car.y<120&&!cp[0])cp[0]=!0;" +
        "if(car.x>650&&car.y>380&&car.y<420&&cp[0]&&!cp[1])cp[1]=!0;" +
        "if(car.x<150&&car.y>80&&car.y<120&&cp[0]&&cp[1]&&!cp[2]){cp=[!1,!1,!1];lap++;}" +
        "if(lap>3){alert('Race done! '+((Date.now()-st)/1000).toFixed(1)+'s');reset();}}" +
        "function reset(){car={x:120,y:250,a:0,s:0};lap=1;cp=[!1,!1,!1];st=Date.now();}" +
        "function draw(){ctx.fillStyle='#333';ctx.fillRect(0,0,W,H);" +
        "for(let w of walls){ctx.fillStyle='#555';ctx.fillRect(w.x,w.y,w.w,w.h);}" +
        "ctx.save();ctx.translate(car.x,car.y);ctx.rotate(car.a);" +
        "ctx.fillStyle='#e33';ctx.fillRect(-15,-8,30,16);ctx.fillStyle='#fff';ctx.fillRect(-3,-8,6,16);ctx.restore();" +
        "document.getElementById('spd').textContent=Math.abs(car.s*10)|0;" +
        "document.getElementById('lap').textContent=lap+'/3';" +
        "document.getElementById('time').textContent=((Date.now()-st)/1000).toFixed(1)+'s';}" +
        "function loop(){up();draw();requestAnimationFrame(loop);}loop();</script></body></html>";

    internal static string Html5RacingDesignDoc(string prompt) =>
        "# Racing (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt top-down racing-spel med lap-baserad bana och wall collision.\n\n" +
        "## Mekanik\n- **Styrning:** piltangenter/WASD\n- **Laps:** 3 varv\n- **Restart:** R\n\n" +
        "## Extension\n- AI-motstandare\n- Fler banor\n- Power-ups\n";

    // ---- Puzzle / Match-3 (HTML5) ------------------------------------------------
    internal static string Html5Puzzle(string prompt) =>
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><title>Match-3</title>" +
        "<style>body{margin:0;background:#1a1a2e;display:flex;justify-content:center;align-items:center;" +
        "height:100vh;flex-direction:column}canvas{border:2px solid#444}" +
        "#score{color:#fff;font:20px monospace;margin-bottom:10px}</style></head>" +
        "<body><div id=\"score\">Score:<span id=\"sc\">0</span></div><canvas id=\"c\"></canvas>" +
        "<script>const COLS=8,ROWS=8,T=60,clrs=['#e44','#4e4','#44e','#ee4','#e4e','#4ee'];" +
        "const c=document.getElementById('c'),ctx=c.getContext('2d');c.width=COLS*T;c.height=ROWS*T;" +
        "let grid=[],sel=null,score=0;" +
        "function init(){grid=[];for(let y=0;y<ROWS;y++){grid[y]=[];for(let x=0;x<COLS;x++)grid[y][x]=Math.random()*clrs.length|0;}}" +
        "c.onclick=e=>{let x=e.offsetX/T|0,y=e.offsetY/T|0;if(!sel){sel={x,y};return;}" +
        "let dx=Math.abs(sel.x-x),dy=Math.abs(sel.y-y);" +
        "if((dx===1&&dy===0)||(dx===0&&dy===1)){[grid[sel.y][sel.x],grid[y][x]]=[grid[y][x],grid[sel.y][sel.x]];" +
        "if(!chk()){[grid[sel.y][sel.x],grid[y][x]]=[grid[y][x],grid[sel.y][sel.x]];}}sel=null;};" +
        "function chk(){let m=!1;" +
        "for(let y=0;y<ROWS;y++)for(let x=0;x<COLS-2;x++)if(grid[y][x]===grid[y][x+1]&&grid[y][x]===grid[y][x+2])" +
        "{grid[y][x]=grid[y][x+1]=grid[y][x+2]=-1;m=!0;score+=30;}" +
        "for(let x=0;x<COLS;x++)for(let y=0;y<ROWS-2;y++)if(grid[y][x]===grid[y+1][x]&&grid[y][x]===grid[y+2][x])" +
        "{grid[y][x]=grid[y+1][x]=grid[y+2][x]=-1;m=!0;score+=30;}if(m){col();return !0;}return !1;}" +
        "function col(){for(let x=0;x<COLS;x++){let a=[];for(let y=ROWS-1;y>=0;y--)if(grid[y][x]!==-1)a.push(grid[y][x]);" +
        "while(a.length<ROWS)a.push(Math.random()*clrs.length|0);a.reverse();for(let y=0;y<ROWS;y++)grid[y][x]=a[y];}chk();}" +
        "function draw(){ctx.fillStyle='#222';ctx.fillRect(0,0,c.width,c.height);" +
        "for(let y=0;y<ROWS;y++)for(let x=0;x<COLS;x++){ctx.fillStyle=clrs[grid[y][x]];" +
        "ctx.fillRect(x*T+2,y*T+2,T-4,T-4);ctx.strokeStyle='#fff';ctx.strokeRect(x*T+2,y*T+2,T-4,T-4);}" +
        "if(sel){ctx.strokeStyle='#fff';ctx.lineWidth=3;ctx.strokeRect(sel.x*T,sel.y*T,T,T);}" +
        "document.getElementById('sc').textContent=score;}" +
        "function loop(){draw();requestAnimationFrame(loop);}init();loop();</script></body></html>";

    internal static string Html5PuzzleDesignDoc(string prompt) =>
        "# Puzzle / Match-3 (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt klassiskt match-3-pussel. Swappa intilliggande rutor for att matcha 3+ av samma farg.\n\n" +
        "## Mekanik\n- **Klicka:** valj tva intilliggande rutor for att swappa\n- **Match:** 3+ i rad/kolumn\n" +
        "- **Collapse:** matchade rutor forsvinner, nya faller ner\n\n## Extension\n- Special tiles\n- Tidsbegransning\n- Combo-multiplikator\n";

    // ---- Tower Defense (HTML5) ---------------------------------------------------
    internal static string Html5TowerDefense(string prompt) =>
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><title>Tower Defense</title>" +
        "<style>body{margin:0;background:#1a2a1a;display:flex;justify-content:center;align-items:center;" +
        "height:100vh;flex-direction:column}canvas{border:2px solid#444}" +
        "#hud{color:#fff;font:14px monospace;margin-bottom:8px}</style></head>" +
        "<body><div id=\"hud\">Gold:<span id=\"gld\">200</span> Wave:<span id=\"wv\">1</span> " +
        "Lives:<span id=\"lvs\">20</span> | 1-3=tower</div><canvas id=\"c\"></canvas>" +
        "<script>const W=800,H=500,T=40,C=W/T|0,R=H/T|0;" +
        "const c=document.getElementById('c'),ctx=c.getContext('2d');c.width=W;c.height=H;" +
        "let gold=200,lives=20,wave=1,en=[],tw=[],pj=[],sp=0,st=0;" +
        "const path=[{x:0,y:5},{x:5,y:5},{x:5,y:2},{x:10,y:2},{x:10,y:8},{x:15,y:8},{x:15,y:4},{x:19,y:4}];" +
        "const types=[{c:50,d:15,r:3,cl:'#48f'},{c:100,d:30,r:3.5,cl:'#f84'},{c:75,d:8,r:2.5,cl:'#8f4'}];" +
        "document.onkeydown=e=>{if(e.key==='1')st=0;if(e.key==='2')st=1;if(e.key==='3')st=2;};" +
        "c.onclick=e=>{let tx=e.offsetX/T|0,ty=e.offsetY/T|0,t=types[st];" +
        "if(gold>=t.c&&!tw.some(w=>w.x===tx&&w.y===ty)&&!path.some(p=>p.x===tx&&p.y===ty))" +
        "{tw.push({x:tx,y:ty,tp:st,cd:0});gold-=t.c;}};" +
        "function spawn(){let n=5+wave*2;for(let i=0;i<n;i++)en.push({x:0,y:5*T+T/2,pi:0,hp:10+wave*5,mhp:10+wave*5,sp:1+wave*.1,rw:5+wave});}" +
        "function up(){for(let e of en){if(e.pi>=path.length-1){lives--;e.hp=0;continue;}" +
        "let tgt=path[e.pi+1],dx=(tgt.x-e.x/T)*T/20,dy=(tgt.y-e.y/T)*T/20;" +
        "e.x+=dx*e.sp;e.y+=dy*e.sp;" +
        "if(Math.abs(tgt.x*T+T/2-e.x)<5&&Math.abs(tgt.y*T+T/2-e.y)<5)e.pi++;}" +
        "en=en.filter(e=>e.hp>0);" +
        "for(let t of tw){t.cd=Math.max(0,t.cd-0.016);if(t.cd<=0){let tp=types[t.tp],r=tp.r*T;" +
        "let tgt=en.find(e=>Math.hypot(e.x-(t.x*T+T/2),e.y-(t.y*T+T/2))<r);" +
        "if(tgt){pj.push({x:t.x*T+T/2,y:t.y*T+T/2,tx:tgt.x,ty:tgt.y,d:tp.d,cl:tp.cl});t.cd=1.2;}}}" +
        "for(let p of pj){let dx=p.tx-p.x,dy=p.ty-p.y,d=Math.hypot(dx,dy);if(d<8){for(let e of en)" +
        "if(Math.hypot(e.x-p.tx,e.y-p.ty)<20){e.hp-=p.d;if(e.hp<=0)gold+=e.rw;break;}p.hit=!0;continue;}" +
        "p.x+=dx/d*6;p.y+=dy/d*6;}pj=pj.filter(p=>!p.hit);sp--;" +
        "if(sp<=0&&en.length===0){wave++;spawn();sp=60;}if(lives<=0){alert('Game Over! Wave '+wave);location.reload();}}" +
        "function draw(){ctx.fillStyle='#2a3';ctx.fillRect(0,0,W,H);" +
        "for(let i=1;i<path.length;i++){ctx.fillStyle='#863';ctx.fillRect(path[i-1].x*T,path[i-1].y*T,(path[i].x-path[i-1].x)*T+T,(path[i].y-path[i-1].y)*T+T);}" +
        "for(let t of tw){let tp=types[t.tp];ctx.fillStyle=tp.cl;ctx.fillRect(t.x*T+4,t.y*T+4,T-8,T-8);ctx.strokeStyle='#fff';ctx.strokeRect(t.x*T+4,t.y*T+4,T-8,T-8);}" +
        "for(let e of en){ctx.fillStyle='#f44';ctx.fillRect(e.x-8,e.y-8,16,16);ctx.fillStyle='#fff';ctx.fillRect(e.x-8,e.y-12,16,3);ctx.fillStyle='#0f0';ctx.fillRect(e.x-8,e.y-12,16*e.hp/e.mhp,3);}" +
        "for(let p of pj){ctx.fillStyle=p.cl;ctx.beginPath();ctx.arc(p.x,p.y,3,0,Math.PI*2);ctx.fill();}" +
        "document.getElementById('gld').textContent=gold;document.getElementById('wv').textContent=wave;document.getElementById('lvs').textContent=lives;}" +
        "function loop(){up();draw();requestAnimationFrame(loop);}spawn();loop();</script></body></html>";

    internal static string Html5TdDesignDoc(string prompt) =>
        "# Tower Defense (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nPlacera torn langs en bana for att stoppa vagon av fiender.\n\n" +
        "## Mekanik\n- **Placera torn:** klicka (kostar guld)\n- **Tornval:** 1-3\n- **Gold:** fran dodade fiender\n" +
        "- **Lives:** fiender som nar slutet tar lives\n\n## Extension\n- Fler torntyper/uppgraderingar\n- Boss-fiender\n- Fler banor\n";

    // ---- Top-Down Shooter (HTML5) ------------------------------------------------
    internal static string Html5Shooter(string prompt) =>
        "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><title>Shooter</title>" +
        "<style>body{margin:0;background:#000;display:flex;justify-content:center;align-items:center;" +
        "height:100vh;overflow:hidden}canvas{border:2px solid#333}" +
        "#hud{position:absolute;top:10px;left:10px;color:#fff;font:14px monospace;pointer-events:none}</style></head>" +
        "<body><canvas id=\"c\"></canvas><div id=\"hud\">Score:<span id=\"sc\">0</span> " +
        "HP:<span id=\"hp\">100</span> Wave:<span id=\"wv\">1</span></div>" +
        "<script>const c=document.getElementById('c'),ctx=c.getContext('2d'),W=800,H=600;" +
        "c.width=W;c.height=H;const keys={},mouse={x:0,y:0,down:!1};" +
        "document.onkeydown=e=>keys[e.key]=!0;document.onkeyup=e=>keys[e.key]=!1;" +
        "c.onmousemove=e=>{mouse.x=e.offsetX;mouse.y=e.offsetY};c.onmousedown=()=>mouse.down=!0;c.onmouseup=()=>mouse.down=!1;" +
        "let player={x:W/2,y:H/2,speed:4,hp:100,mhp:100},bullets=[],enemies=[],particles=[],score=0,wave=1,scd=0;" +
        "function spawn(){let n=3+wave*2;for(let i=0;i<n;i++){let s=Math.random()*4|0,ex,ey;" +
        "if(s===0){ex=Math.random()*W;ey=-20}else if(s===1){ex=W+20;ey=Math.random()*H}" +
        "else if(s===2){ex=Math.random()*W;ey=H+20}else{ex=-20;ey=Math.random()*H}" +
        "enemies.push({x:ex,y:ey,hp:5+wave*2,sp:1+wave*.3});}}" +
        "function up(){let dx=0,dy=0;if(keys['a']||keys['ArrowLeft'])dx=-1;if(keys['d']||keys['ArrowRight'])dx=1;" +
        "if(keys['w']||keys['ArrowUp'])dy=-1;if(keys['s']||keys['ArrowDown'])dy=1;" +
        "if(dx&&dy){dx*=0.7;dy*=0.7;}player.x+=dx*player.speed;player.y+=dy*player.speed;" +
        "player.x=Math.max(10,Math.min(W-10,player.x));player.y=Math.max(10,Math.min(H-10,player.y));" +
        "scd=Math.max(0,scd-0.016);if(mouse.down&&scd<=0){let a=Math.atan2(mouse.y-player.y,mouse.x-player.x);" +
        "bullets.push({x:player.x,y:player.y,vx:Math.cos(a)*8,vy:Math.sin(a)*8});scd=0.15;}" +
        "for(let b of bullets){b.x+=b.vx;b.y+=b.vy;if(b.x<0||b.x>W||b.y<0||b.y>H)b.dead=!0;}bullets=bullets.filter(b=>!b.dead);" +
        "for(let e of enemies){let a=Math.atan2(player.y-e.y,player.x-e.x);e.x+=Math.cos(a)*e.sp;e.y+=Math.sin(a)*e.sp;" +
        "if(Math.hypot(player.x-e.x,player.y-e.y)<20)player.hp-=0.5;" +
        "for(let b of bullets)if(Math.hypot(b.x-e.x,b.y-e.y)<15){e.hp-=10;b.dead=!0;" +
        "for(let i=0;i<5;i++)particles.push({x:e.x,y:e.y,vx:(Math.random()-.5)*4,vy:(Math.random()-.5)*4,life:20});}}" +
        "enemies=enemies.filter(e=>e.hp>0);if(enemies.length===0){wave++;spawn();score+=wave*100;}" +
        "particles=particles.filter(p=>{p.life--;p.x+=p.vx;p.y+=p.vy;return p.life>0;});" +
        "if(player.hp<=0){alert('Game Over! Score: '+score);location.reload();}}" +
        "function draw(){ctx.fillStyle='#111';ctx.fillRect(0,0,W,H);" +
        "ctx.fillStyle='#0af';ctx.fillRect(player.x-10,player.y-10,20,20);" +
        "for(let b of bullets){ctx.fillStyle='#ff0';ctx.fillRect(b.x-2,b.y-2,4,4);}" +
        "for(let e of enemies){ctx.fillStyle='#f44';ctx.fillRect(e.x-9,e.y-9,18,18);}" +
        "for(let p of particles){ctx.fillStyle='#f80';ctx.globalAlpha=p.life/20;ctx.fillRect(p.x-2,p.y-2,4,4);}ctx.globalAlpha=1;" +
        "document.getElementById('sc').textContent=score;document.getElementById('hp').textContent=player.hp|0;document.getElementById('wv').textContent=wave;" +
        "ctx.strokeStyle='#fff';ctx.beginPath();ctx.arc(mouse.x,mouse.y,8,0,Math.PI*2);ctx.moveTo(mouse.x-12,mouse.y);ctx.lineTo(mouse.x+12,mouse.y);ctx.moveTo(mouse.x,mouse.y-12);ctx.lineTo(mouse.x,mouse.y+12);ctx.stroke();}" +
        "function loop(){up();draw();requestAnimationFrame(loop);}spawn();loop();</script></body></html>";

    internal static string Html5ShooterDesignDoc(string prompt) =>
        "# Top-Down Shooter (HTML5)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt top-down bullet-hell shooter. Overlev vagor av fiender med okande svarighetsgrad.\n\n" +
        "## Mekanik\n- **Rorelse:** WASD\n- **Sikta:** muspekare\n- **Skjuta:** hall musknappen\n" +
        "- **Vagor:** rensa alla fiender for nasta vag\n\n## Extension\n- Power-ups\n- Boss-fiender\n- Olika vapen\n";
}