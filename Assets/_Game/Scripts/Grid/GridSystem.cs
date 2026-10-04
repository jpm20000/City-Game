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
    private TileBase[] m_GroundVariants;    // M18c: art variants, picked per cell by hash; null = the scene's tile
    private bool m_GroundDirty;

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

    // Ground art (M18c): these tiles replace the scene's single ground tile, one picked per cell by a hash
    // (so the same city always looks the same). Null or empty = back to the scene's tile. Repaints at once.
    public void SetGroundVariants(TileBase[] variants)
    {
        EnsureInitialized();
        m_GroundVariants = variants != null && variants.Length > 0 ? variants : null;
        m_GroundDirty = true;
        PaintGround(m_GridSize);
    }

    // Repaints the ground to cover logical cells (0..size-1)². Called by GameManager when the map is resized.
    public void PaintGround(Vector2Int size)
    {
        EnsureInitialized();
        m_GridSize = size;
        // An empty array counts as none (a stale or cleared set must never divide by its length below).
        TileBase[] variants = m_GroundVariants != null && m_GroundVariants.Length > 0 ? m_GroundVariants : null;
        if ((size == m_PaintedSize && !m_GroundDirty) || (m_GroundTile == null && variants == null)) return;

        m_GroundTilemap.ClearAllTiles();
        Vector3Int min = LogicalToTileCell(new Vector2Int(0, size.y - 1));
        BoundsInt block = new BoundsInt(min, new Vector3Int(size.x, size.y, 1));
        TileBase[] tiles = new TileBase[size.x * size.y];
        if (variants == null)
        {
            System.Array.Fill(tiles, m_GroundTile);
        }
        else
        {
            // Block order is x fastest from the tile-space minimum; tile rows run opposite to logical y.
            for (int ty = 0; ty < size.y; ty++)
            {
                for (int tx = 0; tx < size.x; tx++)
                {
                    tiles[ty * size.x + tx] = variants[GroundHash(tx, size.y - 1 - ty) % (uint)variants.Length];
                }
            }
        }
        m_GroundTilemap.SetTilesBlock(block, tiles);
        m_PaintedSize = size;
        m_GroundDirty = false;
    }

    private static uint GroundHash(int x, int y)
    {
        uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }
}
