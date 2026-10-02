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

    public static IEnumerable<Vector2Int> GetFootprint(Vector2Int origin, Vector2Int size, int rotation)
    {
        int width = size.x;
        int height = size.y;
        if ((rotation & 1) == 1)
        {
            int swapped = width;
            width = height;
            height = swapped;
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                yield return new Vector2Int(origin.x + x, origin.y + y);
            }
        }
    }
}
