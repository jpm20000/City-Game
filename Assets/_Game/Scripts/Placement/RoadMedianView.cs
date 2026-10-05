using UnityEngine;
using UnityEngine.Tilemaps;

// The median between the two lanes of a paired avenue (M22): a kerb and a planted strip along the inner edge of each lane,
// on a tilemap of its own above the Roads (made at runtime like RoadArrowView), so the road art stays the paved road.
// Presentation only: it reads GridData and never writes it.
public sealed class RoadMedianView : MonoBehaviour
{
    private const int k_Size = 64;
    private const int k_Width = 11;     // px of median along the inner edge of a lane

    private GameManager m_Game;
    private GridSystem m_GridSystem;
    private Tilemap m_Tilemap;
    private readonly Tile[] m_Tiles = new Tile[5];          // by RoadLayout code 1..4: the side the partner lane is on
    private readonly Sprite[] m_Sprites = new Sprite[5];
    private readonly Texture2D[] m_Textures = new Texture2D[5];
    private byte[] m_Painted = new byte[0];
    private GridData m_Grid;
    private bool m_Dirty = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameManager game = FindAnyObjectByType<GameManager>();
        GridSystem gridSystem = FindAnyObjectByType<GridSystem>();
        if (game == null || gridSystem == null) return;
        Tilemap roads = null;
        foreach (Tilemap tilemap in FindObjectsByType<Tilemap>(FindObjectsInactive.Include))
        {
            if (tilemap.name == "Roads") roads = tilemap;
        }
        if (roads == null) return;
        var view = new GameObject("RoadMedians").AddComponent<RoadMedianView>();
        view.Init(game, gridSystem, roads);
    }

    private void Init(GameManager game, GridSystem gridSystem, Tilemap roads)
    {
        m_Game = game;
        m_GridSystem = gridSystem;
        var go = new GameObject("Medians", typeof(Tilemap), typeof(TilemapRenderer));
        go.layer = roads.gameObject.layer;
        go.transform.SetParent(roads.transform.parent, false);
        m_Tilemap = go.GetComponent<Tilemap>();
        var renderer = go.GetComponent<TilemapRenderer>();
        var roadRenderer = roads.GetComponent<TilemapRenderer>();
        renderer.sharedMaterial = roadRenderer.sharedMaterial;
        renderer.sortingLayerID = roadRenderer.sortingLayerID;
        renderer.sortingOrder = roadRenderer.sortingOrder + 1;

        for (byte side = RoadLayout.North; side <= RoadLayout.West; side++)
        {
            m_Textures[side] = DrawStrip(side);
            m_Sprites[side] = Sprite.Create(m_Textures[side], new Rect(0, 0, k_Size, k_Size), new Vector2(0.5f, 0.5f), k_Size);
            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = m_Sprites[side];
            tile.colliderType = Tile.ColliderType.None;
            m_Tiles[side] = tile;
        }
        GameEvents.WorldResized += OnWorldResized;
        GameEvents.CityLoaded += OnCityLoaded;
    }

    private void OnDestroy()
    {
        GameEvents.WorldResized -= OnWorldResized;
        GameEvents.CityLoaded -= OnCityLoaded;
        if (m_Grid != null) m_Grid.OnCellChanged -= OnCellChanged;
        for (int i = 1; i <= 4; i++)
        {
            if (m_Tiles[i] != null) Destroy(m_Tiles[i]);
            if (m_Sprites[i] != null) Destroy(m_Sprites[i]);
            if (m_Textures[i] != null) Destroy(m_Textures[i]);
        }
    }

    private void OnWorldResized(Vector2Int size)
    {
        m_Tilemap.ClearAllTiles();
        m_Painted = new byte[0];
        m_Dirty = true;
    }

    private void OnCityLoaded() => m_Dirty = true;

    private void OnCellChanged(Vector2Int cell) => m_Dirty = true;

    // Texture +x = logical +x, pixel row 0 = logical north (see RoadTilemapView).
    private static Texture2D DrawStrip(byte side)
    {
        var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, true)
        {
            name = "RoadMedian_" + side,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        var pixels = new Color32[k_Size * k_Size];
        for (int py = 0; py < k_Size; py++)
        {
            for (int px = 0; px < k_Size; px++)
            {
                int d = side == RoadLayout.North ? py : side == RoadLayout.South ? k_Size - 1 - py
                    : side == RoadLayout.East ? k_Size - 1 - px : px;
                Color32 c = new Color32(0, 0, 0, 0);
                if (d < k_Width)
                {
                    int grain = ((px * 73856093) ^ (py * 19349663)) >> 4 & 7;
                    c = d < 3 ? new Color32(148, 150, 153, 255)
                        : d < 5 ? new Color32(51, 56, 51, 255)
                        : new Color32((byte)(70 + grain), (byte)(104 + grain * 2), (byte)(62 + grain), 255);
                }
                pixels[py * k_Size + px] = c;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    private void LateUpdate()
    {
        GridData grid = m_Game != null ? m_Game.Grid : null;
        if (grid == null) return;
        if (grid != m_Grid)
        {
            if (m_Grid != null) m_Grid.OnCellChanged -= OnCellChanged;
            m_Grid = grid;
            m_Grid.OnCellChanged += OnCellChanged;
            m_Dirty = true;
        }
        int count = grid.Width * grid.Height;
        if (m_Painted.Length != count)
        {
            m_Tilemap.ClearAllTiles();
            m_Painted = new byte[count];
            m_Dirty = true;
        }
        if (!m_Dirty) return;
        m_Dirty = false;

        for (int i = 0; i < count; i++)
        {
            var cell = new Vector2Int(i % grid.Width, i / grid.Width);
            byte side = grid.GetRoadPair(cell);
            if (side == m_Painted[i]) continue;
            m_Painted[i] = side;
            m_Tilemap.SetTile(m_GridSystem.LogicalToTileCell(cell), side == RoadLayout.None ? null : m_Tiles[side]);
        }
    }
}
