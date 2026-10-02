using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class GridSystem : MonoBehaviour
{
    [SerializeField] private Grid m_Grid;
    [SerializeField] private Tilemap m_GroundTilemap;
    [SerializeField] private Vector3 m_GridOrigin = Vector3.zero;
    [SerializeField] private Vector2Int m_GridSize = new Vector2Int(24, 24);

    public Grid Grid => m_Grid;
    public Tilemap GroundTilemap => m_GroundTilemap;
    public Vector3 GridOrigin => m_GridOrigin;
    public Vector2Int GridSize => m_GridSize;
    public Vector2 CellSize => m_Grid != null ? new Vector2(m_Grid.cellSize.x, m_Grid.cellSize.y) : Vector2.one;

    private void Awake()
    {
        if (m_Grid == null) m_Grid = GetComponent<Grid>();
        if (m_GroundTilemap == null) m_GroundTilemap = GetComponentInChildren<Tilemap>();
    }

    public bool InBounds(Vector2Int cell)
    {
        return CellUtils.IsInBounds(cell, m_GridSize);
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

    public Vector2Int TileCellToLogical(Vector3Int tile)
    {
        BoundsInt bounds = m_GroundTilemap.cellBounds;
        return new Vector2Int(tile.x - bounds.xMin, bounds.yMax - 1 - tile.y);
    }
}
