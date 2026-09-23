using System;
using System.Collections.Generic;
using System.Linq;

namespace Fantasia.Server;

/// Quests: offered by talking to their giver, advanced by bringing items, defeating monsters or
/// reaching levels, and rewarded with xp, items and recipe unlocks.
public sealed partial class ServerWorld
{
    int QuestStage(PlayerEntity p, string q) => p.Acc.Quests.TryGetValue(q, out var v) ? v : 0;
    int QuestVar(PlayerEntity p, string q) => p.Acc.QVars.TryGetValue(q, out var v) ? v : 0;

    void Dialog(PlayerEntity p, string who, int npcId, IEnumerable<string> lines, string[] opts = null, string questId = null) =>
        SendMsg?.Invoke(p.PeerId, Json.Write(new ServerMsg { T = "dialog", S = who, A = npcId, Lines = lines.ToArray(), Opts = opts, S2 = questId }));

    /// Handles quest conversation with an NPC. Returns false when the NPC has nothing quest-related to say.
    bool TalkQuest(PlayerEntity p, NpcEntity n)
    {
        var quests = QuestDb.ByGiver(n.Def.Id).ToList();
        if (quests.Count == 0) return false;
        foreach (var q in quests)
        {
            int st = QuestStage(p, q.Id);
            if (st > 0 && st < QuestDb.Done) { AdvanceQuest(p, n, q, st); return true; }
        }
        foreach (var q in quests)
        {
            if (QuestStage(p, q.Id) != 0) continue;
            var missing = q.Reqs.Where(r => p.Level(r.skill) < r.level).ToList();
            if (missing.Count > 0)
            {
                Dialog(p, n.Def.Name, n.Id, (n.Def.Dialogue ?? Array.Empty<string>()).Take(1).Append(
                    $"I've a task for someone with more experience. Come back with {string.Join(" and ", missing.Select(m => $"{m.skill} {m.level}"))}."));
                return true;
            }
            p.OfferQuest = q.Id;
            p.OfferNpc = n.Id;
            Dialog(p, n.Def.Name, n.Id, q.Offer.Append($"Quest: {q.Name}. Reward: {q.RewardText()}."), new[] { "Accept the quest", "Not right now" }, q.Id);
            return true;
        }
        // Everything done: recall the last quest's parting words now and then.
        var last = quests.LastOrDefault(q => QuestStage(p, q.Id) >= QuestDb.Done);
        if (last?.After != null && rng.NextDouble() < 0.5) { Dialog(p, n.Def.Name, n.Id, last.After); return true; }
        return false;
    }

    void AnswerQuest(PlayerEntity p, string questId, int choice)
    {
        var q = QuestDb.Get(questId);
        if (q == null || p.OfferQuest != q.Id) return;
        p.OfferQuest = null;
        if (!entities.TryGetValue(p.OfferNpc, out var e) || e is not NpcEntity n || n.Map != p.Map || n.Pos.Chebyshev(p.Pos) > 4) return;
        if (choice != 0) { Dialog(p, n.Def.Name, n.Id, new[] { "Suit yourself. The offer stands." }); return; }
        if (QuestStage(p, q.Id) != 0 || q.Reqs.Any(r => p.Level(r.skill) < r.level)) return;
        p.Acc.Quests[q.Id] = 1;
        p.Acc.QVars[q.Id] = 0;
        foreach (var (id, count) in q.StartItems) GiveOrDrop(p, id, count);
        p.PrivateDirty = true;
        GameMsg(p, $"Quest started: {q.Name}.");
        Emit(p.Map, new Fx { T = "quest", A = p.Id, S = "Quest started: " + q.Name });
        var lines = new List<string>(q.Stages[0].Remind);
        if (q.StartItems.Length > 0) lines.Insert(0, "Here, take these: " + string.Join(", ", q.StartItems.Select(s => s.count > 1 ? $"{s.count} {ItemDb.Get(s.item)?.Name}" : ItemDb.Get(s.item)?.Name)) + ".");
        Dialog(p, n.Def.Name, n.Id, lines);
    }

