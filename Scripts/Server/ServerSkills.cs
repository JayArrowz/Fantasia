using System;
using System.Collections.Generic;
using System.Linq;

namespace Fantasia.Server;

/// Gathering (trees, rocks, fishing spots, mulberries, webs), crafting at stations, and farming.
/// Everything is validated and rolled here; clients only ask to start or stop.
public sealed partial class ServerWorld
{
    // Per map: object id -> tick it respawns (int.MaxValue = a fishing spot the shoal has left).
    readonly Dictionary<string, Dictionary<int, int>> depleted = new();
    readonly Dictionary<string, Dictionary<int, int>> frenzied = new();
    int nextFrenzyTick = 150;

    void InitResources()
    {
        foreach (var id in MapGenerator.MapIds)
        {
            var map = MapGenerator.Get(id);
            depleted[id] = new();
            frenzied[id] = new();
            // Each group of fishing spots has a shoal at about half its spots.
            foreach (var g in map.Objects.Where(o => o.Kind == ObjKind.FishingSpot).GroupBy(o => o.Group))
            {
                var list = g.OrderBy(o => o.Id).ToList();
                int active = Math.Max(1, (list.Count + 1) / 2);
                for (int i = active; i < list.Count; i++) depleted[id][list[i].Id] = int.MaxValue;
            }
        }
    }

    bool IsDepleted(MapData map, WorldObject o) => depleted[map.Id].TryGetValue(o.Id, out var t) && t > tick;
    bool IsFrenzied(MapData map, WorldObject o) => frenzied[map.Id].TryGetValue(o.Id, out var t) && t > tick;

    void TickResources()
    {
        foreach (var d in depleted.Values)
            foreach (var k in d.Where(kv => kv.Value != int.MaxValue && kv.Value <= tick).Select(kv => kv.Key).ToList()) d.Remove(k);
        foreach (var d in frenzied.Values)
            foreach (var k in d.Where(kv => kv.Value <= tick).Select(kv => kv.Key).ToList()) d.Remove(k);
        if (tick >= nextFrenzyTick)
        {
            nextFrenzyTick = tick + rng.Next(140, 280);
            StartFrenzy();
        }
        if (tick % 10 == 0)
            foreach (var p in byPeer.Values) CheckPatches(p, true);
    }

    /// A random shoal (preferably one near a player) becomes frenzied: faster bites, more xp, golden carp.
    void StartFrenzy(WorldObject forced = null, MapData forcedMap = null)
    {
        var map = forcedMap ?? MapGenerator.Get(GameConst.OverworldId);
        var spots = map.Objects.Where(o => o.Kind == ObjKind.FishingSpot && !IsDepleted(map, o) && !IsFrenzied(map, o)).ToList();
        if (spots.Count == 0) return;
        var near = spots.Where(o => byPeer.Values.Any(p => p.Map == map && o.DistanceTo(p.Pos) < 25)).ToList();
        var spot = forced ?? (near.Count > 0 && rng.NextDouble() < 0.75 ? near[rng.Next(near.Count)] : spots[rng.Next(spots.Count)]);
        frenzied[map.Id][spot.Id] = tick + 100;
        foreach (var p in byPeer.Values)
        {
            if (p.Map != map || spot.DistanceTo(p.Pos) > 35) continue;
            bool angler = HasBoost(p, Skill.Fishing);
            GameMsg(p, angler ? "Your ring of the angler tingles: a frenzied shoal is churning nearby!" : "The water nearby churns and glitters: a frenzied shoal!");
        }
    }

    /// The shoal at `o` swims to another spot of its group.
    void MoveShoal(MapData map, WorldObject o)
    {
        if (IsFrenzied(map, o)) return;
        var d = depleted[map.Id];
        var free = map.Objects.Where(x => x.Kind == ObjKind.FishingSpot && x.Group == o.Group && x.Id != o.Id && d.TryGetValue(x.Id, out var t) && t == int.MaxValue).ToList();
        if (free.Count == 0) return;
        var n = free[rng.Next(free.Count)];
        d.Remove(n.Id);
        d[o.Id] = int.MaxValue;
    }

