using System.Collections.Generic;
using UnityEngine;

public static class CellUtils
{
    public static readonly Vector2Int[] Neighbors4 =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
    };

    // Quarter-turn rotations about +Y that face a grid neighbour, in rotation order. A prefab's front
    // is local +Z (M18 contract): rotation 0 faces grid +y, 1 faces +x, 2 faces -y, 3 faces -x.
    private static readonly Vector2Int[] s_FacingSteps =
    {
        new Vector2Int(0, 1),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(-1, 0),
    };

    // Which quarter turn (0..3, see above) makes a building on `cell` face a neighbouring road. The scan
    // starts at a rotation picked from the cell hash, so corner lots with two roads vary but stay
    // deterministic. With no road beside it the hash picks the rotation (its bits 20-21, as before M18).
    public static int FacingRoad(GridData grid, Vector2Int cell, uint hash)
    {
        int start = (int)(hash >> 22 & 3);
        for (int i = 0; i < 4; i++)
        {
            int rotation = (start + i) & 3;
            // GridData.IsRoad indexes the flat array without a bounds check, so an off-map neighbour must be
            // skipped here (it would read the wrapped cell on the next row, or throw at y = 0).
            Vector2Int next = cell + s_FacingSteps[rotation];
            if (grid.InBounds(next) && grid.IsRoad(next)) return rotation;
        }
        return (int)(hash >> 20 & 3);
    }

    public static bool IsInBounds(Vector2Int cell, Vector2Int size)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < size.x && cell.y < size.y;
    }

    public static int Index(Vector2Int cell, int width)
    {
        return cell.y * width + cell.x;
    }

    public static Vector3 CellToWorld(Vector2Int cell, Vector3 origin)
    {
        return origin + new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f);
    }

    public static Vector2Int WorldToCell(Vector3 world, Vector3 origin)
    {
        Vector3 local = world - origin;
        return new Vector2Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.z));
    }

    // Footprint dimensions on the grid: odd quarter-turns swap width and depth.
    public static Vector2Int EffectiveSize(Vector2Int size, int rotation)
    {
        return (rotation & 1) == 0 ? size : new Vector2Int(size.y, size.x);
    }

    public static IEnumerable<Vector2Int> GetFootprint(Vector2Int origin, Vector2Int size, int rotation)
    {
        Vector2Int effective = EffectiveSize(size, rotation);
        for (int y = 0; y < effective.y; y++)
        {
            for (int x = 0; x < effective.x; x++)
            {
                yield return new Vector2Int(origin.x + x, origin.y + y);
            }
        }
    }
}
