using UnityEngine;
using UnityEngine.Tilemaps;

// Mirrors GridData roads onto the Roads Tilemap (see GridTilemapView), auto-tiled from the 4
// neighbours with one 16-sprite set per road tier (M16): dirt track, cobbled street, paved road (the
// original look), avenue (wider asphalt, double centre line) and highway (dark asphalt, white lane
// dashes and edge lines). Tiles connect to any tier. Roads at the map edge connect "off-map" (that's
// the entry). Roads not connected to the edge are tinted red. Sprites are generated at runtime, so
// there is no art dependency.
public sealed class RoadTilemapView : GridTilemapView
{
    // Connection bits in logical directions.
    private const int k_North = 1;  // +y
    private const int k_East = 2;   // +x
    private const int k_South = 4;  // -y
    private const int k_West = 8;   // -x

    private const int k_Size = 64;          // px per cell (matches ground_square)
    private const int k_LineHalfWidth = 1;  // centre line is 2 px wide
    private const int k_Dash = 8;           // dash on/off length in px (divides k_Size so dashes tile)
    private const int k_Tiers = GridData.MaxRoadTier;

    // How a tier is drawn. Line offsets are px from the tile centre line (empty = no line).
    private readonly struct Style
    {
        public readonly Color Asphalt;
        public readonly Color Curb;
        public readonly Color Line;
        public readonly int CurbWidth;
        public readonly int[] LineOffsets;
        public readonly bool Speckle;       // stone texture (cobble)

        public Style(Color asphalt, Color curb, Color line, int curbWidth, int[] lineOffsets, bool speckle = false)
        {
            Asphalt = asphalt;
            Curb = curb;
            Line = line;
            CurbWidth = curbWidth;
            LineOffsets = lineOffsets;
            Speckle = speckle;
        }
    }

    [SerializeField] private Color m_AsphaltColor = new Color(0.30f, 0.32f, 0.35f);
    [SerializeField] private Color m_CurbColor = new Color(0.58f, 0.59f, 0.60f);
    [SerializeField] private Color m_LineColor = new Color(0.95f, 0.85f, 0.40f);
    [SerializeField] private Color m_DisconnectedTint = new Color(1.00f, 0.62f, 0.58f);

    private readonly Texture2D[,] m_Textures = new Texture2D[k_Tiers, 16];
    private readonly Sprite[,] m_Sprites = new Sprite[k_Tiers, 16];
    private readonly Tile[,] m_ConnectedTiles = new Tile[k_Tiers, 16];
    private readonly Tile[,] m_DisconnectedTiles = new Tile[k_Tiers, 16];

    // The lanes of a paired avenue (M22): one set per side the partner lane is on (RoadLayout code 1..4, index code - 1),
    // drawn with a median strip along that edge and a single dashed lane line.
    private readonly Texture2D[,] m_LaneTextures = new Texture2D[4, 16];
    private readonly Sprite[,] m_LaneSprites = new Sprite[4, 16];
    private readonly Tile[,] m_LaneTiles = new Tile[4, 16];
    private readonly Tile[,] m_LaneDisconnectedTiles = new Tile[4, 16];
    private const int k_MedianWidth = 11;       // px of median along the inner edge of a paired lane

    private Style StyleOf(int tier)
    {
        switch (tier)
        {
            case 1:     // dirt track: packed earth, soft edges, no markings
                return new Style(new Color(0.46f, 0.35f, 0.23f), new Color(0.37f, 0.28f, 0.18f), Color.clear, 4, new int[0]);
            case 2:     // cobbled street: grey-brown stones
                return new Style(new Color(0.53f, 0.49f, 0.44f), new Color(0.64f, 0.62f, 0.58f), Color.clear, 6, new int[0], speckle: true);
            case 4:     // avenue: darker asphalt, double centre line
                return new Style(new Color(0.26f, 0.28f, 0.31f), m_CurbColor, m_LineColor, 5, new[] { -3, 3 });
            case 5:     // highway: black asphalt, white edge lines and lane dashes
                return new Style(new Color(0.16f, 0.17f, 0.19f), new Color(0.88f, 0.88f, 0.86f), new Color(0.92f, 0.92f, 0.90f), 3, new[] { -11, 11 });
            default:    // paved road: the original look
                return new Style(m_AsphaltColor, m_CurbColor, m_LineColor, 7, new[] { 0 });
        }
    }