    // ================= tools & boosts =================

    /// Best usable tool of a kind in the pack or wielded, or null.
    ItemDef BestTool(PlayerEntity p, ToolKind kind)
    {
        ItemDef best = null;
        foreach (var st in p.Inv.Concat(new[] { p.Equip[(int)EquipSlot.Weapon] }))
        {
            var d = st?.Def;
            if (d == null || d.Tool != kind) continue;
            if (d.ReqLevel > 1 && p.Level(d.ReqSkill) < d.ReqLevel) continue;
            if (best == null || d.ToolPower > best.ToolPower) best = d;
        }
        return best;
    }

    bool HasBoost(PlayerEntity p, Skill s) => p.Equip.Any(e => e?.Def is { BoostPct: > 0 } d && d.BoostSkill == s);
    int BoostPct(PlayerEntity p, Skill s) => p.Equip.Where(e => e?.Def != null && e.Def.BoostSkill == s).Sum(e => e.Def.BoostPct);

    static string ToolName(ToolKind k) => k switch
    {
        ToolKind.Hatchet => "a hatchet", ToolKind.Pickaxe => "a pickaxe", ToolKind.Net => "a small fishing net", ToolKind.Rod => "a fishing rod",
        ToolKind.Harpoon => "a harpoon", ToolKind.Hammer => "a hammer", ToolKind.Needle => "a needle", ToolKind.WateringCan => "a watering can", _ => "a tool",
    };

    static string Gerund(ResourceDef r) => r.Verb switch
    {
        "Chop" => "Chopping", "Mine" => "Mining", "Net" => "Net fishing", "Bait" => "Bait fishing", "Harpoon" => "Harpooning",
        "Gather" => "Gathering cocoons", "Collect" => "Collecting silk", _ => r.Verb,
    };

    static string AnimFor(Skill s) => s switch
    {
        Skill.Woodcutting => "chop", Skill.Mining => "mine", Skill.Fishing => "fish", Skill.Smithing => "smith",
        Skill.Cooking => "cook", Skill.Enchanting => "enchant", Skill.Farming => "farm", _ => "craft",
    };

    void SkillAnim(PlayerEntity p, string anim, ItemDef tool, WorldObject at = null) =>
        Emit(p.Map, new Fx { T = "anim", A = p.Id, S = tool?.Model != null ? $"{anim}|{tool.Id}" : anim, C = at != null ? at.Id + 1 : 0 });

    void GiveOrDrop(PlayerEntity p, string id, int n = 1)
    {
        int left = p.Add(id, n);
        if (left > 0) { DropItem(p.Map, p.Pos, id, left, p.Id); GameMsg(p, "Your pack is full, so it falls to the ground."); }
    }

    // ================= gathering =================

    /// Validates and starts gathering; returns an error message or null.
    string CanGather(PlayerEntity p, WorldObject o, ResourceDef r, out ItemDef tool)
    {
        tool = null;
        int lvl = p.Level(r.Skill);
        if (lvl < r.Level) return $"You need a {r.Skill} level of {r.Level} to do that.";
        if (r.Tool != ToolKind.None)
        {
            tool = BestTool(p, r.Tool);
            if (tool == null) return $"You need {ToolName(r.Tool)} that you can use to do that.";
        }
        if (r.Bait != null && p.CountOf(r.Bait) <= 0) return "You don't have any bait left.";
        var y = r.Yields[^1].item;
        if (!p.CanAdd(y, 1)) return "Your pack is too full to hold any more.";
        if (IsDepleted(p.Map, o)) return r.Fishing ? "The shoal has moved on." : "There's nothing left to gather here right now.";
        return null;
    }

