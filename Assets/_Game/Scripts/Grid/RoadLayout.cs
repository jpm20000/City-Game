using System.Collections.Generic;
using UnityEngine;

// Pure road-layout rules (M22): the direction / side codes stored per road cell, and the one rule for moving
// between two adjacent road cells that traffic and the cosmetic vehicles share.
//   Codes: 0 = none, 1 = north (+y), 2 = east (+x), 3 = south (-y), 4 = west (-x).
//   Highway cells: the code is the way a vehicle may leave the cell (0 = two-way).
//   Paired Avenue cells: the code is the side the partner lane is on (0 = a single-lane avenue).
public static class RoadLayout
{
    public const byte None = 0, North = 1, East = 2, South = 3, West = 4;

    private static readonly Vector2Int[] s_Offsets =
    {
        Vector2Int.zero,
        new Vector2Int(0, 1),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(-1, 0),
    };

    public static bool IsCode(byte code) => code <= West;

    // The neighbour step a code points at (zero for None).
    public static Vector2Int Offset(byte code) => code <= West ? s_Offsets[code] : Vector2Int.zero;

    public static byte Opposite(byte code) => code == None || code > West ? None : (byte)((code + 1) % 4 + 1);

    // The code of a unit step (None when it is not one).
    public static byte FromStep(Vector2Int step)
    {
        for (byte code = North; code <= West; code++)
        {
            if (s_Offsets[code] == step) return code;
        }
        return None;
    }

    // The side to the right of a heading (facing north, east is on the right): the Avenue's second lane.
    public static byte RightOf(byte heading) => heading == None || heading > West ? None : (byte)(heading % 4 + 1);

    public static byte LeftOf(byte heading) => heading == None || heading > West ? None : Opposite(RightOf(heading));

    // True for the two north-south codes (the stretch of a one-way highway keeps its axis).
    public static bool IsVertical(byte code) => code == North || code == South;

    // May a vehicle step from `from` to the adjacent cell `to`? The cell it leaves must be two-way or point at
    // the next cell, and the cell it enters must be two-way or not point back at it (no head-on entry; entering
    // from behind or from the side is a ramp). dirFrom / dirTo are the cells' highway direction codes.
    public static bool CanStep(Vector2Int from, byte dirFrom, Vector2Int to, byte dirTo)
    {
        if (dirFrom != None && from + Offset(dirFrom) != to) return false;
        if (dirTo != None && to + Offset(dirTo) == from) return false;
        return true;
    }

    public static bool CanStep(GridData grid, Vector2Int from, Vector2Int to)
    {
        if (!grid.InBounds(from) || !grid.InBounds(to) || !grid.IsRoad(from) || !grid.IsRoad(to)) return false;
        return CanStep(from, grid.GetRoadDirection(from), to, grid.GetRoadDirection(to));
    }

    public enum LaneProblem { None, OffMap, Blocked, Taken, Higher }

    // Whether the cell can be a lane of an avenue (tier `tier`): free land, or a road of that tier or a lower one.
    public static LaneProblem CheckLane(GridData grid, Vector2Int cell, byte tier)
    {
        if (!grid.InBounds(cell)) return LaneProblem.OffMap;
        byte existing = grid.GetRoadTier(cell);
        if (existing == 0) return grid.CanPlace(cell, Vector2Int.one, 0) ? LaneProblem.None : LaneProblem.Blocked;
        return existing > tier ? LaneProblem.Higher : LaneProblem.None;
    }

    // Whether `cell` and its neighbour on `side` can become one paired avenue: both lanes must be usable and neither may
    // already be paired with some other lane. A pair that already exists (the same two cells) is fine.
    public static LaneProblem CheckPair(GridData grid, Vector2Int cell, byte side, byte tier)
    {
        Vector2Int partner = cell + Offset(side);
        LaneProblem problem = CheckLane(grid, cell, tier);
        if (problem != LaneProblem.None) return problem;
        problem = CheckLane(grid, partner, tier);
        if (problem != LaneProblem.None) return problem;
        if (grid.GetRoadPair(cell) == side) return LaneProblem.None;
        return grid.GetRoadPair(cell) != None || grid.GetRoadPair(partner) != None ? LaneProblem.Taken : LaneProblem.None;
    }

