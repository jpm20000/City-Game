using UnityEngine;
using UnityEngine.Tilemaps;

// One-way highway arrows (M22): a white arrow over the centre of every highway cell that has a direction, on a tilemap of
// its own above the Roads (made at runtime beside HazardView, so the road tile art is untouched). Presentation only: it
// reads GridData and never writes it.
public sealed class RoadArrowView : MonoBehaviour
{
    private const int k_Size = 64;

    private GameManager m_Game;
    private GridSystem m_GridSystem;
    private Tilemap m_Tilemap;
    private readonly Tile[] m_Tiles = new Tile[5];          // by RoadLayout code 1..4
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
        var view = new GameObject("RoadArrows").AddComponent<RoadArrowView>();
        view.Init(game, gridSystem, roads);
    }

    private void Init(GameManager game, GridSystem gridSystem, Tilemap roads)
    {
        m_Game = game;
        m_GridSystem = gridSystem;
        var go = new GameObject("Arrows", typeof(Tilemap), typeof(TilemapRenderer));
        go.layer = roads.gameObject.layer;
        go.transform.SetParent(roads.transform.parent, false);
        m_Tilemap = go.GetComponent<Tilemap>();
        var renderer = go.GetComponent<TilemapRenderer>();
        var roadRenderer = roads.GetComponent<TilemapRenderer>();
        renderer.sharedMaterial = roadRenderer.sharedMaterial;
        renderer.sortingLayerID = roadRenderer.sortingLayerID;
        renderer.sortingOrder = roadRenderer.sortingOrder + 1;
        transform.SetParent(go.transform.parent, false);

        for (byte code = RoadLayout.North; code <= RoadLayout.West; code++)
        {
            m_Textures[code] = DrawArrow(code);
            m_Sprites[code] = Sprite.Create(m_Textures[code], new Rect(0, 0, k_Size, k_Size), new Vector2(0.5f, 0.5f), k_Size);
            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = m_Sprites[code];
            tile.colliderType = Tile.ColliderType.None;
            m_Tiles[code] = tile;
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

    // An arrow along the code's heading: a filled triangle with a short shaft. Texture +x = logical +x, texture +y =
    // logical south (see RoadTilemapView), so the heading's y flips.
    private static Texture2D DrawArrow(byte code)
    {
        var texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, true)
        {
            name = "RoadArrow_" + code,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Vector2Int heading = RoadLayout.Offset(code);
        float fx = heading.x, fy = -heading.y;
        var pixels = new Color32[k_Size * k_Size];
        for (int py = 0; py < k_Size; py++)
        {
            for (int px = 0; px < k_Size; px++)
            {
                float u = px - k_Size * 0.5f + 0.5f, v = py - k_Size * 0.5f + 0.5f;
                float along = u * fx + v * fy;
                float across = Mathf.Abs(-u * fy + v * fx);
                bool head = along >= -2f && along <= 13f && across <= (13f - along) * 0.62f;
                bool shaft = along >= -12f && along < -2f && across <= 2.5f;
                pixels[py * k_Size + px] = head || shaft ? new Color32(245, 245, 240, 235) : new Color32(0, 0, 0, 0);
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
            byte code = grid.GetRoadDirection(cell);
            if (code == m_Painted[i]) continue;
            m_Painted[i] = code;
            m_Tilemap.SetTile(m_GridSystem.LogicalToTileCell(cell), code == RoadLayout.None ? null : m_Tiles[code]);
        }
    }
}
