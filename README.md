# Fantasia

An original medieval-fantasy multiplayer RPG in the classic click-to-move style, built with Godot 4.7 (.NET / C#).
Tile-based click-to-move, melee / ranged / magic combat, 11 equipment slots, shops, a bank,
an overworld with a walled castle town, and two dungeons with bosses. Art (characters, creatures,
props, items, textures, splash) and skeletal animation clips were generated with Higgsfield.

## Running

Open the project with the Godot .NET editor (`/mnt/SSD2/Godot Spine/godot-4.3-4.7.2-stable-mono`),
let it import, press **Build**, then **Play**. Or from a shell:

```bash
dotnet build && "/mnt/SSD2/Godot Spine/godot-4.3-4.7.2-stable-mono" --path .
```

- **Play (Host World)** starts a server on this machine and logs you in. Friends join with your IP.
- **Join Server** connects to someone else's world.
- New names create an account (password is salted + PBKDF2-hashed server-side, saved in `user://saves`).

Dedicated headless server:

```bash
"/mnt/SSD2/Godot Spine/godot-4.3-4.7.2-stable-mono" --headless --path . -- --server --port=7777
```

## Controls

| Input | Action |
| --- | --- |
| Left click | Walk / default action (attack, talk, take, enter...) |
| Right click | Actions menu with every option + Inspect |
| Arrow keys, middle-drag | Rotate camera; mouse wheel zooms |
| Enter | Chat |
| M | World map |
| F1–F6 | Combat, Skills, Inventory, Equipment, Magic, Settings tabs |
| Esc | Close windows |
| O | Settings (UI scale, display, graphics, audio, controls) |
| F7 | Quest journal |

## Game content

- **Combat skills**: Prowess, Might, Fortitude, Archery, Sorcery, Vitality, plus combat level.
- **Gathering & crafting skills** (all server-side, every roll on the server):
  - **Fishing**: net, bait and harpoon spots on River Aln, Lake Mirren and deep Bloodmarch water. Shoals wander
    between spots; *frenzied shoals* glitter gold (double bite rate, 1.5x xp, golden carp). Bottles with loot.
  - **Woodcutting**: pine, ash, elm, willow, hornbeam, elder (among Darkwood's bears) and moonwood (Bloodmarch).
    Trees fall to stumps and regrow; bird's nests drop seeds, gems, rings or recipes. Workbenches carve bows,
    staves, arrow shafts and fletch arrows.
  - **Mining**: Aldmoor Quarry (copper, tin, iron, coal, silver, gold, essence), cobalt/verdite in the Goblin Caves,
    azurite in the Crypt, ember ore in the Bloodmarch. Rocks deplete and refill; random gems.
  - **Smithing**: smelt at furnaces (iron is finicky), forge 12 items per metal at anvils (hammer needed).
  - **Cooking**: fires and hearths (hearths burn less), fish, meat and multi-ingredient dishes.
  - **Farming**: personal allotments by the hen coop. Crops grow in real time (even offline); water each stage,
    compost to prevent disease, cure sick crops before they wither, harvest a bigger crop for good care.
  - **Silkweaving**: gather cocoons in Ilse's mulberry grove and giant spider silk; spin, weave, dye and sew
    mage robes up to moonsilk.
  - **Enchanting**: carve sigils from essence (more per stone as you level; the dark altar doubles them),
    make jewellery and enchant skilling rings, combat pendants and elemental staves.
- **Recipes** marked locked are learnt from recipe scrolls (uncommon/rare monster drops) or quest rewards.
- **Quests** (journal tab, F7): 9 quests from Farmer Hob, Marta, Brom, Ilse, Elara, Gerd, Captain Hale and
  King Aldric, rewarding skill xp, items and recipe unlocks.
- **Melee**: daggers, longswords, sabres, warhammers, greatswords from Bronze through Iron, Steel, Cobalt, Verdite, Azurite to Emberforged; 3 styles.
- **Archery**: shortbows/longbows + arrows (consumed, some drop under the target); aimed/rapid/distant styles.
- **Sorcery**: 13 spells (darts, lances, Cyclone, Inferno, Soul Rend, Hearth/Hold Recall teleports, Mend) cast with sigil stones; elemental staves supply sigils; autocast; icon spellbook with rich tooltips.
- **Armour**: helms, chestplates, legguards, heater shields, leather/drakehide, robes, cloaks, pendants, rings.
- **World**: Aldmoor with enterable, furnished buildings (castle throne room, treasury, inn, forge, shops,
  homes), Hob's farm, Darkwood Forest, goblin camp, stone circle, graveyard, Skarn Hold, bandit camp,
  River Aln, and the **Bloodmarch**
  (PvP: keep your 3 most valuable items on death).
- **Dungeons**: Goblin Caves (boss: Goblin Chieftain Grukk) and the Crypt of the Fallen King
  (boss: the Skeletal Warlord, mixed melee/magic).

## Architecture (server-authoritative)

- `Scripts/Server/` — the authoritative simulation at 0.6s ticks: movement/pathing, NPC AI,
  combat formulas, drops, items, shops, bank, persistence. Clients never send positions or results.
- `Scripts/Net/` — ENet transport. Clients send small *intents* (`ClientMsg`); the server validates
  ranges, line of sight, ownership, levels, prices and rate-limits every peer.
- `Scripts/World/` — deterministic map generation shared by server and clients (maps are never sent).
- `Scripts/Client/` — rendering: terrain splat shader, props (Higgsfield GLBs with procedural fallbacks),
  rigged characters with retargeted animation clips, projectiles, effects.
- `Scripts/UI/` — HUD, minimap, world map, inventory/equipment/skills/magic tabs, shop, bank, dialogs.

## Assets

- `assets/models/` — Higgsfield GLBs (rigged `char_*`/`npc_*`, props, creatures, `item_*`).
  Any missing model falls back to a procedural mesh, so you can drop in replacements by key.
- `assets/anims/` — shared Meshy animation clips, retargeted at runtime onto every rigged character.
- `assets/textures/` — tileable ground/wall textures, parchment, splash.
- `assets/audio/` — optional: `music_<title|overworld|cave|crypt>` and `sfx_<swing|hit|hurt|block|bow|cast|eat|pickup|death|levelup>`
  as .ogg/.mp3/.wav are picked up automatically.

## Dev tools

- `-- --autotest=<dir> [--only=01,10]` hosts a world, tours every area, fights with all three
  combat styles, visits both dungeons, and saves screenshots.
- `-- --jointest=host:port:name` headless client that joins, walks, chats and attempts cheats.
- `-- --posetest=<file.png>` renders rigged characters wearing gear.
- `-- --skilltest=<dir> [--only=mining,fishing,...]` plays through every gathering/crafting skill and a quest.
- `-- --datacheck` validates every recipe, drop, shop and quest reference and that stations are reachable.
- A server started with `--dev` accepts `::tele`, `::item`, `::lvl`, `::boss`, `::heal`, `::skills <lvl>`,
  `::unlock <key|all>`, `::grow`, `::frenzy`, `::quest <id> <stage>` from the host only.