    void StartGather(PlayerEntity p, WorldObject o)
    {
        var r = ResourceDb.Get(o.Res);
        if (r == null) return;
        var err = CanGather(p, o, r, out var tool);
        if (err != null) { GameMsg(p, err); return; }
        p.Action = Interaction.Gather;
        p.ActionTargetId = o.Id;
        p.SkillTimer = 1;
        p.Activity = $"{Gerund(r)}: {r.Name}";
        p.PrivateDirty = true;
        GameMsg(p, r.Verb switch
        {
            "Chop" => $"You swing your {tool.Name.ToLowerInvariant()} at the {r.Name.ToLowerInvariant()}.",
            "Mine" => $"You swing your pickaxe at the {r.Name.ToLowerInvariant()}.",
            "Gather" => "You search the branches for cocoons.",
            "Collect" => "You carefully start unpicking the giant web.",
            _ => IsFrenzied(p.Map, o) ? "You cast into the frenzied shoal!" : "You attempt to catch a fish.",
        });
    }

    void GatherTick(PlayerEntity p)
    {
        var o = p.Map.GetObject(p.ActionTargetId);
        var r = ResourceDb.Get(o?.Res);
        if (o == null || r == null || o.DistanceTo(p.Pos) > 1) { StopSkilling(p); return; }
        if (--p.SkillTimer > 0) return;
        p.SkillTimer = r.Fishing ? 4 : 3;

        var err = CanGather(p, o, r, out var tool);
        if (err != null) { GameMsg(p, err); StopSkilling(p); return; }
        SkillAnim(p, AnimFor(r.Skill), tool, o);

        int lvl = p.Level(r.Skill);
        bool frenzy = r.Fishing && IsFrenzied(p.Map, o);
        var eligible = r.Yields.Where(y => lvl >= y.level).ToList();
        int minReq = eligible[^1].level;
        float chance = r.BaseChance + 0.015f * (lvl - minReq) + 0.05f * ((tool?.ToolPower ?? 1) - 1);
        chance = Math.Clamp(chance, 0.08f, 0.92f) * (1f + BoostPct(p, r.Skill) / 100f) * (frenzy ? 2f : 1f);
        if (rng.NextDouble() > Math.Min(chance, 0.97f)) return;

        // Pick a yield: the best eligible catch is likelier the further above its level you are.
        var got = eligible[^1];
        foreach (var y in eligible)
            if (y.Equals(eligible[^1]) || rng.NextDouble() < 0.4 + 0.01 * (lvl - y.level)) { got = y; break; }

        if (r.Bait != null) p.Remove(r.Bait, 1);
        p.Add(got.item, 1);
        GrantXp(p, r.Skill, (int)Math.Round(got.xp * (frenzy ? 1.5f : 1f)));
        var name = ItemDb.Get(got.item)?.Name.ToLowerInvariant();
        GameMsg(p, r.Skill switch
        {
            Skill.Fishing => $"You catch some {name}!",
            Skill.Mining => $"You mine some {name}.",
            Skill.Woodcutting => $"You get some {name}.",
            _ => $"You gather some {name}.",
        });
        RareFinds(p, r, frenzy);

        if (r.Fishing)
        {
            if (rng.NextDouble() < 1 / 30.0 && !frenzy) { MoveShoal(p.Map, o); GameMsg(p, "The shoal swims away."); StopSkilling(p); }
            return;
        }
        if (r.DepleteChance > 0 && rng.NextDouble() < r.DepleteChance)
        {
            depleted[p.Map.Id][o.Id] = tick + r.RespawnTicks;
            GameMsg(p, r.Skill switch
            {
                Skill.Woodcutting => "Timber! The tree comes down.",
                Skill.Mining => "The rock is empty for now.",
                _ => "There's nothing left for now.",
            });
            foreach (var other in byPeer.Values)
                if (other.Action == Interaction.Gather && other.ActionTargetId == o.Id && other.Map == p.Map) StopSkilling(other);
        }
    }

