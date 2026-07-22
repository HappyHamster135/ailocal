namespace AiLocal.Node.Hosting.GameMechanics;

/// <summary>
/// v2.2.0: Library of reusable game mechanic implementations.
/// Beyond isolated components (inventory, dialog from GameModuleLibrary),
/// these are COMPOSITE patterns — small, complete implementations of
/// common game features that agents can insert into any project.
///
/// Each mechanic provides GDScript, C# (Unity), and JavaScript (HTML5)
/// implementations. Use Get(mechanicName, engine) to retrieve.
/// </summary>
public static class GameMechanicLibrary
{
    public record Mechanic(string Name, string Description, string Category,
        string GDScript, string CSharp, string JavaScript);

    private static readonly Dictionary<string, Mechanic> _mechanics = new();

    static GameMechanicLibrary()
    {
        Add(DoubleJump);
        Add(EnemyPatrol);
        Add(ShopSystem);
        Add(PowerUpTimer);
        Add(HealthBar);
        Add(CheckpointSystem);
        Add(CameraFollow);
        Add(ScorePopup);
        Add(DamageFlash);
        Add(CountdownTimer);
    }

    private static void Add(Mechanic m) => _mechanics[m.Name] = m;

    public static Mechanic? Get(string name, string? engine = null) =>
        _mechanics.TryGetValue(name.ToLowerInvariant(), out var m) ? m : null;

    public static IReadOnlyList<Mechanic> List() => [.. _mechanics.Values];

    public static string? GetCode(string name, string engine) =>
        Get(name) is { } m
            ? engine.ToLowerInvariant() switch
            {
                "godot" => m.GDScript,
                "unity" => m.CSharp,
                "html5" => m.JavaScript,
                _ => m.GDScript
            }
            : null;

    // ──────────────────────────────────────────────────────────────────────
    // 1. DOUBLE JUMP
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Mechanic DoubleJump = new(
        "double_jump",
        "Air jump after leaving ground — coyote time + jump buffer included",
        "movement",
        GDScript: @"# DoubleJump.gd — attach to CharacterBody2D alongside movement script
var can_double_jump := false
var has_double_jumped := false
@export var jump_velocity := -400.0
@export var double_jump_velocity := -340.0
@export var coyote_frames := 6
@export var jump_buffer_frames := 4
var coyote_timer := 0
var buffer_timer := 0

func _physics_process(delta: float) -> void:
    if is_on_floor():
        can_double_jump = true
        has_double_jumped = false
        coyote_timer = coyote_frames
    elif coyote_timer > 0:
        coyote_timer -= 1

    if Input.is_action_just_pressed(""ui_accept""):
        buffer_timer = jump_buffer_frames

    if buffer_timer > 0:
        if is_on_floor() or coyote_timer > 0:
            velocity.y = jump_velocity
            buffer_timer = 0
        elif can_double_jump and not has_double_jumped:
            velocity.y = double_jump_velocity
            has_double_jumped = true
            can_double_jump = false
            buffer_timer = 0
    elif buffer_timer > 0:
        buffer_timer -= 1",

        CSharp: @"// DoubleJump.cs — attach to Rigidbody2D/CharacterController
using UnityEngine;

public class DoubleJump : MonoBehaviour
{
    public float jumpForce = 12f;
    public float doubleJumpForce = 10f;
    public float coyoteTime = 0.15f;
    public float jumpBufferTime = 0.1f;

    private bool canDoubleJump;
    private bool hasDoubleJumped;
    private float coyoteTimer;
    private float bufferTimer;
    private bool isGrounded;

    void Update()
    {
        isGrounded = Physics2D.Raycast(transform.position, Vector2.down, 0.6f);
        if (isGrounded) { canDoubleJump = true; hasDoubleJumped = false; coyoteTimer = coyoteTime; }
        else if (coyoteTimer > 0) coyoteTimer -= Time.deltaTime;

        if (Input.GetButtonDown(""Jump"")) bufferTimer = jumpBufferTime;

        if (bufferTimer > 0)
        {
            if (isGrounded || coyoteTimer > 0)
            {
                GetComponent<Rigidbody2D>().velocity = new Vector2(
                    GetComponent<Rigidbody2D>().velocity.x, jumpForce);
                bufferTimer = 0;
            }
            else if (canDoubleJump && !hasDoubleJumped)
            {
                GetComponent<Rigidbody2D>().velocity = new Vector2(
                    GetComponent<Rigidbody2D>().velocity.x, doubleJumpForce);
                hasDoubleJumped = true; canDoubleJump = false; bufferTimer = 0;
            }
        }
        else if (bufferTimer > 0) bufferTimer -= Time.deltaTime;
    }
}",

        JavaScript: @"// DoubleJump.js — ES module for HTML5 canvas games
export class DoubleJump {
    constructor(player, { jumpVel = -12, doubleJumpVel = -10, coyoteFrames = 8, bufferFrames = 5 } = {}) {
        this.player = player;
        this.jumpVel = jumpVel;
        this.doubleJumpVel = doubleJumpVel;
        this.coyoteFrames = coyoteFrames;
        this.bufferFrames = bufferFrames;
        this.canDoubleJump = false;
        this.hasDoubleJumped = false;
        this.coyoteTimer = 0;
        this.bufferTimer = 0;
    }

    update(keys, isOnGround) {
        // Track coyote time
        if (isOnGround) {
            this.canDoubleJump = true;
            this.hasDoubleJumped = false;
            this.coyoteTimer = this.coyoteFrames;
        } else if (this.coyoteTimer > 0) {
            this.coyoteTimer--;
        }

        // Jump buffer
        if (keys.jump?.justPressed) {
            this.bufferTimer = this.bufferFrames;
        }

        if (this.bufferTimer > 0) {
            if (isOnGround || this.coyoteTimer > 0) {
                this.player.vy = this.jumpVel;
                this.bufferTimer = 0;
            } else if (this.canDoubleJump && !this.hasDoubleJumped) {
                this.player.vy = this.doubleJumpVel;
                this.hasDoubleJumped = true;
                this.canDoubleJump = false;
                this.bufferTimer = 0;
            }
        }

        if (this.bufferTimer > 0) this.bufferTimer--;
    }
}"
    );

