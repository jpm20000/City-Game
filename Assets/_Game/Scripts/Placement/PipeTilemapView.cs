using UnityEngine;
using UnityEngine.Tilemaps;

// Mirrors GridData water pipes (M13) onto the Pipes Tilemap (see GridTilemapView): a thin pipe
// auto-tiled towards neighbouring pipes and roads, blue where it carries water from a tower and grey
// where it doesn't. Pipes are underground, so the tilemap only shows in the Water view or while the
// pipe tool is held. Sprites are generated at runtime, like the roads'.
public sealed class PipeTilemapView : GridTilemapView
{
    private const int k_North = 1;  // +y
    private const int k_East = 2;   // +x
    private const int k_South = 4;  // -y
    private const int k_West = 8;   // -x

    private const int k_Size = 64;          // px per cell (matches ground_square)
    private const int k_HalfWidth = 7;      // pipe is 14 px wide
    private const int k_Rim = 2;            // darker rim on each side

    [SerializeField] private InfoOverlay m_InfoOverlay;
    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private TilemapRenderer m_Renderer;
    [SerializeField] private Color m_PipeColor = new Color(0.82f, 0.86f, 0.92f);
    [SerializeField] private Color m_RimColor = new Color(0.25f, 0.30f, 0.38f);
    [SerializeField] private Color m_CarryingTint = new Color(0.94f, 0.98f, 1f);
    [SerializeField] private Color m_DryTint = new Color(0.55f, 0.55f, 0.58f);

    private readonly Texture2D[] m_Textures = new Texture2D[16];
    private readonly Sprite[] m_Sprites = new Sprite[16];
    private readonly Tile[] m_CarryingTiles = new Tile[16];
    private readonly Tile[] m_DryTiles = new Tile[16];

    private void OnEnable()
    {
        GameEvents.WaterChanged += OnWaterChanged;
        if (m_InfoOverlay != null) m_InfoOverlay.ViewChanged += RefreshVisible;
        if (m_Placement != null) m_Placement.ModeChanged += RefreshVisible;
        RefreshVisible();
    }

    private void OnDisable()
    {
        GameEvents.WaterChanged -= OnWaterChanged;
        if (m_InfoOverlay != null) m_InfoOverlay.ViewChanged -= RefreshVisible;
        if (m_Placement != null) m_Placement.ModeChanged -= RefreshVisible;
    }

    // Whether pipes carry water depends on the towers, not only on the grid.
    private void OnWaterChanged(WaterStatus status) => MarkDirty();

    private void RefreshVisible()
    {
        if (m_Renderer == null) return;
        bool pipeTool = m_Placement != null && m_Placement.CurrentMode == PlacementController.Mode.Pipe;
        bool waterView = m_InfoOverlay != null && m_InfoOverlay.Shown == InfoOverlay.View.Water;
        m_Renderer.enabled = pipeTool || waterView;
    }

    protected override void CreateTiles()
    {
        for (int mask = 0; mask < 16; mask++)
        {
            m_Textures[mask] = DrawPipe(mask);
            m_Sprites[mask] = Sprite.Create(m_Textures[mask], new Rect(0, 0, k_Size, k_Size), new Vector2(0.5f, 0.5f), k_Size);
            m_CarryingTiles[mask] = CreateTile(m_Sprites[mask], m_CarryingTint);
            m_DryTiles[mask] = CreateTile(m_Sprites[mask], m_DryTint);
        }
        RefreshVisible();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        for (int i = 0; i < 16; i++)
        {
            if (m_CarryingTiles[i] != null) Destroy(m_CarryingTiles[i]);
            if (m_DryTiles[i] != null) Destroy(m_DryTiles[i]);
            if (m_Sprites[i] != null) Destroy(m_Sprites[i]);
            if (m_Textures[i] != null) Destroy(m_Textures[i]);
        }
    }

    protected override Tile TileFor(Vector2Int cell)
    {
        if (!Grid.IsPipe(cell)) return null;

        int mask = 0;
        if (Connects(cell + Vector2Int.up)) mask |= k_North;
        if (Connects(cell + Vector2Int.right)) mask |= k_East;
        if (Connects(cell + Vector2Int.down)) mask |= k_South;
        if (Connects(cell + Vector2Int.left)) mask |= k_West;

        bool carrying = GameManager.Simulation != null && GameManager.Simulation.Water.Network.IsCarrying(cell);
        return carrying ? m_CarryingTiles[mask] : m_DryTiles[mask];
    }

    private bool Connects(Vector2Int neighbor)
    {
        return Grid.InBounds(neighbor) && (Grid.IsPipe(neighbor) || Grid.IsRoad(neighbor));
    }

    // Texture pixel x = logical +x; pixel +y = logical south (see RoadTilemapView). Transparent
    // except for the pipe: a centre junction plus an arm towards each connection (a stub when none).
    private Texture2D DrawPipe(int mask)
    {
        Texture2D texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, true)
        {
            name = $"Pipe_{mask}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        Color32[] pixels = new Color32[k_Size * k_Size];
        const float center = (k_Size - 1) * 0.5f;
        int reach = mask == 0 ? k_Size / 4 : 0;   // an isolated pipe draws a short horizontal stub
        for (int py = 0; py < k_Size; py++)
        {
            for (int px = 0; px < k_Size; px++)
            {
                float dx = px - center;
                float dy = py - center;
                bool horizontal = Mathf.Abs(dy) <= k_HalfWidth
                    && ((dx <= k_HalfWidth && dx >= -k_HalfWidth)
                        || (dx > 0 && (mask & k_East) != 0) || (dx < 0 && (mask & k_West) != 0)
                        || Mathf.Abs(dx) <= reach);
                // pixel +y = logical south
                bool vertical = Mathf.Abs(dx) <= k_HalfWidth
                    && ((dy > 0 && (mask & k_South) != 0) || (dy < 0 && (mask & k_North) != 0));

                Color color = Color.clear;
                if (horizontal || vertical)
                {
                    bool rim = horizontal && !vertical ? Mathf.Abs(dy) > k_HalfWidth - k_Rim
                        : vertical && !horizontal ? Mathf.Abs(dx) > k_HalfWidth - k_Rim
                        : false;
                    color = rim ? m_RimColor : m_PipeColor;
                }
                pixels[py * k_Size + px] = color;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