    // The tile art (M18c) supplies a sprite per tier and connection mask; any missing one is drawn here.
    protected override void CreateTiles()
    {
        TileArtSet art = GameManager != null ? GameManager.TileArt : null;
        for (int tier = 1; tier <= k_Tiers; tier++)
        {
            Style style = StyleOf(tier);
            for (int mask = 0; mask < 16; mask++)
            {
                Sprite sprite = art != null ? art.Road(tier, mask) : null;
                if (sprite == null)
                {
                    Texture2D texture = DrawRoad(tier, mask, style);
                    m_Textures[tier - 1, mask] = texture;
                    sprite = Sprite.Create(texture, new Rect(0, 0, k_Size, k_Size), new Vector2(0.5f, 0.5f), k_Size);
                    m_Sprites[tier - 1, mask] = sprite;
                }
                m_ConnectedTiles[tier - 1, mask] = CreateTile(sprite, Color.white);
                m_DisconnectedTiles[tier - 1, mask] = CreateTile(sprite, m_DisconnectedTint);
            }
        }

        Style lane = StyleOf(RoadTiersAvenue);
        lane = new Style(lane.Asphalt, lane.Curb, lane.Line, lane.CurbWidth, new[] { 0 });
        for (int side = 1; side <= 4; side++)
        {
            for (int mask = 0; mask < 16; mask++)
            {
                if ((mask & (1 << (side - 1))) != 0) continue;     // the partner side is never a connection
                Texture2D texture = DrawRoad(RoadTiersAvenue, mask, lane, side);
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, k_Size, k_Size), new Vector2(0.5f, 0.5f), k_Size);
                m_LaneTextures[side - 1, mask] = texture;
                m_LaneSprites[side - 1, mask] = sprite;
                m_LaneTiles[side - 1, mask] = CreateTile(sprite, Color.white);
                m_LaneDisconnectedTiles[side - 1, mask] = CreateTile(sprite, m_DisconnectedTint);
            }
        }
    }

    private const int RoadTiersAvenue = 4;

    protected override void OnDestroy()
    {
        base.OnDestroy();
        for (int tier = 0; tier < k_Tiers; tier++)
        {
            for (int i = 0; i < 16; i++)
            {
                Destroy(m_ConnectedTiles[tier, i]);
                Destroy(m_DisconnectedTiles[tier, i]);
                Destroy(m_Sprites[tier, i]);
                Destroy(m_Textures[tier, i]);
            }
        }
        for (int side = 0; side < 4; side++)
        {
            for (int i = 0; i < 16; i++)
            {
                Destroy(m_LaneTiles[side, i]);
                Destroy(m_LaneDisconnectedTiles[side, i]);
                Destroy(m_LaneSprites[side, i]);
                Destroy(m_LaneTextures[side, i]);
            }
        }
    }

    protected override Tile TileFor(Vector2Int cell)
    {
        int tier = Grid.GetRoadTier(cell);
        if (tier == 0) return null;

        int mask = 0;
        if (Connects(cell, Vector2Int.up)) mask |= k_North;
        if (Connects(cell, Vector2Int.right)) mask |= k_East;
        if (Connects(cell, Vector2Int.down)) mask |= k_South;
        if (Connects(cell, Vector2Int.left)) mask |= k_West;

        bool connected = Roads == null || Roads.IsConnectedToEntry(cell);
        byte pair = tier == RoadTiersAvenue ? Grid.GetRoadPair(cell) : (byte)0;
        if (pair != 0)
        {
            int laneMask = mask & ~(1 << (pair - 1));
            return connected ? m_LaneTiles[pair - 1, laneMask] : m_LaneDisconnectedTiles[pair - 1, laneMask];
        }
        return connected ? m_ConnectedTiles[tier - 1, mask] : m_DisconnectedTiles[tier - 1, mask];
    }

    // Off-map counts as a connection where a road meets the edge head-on (it leads out of town), but
    // not for a road running along the edge, which would otherwise sprout a stub on every cell.
    private bool Connects(Vector2Int cell, Vector2Int direction)
    {
        Vector2Int neighbor = cell + direction;
        if (Grid.InBounds(neighbor)) return Grid.IsRoad(neighbor);

        Vector2Int side = new Vector2Int(direction.y, direction.x);
        return !IsRoadInBounds(cell + side) && !IsRoadInBounds(cell - side);
    }

    private bool IsRoadInBounds(Vector2Int cell)
    {
        return Grid.InBounds(cell) && Grid.IsRoad(cell);
    }

    // Texture pixel x = logical +x. Tile rows run opposite to logical y (see
    // GridSystem.LogicalToTileCell), so pixel +y = logical south.
    private static Texture2D DrawRoad(int tier, int mask, Style style, int medianSide = 0)
    {
        Texture2D texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, true)
        {
            name = $"Road_{tier}_{mask}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        Color32[] pixels = new Color32[k_Size * k_Size];
        const int center = k_Size / 2;
        int curbWidth = style.CurbWidth;
        for (int py = 0; py < k_Size; py++)
        {
            for (int px = 0; px < k_Size; px++)
            {
                // Distances to each logical side.
                int toWest = px;
                int toEast = k_Size - 1 - px;
                int toSouth = k_Size - 1 - py;
                int toNorth = py;

                Color color = style.Asphalt;
                if (style.Speckle)
                {
                    int hash = (px * 73856093) ^ (py * 19349663) ^ (tier * 83492791);
                    color += new Color(1f, 1f, 1f, 0f) * (((hash >> 4) & 15) - 8) * 0.006f;
                    // Joints between the stones.
                    if (px % 16 == 0 || (py + (px / 16 % 2) * 8) % 16 == 0) color *= 0.86f;
                }

                bool curb = (toNorth < curbWidth && (mask & k_North) == 0)
                    || (toSouth < curbWidth && (mask & k_South) == 0)
                    || (toEast < curbWidth && (mask & k_East) == 0)
                    || (toWest < curbWidth && (mask & k_West) == 0);
                // Inner corners between two connected sides keep a small curb nub.
                bool corner = (toNorth < curbWidth && toEast < curbWidth) || (toNorth < curbWidth && toWest < curbWidth)
                    || (toSouth < curbWidth && toEast < curbWidth) || (toSouth < curbWidth && toWest < curbWidth);

                // A paired lane's inner edge: a raised kerb, then a planted strip, running the full length of the tile.
                int toMedian = medianSide == 1 ? toNorth : medianSide == 2 ? toEast : medianSide == 3 ? toSouth : medianSide == 4 ? toWest : int.MaxValue;
                if (toMedian < k_MedianWidth)
                {
                    color = toMedian < 3 ? style.Curb : toMedian < 5 ? new Color(0.20f, 0.22f, 0.20f) : new Color(0.30f, 0.42f, 0.26f);
                }
                else if (curb || corner)
                {
                    color = style.Curb;
                }
                else if (OnCentreLine(px, py, center, mask, style.LineOffsets))
                {
                    color = style.Line;
                }

                color.a = 1f;
                pixels[py * k_Size + px] = color;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    // Dashed line(s) from the centre towards each connected side, each offset sideways by an entry of
    // offsets; a dead end or isolated tile gets a short stub so it still reads as road.
    private static bool OnCentreLine(int px, int py, int center, int mask, int[] offsets)
    {
        bool north = (mask & k_North) != 0;
        bool south = (mask & k_South) != 0;
        bool east = (mask & k_East) != 0;
        bool west = (mask & k_West) != 0;

        foreach (int offset in offsets)
        {
            bool onVertical = Mathf.Abs(px - center + 0.5f - offset) <= k_LineHalfWidth;
            bool onHorizontal = Mathf.Abs(py - center + 0.5f - offset) <= k_LineHalfWidth;
            bool segment =
                (onVertical && north && py <= center) ||
                (onVertical && south && py >= center) ||
                (onHorizontal && west && px <= center) ||
                (onHorizontal && east && px >= center);
            if (!segment) continue;

            // Dash phase measured from the tile edge so dashes line up across neighbouring tiles.
            int along = onVertical && (north || south) ? py : px;
            if (along / k_Dash % 2 == 0) return true;
        }
        return false;
    }
}