    // ──────────────────────────────────────────────────────────────────────
    // 2. ENEMY PATROL
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Mechanic EnemyPatrol = new(
        "enemy_patrol",
        "Enemy walks back and forth between two points with configurable speed and pause",
        "ai",
        GDScript: @"# EnemyPatrol.gd — attach to CharacterBody2D
@export var patrol_distance := 100.0
@export var speed := 80.0
@export var pause_time := 0.8
var start_pos: Vector2
var direction := 1
var pause_timer := 0.0

func _ready() -> void:
    start_pos = global_position

func _physics_process(delta: float) -> void:
    if pause_timer > 0:
        pause_timer -= delta
        return
    velocity.x = direction * speed
    move_and_slide()
    # Reverse at patrol edges
    var dist_from_start := abs(global_position.x - start_pos.x)
    if dist_from_start >= patrol_distance:
        direction *= -1
        pause_timer = pause_time",

        CSharp: @"// EnemyPatrol.cs
using UnityEngine;

public class EnemyPatrol : MonoBehaviour
{
    public float patrolDistance = 3f;
    public float speed = 2f;
    public float pauseTime = 0.8f;
    private Vector2 startPos;
    private int direction = 1;
    private float pauseTimer;

    void Start() => startPos = transform.position;

    void Update()
    {
        if (pauseTimer > 0) { pauseTimer -= Time.deltaTime; return; }
        transform.Translate(Vector2.right * direction * speed * Time.deltaTime);
        if (Mathf.Abs(transform.position.x - startPos.x) >= patrolDistance)
        {
            direction *= -1;
            pauseTimer = pauseTime;
        }
    }
}",

        JavaScript: @"// EnemyPatrol.js
export class EnemyPatrol {
    constructor(enemy, { distance = 100, speed = 2, pauseTime = 0.8 } = {}) {
        this.enemy = enemy;
        this.distance = distance;
        this.speed = speed;
        this.pauseTime = pauseTime;
        this.startX = enemy.x;
        this.direction = 1;
        this.pauseTimer = 0;
    }

    update(dt) {
        if (this.pauseTimer > 0) { this.pauseTimer -= dt; return; }
        this.enemy.x += this.direction * this.speed * dt * 60;
        if (Math.abs(this.enemy.x - this.startX) >= this.distance) {
            this.direction *= -1;
            this.pauseTimer = this.pauseTime;
        }
    }
}"
    );

