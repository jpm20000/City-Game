using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class GridSystem : MonoBehaviour
{
    [SerializeField] private Tilemap m_GroundTilemap;
    [SerializeField] private Vector3 m_GridOrigin = Vector3.zero;
    [SerializeField] private Vector2Int m_GridSize = new Vector2Int(24, 24);

    public Vector2Int GridSize => m_GridSize;

    private void Awake()
    {
        if (m_GroundTilemap == null) m_GroundTilemap = GetComponentInChildren<Tilemap>();
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return CellUtils.CellToWorld(cell, m_GridOrigin);
    }

    public Vector2Int WorldToCell(Vector3 world)
    {
        return CellUtils.WorldToCell(world, m_GridOrigin);
    }

    public Vector3Int LogicalToTileCell(Vector2Int logical)
    {
        BoundsInt bounds = m_GroundTilemap.cellBounds;
        return new Vector3Int(logical.x + bounds.xMin, bounds.yMax - 1 - logical.y, 0);
    }
}
