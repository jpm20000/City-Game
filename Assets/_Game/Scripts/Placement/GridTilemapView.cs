using UnityEngine;
using UnityEngine.Tilemaps;

// Base for tilemaps that mirror GridData (zones, roads). Any cell change can affect tiles far away
// (road connectivity), so OnCellChanged only marks the view dirty and LateUpdate re-diffs every cell
// once, calling SetTile only where the tile actually changed. Subclasses can also MarkDirty() for
// state that isn't in GridData (e.g. a placement preview).
public abstract class GridTilemapView : MonoBehaviour
{
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GridSystem m_GridSystem;
    [SerializeField] private Tilemap m_Tilemap;

    private Tile[] m_Painted;
    private bool m_Dirty;

    protected GameManager GameManager => m_GameManager;
    protected GridData Grid { get; private set; }
    protected RoadNetwork Roads { get; private set; }

    // Start, not Awake: GameManager.Awake creates the GridData.
    private void Start()
    {
        if (m_GameManager == null || m_GameManager.Grid == null || m_GridSystem == null || m_Tilemap == null) return;

        Grid = m_GameManager.Grid;
        Roads = m_GameManager.Roads;
        CreateTiles();

        ResetPainted();
        Grid.OnCellChanged += OnCellChanged;
        Grid.OnResized += OnResized;
    }

    protected virtual void OnDestroy()
    {
        if (Grid == null) return;
        Grid.OnCellChanged -= OnCellChanged;
        Grid.OnResized -= OnResized;
    }

    // Called after the map is replaced by one of a new size, before the full repaint.
    protected virtual void OnGridResized() { }

    // m_Painted starts all-null, so the tilemap must too.
    private void ResetPainted()
    {
        m_Tilemap.ClearAllTiles();
        m_Painted = new Tile[Grid.Width * Grid.Height];
        m_Dirty = true;
    }

    private void OnResized()
    {
        ResetPainted();
        OnGridResized();
    }

    // Build the runtime tiles TileFor returns.
    protected abstract void CreateTiles();

    // The tile a cell should show, or null for none.
    protected abstract Tile TileFor(Vector2Int cell);

    // Called after a repaint pass, for views that mirror the same state elsewhere.
    protected virtual void OnRepainted() { }

    protected void MarkDirty()
    {
        m_Dirty = true;
    }

    private void OnCellChanged(Vector2Int cell)
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
        OnRepainted();
    }

    protected static Tile CreateTile(Sprite sprite, Color color)
    {
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.color = color;
        return tile;
    }

    // White diagonal stripes on transparent, sized like `like` (one cell); tiles tint them. Destroy
    // the sprite and its texture in OnDestroy.
    protected static Sprite CreateStripeSprite(Sprite like, string name)
    {
        const int size = 64;          // matches ground_square's 64 px per unit
        const int stripeWidth = 8;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool stripe = (x + y) / stripeWidth % 2 == 0;
                pixels[y * size + x] = stripe ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        float pixelsPerUnit = like != null ? like.pixelsPerUnit * size / like.rect.width : size;
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        sprite.name = name;
        return sprite;
    }

    protected static void DestroyStripeSprite(Sprite sprite)
    {
        if (sprite == null) return;
        Destroy(sprite.texture);
        Destroy(sprite);
    }
}