    // ──────────────────────────────────────────────────────────────────────
    // 3. SHOP SYSTEM
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Mechanic ShopSystem = new(
        "shop",
        "Buy/sell items with currency, price list, and 'not enough money' guard",
        "economy",
        GDScript: @"# ShopSystem.gd — attach to a Control/Node for shop UI
var items := [
    { ""name"": ""Health Potion"", ""price"": 5, ""effect"": ""heal_25"" },
    { ""name"": ""Speed Boost"",  ""price"": 8, ""effect"": ""speed_10s"" },
    { ""name"": ""Extra Life"",   ""price"": 20, ""effect"": ""extra_life"" },
]
var player_coins := 0

func buy_item(index: int) -> bool:
    if index < 0 or index >= items.size():
        return false
    var item := items[index]
    if player_coins < item[""price""]:
        print(""Not enough coins! Need %d, have %d"" % [item[""price""], player_coins])
        return false
    player_coins -= item[""price""]
    apply_effect(item[""effect""])
    print(""Bought %s for %d coins!"" % [item[""name""], item[""price""]])
    return true

func apply_effect(effect: String) -> void:
    match effect:
        ""heal_25"": pass  # player.heal(25)
        ""speed_10s"": pass  # player.activate_speed_boost(10.0)
        ""extra_life"": pass  # player.lives += 1",

        CSharp: @"// ShopSystem.cs
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ShopItem { public string name; public int price; public string effect; }

public class ShopSystem : MonoBehaviour
{
    public List<ShopItem> items = new() {
        new() { name = ""Health Potion"", price = 5, effect = ""heal_25"" },
        new() { name = ""Speed Boost"", price = 8, effect = ""speed_10s"" },
        new() { name = ""Extra Life"", price = 20, effect = ""extra_life"" },
    };
    public int playerCoins;

    public bool BuyItem(int index)
    {
        if (index < 0 || index >= items.Count) return false;
        var item = items[index];
        if (playerCoins < item.price)
        {
            Debug.Log($""Not enough coins! Need {item.price}, have {playerCoins}"");
            return false;
        }
        playerCoins -= item.price;
        ApplyEffect(item.effect);
        return true;
    }

    void ApplyEffect(string effect) { /* implement per game */ }
}",

        JavaScript: @"// ShopSystem.js
export class ShopSystem {
    constructor(items = [
        { name: 'Health Potion', price: 5, effect: 'heal_25' },
        { name: 'Speed Boost', price: 8, effect: 'speed_10s' },
        { name: 'Extra Life', price: 20, effect: 'extra_life' },
    ]) {
        this.items = items;
        this.coins = 0;
    }

    buy(index) {
        const item = this.items[index];
        if (!item) return false;
        if (this.coins < item.price) {
            console.log(`Not enough coins! Need ${item.price}, have ${this.coins}`);
            return false;
        }
        this.coins -= item.price;
        this.apply(item.effect);
        return true;
    }

    apply(effect) { /* implement per game */ }
}"
    );

