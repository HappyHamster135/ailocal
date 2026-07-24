namespace AiLocal.Node.Hosting.GameModules;

/// <summary>
/// v2.32: BUTIK och PRESTATIONER som riktiga moduler. Composerns kryssrutor
/// för dem fanns sedan v2.30 men saknade allt stöd - "achievement" och
/// "unlock" förekom NOLL gånger i samtliga 21 kit, så agenten fick uppfinna
/// hela systemet från noll varje gång. Just de två är dessutom det som
/// tydligast skiljer ett färdigt spel från en prototyp: något att spendera
/// på, och något att sträva mot.
/// </summary>
public static class ShopModule
{
    public static string Description => "Butik: valuta, prisstegring, köpta uppgraderingar som sparas och märks i spelet";
    public static string[] Dependencies => [];

    public static string GodotCode => """
# Shop.gd - butik med valuta, uppgraderingar och sparning.
# Anvandning:
#   const Shop = preload("res://Shop.gd")
#   Shop.load_state()
#   Shop.earn(25)
#   var lvl = Shop.level("damage")        # 0..max
#   var mult = Shop.multiplier("damage")  # 1.0 + 0.25 per niva
#   Shop.panel(ui_layer, func(): _show_title())
class_name Shop

const SAVE := "user://shop.save"

# BYT HAR for ditt spel. cost_base stiger med cost_step per kopt niva.
const ITEMS := [
    {"id": "damage",  "name": "Sharper Strikes", "desc": "+25% damage",     "max": 5, "cost_base": 50,  "cost_step": 40},
    {"id": "health",  "name": "Tougher Hide",    "desc": "+1 max health",   "max": 5, "cost_base": 60,  "cost_step": 50},
    {"id": "speed",   "name": "Quick Feet",      "desc": "+10% move speed", "max": 4, "cost_base": 70,  "cost_step": 60},
    {"id": "luck",    "name": "Lucky Charm",     "desc": "+15% coin drops", "max": 3, "cost_base": 90,  "cost_step": 80},
    {"id": "magnet",  "name": "Coin Magnet",     "desc": "Pull coins in",   "max": 1, "cost_base": 150, "cost_step": 0},
]

static var coins := 0
static var levels := {}

static func load_state() -> void:
    levels = {}
    coins = 0
    if not FileAccess.file_exists(SAVE):
        return
    var f := FileAccess.open(SAVE, FileAccess.READ)
    if f == null:
        return
    var data = JSON.parse_string(f.get_as_text())
    if data is Dictionary:
        coins = int(data.get("coins", 0))
        var l = data.get("levels", {})
        if l is Dictionary:
            levels = l

static func save_state() -> void:
    var f := FileAccess.open(SAVE, FileAccess.WRITE)
    if f:
        f.store_string(JSON.stringify({"coins": coins, "levels": levels}))

static func earn(amount: int) -> void:
    coins = maxi(0, coins + amount)
    save_state()

static func level(id: String) -> int:
    return int(levels.get(id, 0))

static func _item(id: String) -> Dictionary:
    for it in ITEMS:
        if it["id"] == id:
            return it
    return {}

static func cost(id: String) -> int:
    var it := _item(id)
    if it.is_empty():
        return 0
    return int(it["cost_base"]) + int(it["cost_step"]) * level(id)

static func maxed(id: String) -> bool:
    var it := _item(id)
    return not it.is_empty() and level(id) >= int(it["max"])

static func can_buy(id: String) -> bool:
    return not maxed(id) and coins >= cost(id)

static func buy(id: String) -> bool:
    if not can_buy(id):
        return false
    coins -= cost(id)
    levels[id] = level(id) + 1
    save_state()
    return true

# Effektmultiplikator - anvand i spellogiken sa kopet MARKS.
static func multiplier(id: String, per_level := 0.25) -> float:
    return 1.0 + per_level * float(level(id))

# Butiksskarmen. on_back anropas nar spelaren stanger.
static func panel(parent: Node, on_back: Callable) -> Control:
    load_state()
    var overlay := Control.new()
    overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
    parent.add_child(overlay)
    var box := VBoxContainer.new()
    box.set_anchors_preset(Control.PRESET_FULL_RECT)
    box.alignment = BoxContainer.ALIGNMENT_CENTER
    box.add_theme_constant_override("separation", 8)
    overlay.add_child(box)

    var title := Label.new()
    title.text = "SHOP"
    title.add_theme_font_size_override("font_size", 40)
    title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    box.add_child(title)
    var purse := Label.new()
    purse.add_theme_font_size_override("font_size", 20)
    purse.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    box.add_child(purse)

    var rows := {}
    var refresh := func() -> void:
        purse.text = "Coins: %d" % coins
        for id in rows:
            var b: Button = rows[id]
            var it := _item(id)
            if maxed(id):
                b.text = "%s  -  MAX (%d/%d)" % [it["name"], level(id), it["max"]]
                b.disabled = true
            else:
                b.text = "%s  -  %s  -  %d coins  (%d/%d)" % [
                    it["name"], it["desc"], cost(id), level(id), it["max"]]
                b.disabled = not can_buy(id)

    for it in ITEMS:
        var b := Button.new()
        b.custom_minimum_size = Vector2(560, 42)
        b.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
        box.add_child(b)
        rows[it["id"]] = b
        var id: String = it["id"]
        b.pressed.connect(func() -> void:
            if buy(id):
                refresh.call())

    var back := Button.new()
    back.text = "Back"
    back.custom_minimum_size = Vector2(200, 44)
    back.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
    box.add_child(back)
    back.pressed.connect(func() -> void:
        overlay.queue_free()
        on_back.call())
    refresh.call()
    back.call_deferred("grab_focus")
    return overlay
""";

