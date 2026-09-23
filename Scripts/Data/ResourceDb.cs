using System;
using System.Collections.Generic;
using Godot;

namespace Fantasia;

/// A gatherable world resource (tree, rock, fishing spot, mulberry, web). World objects carry the
/// id in WorldObject.Res; depletion and respawn are tracked by the server.
public sealed class ResourceDef
{
    public string Id, Name, Verb;
    public Skill Skill;
    public ToolKind Tool;
    public string Bait;                                   // consumed per catch (fishing)
    public Yield[] Yields;   // highest eligible is tried first
    public float BaseChance = 0.3f;
    public float DepleteChance;                           // per success; 0 = never depletes
    public int RespawnTicks;
    public Color Tint = Colors.White;

    [System.Text.Json.Serialization.JsonIgnore] public int Level => Yields[0].level;
    [System.Text.Json.Serialization.JsonIgnore] public bool Fishing => Skill == Skill.Fishing;
}

public static class ResourceDb
{
    public static readonly Dictionary<string, ResourceDef> All = new();
    public static ResourceDef Get(string id) => id != null && All.TryGetValue(id, out var r) ? r : null;

    /// Loaded from res://data/resources.json.
    static ResourceDb()
    {
        foreach (var r in GameData.Load<List<ResourceDef>>("resources.json")) All[r.Id] = r;
    }
}
