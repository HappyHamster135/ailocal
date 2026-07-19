using System;

namespace AiLocal.Node.Hosting.GameModules;

/// <summary>
/// Static library of reusable game components that the agent can include in any
/// game project. Each module exposes self-contained Unity (C# MonoBehaviour),
/// Godot (GDScript), and HTML5 (JavaScript) implementations.
/// </summary>
public static class GameModuleLibrary
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. InventorySystem
    // ──────────────────────────────────────────────────────────────────────────
    public static class InventorySystem
    {
        public static string Description => "Item management with stacking, equipping, and drag-drop UI";
        public static string[] Dependencies => [];

        public static string UnityCode => @"
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[System.Serializable]
public class InventoryItem
{
    public string id;
    public string itemName;
    public int maxStack = 99;
    public int currentStack = 1;
    public bool isEquippable;
    public Sprite icon;
}

public class InventorySystem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public List<InventoryItem> items = new();
    public int maxSlots = 20;
    public System.Action<string> onItemAdded, onItemRemoved, onItemEquipped;

    public bool AddItem(InventoryItem item)
    {
        // Try stacking
        if (item.maxStack > 1)
        {
            var existing = items.Find(i => i.id == item.id && i.currentStack < i.maxStack);
            if (existing != null)
            {
                int space = existing.maxStack - existing.currentStack;
                int toAdd = Mathf.Min(space, item.currentStack);
                existing.currentStack += toAdd;
                item.currentStack -= toAdd;
                if (item.currentStack <= 0) return true;
            }
        }
        // New slot
        if (items.Count >= maxSlots) return false;
        items.Add(item);
        onItemAdded?.Invoke(item.id);
        return true;
    }

    public bool RemoveItem(string itemId, int count = 1)
    {
        var item = items.Find(i => i.id == itemId);
        if (item == null) return false;
        item.currentStack -= count;
        if (item.currentStack <= 0) { items.Remove(item); onItemRemoved?.Invoke(itemId); }
        return true;
    }

    public void EquipItem(string itemId)
    {
        var item = items.Find(i => i.id == itemId);
        if (item == null || !item.isEquippable) return;
        onItemEquipped?.Invoke(itemId);
    }

    public void OnPointerEnter(PointerEventData d) { /* hover tooltip */ }
    public void OnPointerExit(PointerEventData d)  { /* hide tooltip */ }
}";

        public static string GodotCode => @"
extends Node

# InventorySystem.gd — attach to a Node or Autoload

signal item_added(item_id)
signal item_removed(item_id)
signal item_equipped(item_id)

var items := []
var max_slots := 20
var max_stack := 99

func add_item(item: Dictionary) -> bool:
    # Try stacking
    if item.get('stackable', true) and item.max_stack > 1:
        for existing in items:
            if existing.id == item.id and existing.current_stack < existing.max_stack:
                var space = existing.max_stack - existing.current_stack
                var to_add = min(space, item.current_stack)
                existing.current_stack += to_add
                item.current_stack -= to_add
                if item.current_stack <= 0:
                    return true
    # New slot
    if items.size() >= max_slots:
        return false
    items.append(item)
    item_added.emit(item.id)
    return true

func remove_item(item_id: String, count := 1) -> bool:
    for i in range(items.size()):
        if items[i].id == item_id:
            items[i].current_stack -= count
            if items[i].current_stack <= 0:
                items.remove_at(i)
                item_removed.emit(item_id)
            return true
    return false

func equip_item(item_id: String) -> void:
    for item in items:
        if item.id == item_id and item.get('equippable', false):
            item_equipped.emit(item_id)
            return";

        public static string Html5Code => @"
// InventorySystem.js — ES Module

export class InventorySystem {
    constructor(maxSlots = 20) {
        this.items = [];
        this.maxSlots = maxSlots;
        this.maxStack = 99;
        this._listeners = { add: [], remove: [], equip: [] };
    }

    on(event, fn) { this._listeners[event]?.push(fn); }

    addItem(item) {
        if (item.maxStack > 1) {
            const existing = this.items.find(i =>
                i.id === item.id && i.currentStack < i.maxStack);
            if (existing) {
                const space = existing.maxStack - existing.currentStack;
                const toAdd = Math.min(space, item.currentStack);
                existing.currentStack += toAdd;
                item.currentStack -= toAdd;
                if (item.currentStack <= 0) return true;
            }
        }
        if (this.items.length >= this.maxSlots) return false;
        this.items.push(item);
        this._fire('add', item.id);
        return true;
    }

    removeItem(itemId, count = 1) {
        const idx = this.items.findIndex(i => i.id === itemId);
        if (idx === -1) return false;
        this.items[idx].currentStack -= count;
        if (this.items[idx].currentStack <= 0) {
            this.items.splice(idx, 1);
            this._fire('remove', itemId);
        }
        return true;
    }

    equipItem(itemId) {
        const item = this.items.find(i => i.id === itemId && i.isEquippable);
        if (!item) return;
        this._fire('equip', itemId);
    }

