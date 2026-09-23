using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Fantasia.Client;

namespace Fantasia;

/// `--skilltest=<dir> [--only=a,b]`: hosts a world and plays through every gathering and crafting
/// skill plus a quest, through the real client -> server messages, saving screenshots and a log.
public partial class Main
{
    PrivateState S => GameWorld.I?.State;
    int Count(string id) => S?.Inv.Where(s => s?.Id == id).Sum(s => s.Count) ?? 0;
    int Lvl(Skill s) => S?.Xp != null ? Xp.LevelForXp(S.Xp[(int)s]) : 0;
    int XpOf(Skill s) => S?.Xp != null ? S.Xp[(int)s] : 0;
    bool Want(string step) => only == null || only.Any(step.Contains);

    static MapData Overworld => MapGenerator.Get(GameConst.OverworldId);

    WorldObject Nearest(Func<WorldObject, bool> pred, Tile from)
        => Overworld.Objects.Where(pred).OrderBy(o => o.DistanceTo(from)).FirstOrDefault();

    void UseObj(WorldObject o, string verb = null, string arg = null) =>
        Net.I.Send(new ClientMsg { T = C2S.Object, A = o.Id, S = verb ?? o.Action, S2 = arg });

    void Log(string msg) => GD.Print($"[SkillTest] {msg}");

    void Check(string what, bool ok) => GD.Print($"[SkillTest] {(ok ? "PASS" : "FAIL")}: {what}");

    async Task Clear()
    {
        // Empty the pack between steps so each has room.
        for (int i = 0; i < GameConst.InventorySize; i++)
            if (S?.Inv[i] != null) Net.I.Send(new ClientMsg { T = C2S.Inv, A = i, S = "Drop" });
        await Wait(1.2);
    }

