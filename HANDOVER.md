# HANDOVER — v2.2.0 → v2.27.0+ session (2026-07-23/24)

## Status: KLART (10 nya Godot-kit + 5 stödsystem)

### Vad som byggdes

**10 nya Godot-kit** (GameScaffoldService.GodotKits.cs, +2934 rader, totalt 9537):
| Kit | Genre | Trigger | Funktionalitet |
|-----|-------|---------|----------------|
| Tower Siege | towerdefense | "tower defense", "torn" | 10x8 grid, 3 torn (arrow/cannon/frost), 3 fiendetyper, 10 vågor, ekonomi |
| Snake | snake | "snake", "orm" | 20x15 grid, växer, fartökning, highscore |
| Breakout | breakout | "breakout", "arkanoid" | Paddle/boll/tegel, vinkelbaserad studs, 3 liv |
| Quiz Night | quiz | "quiz", "frågesport" | 12 frågor, 15s timer, 4 val, 3 liv |
| Memory Match | memory | "memory", "kortspel" | 4x4 kort, matcha par, dragräknare |
| Minesweeper | minesweeper | "minesweeper", "minröj" | 10x10 grid, 15 minor, flagga, flood fill |
| Gold Mine | idle | "idle", "clicker" | Klicka för guld, 5 uppgraderingar, passiv inkomst, spara/ladda |
| Block Puzzle | blockpuzzle | "block", "tetris" | 7 block, rotation, ghost piece, radrensning |
| The Dungeon | roguelike | "roguelike", "dungeon" | Proceduralrum, bump-to-attack, XP/level, permadeath |
| Hero's Quest | rpg | "rpg", "äventyr" | Overworld, turordningsstrid, NPC-dialog, inventory, quests |

**Alla kit har:** Shell.startup(), Shell.options_panel(), quit-knapp, 3 svårighetsgrader, touch-kontroller, CPUParticles2D, screenshake, ljud (click/coin/hurt/win), focus_pending.

### Stödsystem uppdaterade
1. **GenreContracts.cs** — 8 nya genrefiler (towerdefense, snake, breakout, quiz, minesweeper, idle, blockpuzzle, roguelike) med grep-verifierbara constraints
2. **GenreIdeaBank.cs** — Idéfrön för alla nya genrer + uppdaterad SeedsFor-switch
3. **DirectorPass.cs** — Fallback-kontrakt för alla nya genrer
4. **GameScaffoldService.cs** — Routing för alla 9 nya genrer (rpg behåller top-down)
5. **IdeaBankTests.cs** — Uppdaterad quiz-test (nytt facit)

### Routing (ScaffoldGodotCore)
```
towerdefense → ScaffoldGodotTowerDefense
snake → ScaffoldGodotSnake
breakout → ScaffoldGodotBreakout
quiz → ScaffoldGodotQuiz
memory → ScaffoldGodotMemory
minesweeper → ScaffoldGodotMinesweeper
idle/clicker → ScaffoldGodotIdle
blockpuzzle → ScaffoldGodotBlockPuzzle
roguelike → ScaffoldGodotRoguelike
rpg → ScaffoldGodotTopDown (oförändrat)
```

### Tester
- **824 Core + 51 Node = 875 totalt, alla gröna**
- GodotKitTests: AllaKit_HarSpelskalet_OptionsOchQuit passerar (20 Shell.options_panel-förekomster)
- IdeaBankTests: SeedsFor_RattBankPerGenre uppdaterad för quiz

### Kända begränsningar
- RPG-kitet (Hero's Quest) finns men routas INTE automatiskt — rpg-genren går till top-down-kitet (The Glade). Hero's Quest kan aktiveras genom att lägga till routing eller via explicit prompt.
- Block Puzzle, Quiz, Memory saknar ghost piece / next preview (förenklade implementationer).
- Roguelike har rumskorridorer men ingen kartvy.

### Kvar på ROADMAP
1. **Kampanjläget** — ägaren sa "nästa kör" men specificerade inte riktning (story/career/worldmap/tournament). Behöver klargörande.
2. **CC0-assetpaketet** — tveksamt värde (ägaren: "säg till om du vill ha det ändå").
3. **HTML5-kitens engelska** — 16 HTML5-kit kvar på ASCII-svenska sedan v1.99. Mekaniskt textbyte.

### Tekniska noteringar
- C# raw string literals (`"""..."""`) — trippelcitat INNE i GDScript-strängar bryter parsern. Lösning: använd enkla citat i GDScript (`'text'` ej `"text"`).
- Regex-baserad modifikation av GDScript i C#-strängar är FARLIGT — korrumperar triple-quote-strukturen. Skriv alltid kod FÖRSTA gången med rätt innehåll.
- `genre is "idle" or "clicker"` (C# pattern matching) fungerar; `genre == "idle" or genre == "clicker"` gör det INTE.
- `[ADDRESS]` i terminal-output är Hermes display-redaktion — filens faktiska innehåll är korrekt.

### Git-state
6 filer ändrade, inga nya filer (alla ändringar i befintliga filer):
- DirectorPass.cs (+14)
- GameScaffoldService.GodotKits.cs (+2934)
- GameScaffoldService.cs (+23)
- GenreContracts.cs (+101)
- GenreIdeaBank.cs (+84)
- IdeaBankTests.cs (+2/-1)