    _fire(event, id) { this._listeners[event]?.forEach(fn => fn(id)); }
}";

    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. DialogSystem
    // ──────────────────────────────────────────────────────────────────────────
    public static class DialogSystem
    {
        public static string Description => "Branching dialog trees with NPC interaction, conditions, and choices";
        public static string[] Dependencies => [];

        public static string UnityCode => @"
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class DialogNode
{
    public string id;
    [TextArea(3, 6)] public string text;
    public string speakerName;
    public DialogChoice[] choices;
    public string onEnterEvent;
    public string onExitEvent;
}

[System.Serializable]
public class DialogChoice
{
    public string text;
    public string nextNodeId;
    public string conditionId;  // empty = always available
}

public class DialogSystem : MonoBehaviour
{
    public Text uiText, uiSpeaker;
    public Transform choiceContainer;
    public GameObject choicePrefab;
    public Dictionary<string, DialogNode> nodes = new();
    public string startNodeId = ""start"";

    private DialogNode _current;

    void Start() { StartDialog(startNodeId); }

    public void StartDialog(string nodeId)
    {
        if (!nodes.TryGetValue(nodeId, out _current)) return;
        ShowNode(_current);
    }

    void ShowNode(DialogNode node)
    {
        if (!string.IsNullOrEmpty(node.onEnterEvent))
            SendMessage(node.onEnterEvent, SendMessageOptions.DontRequireReceiver);

        uiText.text = node.text;
        uiSpeaker.text = node.speakerName;

        // Clear old choices
        foreach (Transform c in choiceContainer) Destroy(c.gameObject);

        foreach (var choice in node.choices)
        {
            if (!string.IsNullOrEmpty(choice.conditionId) && !EvaluateCondition(choice.conditionId))
                continue;
            var btn = Instantiate(choicePrefab, choiceContainer).GetComponent<Button>();
            btn.GetComponentInChildren<Text>().text = choice.text;
            var captured = choice;
            btn.onClick.AddListener(() => OnChoice(captured));
        }
    }

    void OnChoice(DialogChoice choice)
    {
        if (!string.IsNullOrEmpty(_current.onExitEvent))
            SendMessage(_current.onExitEvent, SendMessageOptions.DontRequireReceiver);

        if (!string.IsNullOrEmpty(choice.nextNodeId) && nodes.TryGetValue(choice.nextNodeId, out var next))
        {
            _current = next;
            ShowNode(_current);
        }
        else
        {
            gameObject.SetActive(false); // end dialog
        }
    }

    bool EvaluateCondition(string conditionId) => true; // override for game-specific logic
}";

        public static string GodotCode => @"
extends Node

# DialogSystem.gd
signal dialog_started(node_id)
signal dialog_ended(node_id)
signal choice_made(node_id, choice_index)

var nodes := {}
var current_node: Dictionary
var start_node_id := ""start""

func _ready() -> void:
    pass

func start_dialog(node_id: String = start_node_id) -> void:
    if not nodes.has(node_id):
        return
    current_node = nodes[node_id]
    dialog_started.emit(node_id)
    _show_node(current_node)

func _show_node(node: Dictionary) -> void:
    if node.has(""on_enter_event"") and node[""on_enter_event""] != """":
        var method = Callable(self, node[""on_enter_event""])
        if method.is_valid(): method.call()

    # Emit signal so UI layer can render
    dialog_started.emit(node[""id""])

func select_choice(index: int) -> void:
    if current_node.is_empty():
        return
    var choices = current_node.get(""choices"", [])
    if index < 0 or index >= choices.size():
        return
    var choice = choices[index]
    if choice.has(""condition_id"") and choice[""condition_id""] != """":
        if not _eval_condition(choice[""condition_id""]):
            return

    if current_node.has(""on_exit_event"") and current_node[""on_exit_event""] != """":
        var method = Callable(self, current_node[""on_exit_event""])
        if method.is_valid(): method.call()

    choice_made.emit(current_node[""id""], index)

    if choice.has(""next_node_id"") and nodes.has(choice[""next_node_id""]):
        current_node = nodes[choice[""next_node_id""]]
        _show_node(current_node)
    else:
        dialog_ended.emit(current_node[""id""])

func _eval_condition(condition_id: String) -> bool:
    return true # override";

        public static string Html5Code => @"
// DialogSystem.js — ES Module

export class DialogSystem {
    constructor() {
        this.nodes = {};
        this.currentNode = null;
        this.startNodeId = 'start';
        this._listeners = { start: [], end: [], choice: [] };
    }

    on(event, fn) { this._listeners[event]?.push(fn); }
    addNode(node) { this.nodes[node.id] = node; }

    startDialog(nodeId) {
        nodeId = nodeId || this.startNodeId;
        this.currentNode = this.nodes[nodeId];
        if (!this.currentNode) return;
        this._fire('start', this.currentNode);
    }

    selectChoice(index) {
        if (!this.currentNode) return;
        const choices = this.currentNode.choices || [];
        const choice = choices[index];
        if (!choice) return;
        if (choice.conditionId && !this._evalCondition(choice.conditionId)) return;

        this._fire('choice', { nodeId: this.currentNode.id, index });

        if (choice.nextNodeId && this.nodes[choice.nextNodeId]) {
            this.currentNode = this.nodes[choice.nextNodeId];
            this._fire('start', this.currentNode);
        } else {
            this._fire('end', this.currentNode.id);
            this.currentNode = null;
        }
    }

    _evalCondition(conditionId) { return true; }
    _fire(event, data) { this._listeners[event]?.forEach(fn => fn(data)); }
}";

    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. QuestSystem
    // ──────────────────────────────────────────────────────────────────────────
    public static class QuestSystem
    {
        public static string Description => "Quest tracking with objectives, progress conditions, and reward delivery";
        public static string[] Dependencies => ["InventorySystem"];

        public static string UnityCode => @"
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Quest
{
    public string id, title, description;
    public QuestObjective[] objectives;
    public QuestReward reward;
    public bool isCompleted, isActive;
}

[Serializable]
public class QuestObjective
{
    public string description;
    public string type;       // ""kill"", ""collect"", ""talk"", ""reach""
    public string targetId;
    public int requiredCount = 1;
    public int currentCount;
    public bool IsSatisfied => currentCount >= requiredCount;
}

[Serializable]
public class QuestReward
{
    public int xp;
    public string itemId;
    public int itemCount = 1;
    public string onCompleteEvent;
}

public class QuestSystem : MonoBehaviour
{
    public List<Quest> activeQuests = new();
    public List<Quest> completedQuests = new();
    public Action<Quest> onQuestStarted, onQuestProgressed, onQuestCompleted;

    public bool StartQuest(Quest quest)
    {
        if (activeQuests.Exists(q => q.id == quest.id)) return false;
        quest.isActive = true;
        activeQuests.Add(quest);
        onQuestStarted?.Invoke(quest);
        return true;
    }

    public void Progress(string objectiveType, string targetId, int amount = 1)
    {
        foreach (var quest in activeQuests)
        {
            bool changed = false;
            foreach (var obj in quest.objectives)
            {
                if (obj.type == objectiveType && obj.targetId == targetId && !obj.IsSatisfied)
                {
                    obj.currentCount = Math.Min(obj.currentCount + amount, obj.requiredCount);
                    changed = true;
                }
            }
            if (changed) onQuestProgressed?.Invoke(quest);

            if (Array.TrueForAll(quest.objectives, o => o.IsSatisfied))
                CompleteQuest(quest);
        }
    }

    void CompleteQuest(Quest quest)
    {
        quest.isCompleted = true;
        quest.isActive = false;
        activeQuests.Remove(quest);
        completedQuests.Add(quest);
        onQuestCompleted?.Invoke(quest);

        // Deliver rewards
        var inv = GetComponent<InventorySystem>();
        if (inv != null && !string.IsNullOrEmpty(quest.reward.itemId))
        {
            var item = new InventoryItem { id = quest.reward.itemId, currentStack = quest.reward.itemCount };
            inv.AddItem(item);
        }
        if (!string.IsNullOrEmpty(quest.reward.onCompleteEvent))
            SendMessage(quest.reward.onCompleteEvent, SendMessageOptions.DontRequireReceiver);
    }
}";

        public static string GodotCode => @"
extends Node

# QuestSystem.gd
signal quest_started(quest_id)
signal quest_progressed(quest_id)
signal quest_completed(quest_id)

var active_quests := []
var completed_quests := []

func start_quest(quest: Dictionary) -> bool:
    for q in active_quests:
        if q.id == quest.id:
            return false
    quest.is_active = true
    active_quests.append(quest)
    quest_started.emit(quest.id)
    return true

func progress(objective_type: String, target_id: String, amount := 1) -> void:
    for quest in active_quests:
        var changed := false
        for obj in quest.objectives:
            if obj.type == objective_type and obj.target_id == target_id and not obj.is_satisfied:
                obj.current_count = min(obj.current_count + amount, obj.required_count)
                changed = true
                if obj.current_count >= obj.required_count:
                    obj.is_satisfied = true
        if changed:
            quest_progressed.emit(quest.id)
        # Check completion
        var all_done := true
        for obj in quest.objectives:
            if not obj.is_satisfied:
                all_done = false
                break
        if all_done:
            _complete_quest(quest)

func _complete_quest(quest: Dictionary) -> void:
    quest.is_completed = true
    quest.is_active = false
    active_quests.erase(quest)
    completed_quests.append(quest)
    quest_completed.emit(quest.id)";

        public static string Html5Code => @"
// QuestSystem.js — ES Module

export class QuestSystem {
    constructor() {
        this.activeQuests = [];
        this.completedQuests = [];
        this._listeners = { start: [], progress: [], complete: [] };
    }

    on(event, fn) { this._listeners[event]?.push(fn); }

    startQuest(quest) {
        if (this.activeQuests.find(q => q.id === quest.id)) return false;
        quest.isActive = true;
        quest.isCompleted = false;
        quest.objectives = (quest.objectives || []).map(o => ({ ...o, currentCount: 0 }));
        this.activeQuests.push(quest);
        this._fire('start', quest.id);
        return true;
    }

    progress(objectiveType, targetId, amount = 1) {
        for (const quest of this.activeQuests) {
            let changed = false;
            for (const obj of quest.objectives) {
                if (obj.type === objectiveType && obj.targetId === targetId && !obj.isSatisfied) {
                    obj.currentCount = Math.min(obj.currentCount + amount, obj.requiredCount);
                    if (obj.currentCount >= obj.requiredCount) obj.isSatisfied = true;
                    changed = true;
                }
            }
            if (changed) this._fire('progress', quest.id);
            if (quest.objectives.every(o => o.isSatisfied)) this._completeQuest(quest);
        }
    }

    _completeQuest(quest) {
        quest.isCompleted = true;
        quest.isActive = false;
        this.activeQuests = this.activeQuests.filter(q => q.id !== quest.id);
        this.completedQuests.push(quest);
        this._fire('complete', quest.id);
    }

    _fire(event, data) { this._listeners[event]?.forEach(fn => fn(data)); }
}";

    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. SaveLoadSystem
    // ──────────────────────────────────────────────────────────────────────────
    public static class SaveLoadSystem
    {
        public static string Description => "JSON serialization of game state to disk with save slots and metadata";
        public static string[] Dependencies => [];

        public static string UnityCode => @"
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]
public class SaveData
{
    public string saveName;
    public string timestamp;
    public int playTimeSeconds;
    public string sceneName;
    public Dictionary<string, object> gameState = new();
}

public class SaveLoadSystem : MonoBehaviour
{
    public string saveFolder => Application.persistentDataPath + ""/saves/"";
    public int maxSlots = 10;

    void Awake() { Directory.CreateDirectory(saveFolder); }

    public bool Save(string slotName, SaveData data)
    {
        var path = saveFolder + slotName + "".json"";
        data.timestamp = System.DateTime.Now.ToString(""o"");
        var json = JsonUtility.ToJson(data, true);
        File.WriteAllText(path, json);
        return true;
    }

    public SaveData Load(string slotName)
    {
        var path = saveFolder + slotName + "".json"";
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonUtility.FromJson<SaveData>(json);
    }

    public bool DeleteSlot(string slotName)
    {
        var path = saveFolder + slotName + "".json"";
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public string[] ListSaves()
    {
        if (!Directory.Exists(saveFolder)) return System.Array.Empty<string>();
        var files = Directory.GetFiles(saveFolder, ""*.json"");
        for (int i = 0; i < files.Length; i++)
            files[i] = Path.GetFileNameWithoutExtension(files[i]);
        return files;
    }

    public T GetValue<T>(SaveData data, string key, T defaultValue = default)
    {
        return data.gameState.TryGetValue(key, out var val) ? (T)val : defaultValue;
    }
}";

        public static string GodotCode => @"
extends Node

# SaveLoadSystem.gd
var save_dir := ""user://saves/""
var max_slots := 10

func _ready() -> void:
    DirAccess.make_dir_recursive_absolute(save_dir)

func save(slot_name: String, data: Dictionary) -> bool:
    data[""timestamp""] = Time.get_datetime_string_from_system()
    var path = save_dir + slot_name + "".json""
    var file = FileAccess.open(path, FileAccess.WRITE)
    if file == null:
        return false
    var json_str = JSON.stringify(data, ""\t"")
    file.store_string(json_str)
    file.close()
    return true

func load_save(slot_name: String) -> Dictionary:
    var path = save_dir + slot_name + "".json""
    if not FileAccess.file_exists(path):
        return {}
    var file = FileAccess.open(path, FileAccess.READ)
    if file == null:
        return {}
    var json_str = file.get_as_text()
    file.close()
    var json = JSON.new()
    json.parse(json_str)
    return json.get_data() as Dictionary

func delete_slot(slot_name: String) -> bool:
    var path = save_dir + slot_name + "".json""
    if not FileAccess.file_exists(path):
        return false
    DirAccess.remove_absolute(path)
    return true

func list_saves() -> PackedStringArray:
    var dir = DirAccess.open(save_dir)
    if dir == null:
        return PackedStringArray()
    var result := PackedStringArray()
    dir.list_dir_begin()
    var f = dir.get_next()
    while f != """":
        if f.ends_with("".json""):
            result.append(f.trim_suffix("".json""))
        f = dir.get_next()
    return result";

        public static string Html5Code => @"
// SaveLoadSystem.js — ES Module

export class SaveLoadSystem {
    constructor(maxSlots = 10) {
        this.maxSlots = maxSlots;
        this._prefix = 'game_save_';
    }

    save(slotName, data) {
        try {
            const saveData = {
                ...data,
                timestamp: new Date().toISOString(),
                gameState: data.gameState || {}
            };
            localStorage.setItem(this._prefix + slotName, JSON.stringify(saveData));
            return true;
        } catch (e) {
            console.error('Save failed:', e);
            return false;
        }
    }

    load(slotName) {
        try {
            const raw = localStorage.getItem(this._prefix + slotName);
            if (!raw) return null;
            return JSON.parse(raw);
        } catch (e) {
            console.error('Load failed:', e);
            return null;
        }
    }

    deleteSlot(slotName) {
        localStorage.removeItem(this._prefix + slotName);
        return true;
    }

    listSaves() {
        const saves = [];
        for (let i = 0; i < localStorage.length; i++) {
            const key = localStorage.key(i);
            if (key && key.startsWith(this._prefix)) {
                saves.push(key.slice(this._prefix.length));
            }
        }
        return saves;
    }
}";

    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. HealthCombat
    // ──────────────────────────────────────────────────────────────────────────
    public static class HealthCombat
    {
        public static string Description => "Health/damage/healing system with damage types, resistances, and death handling";
        public static string[] Dependencies => [];

        public static string UnityCode => @"
using System;
using UnityEngine;
using UnityEngine.Events;

public enum DamageType { Physical, Fire, Ice, Poison, Lightning, Holy, Dark }

[Serializable]
public struct DamageResistance
{
    public DamageType type;
    public float multiplier; // 0 = immune, 0.5 = resistant, 2 = vulnerable
}

public class HealthCombat : MonoBehaviour
{
    public float maxHealth = 100f;
    public float currentHealth;
    public bool isInvulnerable;
    public DamageResistance[] resistances;
    public UnityEvent onDamage, onHeal, onDeath, onRevive;

    public float HealthPercent => currentHealth / maxHealth;

    void Awake() { currentHealth = maxHealth; }

    public void TakeDamage(float rawAmount, DamageType type = DamageType.Physical, GameObject source = null)
    {
        if (isInvulnerable || currentHealth <= 0) return;
        float multiplier = 1f;
        foreach (var r in resistances)
            if (r.type == type) { multiplier = r.multiplier; break; }
        float final = rawAmount * multiplier;
        currentHealth = Mathf.Max(0, currentHealth - final);
        onDamage.Invoke();
        if (currentHealth <= 0) onDeath.Invoke();
    }

    public void Heal(float amount)
    {
        if (currentHealth <= 0) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        onHeal.Invoke();
    }

    public void Revive(float healPercent = 0.5f)
    {
        currentHealth = maxHealth * healPercent;
        onRevive.Invoke();
    }

    public bool IsAlive => currentHealth > 0;
}";

        public static string GodotCode => @"
extends Node

# HealthCombat.gd
signal on_damage(amount, type)
signal on_heal(amount)
signal on_death()
signal on_revive()

enum DamageType { PHYSICAL, FIRE, ICE, POISON, LIGHTNING, HOLY, DARK }

var max_health := 100.0
var current_health := 100.0
var is_invulnerable := false
var resistances := {}  # DamageType -> float multiplier

func _ready() -> void:
    current_health = max_health

func take_damage(raw_amount: float, type: int = DamageType.PHYSICAL, source = null) -> void:
    if is_invulnerable or current_health <= 0:
        return
    var multiplier := resistances.get(type, 1.0)
    var final_amount := raw_amount * multiplier
    current_health = max(0.0, current_health - final_amount)
    on_damage.emit(final_amount, type)
    if current_health <= 0:
        on_death.emit()

func heal(amount: float) -> void:
    if current_health <= 0:
        return
    current_health = min(max_health, current_health + amount)
    on_heal.emit(amount)

func revive(heal_percent := 0.5) -> void:
    current_health = max_health * heal_percent
    on_revive.emit()

func is_alive() -> bool:
    return current_health > 0";

        public static string Html5Code => @"
// HealthCombat.js — ES Module

export const DamageType = {
    PHYSICAL: 'physical', FIRE: 'fire', ICE: 'ice',
    POISON: 'poison', LIGHTNING: 'lightning', HOLY: 'holy', DARK: 'dark'
};

export class HealthCombat {
    constructor(maxHealth = 100) {
        this.maxHealth = maxHealth;
        this.currentHealth = maxHealth;
        this.isInvulnerable = false;
        this.resistances = {};
        this._listeners = { damage: [], heal: [], death: [], revive: [] };
    }

    on(event, fn) { this._listeners[event]?.push(fn); }

    takeDamage(rawAmount, type = DamageType.PHYSICAL, source = null) {
        if (this.isInvulnerable || this.currentHealth <= 0) return;
        const multiplier = this.resistances[type] ?? 1.0;
        const finalAmt = rawAmount * multiplier;
        this.currentHealth = Math.max(0, this.currentHealth - finalAmt);
        this._fire('damage', { amount: finalAmt, type });
        if (this.currentHealth <= 0) this._fire('death', {});
    }

    heal(amount) {
        if (this.currentHealth <= 0) return;
        this.currentHealth = Math.min(this.maxHealth, this.currentHealth + amount);
        this._fire('heal', { amount });
    }

    revive(healPercent = 0.5) {
        this.currentHealth = this.maxHealth * healPercent;
        this._fire('revive', {});
    }

    get isAlive() { return this.currentHealth > 0; }

    _fire(event, data) { this._listeners[event]?.forEach(fn => fn(data)); }
}";

    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. ProgressionSystem
    // ──────────────────────────────────────────────────────────────────────────
    public static class ProgressionSystem
    {
        public static string Description => "XP, leveling, skill trees with unlockable abilities and stat scaling";
        public static string[] Dependencies => [];

        public static string UnityCode => @"
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SkillNode
{
    public string id, name, description;
    public int maxRank = 1;
    public int currentRank;
    public string[] prerequisiteIds;
    public int costPerRank = 1;
    public System.Action<int> onRankUp;
}

[Serializable]
public class ProgressionData
{
    public int level = 1;
    public int currentXp;
    public int xpToNext = 100;
    public int totalSkillPoints;
    public int spentSkillPoints;
}

public class ProgressionSystem : MonoBehaviour
{
    public ProgressionData data = new();
    public Dictionary<string, SkillNode> skillTree = new();
    public float xpCurveMultiplier = 1.5f;

    public Action<int> onLevelUp;
    public Action<string, int> onSkillRankedUp;

    public void AddXp(int amount)
    {
        data.currentXp += amount;
        while (data.currentXp >= data.xpToNext)
        {
            data.currentXp -= data.xpToNext;
            data.level++;
            data.totalSkillPoints++;
            data.xpToNext = Mathf.RoundToInt(100 * Mathf.Pow(xpCurveMultiplier, data.level - 1));
            onLevelUp?.Invoke(data.level);
        }
    }

    public bool RankUpSkill(string skillId)
    {
        if (!skillTree.TryGetValue(skillId, out var node)) return false;
        if (node.currentRank >= node.maxRank) return false;
        if (data.spentSkillPoints >= data.totalSkillPoints) return false;

        // Check prerequisites
        foreach (var prereq in node.prerequisiteIds)
        {
            if (!skillTree.TryGetValue(prereq, out var pNode) || pNode.currentRank < pNode.maxRank)
                return false;
        }

        node.currentRank++;
        data.spentSkillPoints++;
        node.onRankUp?.Invoke(node.currentRank);
        onSkillRankedUp?.Invoke(skillId, node.currentRank);
        return true;
    }

    public bool IsSkillUnlocked(string skillId)
    {
        if (!skillTree.TryGetValue(skillId, out var node)) return false;
        if (node.currentRank > 0) return true;
        foreach (var prereq in node.prerequisiteIds)
        {
            if (!skillTree.TryGetValue(prereq, out var pNode) || pNode.currentRank < pNode.maxRank)
                return false;
        }
        return data.spentSkillPoints < data.totalSkillPoints;
    }
}";

        public static string GodotCode => @"
extends Node

# ProgressionSystem.gd
signal on_level_up(new_level)
signal on_skill_ranked_up(skill_id, rank)

var data := {
    ""level"": 1,
    ""current_xp"": 0,
    ""xp_to_next"": 100,
    ""total_skill_points"": 0,
    ""spent_skill_points"": 0
}
var skill_tree := {}
var xp_curve_multiplier := 1.5

func add_xp(amount: int) -> void:
    data.current_xp += amount
    while data.current_xp >= data.xp_to_next:
        data.current_xp -= data.xp_to_next
        data.level += 1
        data.total_skill_points += 1
        data.xp_to_next = int(100 * pow(xp_curve_multiplier, data.level - 1))
        on_level_up.emit(data.level)

func rank_up_skill(skill_id: String) -> bool:
    if not skill_tree.has(skill_id):
        return false
    var node = skill_tree[skill_id]
    if node.current_rank >= node.max_rank:
        return false
    if data.spent_skill_points >= data.total_skill_points:
        return false
    # Check prerequisites
    for prereq in node.prerequisite_ids:
        if not skill_tree.has(prereq) or skill_tree[prereq].current_rank < skill_tree[prereq].max_rank:
            return false
    node.current_rank += 1
    data.spent_skill_points += 1
    on_skill_ranked_up.emit(skill_id, node.current_rank)
    return true

func is_skill_unlocked(skill_id: String) -> bool:
    if not skill_tree.has(skill_id):
        return false
    var node = skill_tree[skill_id]
    if node.current_rank > 0:
        return true
    for prereq in node.prerequisite_ids:
        if not skill_tree.has(prereq) or skill_tree[prereq].current_rank < skill_tree[prereq].max_rank:
            return false
    return data.spent_skill_points < data.total_skill_points";

        public static string Html5Code => @"
// ProgressionSystem.js — ES Module

export class ProgressionSystem {
    constructor() {
        this.data = { level: 1, currentXp: 0, xpToNext: 100, totalSkillPoints: 0, spentSkillPoints: 0 };
        this.skillTree = {};
        this.xpCurveMultiplier = 1.5;
        this._listeners = { levelUp: [], skillRankedUp: [] };
    }

    on(event, fn) { this._listeners[event]?.push(fn); }

    addXp(amount) {
        this.data.currentXp += amount;
        while (this.data.currentXp >= this.data.xpToNext) {
            this.data.currentXp -= this.data.xpToNext;
            this.data.level++;
            this.data.totalSkillPoints++;
            this.data.xpToNext = Math.round(100 * Math.pow(this.xpCurveMultiplier, this.data.level - 1));
            this._fire('levelUp', this.data.level);
        }
    }

    rankUpSkill(skillId) {
        const node = this.skillTree[skillId];
        if (!node) return false;
        if (node.currentRank >= node.maxRank) return false;
        if (this.data.spentSkillPoints >= this.data.totalSkillPoints) return false;
        for (const prereq of (node.prerequisiteIds || [])) {
            const pNode = this.skillTree[prereq];
            if (!pNode || pNode.currentRank < pNode.maxRank) return false;
        }
        node.currentRank++;
        this.data.spentSkillPoints++;
        this._fire('skillRankedUp', { skillId, rank: node.currentRank });
        return true;
    }

    isSkillUnlocked(skillId) {
        const node = this.skillTree[skillId];
        if (!node) return false;
        if (node.currentRank > 0) return true;
        for (const prereq of (node.prerequisiteIds || [])) {
            const pNode = this.skillTree[prereq];
            if (!pNode || pNode.currentRank < pNode.maxRank) return false;
        }
        return this.data.spentSkillPoints < this.data.totalSkillPoints;
    }

    _fire(event, data) { this._listeners[event]?.forEach(fn => fn(data)); }
}";

    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. SimpleAI
    // ──────────────────────────────────────────────────────────────────────────
    public static class SimpleAI
    {
        public static string Description => "Basic enemy AI with patrol, chase, and attack state machine";
        public static string[] Dependencies => ["HealthCombat"];

        public static string UnityCode => @"
using UnityEngine;

public enum AIState { Patrol, Chase, Attack, Return }

public class SimpleAI : MonoBehaviour
{
    public AIState currentState = AIState.Patrol;
    public float patrolRadius = 5f;
    public float chaseRange = 8f;
    public float attackRange = 2f;
    public float moveSpeed = 3f;
    public float patrolWaitTime = 2f;
    public Transform target;

    private Vector3 _origin;
    private Vector3 _patrolTarget;
    private float _waitTimer;

    void Start()
    {
        _origin = transform.position;
        PickNewPatrolTarget();
    }

    void Update()
    {
        if (target == null)
        {
            FindTarget();
            if (target == null) { Patrol(); return; }
        }

        float dist = Vector3.Distance(transform.position, target.position);

        switch (currentState)
        {
            case AIState.Patrol:
                if (dist <= chaseRange) { currentState = AIState.Chase; }
                else Patrol();
                break;

            case AIState.Chase:
                if (dist <= attackRange) { currentState = AIState.Attack; }
                else if (dist > chaseRange * 1.5f) { currentState = AIState.Return; }
                else Chase();
                break;

            case AIState.Attack:
                if (dist > attackRange * 1.2f) { currentState = AIState.Chase; }
                else Attack();
                break;

            case AIState.Return:
                if (dist <= chaseRange) { currentState = AIState.Chase; }
                else ReturnToOrigin();
                break;
        }
    }

    void Patrol()
    {
        MoveToward(_patrolTarget);
        if (Vector3.Distance(transform.position, _patrolTarget) < 0.5f)
        {
            _waitTimer += Time.deltaTime;
            if (_waitTimer >= patrolWaitTime) { PickNewPatrolTarget(); _waitTimer = 0; }
        }
    }

    void Chase() => MoveToward(target.position);

    void Attack()
    {
        // Override for attack logic; base just faces target
        transform.LookAt(target);
    }

    void ReturnToOrigin()
    {
        MoveToward(_origin);
        if (Vector3.Distance(transform.position, _origin) < 0.5f)
        {
            currentState = AIState.Patrol;
            PickNewPatrolTarget();
        }
    }

    void MoveToward(Vector3 destination)
    {
        Vector3 dir = (destination - transform.position).normalized;
        transform.position += dir * moveSpeed * Time.deltaTime;
        if (dir != Vector3.zero) transform.rotation = Quaternion.LookRotation(dir);
    }

    void PickNewPatrolTarget()
    {
        Vector2 r = Random.insideUnitCircle * patrolRadius;
        _patrolTarget = _origin + new Vector3(r.x, 0, r.y);
    }

    void FindTarget()
    {
        var player = GameObject.FindGameObjectWithTag(""Player"");
        if (player != null) target = player.transform;
    }
}";

        public static string GodotCode => @"
extends CharacterBody3D

# SimpleAI.gd
enum AIState { PATROL, CHASE, ATTACK, RETURN }

var current_state := AIState.PATROL
var patrol_radius := 5.0
var chase_range := 8.0
var attack_range := 2.0
var move_speed := 3.0
var patrol_wait_time := 2.0
var target: Node3D = null

var _origin: Vector3
var _patrol_target: Vector3
var _wait_timer := 0.0
var _nav_agent: NavigationAgent3D = null

func _ready() -> void:
    _origin = global_position
    _pick_new_patrol_target()
    _nav_agent = get_node_or_null(""NavigationAgent3D"")

func _physics_process(delta: float) -> void:
    if target == null:
        _find_target()
        if target == null:
            _patrol(delta)
            return
    var dist := global_position.distance_to(target.global_position)
    match current_state:
        AIState.PATROL:
            if dist <= chase_range:
                current_state = AIState.CHASE
            else:
                _patrol(delta)
        AIState.CHASE:
            if dist <= attack_range:
                current_state = AIState.ATTACK
            elif dist > chase_range * 1.5:
                current_state = AIState.RETURN
            else:
                _chase(delta)
        AIState.ATTACK:
            if dist > attack_range * 1.2:
                current_state = AIState.CHASE
            else:
                _attack()
        AIState.RETURN:
            if dist <= chase_range:
                current_state = AIState.CHASE
            else:
                _return_to_origin(delta)
    move_and_slide()

func _patrol(delta: float) -> void:
    _move_toward(_patrol_target)
    if global_position.distance_to(_patrol_target) < 0.5:
        _wait_timer += delta
        if _wait_timer >= patrol_wait_time:
            _pick_new_patrol_target()
            _wait_timer = 0.0

func _chase(_delta: float) -> void:
    _move_toward(target.global_position)

func _attack() -> void:
    look_at(target.global_position, Vector3.UP)

func _return_to_origin(delta: float) -> void:
    _move_toward(_origin)
    if global_position.distance_to(_origin) < 0.5:
        current_state = AIState.PATROL
        _pick_new_patrol_target()

func _move_toward(destination: Vector3) -> void:
    var dir := (destination - global_position).normalized()
    velocity = dir * move_speed

func _pick_new_patrol_target() -> void:
    var r := Vector2(
        randf_range(-patrol_radius, patrol_radius),
        randf_range(-patrol_radius, patrol_radius)
    )
    _patrol_target = _origin + Vector3(r.x, 0, r.y)

func _find_target() -> void:
    var player = get_tree().get_first_node_in_group(""Player"")
    if player != null:
        target = player as Node3D";

        public static string Html5Code => @"
// SimpleAI.js — ES Module

export const AIState = { PATROL: 'patrol', CHASE: 'chase', ATTACK: 'attack', RETURN: 'return' };

export class SimpleAI {
    constructor(options = {}) {
        this.state = AIState.PATROL;
        this.patrolRadius = options.patrolRadius ?? 5;
        this.chaseRange = options.chaseRange ?? 8;
        this.attackRange = options.attackRange ?? 2;
        this.moveSpeed = options.moveSpeed ?? 3;
        this.patrolWaitTime = options.patrolWaitTime ?? 2;
        this.target = null;
        this.origin = { x: 0, y: 0, z: 0 };
        this.patrolTarget = { x: 0, y: 0, z: 0 };
        this.position = { x: 0, y: 0, z: 0 };
        this.waitTimer = 0;
        this._pickNewPatrolTarget();
    }

    setPosition(x, y, z) { this.position = { x, y, z }; }

    update(delta) {
        if (!this.target) {
            this._findTarget();
            if (!this.target) { this._patrol(delta); return; }
        }
        const dist = this._dist3D(this.position, this.target);

        switch (this.state) {
            case AIState.PATROL:
                if (dist <= this.chaseRange) this.state = AIState.CHASE;
                else this._patrol(delta);
                break;
            case AIState.CHASE:
                if (dist <= this.attackRange) this.state = AIState.ATTACK;
                else if (dist > this.chaseRange * 1.5) this.state = AIState.RETURN;
                else this._chase(delta);
                break;
            case AIState.ATTACK:
                if (dist > this.attackRange * 1.2) this.state = AIState.CHASE;
                else this._attack();
                break;
            case AIState.RETURN:
                if (dist <= this.chaseRange) this.state = AIState.CHASE;
                else this._returnToOrigin(delta);
                break;
        }
    }

    _patrol(delta) {
        this._moveToward(this.patrolTarget);
        if (this._dist3D(this.position, this.patrolTarget) < 0.5) {
            this.waitTimer += delta;
            if (this.waitTimer >= this.patrolWaitTime) {
                this._pickNewPatrolTarget();
                this.waitTimer = 0;
            }
        }
    }

    _chase() { this._moveToward(this.target); }
    _attack() { /* override — base does nothing */ }

    _returnToOrigin(delta) {
        this._moveToward(this.origin);
        if (this._dist3D(this.position, this.origin) < 0.5) {
            this.state = AIState.PATROL;
            this._pickNewPatrolTarget();
        }
    }

    _moveToward(dest) {
        const dx = dest.x - this.position.x;
        const dz = dest.z - this.position.z;
        const len = Math.sqrt(dx * dx + dz * dz);
        if (len > 0.01) {
            this.position.x += (dx / len) * this.moveSpeed * 0.016;
            this.position.z += (dz / len) * this.moveSpeed * 0.016;
        }
    }

    _pickNewPatrolTarget() {
        const angle = Math.random() * Math.PI * 2;
        const r = Math.random() * this.patrolRadius;
        this.patrolTarget = {
            x: this.origin.x + Math.cos(angle) * r,
            y: 0,
            z: this.origin.z + Math.sin(angle) * r
        };
    }

    _findTarget() { /* override — set this.target to a { x, y, z } object */ }

    _dist3D(a, b) {
        const dx = a.x - (b.x || b.position?.x ?? 0);
        const dy = a.y - (b.y || b.position?.y ?? 0);
        const dz = a.z - (b.z || b.position?.z ?? 0);
        return Math.sqrt(dx * dx + dy * dy + dz * dz);
    }

    _dist3D(a, b) {
        // If b is an object with position, use that
        const bx = b.x ?? b.position?.x ?? 0;
        const by = b.y ?? b.position?.y ?? 0;
        const bz = b.z ?? b.position?.z ?? 0;
        return Math.sqrt((a.x - bx) ** 2 + (a.y - by) ** 2 + (a.z - bz) ** 2);
    }
}";

    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. ParticleEffects
    // ──────────────────────────────────────────────────────────────────────────
    public static class ParticleEffects
    {
        public static string Description => "Simple particle system with burst, stream, and attraction modes";
        public static string[] Dependencies => [];

        public static string UnityCode => @"
using System.Collections.Generic;
using UnityEngine;

public class ParticleEffects : MonoBehaviour
{
    public ParticleSystem prefab;
    public Color defaultColor = Color.white;
    public float defaultLifetime = 2f;
    public float defaultSpeed = 3f;

    public void Burst(Vector3 position, int count = 20, Color? color = null, float? speed = null)
    {
        var ps = Instantiate(prefab, position, Quaternion.identity);
        var main = ps.main;
        main.startColor = color ?? defaultColor;
        main.startSpeed = speed ?? defaultSpeed;
        main.startLifetime = defaultLifetime;

        var burst = ps.emission.GetBurst(0);
        burst.count = count;

        ps.Play();
        Destroy(ps.gameObject, ps.main.startLifetime.constantMax + 0.5f);
    }

    public void Stream(Vector3 origin, Vector3 target, int count = 10, Color? color = null)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = Vector3.Lerp(origin, target, (float)i / count);
            Burst(pos, 1, color);
        }
    }

    public void Attract(Vector3 center, float radius = 2f, int count = 30, Color? color = null)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = center + Random.insideUnitSphere * radius;
            var ps = Instantiate(prefab, pos, Random.rotation);
            var main = ps.main;
            main.startColor = color ?? defaultColor;
            main.startSpeed = Random.Range(1f, 3f);
            main.startLifetime = defaultLifetime;

            // Set velocity toward center
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            Vector3 dir = (center - pos).normalized * Random.Range(1f, 4f);
            vel.x = dir.x; vel.y = dir.y; vel.z = dir.z;

            ps.Play();
            Destroy(ps.gameObject, ps.main.startLifetime.constantMax + 0.5f);
        }
    }
}";

        public static string GodotCode => @"
extends Node3D

# ParticleEffects.gd — utility node for spawning GPU particles

var _particle_scene: PackedScene = preload(""res://Particles/ParticleTemplate.tscn"")
var default_color := Color.WHITE
var default_lifetime := 2.0
var default_speed := 3.0

func burst(position: Vector3, count := 20, color := default_color, speed := default_speed) -> void:
    var gp := _particle_scene.instantiate() as GpuParticles3D
    gp.global_position = position
    var mat := gp.process_material as ParticleProcessMaterial
    if mat:
        mat.color = color
        mat.initial_velocity_min = speed * 0.5
        mat.initial_velocity_max = speed * 1.5
    gp.lifetime = default_lifetime
    gp.amount = count
    gp.emitting = true
    add_child(gp)
    # Auto-cleanup
    await get_tree().create_timer(default_lifetime + 1.0).timeout
    gp.queue_free()

func stream(origin: Vector3, target: Vector3, count := 10, color := default_color) -> void:
    for i in range(count):
        var t := float(i) / float(count)
        var pos := origin.lerp(target, t)
        burst(pos, 1, color)

func attract(center: Vector3, radius := 2.0, count := 30, color := default_color) -> void:
    for i in range(count):
        var pos := center + Vector3(
            randf_range(-radius, radius),
            randf_range(-radius, radius),
            randf_range(-radius, radius)
        )
        var gp := _particle_scene.instantiate() as GpuParticles3D
        gp.global_position = pos
        var mat := gp.process_material as ParticleProcessMaterial
        if mat:
            mat.color = color
            mat.initial_velocity_min = 1.0
            mat.initial_velocity_max = 3.0
            mat.direction = (center - pos).normalized()
            mat.direction_spread = 15.0
        gp.lifetime = default_lifetime
        gp.amount = 1
        gp.emitting = true
        add_child(gp)
        await get_tree().create_timer(default_lifetime + 0.5).timeout
        gp.queue_free()";

        public static string Html5Code => @"
// ParticleEffects.js — ES Module

export class ParticleEffects {
    constructor(canvas) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        this.particles = [];
        this.defaultColor = '#ffffff';
        this.defaultLifetime = 2;
        this.defaultSpeed = 3;
    }

    burst(x, y, count = 20, color = this.defaultColor, speed = this.defaultSpeed) {
        for (let i = 0; i < count; i++) {
            const angle = Math.random() * Math.PI * 2;
            const spd = (Math.random() * 0.5 + 0.5) * speed;
            this.particles.push({
                x, y,
                vx: Math.cos(angle) * spd,
                vy: Math.sin(angle) * spd,
                lifetime: this.defaultLifetime + Math.random() * 0.5,
                age: 0,
                color,
                size: Math.random() * 4 + 2
            });
        }
    }

    stream(x1, y1, x2, y2, count = 10, color = this.defaultColor) {
        for (let i = 0; i < count; i++) {
            const t = i / count;
            const x = x1 + (x2 - x1) * t;
            const y = y1 + (y2 - y1) * t;
            this.burst(x, y, 1, color);
        }
    }

    attract(cx, cy, radius = 100, count = 30, color = this.defaultColor) {
        for (let i = 0; i < count; i++) {
            const angle = Math.random() * Math.PI * 2;
            const r = Math.random() * radius;
            const x = cx + Math.cos(angle) * r;
            const y = cy + Math.sin(angle) * r;
            const dx = cx - x, dy = cy - y;
            const len = Math.sqrt(dx * dx + dy * dy) || 1;
            const spd = (Math.random() * 0.5 + 0.5) * this.defaultSpeed;
            this.particles.push({
                x, y,
                vx: (dx / len) * spd,
                vy: (dy / len) * spd,
                lifetime: this.defaultLifetime,
                age: 0,
                color,
                size: Math.random() * 3 + 1
            });
        }
    }

    update(delta) {
        for (let i = this.particles.length - 1; i >= 0; i--) {
            const p = this.particles[i];
            p.x += p.vx * delta;
            p.y += p.vy * delta;
            p.age += delta;
            p.vx *= 0.97;
            p.vy *= 0.97;
            if (p.age >= p.lifetime) this.particles.splice(i, 1);
        }
    }

    draw() {
        for (const p of this.particles) {
            const alpha = 1 - p.age / p.lifetime;
            this.ctx.globalAlpha = alpha;
            this.ctx.fillStyle = p.color;
            this.ctx.beginPath();
            this.ctx.arc(p.x, p.y, p.size * alpha, 0, Math.PI * 2);
            this.ctx.fill();
        }
        this.ctx.globalAlpha = 1;
    }
}";

    }

    // ---- Registry (consumed by the agent-facing game_module tool) ----------

    public sealed record ModuleInfo(string Name, string Description, string[] Dependencies);

    /// <summary>Every module in the library with its description and
    /// dependencies, for the tool's 'list' action.</summary>
    public static IReadOnlyList<ModuleInfo> List() =>
    [
        new(nameof(InventorySystem), InventorySystem.Description, InventorySystem.Dependencies),
        new(nameof(DialogSystem), DialogSystem.Description, DialogSystem.Dependencies),
        new(nameof(QuestSystem), QuestSystem.Description, QuestSystem.Dependencies),
        new(nameof(SaveLoadSystem), SaveLoadSystem.Description, SaveLoadSystem.Dependencies),
        new(nameof(HealthCombat), HealthCombat.Description, HealthCombat.Dependencies),
        new(nameof(ProgressionSystem), ProgressionSystem.Description, ProgressionSystem.Dependencies),
        new(nameof(SimpleAI), SimpleAI.Description, SimpleAI.Dependencies),
        new(nameof(ParticleEffects), ParticleEffects.Description, ParticleEffects.Dependencies),
    ];

    /// <summary>Drop-in source for one module in one engine's language, or
    /// null when the module or engine is unknown. Module lookup is
    /// case-insensitive and accepts the short alias without the System
    /// suffix ("inventory" -> InventorySystem).</summary>
    public static string? GetCode(string moduleName, string engine)
    {
        var codes = (moduleName ?? "").Trim().ToLowerInvariant() switch
        {
            "inventorysystem" or "inventory" => (InventorySystem.UnityCode, InventorySystem.GodotCode, InventorySystem.Html5Code),
            "dialogsystem" or "dialog" => (DialogSystem.UnityCode, DialogSystem.GodotCode, DialogSystem.Html5Code),
            "questsystem" or "quest" => (QuestSystem.UnityCode, QuestSystem.GodotCode, QuestSystem.Html5Code),
            "saveloadsystem" or "saveload" or "save" => (SaveLoadSystem.UnityCode, SaveLoadSystem.GodotCode, SaveLoadSystem.Html5Code),
            "healthcombat" or "health" or "combat" => (HealthCombat.UnityCode, HealthCombat.GodotCode, HealthCombat.Html5Code),
            "progressionsystem" or "progression" or "xp" => (ProgressionSystem.UnityCode, ProgressionSystem.GodotCode, ProgressionSystem.Html5Code),
            "simpleai" or "ai" or "enemyai" => (SimpleAI.UnityCode, SimpleAI.GodotCode, SimpleAI.Html5Code),
            "particleeffects" or "particles" => (ParticleEffects.UnityCode, ParticleEffects.GodotCode, ParticleEffects.Html5Code),
            _ => ((string, string, string)?)null
        };
        if (codes is null) return null;
        return (engine ?? "").Trim().ToLowerInvariant() switch
        {
            "unity" => codes.Value.Item1,
            "godot" => codes.Value.Item2,
            "html5" or "html" or "js" or "javascript" => codes.Value.Item3,
            _ => null
        };
    }
}