    // ──────────────────────────────────────────────────────────────────────
    // 4. POWER-UP TIMER
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Mechanic PowerUpTimer = new(
        "powerup_timer",
        "Temporary buff with countdown display and visual indicator",
        "powerup",
        GDScript: @"# PowerUpTimer.gd — attach to player Node
var active_powerup := """"
var powerup_timer := 0.0
var powerup_label: Label  # assign a Label child in _ready

func activate(name: String, duration: float, multiplier: float) -> void:
    active_powerup = name
    powerup_timer = duration
    # Apply effect (example: speed boost)
    if name == ""speed"":
        SPEED *= multiplier

func _process(delta: float) -> void:
    if powerup_timer > 0:
        powerup_timer -= delta
        if powerup_label:
            powerup_label.text = ""%s: %.1fs"" % [active_powerup, powerup_timer]
            powerup_label.visible = true
        if powerup_timer <= 0:
            _deactivate()

func _deactivate() -> void:
    if active_powerup == ""speed"":
        SPEED /= multiplier  # restore original
    active_powerup = """"
    if powerup_label:
        powerup_label.visible = false",

        CSharp: @"// PowerUpTimer.cs
using UnityEngine;
using UnityEngine.UI;

public class PowerUpTimer : MonoBehaviour
{
    public Text timerLabel;
    private string activePowerup;
    private float powerupTimer;
    private float originalSpeed;

    public void Activate(string name, float duration, float multiplier)
    {
        activePowerup = name;
        powerupTimer = duration;
        if (name == ""speed"") { originalSpeed = GetComponent<Player>().speed; GetComponent<Player>().speed *= multiplier; }
    }

    void Update()
    {
        if (powerupTimer > 0)
        {
            powerupTimer -= Time.deltaTime;
            if (timerLabel) { timerLabel.text = $""{activePowerup}: {powerupTimer:F1}s""; timerLabel.enabled = true; }
            if (powerupTimer <= 0) Deactivate();
        }
    }

    void Deactivate()
    {
        if (activePowerup == ""speed"") GetComponent<Player>().speed = originalSpeed;
        activePowerup = null;
        if (timerLabel) timerLabel.enabled = false;
    }
}",

        JavaScript: @"// PowerUpTimer.js
export class PowerUpTimer {
    constructor(labelEl) {
        this.label = labelEl;
        this.active = null;
        this.timer = 0;
    }

    activate(name, duration, applyFn, removeFn) {
        this.active = { name, removeFn };
        this.timer = duration;
        applyFn();
    }

    update(dt) {
        if (this.timer > 0) {
            this.timer -= dt;
            if (this.label) this.label.textContent = `${this.active.name}: ${this.timer.toFixed(1)}s`;
            if (this.timer <= 0) {
                this.active.removeFn();
                this.active = null;
                if (this.label) this.label.textContent = '';
            }
        }
    }
}"
    );

    // ──────────────────────────────────────────────────────────────────────
    // 5. HEALTH BAR (simplified, no UI library dependency)
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Mechanic HealthBar = new(
        "health_bar",
        "HP bar with damage/heal tween animation and color gradient (green→yellow→red)",
        "ui",
        GDScript: @"# HealthBar.gd — attach to a ColorRect (as bar fill) + Label (text)
@export var max_hp := 100.0
var hp: float = max_hp
@onready var fill := $Fill as ColorRect  # child ColorRect, scale x for fill
@onready var label := $Label as Label

func _ready() -> void:
    hp = max_hp
    update_display()

func take_damage(amount: float) -> void:
    hp = max(0, hp - amount)
    var tween := create_tween()
    tween.tween_property(fill, ""scale:x"", hp / max_hp, 0.2)
    update_color()
    if label: label.text = ""%d/%d"" % [int(hp), int(max_hp)]

func heal(amount: float) -> void:
    hp = min(max_hp, hp + amount)
    update_display()

func update_display() -> void:
    fill.scale.x = hp / max_hp
    update_color()

func update_color() -> void:
    var ratio := hp / max_hp
    if ratio > 0.5:
        fill.color = Color(0.2 + (1 - ratio) * 1.6, 1, 0.2)
    else:
        fill.color = Color(1, ratio * 2, 0.1)",

        CSharp: @"// HealthBar.cs
using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    public float maxHp = 100f;
    public Image fillImage;
    public Text label;
    private float hp;

    void Start() { hp = maxHp; UpdateDisplay(); }

    public void TakeDamage(float amount)
    {
        hp = Mathf.Max(0, hp - amount);
        UpdateDisplay();
    }

    public void Heal(float amount)
    {
        hp = Mathf.Min(maxHp, hp + amount);
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        var ratio = hp / maxHp;
        fillImage.fillAmount = ratio;
        fillImage.color = ratio > 0.5f
            ? new Color(0.2f + (1 - ratio) * 1.6f, 1f, 0.2f)
            : new Color(1f, ratio * 2f, 0.1f);
        if (label) label.text = $""{hp:F0}/{maxHp}"";
    }
}",