    // The dominant axis of a drag step as a code (None when it did not move).
    public static byte HeadingOf(Vector2Int from, Vector2Int to)
    {
        Vector2Int d = to - from;
        if (d == Vector2Int.zero) return None;
        if (Mathf.Abs(d.x) >= Mathf.Abs(d.y)) return d.x > 0 ? East : West;
        return d.y > 0 ? North : South;
    }

    // The run of one-way highway cells the selected one belongs to, following the flow (forward through the cell it
    // points at, backward through every cell that points at it), so a bend stays in the stretch. Empty when the cell is
    // not a one-way highway.
    public static List<Vector2Int> CollectStretch(GridData grid, Vector2Int start)
    {
        var stretch = new List<Vector2Int>();
        if (!IsOneWay(grid, start)) return stretch;
        var seen = new HashSet<Vector2Int> { start };
        stretch.Add(start);
        for (int i = 0; i < stretch.Count; i++)
        {
            Vector2Int c = stretch[i];
            Vector2Int ahead = c + Offset(grid.GetRoadDirection(c));
            if (IsOneWay(grid, ahead) && ahead + Offset(grid.GetRoadDirection(ahead)) != c && seen.Add(ahead)) stretch.Add(ahead);
            foreach (Vector2Int step in CellUtils.Neighbors4)
            {
                Vector2Int behind = c + step;
                if (IsOneWay(grid, behind) && behind + Offset(grid.GetRoadDirection(behind)) == c && seen.Add(behind)) stretch.Add(behind);
            }
        }
        return stretch;
    }

    private static bool IsOneWay(GridData grid, Vector2Int cell) =>
        grid.InBounds(cell) && grid.GetRoadTier(cell) == GridData.HighwayTier && grid.GetRoadDirection(cell) != None;

    // Turns the whole stretch around: every cell now points at the cell that used to lead into it (the old first cell
    // points back the way it came). Returns how many cells changed.
    public static int ReverseStretch(GridData grid, Vector2Int start)
    {
        List<Vector2Int> stretch = CollectStretch(grid, start);
        var next = new byte[stretch.Count];
        for (int i = 0; i < stretch.Count; i++)
        {
            Vector2Int cell = stretch[i];
            next[i] = Opposite(grid.GetRoadDirection(cell));
            foreach (Vector2Int other in stretch)
            {
                if (other + Offset(grid.GetRoadDirection(other)) != cell) continue;
                next[i] = FromStep(other - cell);
                break;
            }
        }
        for (int i = 0; i < stretch.Count; i++) grid.SetRoadDirection(stretch[i], next[i]);
        return stretch.Count;
    }

    // Makes the whole stretch two-way. Returns how many cells changed.
    public static int MakeStretchTwoWay(GridData grid, Vector2Int start)
    {
        List<Vector2Int> stretch = CollectStretch(grid, start);
        foreach (Vector2Int cell in stretch) grid.SetRoadDirection(cell, None);
        return stretch.Count;
    }

    // Makes the straight run of two-way highway cells through `start` one-way along `heading` (the run follows the
    // heading's axis). Returns how many cells changed.
    public static int MakeLineOneWay(GridData grid, Vector2Int start, byte heading)
    {
        if (!grid.InBounds(start) || grid.GetRoadTier(start) != GridData.HighwayTier || heading == None) return 0;
        Vector2Int step = Offset(heading);
        int changed = 0;
        foreach (Vector2Int direction in new[] { step, -step })
        {
            for (Vector2Int c = direction == step ? start : start + direction; grid.InBounds(c) && grid.GetRoadTier(c) == GridData.HighwayTier; c += direction)
            {
                if (grid.GetRoadDirection(c) != None) break;
                grid.SetRoadDirection(c, heading);
                changed++;
            }
        }
        return changed;
    }

    // The other lane of a paired avenue cell (false for a single lane).
    public static bool TryGetPartner(GridData grid, Vector2Int cell, out Vector2Int partner)
    {
        partner = cell;
        if (!grid.InBounds(cell)) return false;
        byte side = grid.GetRoadPair(cell);
        if (side == None) return false;
        partner = cell + Offset(side);
        return grid.InBounds(partner);
    }
}
