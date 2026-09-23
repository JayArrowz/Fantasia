using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Fantasia;

/// `--exportdata=<dir>`: writes every content table as it is loaded right now to <dir>/*.json.
/// Used to check that loading a data file and writing it back gives the same file.
public partial class Main
{
    void ExportData(string dir)
    {
        Directory.CreateDirectory(dir);
        void Write(string file, object v) => File.WriteAllText(Path.Combine(dir, file), GameData.Write(v) + "\n");
        Write("items.json", ItemDb.All.Values.ToList());
        Write("npcs.json", NpcDb.All.Values.ToList());
        Write("shops.json", ShopDb.All.Values.ToList());
        Write("spells.json", SpellDb.List);
        Write("recipes.json", RecipeDb.File);
        Write("quests.json", QuestDb.All);
        Write("crops.json", FarmDb.Crops);
        Write("resources.json", ResourceDb.All.Values.ToList());
        Write("migrations.json", new IdMigration.MigrationFile { Items = IdMigration.Items, Spells = IdMigration.Spells });
        GD.Print($"[DataExport] wrote {Directory.GetFiles(dir, "*.json").Length} files to {dir}");
        GetTree().Quit();
    }
}

public partial class Main
{
    /// `--worldhash`: fingerprints every map (ground, heights, objects, spawns, labels) so a refactor
    /// of the generator can be checked to produce exactly the same world.
    void WorldHash()
    {
        foreach (var id in MapGenerator.MapIds)
        {
            var m = MapGenerator.Get(id);
            var sb = new System.Text.StringBuilder();
            sb.Append(m.Name).Append(m.Spawn).Append(m.Ambient).Append(m.Fog).Append(m.FogDensity).Append(m.Music);
            for (int x = 0; x < m.W; x++) for (int z = 0; z < m.H; z++) sb.Append((int)m.Ground[x, z]);
            foreach (var h in m.Heights) sb.Append(h.ToString("0.###"));
            string objs = string.Join("\n", m.Objects.Select(o => $"{o.Id} {o.Kind} {o.X},{o.Z} {o.W}x{o.D} r{o.Rot:0.###} s{o.Scale:0.###} {o.Blocks}{o.BlocksLos} {o.Name}|{o.Action}|{o.TargetMap}{o.TargetX},{o.TargetZ}|{o.Text}|{o.Tint}|{o.Res}|{o.Group}|{o.Station}"));
            string spawns = string.Join("\n", m.Spawns.Select(s => $"{s.NpcId} {s.X},{s.Z}"));
            string labels = string.Join("\n", m.Labels.Select(l => $"{l.Text} {l.X},{l.Z}"));
            string H(string s) => System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)))[..16];
            GD.Print($"[WorldHash] {id}: terrain={H(sb.ToString())} objects={H(objs)} ({m.Objects.Count}) spawns={H(spawns)} ({m.Spawns.Count}) labels={H(labels)} ({m.Labels.Count})");
        }
        GetTree().Quit();
    }
}
