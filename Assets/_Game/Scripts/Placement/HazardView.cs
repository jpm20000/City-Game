using UnityEngine;
using UnityEngine.Tilemaps;

// Draws the ground state of the M17 hazards on a Tilemap of its own (a child of the Grid, created at runtime beside
// Roads so it shares their material): rubble where a fire destroyed a building, a sickly yellow wash under infected homes and an
// orange glow under burning blocks. The state is read from DisasterSystem's byte layers and diffed against a painted
// cache every frame (a 96x96 map is ~10k byte compares), so only changed cells touch the Tilemap.
public sealed class HazardView : MonoBehaviour
{
    private const byte None = 0, Rubble = 1, Plague = 2, Fire = 3;

    private GameManager m_Game;
    private GridSystem m_GridSystem;
    private Tilemap m_Tilemap;
    private Tile m_RubbleTile;
    private Tile m_PlagueTile;
    private Tile m_FireTile;
    private Texture2D m_RubbleTexture;
    private Texture2D m_FlatTexture;
    private Sprite m_RubbleSprite;
    private Sprite m_FlatSprite;
    private byte[] m_Painted = new byte[0];

    public void Init(GameManager game, GridSystem gridSystem, Tilemap roads)
    {
        m_Game = game;
        m_GridSystem = gridSystem;

        // Beside Roads: same parent, layer and material; one order above the zone overlay so rubble shows on zoned land.
        var go = new GameObject("Hazards", typeof(Tilemap), typeof(TilemapRenderer));
        go.layer = roads.gameObject.layer;
        go.transform.SetParent(roads.transform.parent, false);
        m_Tilemap = go.GetComponent<Tilemap>();
        var renderer = go.GetComponent<TilemapRenderer>();
        var roadRenderer = roads.GetComponent<TilemapRenderer>();
        renderer.sharedMaterial = roadRenderer.sharedMaterial;
        renderer.sortingLayerID = roadRenderer.sortingLayerID;
        renderer.sortingOrder = roadRenderer.sortingOrder + 1;

        CreateTiles();
        GameEvents.WorldResized += OnWorldResized;
    }

    private void OnDestroy()
    {
        GameEvents.WorldResized -= OnWorldResized;
        if (m_RubbleSprite != null) Destroy(m_RubbleSprite);
        if (m_FlatSprite != null) Destroy(m_FlatSprite);
        if (m_RubbleTexture != null) Destroy(m_RubbleTexture);
        if (m_FlatTexture != null) Destroy(m_FlatTexture);
        if (m_RubbleTile != null) Destroy(m_RubbleTile);
        if (m_PlagueTile != null) Destroy(m_PlagueTile);
        if (m_FireTile != null) Destroy(m_FireTile);
    }

    private void CreateTiles()
    {
        const int size = 16;
        m_RubbleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "RubbleTexture" };
        m_FlatTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "HazardFlatTexture" };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // A deterministic speckle: darker and lighter flecks on a grey-brown ground.
                int h = (x * 73856093) ^ (y * 19349663);
                float shade = (h & 7) == 0 ? 0.55f : (h & 7) == 1 ? 1.25f : 1f;
                m_RubbleTexture.SetPixel(x, y, new Color(0.46f * shade, 0.40f * shade, 0.34f * shade, 0.92f));
                m_FlatTexture.SetPixel(x, y, Color.white);
            }
        }
        m_RubbleTexture.Apply();
        m_FlatTexture.Apply();
        m_RubbleSprite = Sprite.Create(m_RubbleTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        m_FlatSprite = Sprite.Create(m_FlatTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);

        m_RubbleTile = MakeTile(m_RubbleSprite, Color.white);
        m_PlagueTile = MakeTile(m_FlatSprite, new Color(0.85f, 0.80f, 0.10f, 0.65f));
        m_FireTile = MakeTile(m_FlatSprite, new Color(1f, 0.45f, 0.08f, 0.55f));
    }

    private static Tile MakeTile(Sprite sprite, Color color)
    {
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.color = color;
        tile.colliderType = Tile.ColliderType.None;
        return tile;
    }

    private void OnWorldResized(Vector2Int size)
    {
        m_Tilemap.ClearAllTiles();
        m_Painted = new byte[0];
    }

    private void LateUpdate()
    {
        SimulationSystem sim = m_Game.Simulation;
        if (sim == null || m_Game.Grid == null) return;

        DisasterSystem d = sim.Disasters;
        GridData grid = m_Game.Grid;
        int count = grid.Width * grid.Height;
        if (m_Painted.Length != count)
        {
            m_Tilemap.ClearAllTiles();
            m_Painted = new byte[count];
        }

        byte[] fires = d.Fires;
        byte[] rubble = d.Rubble;
        byte[] plague = d.Plague;
        if (fires.Length != count) return;       // mid-resize

        for (int i = 0; i < count; i++)
        {
            byte state = fires[i] > 0 ? Fire
                : plague[i] != 0 && plague[i] != DisasterSystem.PlagueRecovered ? Plague
                : rubble[i] > 0 ? Rubble : None;
            if (state == m_Painted[i]) continue;

            m_Painted[i] = state;
            Vector3Int tileCell = m_GridSystem.LogicalToTileCell(new Vector2Int(i % grid.Width, i / grid.Width));
            m_Tilemap.SetTile(tileCell, state == Fire ? m_FireTile : state == Plague ? m_PlagueTile : state == Rubble ? m_RubbleTile : null);
        }
    }
}
