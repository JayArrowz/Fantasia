using System;
using System.Collections.Generic;

namespace Fantasia;

/// 8-directional A* on the tile grid. Diagonals may not cut corners.
public static class Pathfinder
{
    const int MaxNodes = 8000;

    static readonly (int dx, int dz)[] Dirs =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    public static bool CanStep(MapData map, Tile from, int dx, int dz)
    {
        int nx = from.X + dx, nz = from.Z + dz;
        if (!map.Walkable(nx, nz)) return false;
        if (dx != 0 && dz != 0)
            return map.Walkable(from.X + dx, from.Z) && map.Walkable(from.X, from.Z + dz);
        return true;
    }

    /// Path to an exact tile, or the closest reachable tile if the goal is unreachable.
    public static List<Tile> Find(MapData map, Tile start, Tile goal)
        => Search(map, start, t => t == goal, goal);

    /// Path until isGoal(tile) holds. `aim` guides the heuristic.
    public static List<Tile> Search(MapData map, Tile start, Func<Tile, bool> isGoal, Tile aim)
    {
        var result = new List<Tile>();
        if (isGoal(start)) return result;

        var open = new PriorityQueue<Tile, int>();
        var came = new Dictionary<Tile, Tile>();
        var cost = new Dictionary<Tile, int> { [start] = 0 };
        open.Enqueue(start, H(start, aim));
        Tile best = start;
        int bestH = H(start, aim);
        int expanded = 0;
        bool found = false;
        Tile end = start;

        while (open.Count > 0 && expanded < MaxNodes)
        {
            var cur = open.Dequeue();
            expanded++;
            if (isGoal(cur)) { found = true; end = cur; break; }
            int h = H(cur, aim);
            if (h < bestH || (h == bestH && cost[cur] < cost[best])) { bestH = h; best = cur; }
            int c = cost[cur];
            foreach (var (dx, dz) in Dirs)
            {
                if (!CanStep(map, cur, dx, dz)) continue;
                var n = new Tile(cur.X + dx, cur.Z + dz);
                int nc = c + (dx != 0 && dz != 0 ? 14 : 10);
                if (cost.TryGetValue(n, out int old) && old <= nc) continue;
                cost[n] = nc;
                came[n] = cur;
                open.Enqueue(n, nc + H(n, aim));
            }
        }

        if (!found) end = best;
        var t = end;
        while (t != start)
        {
            result.Add(t);
            t = came[t];
        }
        result.Reverse();
        return result;
    }

    static int H(Tile a, Tile b)
    {
        int dx = Math.Abs(a.X - b.X), dz = Math.Abs(a.Z - b.Z);
        return 10 * Math.Max(dx, dz) + 4 * Math.Min(dx, dz);
    }
}
