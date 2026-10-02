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
