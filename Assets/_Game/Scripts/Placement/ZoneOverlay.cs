using UnityEngine;
using UnityEngine.Tilemaps;

// Paints a translucent tint on every zoned cell of the Zones Tilemap, mirroring GridData zones.
// Zoned cells without road access get diagonal stripes instead, since they can't grow.
public sealed class ZoneOverlay : GridTilemapView
{
    [SerializeField] private Sprite m_Sprite;
    [SerializeField, Range(0f, 1f)] private float m_Alpha = 0.45f;
    [SerializeField, Range(0f, 1f)] private float m_NoAccessAlpha = 0.7f;

    private Tile[] m_Tiles;          // [zone] with road access
    private Tile[] m_NoAccessTiles;  // [zone] without
    private Sprite m_StripeSprite;

    protected override void CreateTiles()
    {
        m_StripeSprite = CreateStripeSprite(m_Sprite, "ZoneNoAccessStripes");

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
        DestroyStripeSprite(m_StripeSprite);
    }

    protected override Tile TileFor(Vector2Int cell)
    {
        ZoneType zone = Grid.GetZone(cell);
        if (zone == ZoneType.None) return null;

        bool hasAccess = Roads == null || Roads.HasRoadAccess(cell);
        return hasAccess ? m_Tiles[(int)zone] : m_NoAccessTiles[(int)zone];
    }
}