    public static string Html5Code => """
// shop.js - butik med valuta, uppgraderingar och localStorage-sparning.
export const SHOP_ITEMS = [
  { id: 'damage', name: 'Sharper Strikes', desc: '+25% damage', max: 5, costBase: 50, costStep: 40 },
  { id: 'health', name: 'Tougher Hide', desc: '+1 max health', max: 5, costBase: 60, costStep: 50 },
  { id: 'speed', name: 'Quick Feet', desc: '+10% move speed', max: 4, costBase: 70, costStep: 60 },
  { id: 'luck', name: 'Lucky Charm', desc: '+15% coin drops', max: 3, costBase: 90, costStep: 80 },
];
const KEY = 'shop_state';
export const Shop = {
  coins: 0, levels: {},
  load() {
    try {
      const d = JSON.parse(localStorage.getItem(KEY) || '{}');
      this.coins = d.coins | 0; this.levels = d.levels || {};
    } catch { this.coins = 0; this.levels = {}; }
  },
  save() { try { localStorage.setItem(KEY, JSON.stringify({ coins: this.coins, levels: this.levels })); } catch {} },
  earn(n) { this.coins = Math.max(0, this.coins + n); this.save(); },
  level(id) { return this.levels[id] | 0; },
  item(id) { return SHOP_ITEMS.find(i => i.id === id); },
  cost(id) { const i = this.item(id); return i ? i.costBase + i.costStep * this.level(id) : 0; },
  maxed(id) { const i = this.item(id); return !!i && this.level(id) >= i.max; },
  canBuy(id) { return !this.maxed(id) && this.coins >= this.cost(id); },
  buy(id) { if (!this.canBuy(id)) return false; this.coins -= this.cost(id); this.levels[id] = this.level(id) + 1; this.save(); return true; },
  multiplier(id, perLevel = 0.25) { return 1 + perLevel * this.level(id); },
};
""";

