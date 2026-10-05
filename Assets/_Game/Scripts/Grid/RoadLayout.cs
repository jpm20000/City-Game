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
