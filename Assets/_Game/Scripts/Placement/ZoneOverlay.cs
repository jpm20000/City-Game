using UnityEngine;
using UnityEngine.Tilemaps;

// Paints a translucent tint on every zoned cell of the Zones Tilemap, mirroring GridData zones.
// Zoned cells without road access get diagonal stripes instead, since they can't grow.
public sealed class ZoneOverlay : GridTilemapView
{
    private const int k_StripeTextureSize = 64;   // matches ground_square's 64 px per unit
    private const int k_StripeWidth = 8;

    [SerializeField] private Sprite m_Sprite;
    [SerializeField, Range(0f, 1f)] private float m_Alpha = 0.45f;
    [SerializeField, Range(0f, 1f)] private float m_NoAccessAlpha = 0.7f;

    private Tile[] m_Tiles;          // [zone] with road access
    private Tile[] m_NoAccessTiles;  // [zone] without
    private Texture2D m_StripeTexture;
    private Sprite m_StripeSprite;

    protected override void CreateTiles()
    {
        CreateStripeSprite();

        m_Tiles = new Tile[4];
        m_NoAccessTiles = new Tile[4];
        foreach (ZoneType zone in new[] { ZoneType.Residential, ZoneType.Commercial, ZoneType.Industrial })
        {
            Color color = ZonePalette.Get(zone);
            m_Tiles[(int)zone] = CreateTile(m_Sprite, new Color(color.r, color.g, color.b, m_Alpha));
            m_NoAccessTiles[(int)zone] = CreateTile(m_StripeSprite, new Color(color.r, color.g, color.b, m_NoAccessAlpha));
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (m_Tiles != null) foreach (Tile tile in m_Tiles) Destroy(tile);
        if (m_NoAccessTiles != null) foreach (Tile tile in m_NoAccessTiles) Destroy(tile);
        Destroy(m_StripeSprite);
        Destroy(m_StripeTexture);
    }

    protected override Tile TileFor(Vector2Int cell)
    {
        ZoneType zone = Grid.GetZone(cell);
        if (zone == ZoneType.None) return null;

        bool hasAccess = Roads == null || Roads.HasRoadAccess(cell);
        return hasAccess ? m_Tiles[(int)zone] : m_NoAccessTiles[(int)zone];
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
