using UnityEngine;
using UnityEngine.Tilemaps;

// Mirrors GridData roads onto the Roads Tilemap, auto-tiled from the 4 neighbours: asphalt, curbs on
// unconnected sides and a dashed centre line towards each connection. Roads at the map edge connect
// "off-map" (that's the entry). Roads not connected to the edge are tinted red. Sprites are
// generated at runtime, so there is no art dependency.
public sealed class RoadTilemapView : MonoBehaviour
{
    // Connection bits in logical directions.
    private const int k_North = 1;  // +y
    private const int k_East = 2;   // +x
    private const int k_South = 4;  // -y
    private const int k_West = 8;   // -x

    private const int k_Size = 64;          // px per cell (matches ground_square)
    private const int k_Curb = 7;           // curb width in px
    private const int k_LineHalfWidth = 1;  // centre line is 2 px wide
    private const int k_Dash = 8;           // dash on/off length in px (divides k_Size so dashes tile)

    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GridSystem m_GridSystem;
    [SerializeField] private Tilemap m_Tilemap;
    [SerializeField] private Color m_AsphaltColor = new Color(0.30f, 0.32f, 0.35f);
    [SerializeField] private Color m_CurbColor = new Color(0.58f, 0.59f, 0.60f);
    [SerializeField] private Color m_LineColor = new Color(0.95f, 0.85f, 0.40f);
    [SerializeField] private Color m_DisconnectedTint = new Color(1.00f, 0.62f, 0.58f);

    private GridData m_Grid;
    private RoadNetwork m_Roads;
    private readonly Texture2D[] m_Textures = new Texture2D[16];
    private readonly Sprite[] m_Sprites = new Sprite[16];
    private readonly Tile[] m_ConnectedTiles = new Tile[16];
    private readonly Tile[] m_DisconnectedTiles = new Tile[16];
    private Tile[] m_Painted;
    private bool m_Dirty;

    // Start, not Awake: GameManager.Awake creates the GridData.
    private void Start()
    {
        if (m_GameManager == null || m_GameManager.Grid == null || m_Tilemap == null || m_GridSystem == null) return;

        m_Grid = m_GameManager.Grid;
        m_Roads = m_GameManager.Roads;
        for (int mask = 0; mask < 16; mask++)
        {
            m_Textures[mask] = DrawRoad(mask);
            m_Sprites[mask] = Sprite.Create(m_Textures[mask], new Rect(0, 0, k_Size, k_Size), new Vector2(0.5f, 0.5f), k_Size);
            m_ConnectedTiles[mask] = CreateTile(m_Sprites[mask], Color.white);
            m_DisconnectedTiles[mask] = CreateTile(m_Sprites[mask], m_DisconnectedTint);
        }

        m_Tilemap.ClearAllTiles();
        m_Painted = new Tile[m_Grid.Width * m_Grid.Height];
        m_Grid.OnCellChanged += OnCellChanged;
        m_Dirty = true;
    }

    private void OnDestroy()
    {
        if (m_Grid != null) m_Grid.OnCellChanged -= OnCellChanged;
        for (int i = 0; i < 16; i++)
        {
            Destroy(m_ConnectedTiles[i]);
            Destroy(m_DisconnectedTiles[i]);
            Destroy(m_Sprites[i]);
            Destroy(m_Textures[i]);
        }
    }

    // A road edit changes neighbours' shapes and connectivity anywhere, so re-diff once per frame.
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
        if (!m_Grid.IsRoad(cell)) return null;

        int mask = 0;
        if (Connects(cell, Vector2Int.up)) mask |= k_North;
        if (Connects(cell, Vector2Int.right)) mask |= k_East;
        if (Connects(cell, Vector2Int.down)) mask |= k_South;
        if (Connects(cell, Vector2Int.left)) mask |= k_West;

        bool connected = m_Roads == null || m_Roads.IsConnectedToEntry(cell);
        return connected ? m_ConnectedTiles[mask] : m_DisconnectedTiles[mask];
    }

    // Off-map counts as a connection where a road meets the edge head-on (it leads out of town), but
    // not for a road running along the edge, which would otherwise sprout a stub on every cell.
    private bool Connects(Vector2Int cell, Vector2Int direction)
    {
        Vector2Int neighbor = cell + direction;
        if (m_Grid.InBounds(neighbor)) return m_Grid.IsRoad(neighbor);

        Vector2Int side = new Vector2Int(direction.y, direction.x);
        return !IsRoadInBounds(cell + side) && !IsRoadInBounds(cell - side);
    }

    private bool IsRoadInBounds(Vector2Int cell)
    {
        return m_Grid.InBounds(cell) && m_Grid.IsRoad(cell);
    }

    private static Tile CreateTile(Sprite sprite, Color color)
    {
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.color = color;
        return tile;
    }

    // Texture pixel x = logical +x. Tile rows run opposite to logical y (see
    // GridSystem.LogicalToTileCell), so pixel +y = logical south.
    private Texture2D DrawRoad(int mask)
    {
        Texture2D texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, true)
        {
            name = $"Road_{mask}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        Color32[] pixels = new Color32[k_Size * k_Size];
        const int center = k_Size / 2;
        for (int py = 0; py < k_Size; py++)
        {
            for (int px = 0; px < k_Size; px++)
            {
                // Distances to each logical side.
                int toWest = px;
                int toEast = k_Size - 1 - px;
                int toSouth = k_Size - 1 - py;
                int toNorth = py;

                Color color = m_AsphaltColor;

                bool curb = (toNorth < k_Curb && (mask & k_North) == 0)
                    || (toSouth < k_Curb && (mask & k_South) == 0)
                    || (toEast < k_Curb && (mask & k_East) == 0)
                    || (toWest < k_Curb && (mask & k_West) == 0);
                // Inner corners between two connected sides keep a small curb nub.
                bool corner = (toNorth < k_Curb && toEast < k_Curb) || (toNorth < k_Curb && toWest < k_Curb)
                    || (toSouth < k_Curb && toEast < k_Curb) || (toSouth < k_Curb && toWest < k_Curb);

                if (curb || corner)
                {
                    color = m_CurbColor;
                }
                else if (OnCentreLine(px, py, center, mask))
                {
                    color = m_LineColor;
                }

                pixels[py * k_Size + px] = color;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    // Dashed line from the centre towards each connected side; a dead end or isolated tile gets a
    // short stub so it still reads as road.
    private static bool OnCentreLine(int px, int py, int center, int mask)
    {
        bool onVertical = Mathf.Abs(px - center + 0.5f) <= k_LineHalfWidth;
        bool onHorizontal = Mathf.Abs(py - center + 0.5f) <= k_LineHalfWidth;
        bool north = (mask & k_North) != 0;
        bool south = (mask & k_South) != 0;
        bool east = (mask & k_East) != 0;
        bool west = (mask & k_West) != 0;

        bool segment =
            (onVertical && north && py <= center) ||
            (onVertical && south && py >= center) ||
            (onHorizontal && west && px <= center) ||
            (onHorizontal && east && px >= center);
        if (!segment) return false;

        // Dash phase measured from the tile edge so dashes line up across neighbouring tiles.
        int along = onVertical && (north || south) ? py : px;
        return along / k_Dash % 2 == 0;
    }
}