        JavaScript: @"// HealthBar.js
export class HealthBar {
    constructor(maxHp, fillEl, labelEl) {
        this.maxHp = maxHp;
        this.hp = maxHp;
        this.fill = fillEl;
        this.label = labelEl;
    }

    takeDamage(amount) {
        this.hp = Math.max(0, this.hp - amount);
        this.update();
    }

    heal(amount) {
        this.hp = Math.min(this.maxHp, this.hp + amount);
        this.update();
    }

    update() {
        const ratio = this.hp / this.maxHp;
        if (this.fill) this.fill.style.transform = `scaleX(${ratio})`;
        if (this.label) this.label.textContent = `${Math.floor(this.hp)}/${this.maxHp}`;
        // Color gradient
        if (this.fill) {
            const r = ratio > 0.5 ? 0.2 + (1 - ratio) * 1.6 : 1;
            const g = ratio > 0.5 ? 1 : ratio * 2;
            this.fill.style.backgroundColor = `rgb(${Math.floor(r*255)},${Math.floor(g*255)},25)`;
        }
    }
}"
    );

    // ──────────────────────────────────────────────────────────────────────
    // 6. CHECKPOINT SYSTEM
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Mechanic CheckpointSystem = new(
        "checkpoint",
        "Respawn at last checkpoint on death with visual activation feedback",
        "gameplay",
        GDScript: @"# CheckpointSystem.gd — attach to player Node
var checkpoint_pos := Vector2.ZERO
var checkpoint_active := false

func _ready() -> void:
    checkpoint_pos = global_position

func set_checkpoint(pos: Vector2) -> void:
    if not checkpoint_active or pos != checkpoint_pos:
        checkpoint_pos = pos
        checkpoint_active = true
        # Visual feedback: brief flash or particle burst at checkpoint
        print(""Checkpoint set!"")  # replace with particles/sound

func respawn() -> void:
    global_position = checkpoint_pos
    hp = max_hp  # reset health
    print(""Respawned at checkpoint"")",

        CSharp: @"// CheckpointSystem.cs
using UnityEngine;

public class CheckpointSystem : MonoBehaviour
{
    private Vector2 checkpointPos;
    private bool checkpointActive;

    void Start() => checkpointPos = transform.position;

    public void SetCheckpoint(Vector2 pos)
    {
        checkpointPos = pos;
        checkpointActive = true;
        Debug.Log(""Checkpoint set!"");
    }

    public void Respawn()
    {
        transform.position = checkpointPos;
        GetComponent<HealthBar>()?.Heal(999);  // reset HP
        Debug.Log(""Respawned at checkpoint"");
    }
}",

        JavaScript: @"// CheckpointSystem.js
export class CheckpointSystem {
    constructor(player) {
        this.player = player;
        this.pos = { x: player.x, y: player.y };
    }

    set(x, y) {
        this.pos = { x, y };
        console.log('Checkpoint set!');
    }

    respawn() {
        this.player.x = this.pos.x;
        this.player.y = this.pos.y;
        this.player.hp = this.player.maxHp;
    }
}"
    );

    // ──────────────────────────────────────────────────────────────────────
    // 7. CAMERA FOLLOW
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Mechanic CameraFollow = new(
        "camera_follow",
        "Smooth camera that follows player with dead zone and look-ahead",
        "camera",
        GDScript: @"# CameraFollow.gd — attach to Camera2D
@export var follow_speed := 5.0
@export var look_ahead_factor := 0.3
@export var dead_zone := Vector2(30, 20)
var target: Node2D

func _ready() -> void:
    target = get_parent() as Node2D  # or assign manually

func _process(delta: float) -> void:
    if not target: return
    var desired := target.global_position
    # Look-ahead based on velocity
    if target is CharacterBody2D:
        desired += target.velocity * look_ahead_factor
    # Dead zone: only move if target is outside zone
    var diff := desired - global_position
    if abs(diff.x) > dead_zone.x:
        desired.x = global_position.x + sign(diff.x) * (abs(diff.x) - dead_zone.x)
    else:
        desired.x = global_position.x
    if abs(diff.y) > dead_zone.y:
        desired.y = global_position.y + sign(diff.y) * (abs(diff.y) - dead_zone.y)
    else:
        desired.y = global_position.y
    global_position = global_position.lerp(desired, follow_speed * delta)",

        CSharp: @"// CameraFollow.cs
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float followSpeed = 5f;
    public float lookAheadFactor = 0.3f;
    public Vector2 deadZone = new(1f, 0.6f);

    void LateUpdate()
    {
        if (!target) return;
        Vector3 desired = target.position;
        var diff = desired - transform.position;
        if (Mathf.Abs(diff.x) < deadZone.x) diff.x = 0;
        if (Mathf.Abs(diff.y) < deadZone.y) diff.y = 0;
        transform.position = Vector3.Lerp(transform.position, desired - diff, followSpeed * Time.deltaTime);
        transform.position = new Vector3(transform.position.x, transform.position.y, -10);
    }
}",

        JavaScript: @"// CameraFollow.js