    void RareFinds(PlayerEntity p, ResourceDef r, bool frenzy)
    {
        switch (r.Skill)
        {
            case Skill.Woodcutting:
                if (rng.NextDouble() < (HasBoost(p, Skill.Woodcutting) ? 1 / 60.0 : 1 / 110.0))
                {
                    DropItem(p.Map, p.Pos, "bird_nest", 1, p.Id);
                    GameMsg(p, "A bird's nest tumbles out of the branches!");
                }
                break;
            case Skill.Mining:
                if (rng.NextDouble() < (HasBoost(p, Skill.Mining) ? 3 / 130.0 : 1 / 130.0))
                {
                    double g = rng.NextDouble();
                    string gem = g < 0.5 ? "sapphire" : g < 0.8 ? "emerald" : g < 0.95 ? "ruby" : "diamond";
                    GiveOrDrop(p, gem);
                    GameMsg(p, $"You find a {ItemDb.Get(gem).Name.ToLowerInvariant()} glinting in the rock!");
                }
                break;
            case Skill.Fishing:
                if (frenzy && rng.NextDouble() < 1 / 25.0)
                {
                    GiveOrDrop(p, "golden_carp");
                    GameMsg(p, "Incredible! You haul in a GOLDEN CARP!");
                    foreach (var o in byPeer.Values) if (o != p) GameMsg(o, $"{p.Name} has caught a golden carp!");
                }
                else if (rng.NextDouble() < 1 / 300.0)
                {
                    GiveOrDrop(p, "message_bottle");
                    GameMsg(p, "Your line snags on... a bottle with a message inside!");
                }
                break;
            case Skill.Silkweaving:
                if (r.Id == "mulberry" && rng.NextDouble() < 1 / 90.0)
                {
                    GiveOrDrop(p, "moonpetal_seed");
                    GameMsg(p, "A pale moth flutters off, leaving a moonpetal seed behind.");
                }
                break;
        }
    }

    void StopSkilling(PlayerEntity p)
    {
        if (p.Action is Interaction.Gather or Interaction.Craft or Interaction.Harvest)
        {
            ClearAction(p);
            Emit(p.Map, new Fx { T = "anim", A = p.Id, S = "stop" });
        }
        if (p.Activity != null) { p.Activity = null; p.PrivateDirty = true; }
    }

    // ================= crafting =================

    void OpenStation(PlayerEntity p, WorldObject o, string station)
    {
        p.CraftStation = station;
        p.CraftObj = o?.Id ?? -1;
        string flavour = o == null ? null : o.Kind == ObjKind.Altar ? "altar" : o.Kind == ObjKind.Fireplace ? "hearth" : null;
        SendMsg?.Invoke(p.PeerId, Json.Write(new ServerMsg { T = "craft", S = station, A = p.CraftObj, S2 = flavour }));
    }

    bool StationInReach(PlayerEntity p)
    {
        if (p.CraftStation == null) return false;
        if (p.CraftObj < 0) return p.CraftStation == "sew" && BestTool(p, ToolKind.Needle) != null;
        var o = p.Map.GetObject(p.CraftObj);
        return o != null && o.Station == p.CraftStation && o.DistanceTo(p.Pos) <= 1;
    }

    bool Knows(PlayerEntity p, Recipe r) => r.Unlock == null || p.Acc.Unlocks.Contains(r.Unlock);

    string CanCraft(PlayerEntity p, Recipe r)
    {
        if (!Knows(p, r)) return $"You haven't learnt how to make that. ({RecipeDb.GetUnlock(r.Unlock)?.Hint})";
        if (p.Level(r.Skill) < r.Level) return $"You need a {r.Skill} level of {r.Level} to make that.";
        if (r.Station == "smith" && BestTool(p, ToolKind.Hammer) == null) return "You need a hammer to work the metal.";
        if (r.Station == "sew" && BestTool(p, ToolKind.Needle) == null) return "You need a needle.";
        foreach (var (id, n) in r.In)
            if (p.CountOf(id) < n) return $"You need {n} x {ItemDb.Get(id)?.Name ?? id}.";
        var outDef = ItemDb.Get(r.Out);
        int freed = r.In.Where(x => !ItemDb.Get(x.item).Stackable).Sum(x => x.count);
        if (outDef.Stackable ? !p.CanAdd(r.Out, 1) && freed == 0 : p.FreeSlots() + freed < r.OutCount)
            return "You don't have enough room in your pack.";
        return null;
    }

