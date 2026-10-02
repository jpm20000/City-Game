using UnityEngine;
using UnityEngine.Tilemaps;

// Paints a translucent tint on every zoned cell of the Zones Tilemap, mirroring GridData zones.
// Zoned cells without road access get diagonal stripes instead, since they can't grow.
public sealed class ZoneOverlay : MonoBehaviour
{
    private const int k_StripeTextureSize = 64;   // matches ground_square's 64 px per unit
    private const int k_StripeWidth = 8;

    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GridSystem m_GridSystem;
    [SerializeField] private Tilemap m_Tilemap;
    [SerializeField] private Sprite m_Sprite;
    [SerializeField] private Color m_ResidentialColor = new Color(0.40f, 0.85f, 0.35f, 0.45f);
    [SerializeField] private Color m_CommercialColor = new Color(0.30f, 0.55f, 0.95f, 0.45f);
    [SerializeField] private Color m_IndustrialColor = new Color(0.95f, 0.80f, 0.25f, 0.45f);
    [SerializeField, Range(0f, 1f)] private float m_NoAccessAlpha = 0.7f;

    private GridData m_Grid;
    private RoadNetwork m_Roads;
    private Tile[] m_Tiles;          // [zone] with road access
    private Tile[] m_NoAccessTiles;  // [zone] without
    private Texture2D m_StripeTexture;
    private Sprite m_StripeSprite;
    private Tile[] m_Painted;        // what each cell currently shows, to skip redundant SetTile calls
    private bool m_Dirty;

    // Start, not Awake: GameManager.Awake creates the GridData.
    private void Start()
    {
        if (m_GameManager == null || m_GameManager.Grid == null || m_Tilemap == null) return;

        m_Grid = m_GameManager.Grid;
        m_Roads = m_GameManager.Roads;
        CreateStripeSprite();

        m_Tiles = new Tile[4];
        m_NoAccessTiles = new Tile[4];
        CreateTiles(ZoneType.Residential, m_ResidentialColor);
        CreateTiles(ZoneType.Commercial, m_CommercialColor);
        CreateTiles(ZoneType.Industrial, m_IndustrialColor);

        // m_Painted starts all-null, so the tilemap must too.
        m_Tilemap.ClearAllTiles();
        m_Painted = new Tile[m_Grid.Width * m_Grid.Height];
        m_Grid.OnCellChanged += OnCellChanged;
        m_Dirty = true;
    }

    private void OnDestroy()
    {
        if (m_Grid != null) m_Grid.OnCellChanged -= OnCellChanged;
        if (m_Tiles != null) foreach (Tile tile in m_Tiles) Destroy(tile);
        if (m_NoAccessTiles != null) foreach (Tile tile in m_NoAccessTiles) Destroy(tile);
        Destroy(m_StripeSprite);
        Destroy(m_StripeTexture);
    }

    // Any road change can alter access for distant cells, so resync everything once per frame.
    private void OnCellChanged(Vector2Int cell)
    {
        m_Dirty = true;
    }

    private void LateUpdate()
    {
        if (!m_Dirty || m_Grid == null) return;
        m_Dirty = false;

        for (int y = 0; y < m_Grid.Height; y++)
        {
            for (int x = 0; x < m_Grid.Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Tile tile = TileFor(cell);
                int i = y * m_Grid.Width + x;
                if (m_Painted[i] == tile) continue;

                m_Painted[i] = tile;
                m_Tilemap.SetTile(m_GridSystem.LogicalToTileCell(cell), tile);
            }
        }
    }

    private Tile TileFor(Vector2Int cell)
    {
        ZoneType zone = m_Grid.GetZone(cell);
        if (zone == ZoneType.None) return null;

        bool hasAccess = m_Roads == null || m_Roads.HasRoadAccess(cell);
        return hasAccess ? m_Tiles[(int)zone] : m_NoAccessTiles[(int)zone];
    }

    private void CreateTiles(ZoneType zone, Color color)
    {
        m_Tiles[(int)zone] = CreateTile(m_Sprite, color);
        m_NoAccessTiles[(int)zone] = CreateTile(m_StripeSprite, new Color(color.r, color.g, color.b, m_NoAccessAlpha));
    }

    private static Tile CreateTile(Sprite sprite, Color color)
    {
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.color = color;
        return tile;
    }

    // White diagonal stripes on transparent; the tile colour tints them per zone.
    private void CreateStripeSprite()
    {
        const int size = k_StripeTextureSize;
        m_StripeTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ZoneNoAccessStripes",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool stripe = (x + y) / k_StripeWidth % 2 == 0;
                pixels[y * size + x] = stripe ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        m_StripeTexture.SetPixels32(pixels);
        m_StripeTexture.Apply();

        float pixelsPerUnit = m_Sprite != null ? m_Sprite.pixelsPerUnit * size / m_Sprite.rect.width : size;
        m_StripeSprite = Sprite.Create(m_StripeTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        m_StripeSprite.name = "ZoneNoAccessStripes";
    }
}
