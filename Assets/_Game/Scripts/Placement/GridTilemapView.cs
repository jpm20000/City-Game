using UnityEngine;
using UnityEngine.Tilemaps;

// Base for tilemaps that mirror GridData (zones, roads). Any cell change can affect tiles far away
// (road connectivity), so OnCellChanged only marks the view dirty and LateUpdate re-diffs every cell
// once, calling SetTile only where the tile actually changed.
public abstract class GridTilemapView : MonoBehaviour
{
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GridSystem m_GridSystem;
    [SerializeField] private Tilemap m_Tilemap;

    private Tile[] m_Painted;
    private bool m_Dirty;

    protected GridData Grid { get; private set; }
    protected RoadNetwork Roads { get; private set; }

    // Start, not Awake: GameManager.Awake creates the GridData.
    private void Start()
    {
        if (m_GameManager == null || m_GameManager.Grid == null || m_GridSystem == null || m_Tilemap == null) return;

        Grid = m_GameManager.Grid;
        Roads = m_GameManager.Roads;
        CreateTiles();

        // m_Painted starts all-null, so the tilemap must too.
        m_Tilemap.ClearAllTiles();
        m_Painted = new Tile[Grid.Width * Grid.Height];
        Grid.OnCellChanged += MarkDirty;
        m_Dirty = true;
    }

    protected virtual void OnDestroy()
    {
        if (Grid != null) Grid.OnCellChanged -= MarkDirty;
    }

    // Build the runtime tiles TileFor returns.
    protected abstract void CreateTiles();

    // The tile a cell should show, or null for none.
    protected abstract Tile TileFor(Vector2Int cell);

    private void MarkDirty(Vector2Int cell)
    {
        m_Dirty = true;
    }

    private void LateUpdate()
    {
        if (!m_Dirty || Grid == null) return;
        m_Dirty = false;

        for (int y = 0; y < Grid.Height; y++)
        {
            for (int x = 0; x < Grid.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Tile tile = TileFor(cell);
                int i = CellUtils.Index(cell, Grid.Width);
                if (m_Painted[i] == tile) continue;

                m_Painted[i] = tile;
                m_Tilemap.SetTile(m_GridSystem.LogicalToTileCell(cell), tile);
            }
        }
    }

    protected static Tile CreateTile(Sprite sprite, Color color)
    {
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.color = color;
        return tile;
    }
}