    void StartCraft(PlayerEntity p, string recipeId, int count)
    {
        var r = RecipeDb.Get(recipeId);
        if (r == null || r.Station != p.CraftStation) return;
        if (!StationInReach(p)) { GameMsg(p, "You need to be at the right station to do that."); return; }
        var err = CanCraft(p, r);
        if (err != null) { GameMsg(p, err); return; }
        p.Action = Interaction.Craft;
        p.ActionTargetId = p.CraftObj;
        p.CraftRecipe = r.Id;
        p.CraftLeft = Math.Clamp(count, 1, 5000);
        p.CraftMade = 0;
        p.SkillTimer = 1;
        p.Path.Clear();
        p.Activity = $"{RecipeDb.StationName(r.Station)}: {r.Name}";
        p.PrivateDirty = true;
    }

    void CraftTick(PlayerEntity p)
    {
        var r = RecipeDb.Get(p.CraftRecipe);
        if (r == null || !StationInReach(p)) { StopSkilling(p); return; }
        if (--p.SkillTimer > 0) return;
        p.SkillTimer = r.Station is "sew" or "enchant" ? 2 : 3;
        var err = CanCraft(p, r);
        if (err != null)
        {
            // Running out of materials part-way through a batch is expected, not an error.
            GameMsg(p, p.CraftMade > 0 ? $"You make {p.CraftMade}, and run out of materials." : err);
            StopSkilling(p);
            return;
        }
        var station = p.CraftObj >= 0 ? p.Map.GetObject(p.CraftObj) : null;
        SkillAnim(p, AnimFor(r.Skill), r.Station == "smith" ? BestTool(p, ToolKind.Hammer) : null, station);
        int lvl = p.Level(r.Skill);

        if (r.Sigil)
        {
            // Carve every essence at once, like pouring a mould.
            int n = Math.Min(p.CraftLeft, p.CountOf("essence_stone"));
            bool altar = station?.Kind == ObjKind.Altar;
            int per = RecipeDb.SigilYield(r, lvl, altar);
            p.Remove("essence_stone", n);
            p.Add(r.Out, n * per);
            GrantXp(p, r.Skill, (int)Math.Round(r.Xp * n * RecipeDb.SigilYield(r, lvl, false)));
            GameMsg(p, $"You carve {n * per} {ItemDb.Get(r.Out).Name.ToLowerInvariant()}s{(altar ? ", the dark altar doubling your work" : "")}.");
            StopSkilling(p);
            return;
        }

        foreach (var (id, n) in r.In) p.Remove(id, n);
        p.CraftLeft--;
        p.CraftMade++;
        var outName = ItemDb.Get(r.Out).Name.ToLowerInvariant();
        if (r.Station == "smelt" && rng.NextDouble() < RecipeDb.SmeltFail(r, lvl))
        {
            GameMsg(p, "The ore is too impure and crumbles in the heat.");
        }
        else if (r.BurnStop > 0 && rng.NextDouble() < RecipeDb.BurnChance(r, lvl, station?.Kind == ObjKind.Fireplace, HasBoost(p, Skill.Cooking)))
        {
            p.Add("burnt_food", 1);
            GameMsg(p, $"You accidentally burn the {outName}.");
        }
        else
        {
            p.Add(r.Out, r.OutCount);
            GrantXp(p, r.Skill, (int)Math.Round(r.Xp));
            GameMsg(p, r.Station switch
            {
                "smelt" => $"You smelt a {outName}.",
                "smith" => $"You hammer out {(r.OutCount > 1 ? r.OutCount + " " : "a ")}{outName}.",
                "cook" => $"You cook the {outName}.",
                "spin" => $"You spin some {outName}.",
                "weave" => $"You weave a bolt of {outName}.",
                "sew" => $"You sew a {outName}.",
                "workbench" => $"You carve {(r.OutCount > 1 ? r.OutCount + " " : "a ")}{outName}.",
                _ => $"You make a {outName}.",
            });
        }
        if (p.CraftLeft <= 0) StopSkilling(p);
    }