    async Task SkillTest(int port)
    {
        await Wait(0.5);
        UI.Hud.EchoChat = true;
        string name = "Sk" + (Time.GetUnixTimeFromSystem() % 100000).ToString("00000");
        var err = Net.I.Host(port, false);
        if (err != Error.Ok) { GD.PrintErr("[SkillTest] host failed"); GetTree().Quit(1); return; }
        Net.I.Server.DevMode = true;
        Net.I.LoginLocal(name, "skilltest");
        await Wait(4);
        Dev("::lvl vitality 70"); Dev("::lvl fortitude 70"); Dev("::lvl sorcery 40"); Dev("::skills 10");
        await Wait(1);
        await Clear();

        if (Want("mining"))
        {
            Dev("::tele 101 63");
            Dev("::item bronze_pickaxe 1");
            await Wait(2.5);
            var rock = Nearest(o => o.Res == "rock_copper", new Tile(101, 63));
            int xp0 = XpOf(Skill.Mining);
            UseObj(rock);
            await Wait(9);
            await Shot("s01_mining");
            Check($"mining copper: ore={Count("copper_ore")} xp+{XpOf(Skill.Mining) - xp0}", Count("copper_ore") > 0);
        }

        if (Want("smelt"))
        {
            Dev("::tele 104 68");
            Dev("::item copper_ore 5"); Dev("::item tin_ore 5");
            await Wait(2.5);
            var furnace = Nearest(o => o.Station == "smelt", new Tile(104, 68));
            UseObj(furnace);
            await Wait(3);
            await Shot("s02_furnace_window");
            Net.I.Send(new ClientMsg { T = C2S.Craft, S = "smelt:bronze_bar", A = 5 });
            await Wait(11);
            Check($"smelting: bronze bars={Count("bronze_bar")}", Count("bronze_bar") >= 5);
            Dev("::item hammer 1");
            await Wait(1);
            UseObj(Nearest(o => o.Station == "smith", new Tile(104, 68)));
            await Wait(3);
            await Shot("s03_anvil_window");
            Net.I.Send(new ClientMsg { T = C2S.Craft, S = "smith:bronze_sword", A = 1 });
            await Wait(4);
            Check($"smithing: bronze swords={Count("bronze_sword")}", Count("bronze_sword") >= 1);
            GameWorld.I.Hud.CloseWindows(true);
            await Clear();
        }

        if (Want("woodcut"))
        {
            var ash = Nearest(o => o.Res == "tree_ash", new Tile(70, 72));
            Dev($"::tele {ash.X + 1} {ash.Z + 1}");
            Dev("::item steel_hatchet 1");
            await Wait(2.5);
            int xp0 = XpOf(Skill.Woodcutting);
            UseObj(ash);
            await Wait(10);
            await Shot("s04_woodcutting");
            Check($"woodcutting ash: logs={Count("ash_log")} xp+{XpOf(Skill.Woodcutting) - xp0}", Count("ash_log") > 0);
            await Wait(12);
            await Shot("s04b_stump");
            // Fletch at the lodge workbench
            Dev("::tele 69 66");
            Dev("::item silk_thread 2"); Dev("::item pine_log 2");
            await Wait(2.5);
            UseObj(Nearest(o => o.Station == "workbench", new Tile(69, 66)));
            await Wait(3);
            await Shot("s05_workbench");
            Net.I.Send(new ClientMsg { T = C2S.Craft, S = "workbench:shortbow", A = 1 });
            await Wait(4);
            Check($"workbench: shortbows={Count("shortbow")}", Count("shortbow") >= 1);
            GameWorld.I.Hud.CloseWindows(true);
            await Clear();
        }

        if (Want("fishing"))
        {
            Dev("::tele 38 133");
            Dev("::item fishing_net 1");
            await Wait(2.5);
            var gw = GameWorld.I;
            var spot = Overworld.Objects.Where(o => o.Res == "fish_net" && o.Group == "lake_net")
                .OrderBy(o => o.DistanceTo(new Tile(38, 133))).FirstOrDefault(o => !IsHidden(o));
            Dev("::frenzy");
            await Wait(1.5);
            int xp0 = XpOf(Skill.Fishing);
            Log($"spot {spot?.Id} at {spot?.X},{spot?.Z} hidden={IsHidden(spot)}");
            UseObj(spot);
            for (int i = 0; i < 7; i++) { await Wait(2); Log($"t={i * 2} me={GameWorld.I.Me?.Tile} act={S?.Activity} perch={Count("raw_perch")}"); }
            await Shot("s06_fishing");
            Check($"fishing: perch={Count("raw_perch")} xp+{XpOf(Skill.Fishing) - xp0}", Count("raw_perch") > 0);
            var fire = Nearest(o => o.Station == "cook", new Tile(42, 137));
            Log($"fire {fire.Name} at {fire.X},{fire.Z}");
            UseObj(fire);
            await Wait(4);
            Log($"me={GameWorld.I.Me?.Tile} craftOpen={GameWorld.I.Hud.CraftOpen}");
            Net.I.Send(new ClientMsg { T = C2S.Craft, S = "cook:roast_perch", A = 28 });
            await Wait(12);
            Check($"cooking: roast perch={Count("roast_perch")} burnt={Count("burnt_food")}", Count("roast_perch") + Count("burnt_food") > 0);
            GameWorld.I.Hud.CloseWindows(true);
            await Clear();
        }

        if (Want("farming"))
        {
            Dev("::tele 32 111");
            Dev("::item potato_seed 5"); Dev("::item watering_can 1"); Dev("::item compost 1");
            await Wait(2.5);
            var patch = Nearest(o => o.Kind == ObjKind.FarmPatch, new Tile(32, 111));
            UseObj(patch, "Plant", "potato_seed");
            await Wait(3);
            UseObj(patch, "Water");
            await Wait(3);
            await Shot("s07_planted");
            Check($"farming: planted (patches={S?.Patches?.Count(p => p.Crop != null)}) watered={S?.Patches?.FirstOrDefault()?.Watered}", S?.Patches?.Any(p => p.Crop == "potato") == true);
            Dev("::grow");
            await Wait(3);
            await Shot("s08_ready");
            UseObj(patch, "Harvest");
            await Wait(16);
            Check($"farming: potatoes={Count("potato")}", Count("potato") >= 3);
            await Clear();
        }

        if (Want("silk"))
        {
            Dev("::tele 106 110");
            await Wait(2.5);
            var mul = Nearest(o => o.Res == "mulberry", new Tile(106, 110));
            UseObj(mul);
            await Wait(10);
            Check($"silk: cocoons={Count("silk_cocoon")}", Count("silk_cocoon") > 0);
            await Shot("s09_mulberry");
            Dev("::item silk_cocoon 6"); Dev("::item needle 1");
            await Wait(1);
            UseObj(Nearest(o => o.Station == "spin", new Tile(106, 110)));
            await Wait(3);
            await Shot("s10_spin");
            Net.I.Send(new ClientMsg { T = C2S.Craft, S = "spin:silk_thread", A = 6 });
            await Wait(20);
            Check($"spinning: thread={Count("silk_thread")}", Count("silk_thread") >= 6);
            UseObj(Nearest(o => o.Station == "weave", new Tile(106, 110)));
            await Wait(2);
            Net.I.Send(new ClientMsg { T = C2S.Craft, S = "weave:silk_cloth", A = 2 });
            await Wait(6);
            Check($"weaving: cloth={Count("silk_cloth")}", Count("silk_cloth") >= 2);
            int needle = Array.FindIndex(S.Inv, s => s?.Id == "needle");
            Net.I.Send(new ClientMsg { T = C2S.Inv, A = needle, S = "Sew" });
            await Wait(1.5);
            Net.I.Send(new ClientMsg { T = C2S.Craft, S = "sew:silk_gloves", A = 1 });
            await Wait(3);
            Check($"sewing: gloves={Count("silk_gloves")}", Count("silk_gloves") >= 1);
            GameWorld.I.Hud.CloseWindows(true);
            await Clear();
        }

        if (Want("enchant"))
        {
            Dev("::tele 64 85");
            Dev("::item essence_stone 40");
            await Wait(2.5);
            UseObj(Nearest(o => o.Kind == ObjKind.EnchantingTable, new Tile(64, 85)));
            await Wait(3);
            await Shot("s11_enchanting");
            Net.I.Send(new ClientMsg { T = C2S.Craft, S = "enchant:gale_sigil", A = 40 });
            await Wait(3);
            Check($"enchanting: gale sigils={Count("gale_sigil")}", Count("gale_sigil") >= 28);
            GameWorld.I.Hud.CloseWindows(true);
            await Clear();
        }

        if (Want("quest"))
        {
            Dev("::tele 53 103");
            await Wait(2.5);
            var hob = GameWorld.I.Views.Values.FirstOrDefault(v => v.Def?.Id == "farmer");
            Net.I.Send(new ClientMsg { T = C2S.Npc, A = hob.Id, S = "Talk" });
            await Wait(4);
            await Shot("s12_quest_offer");
            Net.I.Send(new ClientMsg { T = C2S.Quest, S = "hobs_harvest", A = 0 });
            await Wait(2);
            Check($"quest accepted: stage={QStage("hobs_harvest")} seeds={Count("potato_seed")}", QStage("hobs_harvest") == 1);
            Dev("::item potato 5"); Dev("::item onion 3");
            await Wait(1);
            int fx0 = XpOf(Skill.Farming);
            Net.I.Send(new ClientMsg { T = C2S.Npc, A = hob.Id, S = "Talk" });
            await Wait(4);
            Check($"quest complete: stage={QStage("hobs_harvest")} farming xp+{XpOf(Skill.Farming) - fx0} unlocks={string.Join(",", S.Unlocks)}",
                QStage("hobs_harvest") >= QuestDb.Done && S.Unlocks.Contains("cabbage_soup"));
            GameWorld.I.Hud.ShowTab(6);
            await Wait(1);
            await Shot("s13_quest_journal");
            GameWorld.I.Hud.ShowTab(1);
            await Wait(1);
            await Shot("s14_skills");
            Dev("::item scroll_hunters_roast 1");
            await Wait(1.5);
            int sc = Array.FindIndex(S.Inv, s => s?.Id == "scroll_hunters_roast");
            Net.I.Send(new ClientMsg { T = C2S.Inv, A = sc, S = "Read" });
            await Wait(1.5);
            Check("recipe scroll learnt", S.Unlocks.Contains("hunters_roast"));
        }

        if (Want("gear"))
        {
            // In-game look at worn gear: idle from four sides, then mid-walk.
            foreach (var sk in Skills.All) Dev($"::lvl {sk} 99");
            Dev("::tele 36 128");
            await Wait(2);
            await Clear();
            var gearSets = (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--gear=")) is string g ? g[7..] : "azurite_helm,azurite_chestplate,azurite_legguards,azurite_heater,iron_sword;leather_hood,leather_jerkin,leather_leggings,leather_boots,wooden_shield,bronze_hatchet,leather_gloves;apprentice_robe,apprentice_skirt,apprentice_hat,ember_staff,ruby_pendant").Split(';');
            var rig = GameWorld.I.Rig;
            for (int gi = 0; gi < gearSets.Length; gi++)
            {
                var items = gearSets[gi].Split(',');
                foreach (var id in items) Dev($"::item {id} 1");
                await Wait(1.3);
                foreach (var id in items)
                {
                    int idx = Array.FindIndex(S.Inv, x => x?.Id == id);
                    if (idx >= 0) Net.I.Send(new ClientMsg { T = C2S.Inv, A = idx, S = "Equip" });
                    await Wait(0.3);
                }
                await Wait(1.5);
                rig.SetZoom(2.6f);
                rig.Pitch = Mathf.DegToRad(12);
                var me = GameWorld.I.Me;
                foreach (var (tag, yaw) in new[] { ("front", 180f), ("left", 270f), ("back", 0f), ("right", 90f) })
                {
                    rig.Yaw = Mathf.DegToRad(yaw) + (me?.Body?.GlobalRotation.Y ?? 0);
                    await Wait(0.6);
                    await Shot($"gear{gi}_{tag}");
                }
                var t = GameWorld.I.Me.Tile;
                Net.I.Send(new ClientMsg { T = C2S.Walk, A = t.X + 6, B = t.Z });
                await Wait(1.2);
                rig.Yaw = Mathf.DegToRad(60);
                await Wait(0.4);
                await Shot($"gear{gi}_walk");
                await Wait(4);
                for (int e = 0; e < 11; e++) if (S.Eq[e] != null) Net.I.Send(new ClientMsg { T = C2S.Unequip, A = e });
                await Wait(1.2);
                await Clear();
            }
        }

        if (Want("equip"))
        {
            // Every equippable item, 8 at a time: spawn into the pack, click Equip, confirm it's worn.
            foreach (var s in Skills.All) Dev($"::lvl {s} 99");
            await Wait(1);
            await Clear();
            var all = ItemDb.All.Values.Where(d => d.Equippable).ToList();
            int bad = 0;
            for (int i = 0; i < all.Count; i += 8)
            {
                var batch = all.Skip(i).Take(8).ToList();
                foreach (var d in batch) Dev($"::item {d.Id} {(d.Stackable ? 10 : 1)}");
                await Wait(1.3);
                foreach (var d in batch)
                {
                    int idx = Array.FindIndex(S.Inv, x => x?.Id == d.Id);
                    if (idx < 0) { Log($"equip: {d.Id} never arrived"); bad++; continue; }
                    Net.I.Send(new ClientMsg { T = C2S.Inv, A = idx, S = "Equip" });
                    await Wait(0.7);
                    if (S.Eq[(int)d.Slot]?.Id != d.Id) { Log($"equip FAIL: {d.Id} slot={d.Slot} worn={S.Eq[(int)d.Slot]?.Id}"); bad++; }
                }
                await Clear();
            }
            Check($"equip all {all.Count} items: {bad} failed", bad == 0);
        }

        Log("Levels: " + string.Join(", ", Skills.All.Where(s => !Skills.IsCombat(s)).Select(s => $"{s} {Lvl(s)} ({XpOf(s)}xp)")));
        await Wait(1);
        Net.I.Shutdown();
        GetTree().Quit();
    }

    int QStage(string q) => S?.Quests != null && S.Quests.TryGetValue(q, out var v) ? v : 0;

    bool IsHidden(WorldObject o) => GameWorld.I?.MapViewNode?.IsDepleted(o.Id) == true;
}
