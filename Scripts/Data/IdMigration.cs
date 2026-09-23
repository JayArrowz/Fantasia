using System.Collections.Generic;

namespace Fantasia;

/// Maps item/spell ids from older saves to their current names.
public static class IdMigration
{
    /// migrations.json: renamed item and spell ids (old -> new), applied to saved accounts.
    public sealed class MigrationFile
    {
        public Dictionary<string, string> Items = new(), Spells = new();
    }

    static readonly MigrationFile Data = GameData.Load<MigrationFile>("migrations.json");
    public static readonly Dictionary<string, string> Items = Data.Items;
    public static readonly Dictionary<string, string> Spells = Data.Spells;

    public static string Item(string id) => id != null && Items.TryGetValue(id, out var n) ? n : id;
}