    // ================= farming =================

    PatchState PatchFor(PlayerEntity p, WorldObject o, bool create)
    {
        var pt = p.Acc.Patches.FirstOrDefault(x => x.Obj == o.Id);
        if (pt == null && create) { pt = new PatchState { Obj = o.Id }; p.Acc.Patches.Add(pt); }
        return pt;
    }

    /// Picks the sensible verb for a plain left-click on an allotment.
    string DefaultPatchVerb(PlayerEntity p, PatchState pt)
    {
        var (stage, phase, _) = FarmDb.Eval(pt, FarmDb.Now);
        return phase switch
        {
            PatchPhase.Ready => "Harvest",
            PatchPhase.Diseased => "Cure",
            PatchPhase.Dead => "Clear",
            PatchPhase.Growing when stage > pt.WaterStage && pt.Watered < 3 && BestTool(p, ToolKind.WateringCan) != null => "Water",
            PatchPhase.Growing => "Inspect",
            _ => "Plant",
        };
    }

    void UsePatch(PlayerEntity p, WorldObject o, string verb, string arg)
    {
        var pt = PatchFor(p, o, true);
        long now = FarmDb.Now;
        if (verb is null or "Tend") verb = DefaultPatchVerb(p, pt);
        var (stage, phase, _) = FarmDb.Eval(pt, now);
        var crop = FarmDb.Get(pt.Crop);
        int lvl = p.Level(Skill.Farming);
        switch (verb)
        {
            case "Plant":
            {
                if (phase != PatchPhase.Empty) { GameMsg(p, "Something is already growing here."); return; }
                CropDef c = arg != null ? FarmDb.BySeed(arg) : null;
                c ??= FarmDb.Crops.Where(x => p.CountOf(x.Seed) > 0 && x.Level <= lvl).OrderByDescending(x => x.Level).FirstOrDefault();
                if (c == null || p.CountOf(c.Seed) <= 0) { GameMsg(p, "You need some seeds to plant. Farmer Hob sells them."); return; }
                if (lvl < c.Level) { GameMsg(p, $"You need a Farming level of {c.Level} to plant {c.Name.ToLowerInvariant()} seeds."); return; }
                p.Remove(c.Seed, 1);
                pt.Crop = c.Id; pt.Planted = now; pt.Watered = 0; pt.WaterStage = -1; pt.DiseaseAt = -1;
                pt.Compost = p.Remove("compost", 1);
                bool immune = HasBoost(p, Skill.Farming);
                if (!immune)
                    for (int s = 1; s < FarmDb.Stages; s++)
                        if (rng.NextDouble() < (pt.Compost ? 0.04 : 0.12)) { pt.DiseaseAt = s; break; }
                GrantXp(p, Skill.Farming, (int)Math.Round(c.PlantXp));
                SkillAnim(p, "farm", null, o);
                GameMsg(p, $"You plant {c.Name.ToLowerInvariant()} seeds{(pt.Compost ? " in rich compost" : "")}. It'll be ready in about {Math.Max(1, c.GrowSeconds / 60)} minutes.");
                break;
            }
            case "Water":
            {
                if (phase != PatchPhase.Growing) { GameMsg(p, "That doesn't need watering."); return; }
                if (BestTool(p, ToolKind.WateringCan) == null) { GameMsg(p, "You need a watering can."); return; }
                if (stage <= pt.WaterStage || pt.Watered >= 3) { GameMsg(p, "The soil is still damp. Water it again at the next stage."); return; }
                pt.WaterStage = stage; pt.Watered++;
                GrantXp(p, Skill.Farming, 4 + crop.Level / 5);
                SkillAnim(p, "farm", null, o);
                GameMsg(p, $"You water the {crop.Name.ToLowerInvariant()}. It perks up. ({pt.Watered}/3)");
                break;
            }
            case "Cure":
            {
                if (phase != PatchPhase.Diseased) { GameMsg(p, "That crop is healthy."); return; }
                if (!p.Remove("plant_cure", 1)) { GameMsg(p, "You need some plant cure. Farmer Hob sells it."); return; }
                float stageLen = crop.GrowSeconds / (float)FarmDb.Stages;
                long strike = pt.Planted + (long)(pt.DiseaseAt * stageLen);
                pt.Planted += Math.Max(0, now - strike);
                pt.DiseaseAt = -1;
                GrantXp(p, Skill.Farming, 10 + crop.Level / 3);
                SkillAnim(p, "farm", null, o);
                GameMsg(p, "You treat the crop. It recovers and starts growing again.");
                break;
            }
            case "Clear":
                if (phase != PatchPhase.Dead) { GameMsg(p, "There's nothing to clear."); return; }
                ResetPatch(pt);
                GameMsg(p, "You clear away the withered plants.");
                break;
            case "Harvest":
                if (phase != PatchPhase.Ready) { GameMsg(p, "It isn't ready to harvest yet."); return; }
                if (!p.CanAdd(crop.Produce, 1)) { GameMsg(p, "Your pack is too full to harvest."); return; }
                p.Action = Interaction.Harvest;
                p.ActionTargetId = o.Id;
                p.SkillTimer = 1;
                p.Activity = $"Harvesting {crop.ProduceName.ToLowerInvariant()}";
                break;
            default:
                GameMsg(p, FarmDb.Describe(pt, now));
                return;
        }
        p.PrivateDirty = true;
    }

