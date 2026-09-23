using System;
using System.Collections.Generic;
using System.Linq;

namespace Fantasia;

public enum QStep { Bring, Kill, Level }

public sealed class QuestStage
{
    public QStep Kind;
    public ItemCount[] Items = Array.Empty<ItemCount>();
    public string[] Npcs = Array.Empty<string>();   // Kill: any of these npc ids counts
    public int Count;
    public Skill Skill;
    public int Level;
    public string Journal;                          // objective text for the quest journal
    public string[] Remind;                         // said by the giver while the stage is unfinished
    public string[] Done;                           // said when the stage is handed in
}

public sealed class QuestDef
{
    public string Id, Name, Giver, Summary;
    public SkillLevel[] Reqs = Array.Empty<SkillLevel>();
    public string[] Offer, After;
    public ItemCount[] StartItems = Array.Empty<ItemCount>();
    public QuestStage[] Stages;
    public SkillXp[] XpReward = Array.Empty<SkillXp>();
    public ItemCount[] ItemReward = Array.Empty<ItemCount>();
    public string[] Unlocks = Array.Empty<string>();

    public string RewardText()
    {
        var parts = new List<string>();
        foreach (var (s, xp) in XpReward) parts.Add($"{xp:N0} {s} xp");
        foreach (var (id, n) in ItemReward) parts.Add(n > 1 ? $"{n:N0} x {ItemDb.Get(id)?.Name ?? id}" : ItemDb.Get(id)?.Name ?? id);
        foreach (var u in Unlocks) parts.Add("Recipe: " + (RecipeDb.GetUnlock(u)?.Name ?? u));
        return string.Join(", ", parts);
    }
}

/// Quest progress values stored per account: 0 = not started, 1..Stages = on that stage, Done = finished.
public static class QuestDb
{
    public const int Done = 1000;
    public static readonly List<QuestDef> All = new();
    public static QuestDef Get(string id) => All.FirstOrDefault(q => q.Id == id);
    public static IEnumerable<QuestDef> ByGiver(string npc) => All.Where(q => q.Giver == npc);

    /// Loaded from res://data/quests.json.
    static QuestDb() => All.AddRange(GameData.Load<List<QuestDef>>("quests.json"));
}