    public static string UnityCode => """
// Shop.cs - butik med valuta, uppgraderingar och PlayerPrefs-sparning.
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ShopItem
{
    public string id, itemName, description;
    public int maxLevel = 5, costBase = 50, costStep = 40;
}

public static class Shop
{
    public static readonly List<ShopItem> Items = new()
    {
        new() { id = "damage", itemName = "Sharper Strikes", description = "+25% damage", maxLevel = 5, costBase = 50, costStep = 40 },
        new() { id = "health", itemName = "Tougher Hide", description = "+1 max health", maxLevel = 5, costBase = 60, costStep = 50 },
        new() { id = "speed", itemName = "Quick Feet", description = "+10% move speed", maxLevel = 4, costBase = 70, costStep = 60 },
    };

    public static int Coins
    {
        get => PlayerPrefs.GetInt("shop_coins", 0);
        private set { PlayerPrefs.SetInt("shop_coins", Mathf.Max(0, value)); PlayerPrefs.Save(); }
    }

    public static void Earn(int amount) => Coins += amount;
    public static int Level(string id) => PlayerPrefs.GetInt("shop_lvl_" + id, 0);
    public static ShopItem Item(string id) => Items.Find(i => i.id == id);

    public static int Cost(string id)
    {
        var it = Item(id);
        return it == null ? 0 : it.costBase + it.costStep * Level(id);
    }

    public static bool Maxed(string id)
    {
        var it = Item(id);
        return it != null && Level(id) >= it.maxLevel;
    }

    public static bool CanBuy(string id) => !Maxed(id) && Coins >= Cost(id);

    public static bool Buy(string id)
    {
        if (!CanBuy(id)) return false;
        Coins -= Cost(id);
        PlayerPrefs.SetInt("shop_lvl_" + id, Level(id) + 1);
        PlayerPrefs.Save();
        return true;
    }

    public static float Multiplier(string id, float perLevel = 0.25f) => 1f + perLevel * Level(id);
}
""";
}

/// <summary>Prestationer med villkor, upplåsningspopup och lista i menyn.</summary>
public static class AchievementModule
{
    public static string Description => "Prestationer: namngivna mål, upplåsningspopup, lista i menyn, sparas i user://";
    public static string[] Dependencies => [];

