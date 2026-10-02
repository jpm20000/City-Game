using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class GridSystem : MonoBehaviour
{
    [SerializeField] private Tilemap m_GroundTilemap;
    [SerializeField] private Vector3 m_GridOrigin = Vector3.zero;
    [SerializeField] private Vector2Int m_GridSize = new Vector2Int(24, 24);

    // The ground tilemap never changes at runtime, so its bounds are read once.
    private BoundsInt m_TileBounds;

    public Vector2Int GridSize => m_GridSize;

    private void Awake()
    {
        if (m_GroundTilemap == null) m_GroundTilemap = GetComponentInChildren<Tilemap>();
        m_TileBounds = m_GroundTilemap.cellBounds;
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
        return new Vector3Int(logical.x + m_TileBounds.xMin, m_TileBounds.yMax - 1 - logical.y, 0);
    }
}