    static void ResetPatch(PatchState pt)
    {
        pt.Crop = null; pt.Planted = 0; pt.DiseaseAt = -1; pt.WaterStage = -1; pt.Watered = 0; pt.Compost = false; pt.Left = 0;
    }

    void HarvestTick(PlayerEntity p)
    {
        var o = p.Map.GetObject(p.ActionTargetId);
        if (o == null || o.Kind != ObjKind.FarmPatch || o.DistanceTo(p.Pos) > 1) { StopSkilling(p); return; }
        var pt = PatchFor(p, o, false);
        var crop = FarmDb.Get(pt?.Crop);
        if (crop == null || FarmDb.Eval(pt, FarmDb.Now).phase != PatchPhase.Ready) { StopSkilling(p); return; }
        if (--p.SkillTimer > 0) return;
        p.SkillTimer = 2;
        if (pt.Left <= 0)
        {
            int lvl = p.Level(Skill.Farming);
            pt.Left = 3 + lvl / 20 + (pt.Compost ? 2 : 0) + pt.Watered + rng.Next(0, 3) + (HasBoost(p, Skill.Farming) ? 2 : 0);
        }
        if (!p.CanAdd(crop.Produce, 1)) { GameMsg(p, "Your pack is full. Come back for the rest."); StopSkilling(p); return; }
        SkillAnim(p, "farm", null, o);
        p.Add(crop.Produce, 1);
        GrantXp(p, Skill.Farming, (int)Math.Round(crop.HarvestXp));
        if (--pt.Left <= 0)
        {
            GrantXp(p, Skill.Farming, (int)Math.Round(crop.PlantXp * 2));
            GameMsg(p, $"You harvest the last of the {crop.ProduceName.ToLowerInvariant()}. The patch is clear.");
            ResetPatch(pt);
            StopSkilling(p);
        }
        p.PrivateDirty = true;
    }

    /// Tells a player when one of their crops ripens, sickens or withers.
    void CheckPatches(PlayerEntity p, bool announce)
    {
        long now = FarmDb.Now;
        foreach (var pt in p.Acc.Patches)
        {
            var crop = FarmDb.Get(pt.Crop);
            var phase = FarmDb.Eval(pt, now).phase;
            if (p.PatchSeen.TryGetValue(pt.Obj, out var was) && was != phase && announce && crop != null)
            {
                string msg = phase switch
                {
                    PatchPhase.Ready => $"Your {crop.Name.ToLowerInvariant()} crop is ready to harvest!",
                    PatchPhase.Diseased => $"Your {crop.Name.ToLowerInvariant()} crop has become diseased! Treat it with plant cure.",
                    PatchPhase.Dead => $"Your {crop.Name.ToLowerInvariant()} crop has withered.",
                    _ => null,
                };
                if (msg != null) { GameMsg(p, msg); p.PrivateDirty = true; }
            }
            p.PatchSeen[pt.Obj] = phase;
        }
    }