    public static string GodotCode => """
# Achievements.gd - namngivna mal med popup och lista.
# Anvandning:
#   const Ach = preload("res://Achievements.gd")
#   Ach.load_state()
#   Ach.attach(self)                  # EN gang: vart popupen ritas
#   Ach.report("coins", total_coins)  # nar ett varde andras
#   Ach.unlock("first_win")           # eller direkt
#   Ach.panel(ui_layer, func(): _show_title())
class_name Achievements

const SAVE := "user://achievements.save"

# BYT HAR. "stat"+"goal" = las upp automatiskt nar report(stat) nar goal.
# Utan stat lases den bara upp via unlock().
const LIST := [
    {"id": "first_win",  "name": "First Blood",   "desc": "Win your first run"},
    {"id": "coins_100",  "name": "Pocket Change", "desc": "Collect 100 coins",    "stat": "coins", "goal": 100},
    {"id": "coins_1000", "name": "Treasurer",     "desc": "Collect 1000 coins",   "stat": "coins", "goal": 1000},
    {"id": "streak_5",   "name": "On a Roll",     "desc": "Win 5 times in a row", "stat": "streak", "goal": 5},
    {"id": "level_10",   "name": "Seasoned",      "desc": "Reach level 10",       "stat": "level", "goal": 10},
    {"id": "no_damage",  "name": "Untouchable",   "desc": "Clear a stage unharmed"},
    {"id": "speedrun",   "name": "Blink and Miss","desc": "Clear a stage under 30s"},
    {"id": "completion", "name": "Completionist", "desc": "Unlock everything else"},
]

static var unlocked := {}
static var stats := {}
static var _host: Node = null

static func load_state() -> void:
    unlocked = {}
    stats = {}
    if not FileAccess.file_exists(SAVE):
        return
    var f := FileAccess.open(SAVE, FileAccess.READ)
    if f == null:
        return
    var d = JSON.parse_string(f.get_as_text())
    if d is Dictionary:
        var u = d.get("unlocked", {})
        if u is Dictionary:
            unlocked = u
        var s = d.get("stats", {})
        if s is Dictionary:
            stats = s

static func save_state() -> void:
    var f := FileAccess.open(SAVE, FileAccess.WRITE)
    if f:
        f.store_string(JSON.stringify({"unlocked": unlocked, "stats": stats}))

# Var popupen ska ritas. Utan detta lases prestationer upp tyst.
static func attach(host: Node) -> void:
    _host = host

static func has(id: String) -> bool:
    return bool(unlocked.get(id, false))

static func count() -> int:
    var n := 0
    for a in LIST:
        if has(a["id"]):
            n += 1
    return n

static func unlock(id: String) -> bool:
    if has(id):
        return false
    unlocked[id] = true
    save_state()
    _popup(id)
    _check_completion()
    return true

# Rapportera ett varde; alla mal knutna till stat:en provas.
static func report(stat: String, value) -> void:
    stats[stat] = value
    var changed := false
    for a in LIST:
        if a.get("stat", "") == stat and not has(a["id"]):
            if float(value) >= float(a.get("goal", 0)):
                unlocked[a["id"]] = true
                changed = true
                _popup(a["id"])
    if changed:
        save_state()
        _check_completion()
    else:
        save_state()

static func _check_completion() -> void:
    for a in LIST:
        if a["id"] == "completion":
            continue
        if not has(a["id"]):
            return
    if not has("completion"):
        unlocked["completion"] = true
        save_state()
        _popup("completion")

static func _info(id: String) -> Dictionary:
    for a in LIST:
        if a["id"] == id:
            return a
    return {}

static func _popup(id: String) -> void:
    if _host == null or not is_instance_valid(_host):
        return
    var a := _info(id)
    if a.is_empty():
        return
    var layer := CanvasLayer.new()
    layer.layer = 100
    _host.add_child(layer)
    var p := PanelContainer.new()
    p.position = Vector2(1152 - 380, -110)
    p.custom_minimum_size = Vector2(360, 92)
    layer.add_child(p)
    var v := VBoxContainer.new()
    p.add_child(v)
    var t := Label.new()
    t.text = "ACHIEVEMENT UNLOCKED"
    t.add_theme_font_size_override("font_size", 13)
    v.add_child(t)
    var n := Label.new()
    n.text = str(a["name"])
    n.add_theme_font_size_override("font_size", 22)
    v.add_child(n)
    var d := Label.new()
    d.text = str(a["desc"])
    d.add_theme_font_size_override("font_size", 14)
    v.add_child(d)
    # In, ligg kvar, ut - och stada upp sig sjalv.
    var tw := p.create_tween()
    tw.tween_property(p, "position:y", 20.0, 0.35).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
    tw.tween_interval(2.6)
    tw.tween_property(p, "position:y", -110.0, 0.3)
    tw.tween_callback(layer.queue_free)

static func panel(parent: Node, on_back: Callable) -> Control:
    load_state()
    var overlay := Control.new()
    overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
    parent.add_child(overlay)
    var box := VBoxContainer.new()
    box.set_anchors_preset(Control.PRESET_FULL_RECT)
    box.alignment = BoxContainer.ALIGNMENT_CENTER
    box.add_theme_constant_override("separation", 6)
    overlay.add_child(box)
    var title := Label.new()
    title.text = "ACHIEVEMENTS  %d/%d" % [count(), LIST.size()]
    title.add_theme_font_size_override("font_size", 34)
    title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    box.add_child(title)
    for a in LIST:
        var row := Label.new()
        var got := has(a["id"])
        row.text = ("[X]  %s - %s" % [a["name"], a["desc"]]) if got else ("[ ]  %s - ???" % a["name"])
        row.add_theme_font_size_override("font_size", 17)
        row.modulate = Color(1, 1, 1) if got else Color(0.6, 0.6, 0.66)
        row.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
        box.add_child(row)
    var back := Button.new()
    back.text = "Back"
    back.custom_minimum_size = Vector2(200, 44)
    back.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
    box.add_child(back)
    back.pressed.connect(func() -> void:
        overlay.queue_free()
        on_back.call())
    back.call_deferred("grab_focus")
    return overlay
""";