    bool StageMet(PlayerEntity p, QuestDef q, QuestStage s) => s.Kind switch
    {
        QStep.Bring => s.Items.All(x => p.CountOf(x.item) >= x.count),
        QStep.Kill => QuestVar(p, q.Id) >= s.Count,
        QStep.Level => p.Level(s.Skill) >= s.Level,
        _ => false,
    };

    string Progress(PlayerEntity p, QuestDef q, QuestStage s) => s.Kind switch
    {
        QStep.Bring => "You have: " + string.Join(", ", s.Items.Select(x => $"{Math.Min(p.CountOf(x.item), x.count)}/{x.count} {ItemDb.Get(x.item)?.Name}")) + ".",
        QStep.Kill => $"Defeated so far: {Math.Min(QuestVar(p, q.Id), s.Count)}/{s.Count}.",
        _ => $"Your {s.Skill} is level {p.Level(s.Skill)} of {s.Level}.",
    };

    void AdvanceQuest(PlayerEntity p, NpcEntity n, QuestDef q, int st)
    {
        var s = q.Stages[st - 1];
        if (!StageMet(p, q, s)) { Dialog(p, n.Def.Name, n.Id, s.Remind.Append(Progress(p, q, s))); return; }
        if (s.Kind == QStep.Bring) foreach (var (id, count) in s.Items) p.Remove(id, count);
        var lines = new List<string>(s.Done);
        if (st >= q.Stages.Length)
        {
            CompleteQuest(p, q);
            lines.AddRange(q.After ?? Array.Empty<string>());
            lines.Add($"Quest complete! Reward: {q.RewardText()}.");
        }
        else
        {
            p.Acc.Quests[q.Id] = st + 1;
            p.Acc.QVars[q.Id] = 0;
            lines.AddRange(q.Stages[st].Remind);
            GameMsg(p, $"{q.Name}: {q.Stages[st].Journal}");
        }
        p.PrivateDirty = true;
        Dialog(p, n.Def.Name, n.Id, lines);
    }

    void CompleteQuest(PlayerEntity p, QuestDef q)
    {
        p.Acc.Quests[q.Id] = QuestDb.Done;
        p.Acc.QVars.Remove(q.Id);
        foreach (var (s, xp) in q.XpReward) GrantXp(p, s, xp);
        foreach (var (id, count) in q.ItemReward) GiveOrDrop(p, id, count);
        foreach (var u in q.Unlocks) Learn(p, u);
        GameMsg(p, $"Congratulations! Quest complete: {q.Name}.");
        Emit(p.Map, new Fx { T = "quest", A = p.Id, S = "Quest complete: " + q.Name });
        foreach (var o in byPeer.Values) if (o != p && o.Map == p.Map && o.Pos.Chebyshev(p.Pos) < 15) GameMsg(o, $"{p.Name} has completed {q.Name}!");
        p.PrivateDirty = true;
    }

    /// Kill-count progress for active quests.
    void QuestKill(PlayerEntity p, NpcDef def)
    {
        foreach (var q in QuestDb.All)
        {
            int st = QuestStage(p, q.Id);
            if (st <= 0 || st >= QuestDb.Done) continue;
            var s = q.Stages[st - 1];
            if (s.Kind != QStep.Kill || !s.Npcs.Contains(def.Id)) continue;
            int v = QuestVar(p, q.Id);
            if (v >= s.Count) continue;
            p.Acc.QVars[q.Id] = ++v;
            p.PrivateDirty = true;
            var giver = NpcDb.Get(q.Giver)?.Name ?? "the quest giver";
            GameMsg(p, v >= s.Count ? $"{q.Name}: done! Return to {giver}." : $"{q.Name}: {v}/{s.Count} {def.Name.ToLowerInvariant()}s defeated.");
        }
    }
}