export class CameraFollow {
    constructor(camera, target, { speed = 5, deadZone = { x: 30, y: 20 } } = {}) {
        this.cam = camera;
        this.target = target;
        this.speed = speed;
        this.deadZone = deadZone;
    }

    update(dt) {
        const dx = this.target.x - this.cam.x;
        const dy = this.target.y - this.cam.y;
        if (Math.abs(dx) > this.deadZone.x) this.cam.x += (dx - Math.sign(dx) * this.deadZone.x) * this.speed * dt;
        if (Math.abs(dy) > this.deadZone.y) this.cam.y += (dy - Math.sign(dy) * this.deadZone.y) * this.speed * dt;
    }
}"
    );

    // ──────────────────────────────────────────────────────────────────────
    // 8. SCORE POPUP
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Mechanic ScorePopup = new(
        "score_popup",
        "Floating score text that rises and fades out (like '+100' on coin pickup)",
        "juice",
        GDScript: @"# ScorePopup.gd — instantiate as child of CanvasLayer
func spawn(pos: Vector2, text: String, color := Color(1, 0.85, 0.2)) -> void:
    var label := Label.new()
    label.text = text
    label.add_theme_font_size_override(""font_size"", 20)
    label.add_theme_color_override(""font_color"", color)
    label.position = pos - Vector2(20, 0)
    add_child(label)

    var tween := create_tween()
    tween.set_parallel()
    tween.tween_property(label, ""position:y"", pos.y - 60, 0.8).set_ease(Tween.EASE_OUT)
    tween.tween_property(label, ""modulate:a"", 0.0, 0.8).set_ease(Tween.EASE_IN)
    tween.tween_callback(label.queue_free)",

        CSharp: @"// ScorePopup.cs
using UnityEngine;
using UnityEngine.UI;

public class ScorePopup : MonoBehaviour
{
    public GameObject popupPrefab;  // Text prefab

    public void Spawn(Vector2 pos, string text, Color? color = null)
    {
        var go = Instantiate(popupPrefab, pos, Quaternion.identity, transform);
        var label = go.GetComponent<Text>();
        label.text = text;
        label.color = color ?? new Color(1f, 0.85f, 0.2f);
        StartCoroutine(FadeAndRise(go, label));
    }

    System.Collections.IEnumerator FadeAndRise(GameObject go, Text label)
    {
        float t = 0;
        var startY = go.transform.position.y;
        while (t < 0.8f)
        {
            t += Time.deltaTime;
            go.transform.position = new Vector3(go.transform.position.x, startY + t * 2f, 0);
            var c = label.color; c.a = 1 - t / 0.8f; label.color = c;
            yield return null;
        }
        Destroy(go);
    }
}",

        JavaScript: @"// ScorePopup.js
export function spawnScorePopup(container, x, y, text, color = '#ffd833') {
    const el = document.createElement('div');
    el.textContent = text;
    el.style.cssText = `position:absolute;left:${x}px;top:${y}px;color:${color};
        font:bold 20px system-ui;pointer-events:none;transition:all 0.8s ease-out;`;
    container.appendChild(el);
    requestAnimationFrame(() => {
        el.style.transform = 'translateY(-60px)';
        el.style.opacity = '0';
    });
    setTimeout(() => el.remove(), 800);
}"
    );

    // ──────────────────────────────────────────────────────────────────────
    // 9. DAMAGE FLASH
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Mechanic DamageFlash = new(
        "damage_flash",
        "Sprite briefly flashes white/red on damage — essential game-feel juice",
        "juice",
        GDScript: @"# DamageFlash.gd — attach to player/enemy Sprite2D
@export var flash_duration := 0.12
@export var flash_color := Color(1, 0.3, 0.3, 0.8)
var original_modulate := Color.WHITE

func flash() -> void:
    modulate = flash_color
    var tween := create_tween()
    tween.tween_property(self, ""modulate"", original_modulate, flash_duration)",

        CSharp: @"// DamageFlash.cs
using UnityEngine;

public class DamageFlash : MonoBehaviour
{
    public float flashDuration = 0.12f;
    public Color flashColor = new(1f, 0.3f, 0.3f, 0.8f);
    private SpriteRenderer sr;
    private Color original;

    void Start() { sr = GetComponent<SpriteRenderer>(); original = sr.color; }

    public void Flash() => StartCoroutine(FlashRoutine());
    System.Collections.IEnumerator FlashRoutine()
    {
        sr.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        sr.color = original;
    }
}",

        JavaScript: @"// DamageFlash.js
export class DamageFlash {
    constructor(el, { duration = 120, color = 'rgba(255,80,80,0.8)' } = {}) {
        this.el = el;
        this.duration = duration;
        this.color = color;
        this.originalFilter = el.style.filter || '';
    }

    flash() {
        this.el.style.filter = `drop-shadow(0 0 8px ${this.color}) brightness(1.5)`;
        setTimeout(() => { this.el.style.filter = this.originalFilter; }, this.duration);
    }
}"
    );

    // ──────────────────────────────────────────────────────────────────────
    // 10. COUNTDOWN TIMER
    // ──────────────────────────────────────────────────────────────────────
    private static readonly Mechanic CountdownTimer = new(
        "countdown_timer",
        "\"3-2-1-GO!\" countdown sequence with scaling text -- used before races/minigames",
        "ui",
        GDScript: @"# CountdownTimer.gd — attach to CanvasLayer for pre-game countdown
signal finished

func start(seconds := 3, label: Label = null) -> void:
    var count := seconds
    while count > 0:
        if label:
            label.text = str(count)
            _pulse(label)
        await get_tree().create_timer(1.0).timeout
        count -= 1
    if label:
        label.text = ""GO!""
        _pulse(label)
    finished.emit()

func _pulse(l: Label) -> void:
    l.scale = Vector2(1.5, 1.5)
    var tween := create_tween()
    tween.tween_property(l, ""scale"", Vector2(1, 1), 0.5).set_ease(Tween.EASE_OUT)",

        CSharp: @"// CountdownTimer.cs
using UnityEngine;
using UnityEngine.UI;

public class CountdownTimer : MonoBehaviour
{
    public Text label;
    public System.Action onFinished;

    public void StartCountdown(int seconds = 3) => StartCoroutine(Run(seconds));

    System.Collections.IEnumerator Run(int seconds)
    {
        for (int i = seconds; i > 0; i--)
        {
            if (label) { label.text = i.ToString(); label.transform.localScale = Vector3.one * 1.5f; }
            yield return new WaitForSeconds(1f);
        }
        if (label) { label.text = ""GO!""; label.transform.localScale = Vector3.one * 1.5f; }
        onFinished?.Invoke();
    }
}",

        JavaScript: @"// CountdownTimer.js
export class CountdownTimer {
    constructor(labelEl, onFinish) {
        this.label = labelEl;
        this.onFinish = onFinish;
    }

    async start(seconds = 3) {
        for (let i = seconds; i > 0; i--) {
            if (this.label) { this.label.textContent = i; this.label.style.transform = 'scale(1.5)'; }
            await new Promise(r => setTimeout(r, 1000));
        }
        if (this.label) { this.label.textContent = 'GO!'; this.label.style.transform = 'scale(1.5)'; }
        this.onFinish?.();
    }
}"
    );
}