    public static string Html5Code => """
// achievements.js - namngivna mal med popup och localStorage.
export const ACHIEVEMENTS = [
  { id: 'first_win', name: 'First Blood', desc: 'Win your first run' },
  { id: 'coins_100', name: 'Pocket Change', desc: 'Collect 100 coins', stat: 'coins', goal: 100 },
  { id: 'coins_1000', name: 'Treasurer', desc: 'Collect 1000 coins', stat: 'coins', goal: 1000 },
  { id: 'streak_5', name: 'On a Roll', desc: 'Win 5 times in a row', stat: 'streak', goal: 5 },
];
const KEY = 'achievements';
export const Ach = {
  unlocked: {}, stats: {},
  load() { try { const d = JSON.parse(localStorage.getItem(KEY) || '{}'); this.unlocked = d.unlocked || {}; this.stats = d.stats || {}; } catch {} },
  save() { try { localStorage.setItem(KEY, JSON.stringify({ unlocked: this.unlocked, stats: this.stats })); } catch {} },
  has(id) { return !!this.unlocked[id]; },
  count() { return ACHIEVEMENTS.filter(a => this.has(a.id)).length; },
  unlock(id) { if (this.has(id)) return false; this.unlocked[id] = true; this.save(); this.popup(id); return true; },
  report(stat, value) {
    this.stats[stat] = value;
    for (const a of ACHIEVEMENTS)
      if (a.stat === stat && !this.has(a.id) && value >= a.goal) { this.unlocked[a.id] = true; this.popup(a.id); }
    this.save();
  },
  popup(id) {
    const a = ACHIEVEMENTS.find(x => x.id === id); if (!a) return;
    const el = document.createElement('div');
    el.textContent = `Achievement: ${a.name}`;
    el.style.cssText = 'position:fixed;right:16px;top:-80px;padding:12px 18px;background:#1b1624;color:#fff;border-radius:8px;font:16px sans-serif;transition:top .35s;z-index:9999';
    document.body.appendChild(el);
    requestAnimationFrame(() => { el.style.top = '16px'; });
    setTimeout(() => { el.style.top = '-80px'; setTimeout(() => el.remove(), 400); }, 2600);
  },
};
""";

    public static string UnityCode => """
// Achievements.cs - namngivna mal med PlayerPrefs-sparning.
using System.Collections.Generic;
using UnityEngine;

public class Achievement
{
    public string id, name, description, stat;
    public float goal;
}

public static class Achievements
{
    public static readonly List<Achievement> All = new()
    {
        new() { id = "first_win", name = "First Blood", description = "Win your first run" },
        new() { id = "coins_100", name = "Pocket Change", description = "Collect 100 coins", stat = "coins", goal = 100 },
        new() { id = "streak_5", name = "On a Roll", description = "Win 5 times in a row", stat = "streak", goal = 5 },
    };

    public static bool Has(string id) => PlayerPrefs.GetInt("ach_" + id, 0) == 1;
    public static int Count() => All.FindAll(a => Has(a.id)).Count;

    public static bool Unlock(string id)
    {
        if (Has(id)) return false;
        PlayerPrefs.SetInt("ach_" + id, 1);
        PlayerPrefs.Save();
        Debug.Log($"Achievement unlocked: {id}");
        return true;
    }

    public static void Report(string stat, float value)
    {
        foreach (var a in All)
            if (a.stat == stat && !Has(a.id) && value >= a.goal)
                Unlock(a.id);
    }
}
""";
}