    // ================= pack items =================

    void ReadScroll(PlayerEntity p, int slot)
    {
        var d = p.Inv[slot].Def;
        var u = RecipeDb.GetUnlock(d.Teaches);
        if (u == null) return;
        if (p.Acc.Unlocks.Contains(u.Key)) { GameMsg(p, $"You already know {u.Name}."); return; }
        p.Inv[slot] = null;
        Learn(p, u.Key);
    }

    void Learn(PlayerEntity p, string key)
    {
        var u = RecipeDb.GetUnlock(key);
        if (u == null || p.Acc.Unlocks.Contains(key)) return;
        p.Acc.Unlocks.Add(key);
        p.PrivateDirty = true;
        GameMsg(p, $"You have learnt: {u.Name}!");
        Emit(p.Map, new Fx { T = "learn", A = p.Id, S = u.Name });
    }

    void OpenContainer(PlayerEntity p, int slot)
    {
        var d = p.Inv[slot].Def;
        p.Inv[slot] = null;
        p.PrivateDirty = true;
        double r = rng.NextDouble();
        string[] lowSeeds = { "potato_seed", "onion_seed", "cabbage_seed", "wheat_seed", "strawberry_seed" };
        string[] highSeeds = { "madder_seed", "woad_seed", "moonpetal_seed", "emberbloom_seed" };
        (string id, int n) loot;
        if (d.Id == "bird_nest")
        {
            loot = r < 0.45 ? (lowSeeds[rng.Next(lowSeeds.Length)], rng.Next(2, 5))
                : r < 0.65 ? (highSeeds[rng.Next(highSeeds.Length)], 1)
                : r < 0.85 ? (new[] { "sapphire", "sapphire", "emerald", "ruby" }[rng.Next(4)], 1)
                : r < 0.96 ? (rng.Next(2) == 0 ? "silver_ring" : "gold_ring", 1)
                : ("scroll_" + new[] { "elder_bows", "moonwood_bows", "hunters_roast", "dyeing" }[rng.Next(4)], 1);
            GameMsg(p, "You carefully open the nest...");
        }
        else
        {
            loot = r < 0.35 ? ("coins", rng.Next(200, 900))
                : r < 0.6 ? (new[] { "sapphire", "emerald", "ruby" }[rng.Next(3)], 1)
                : r < 0.8 ? ("fishing_bait", rng.Next(40, 120))
                : r < 0.92 ? ("essence_stone", rng.Next(20, 60))
                : ("scroll_" + new[] { "fishermans_pie", "spidersilk", "void_sigils", "moonsilk" }[rng.Next(4)], 1);
            GameMsg(p, "You uncork the bottle. The note reads: 'For whoever finds this, with luck.'");
        }
        GiveOrDrop(p, loot.id, loot.n);
        GameMsg(p, $"Inside you find: {(loot.n > 1 ? loot.n + " x " : "")}{ItemDb.Get(loot.id)?.Name}.");
    }

    // ================= snapshot helpers =================

    (int[] dep, int[] frz) ResourceView(PlayerEntity p)
    {
        int range = GameConst.ViewDistance + 8;
        var dep = depleted[p.Map.Id].Where(kv => kv.Value > tick && p.Map.GetObject(kv.Key)?.DistanceTo(p.Pos) <= range).Select(kv => kv.Key).ToArray();
        var frz = frenzied[p.Map.Id].Where(kv => kv.Value > tick && p.Map.GetObject(kv.Key)?.DistanceTo(p.Pos) <= range).Select(kv => kv.Key).ToArray();
        return (dep.Length > 0 ? dep : null, frz.Length > 0 ? frz : null);
    }
}
