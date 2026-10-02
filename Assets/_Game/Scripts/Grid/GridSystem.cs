using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class GridSystem : MonoBehaviour
{
    [SerializeField] private Tilemap m_GroundTilemap;
    [SerializeField] private Vector3 m_GridOrigin = Vector3.zero;
    [Tooltip("Map size at startup (New City / Load can change it).")]
    [SerializeField] private Vector2Int m_GridSize = new Vector2Int(64, 64);

    // Tile-space anchor of logical cell (0, 0), read once from the ground painted in the scene: the
    // scene's tilemap spans -12..11 with a Y flip, so logical (x, y) -> tile (x + xMin, yMax - 1 - y).
    // Maps of any size grow from that anchor, so the Grid GameObject never moves.
    private int m_TileXMin;
    private int m_TileYMax;
    private TileBase m_GroundTile;
    private Vector2Int m_PaintedSize;
    private bool m_Initialized;

    public Vector2Int GridSize => m_GridSize;

    private void Awake()
    {
        EnsureInitialized();
    }

    // Lazy as well, since GameManager.Awake may paint the ground before this Awake runs.
    private void EnsureInitialized()
    {
        if (m_Initialized) return;
        m_Initialized = true;

        if (m_GroundTilemap == null) m_GroundTilemap = GetComponentInChildren<Tilemap>();
        m_GroundTilemap.CompressBounds();
        BoundsInt bounds = m_GroundTilemap.cellBounds;
        m_TileXMin = bounds.xMin;
        m_TileYMax = bounds.yMax;
        m_GroundTile = m_GroundTilemap.GetTile(LogicalToTileCell(Vector2Int.zero));
        m_PaintedSize = new Vector2Int(bounds.size.x, bounds.size.y);
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
        EnsureInitialized();
        return new Vector3Int(logical.x + m_TileXMin, m_TileYMax - 1 - logical.y, 0);
    }

    // Repaints the ground to cover logical cells (0..size-1)². Called by GameManager when the map is resized.
    public void PaintGround(Vector2Int size)
    {
        EnsureInitialized();
        m_GridSize = size;
        if (size == m_PaintedSize || m_GroundTile == null) return;

        m_GroundTilemap.ClearAllTiles();
        Vector3Int min = LogicalToTileCell(new Vector2Int(0, size.y - 1));
        BoundsInt block = new BoundsInt(min, new Vector3Int(size.x, size.y, 1));
        TileBase[] tiles = new TileBase[size.x * size.y];
        System.Array.Fill(tiles, m_GroundTile);
        m_GroundTilemap.SetTilesBlock(block, tiles);
        m_PaintedSize = size;
    }
}
