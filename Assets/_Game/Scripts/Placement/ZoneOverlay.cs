using UnityEngine;
using UnityEngine.Tilemaps;

// Paints a translucent tint on every zoned cell of the Zones Tilemap, mirroring GridData zones.
public sealed class ZoneOverlay : MonoBehaviour
{
    [SerializeField] private GameManager m_GameManager;
    [SerializeField] private GridSystem m_GridSystem;
    [SerializeField] private Tilemap m_Tilemap;
    [SerializeField] private Sprite m_Sprite;
    [SerializeField] private Color m_ResidentialColor = new Color(0.40f, 0.85f, 0.35f, 0.45f);
    [SerializeField] private Color m_CommercialColor = new Color(0.30f, 0.55f, 0.95f, 0.45f);
    [SerializeField] private Color m_IndustrialColor = new Color(0.95f, 0.80f, 0.25f, 0.45f);

    private GridData m_Grid;
    private Tile m_ResidentialTile;
    private Tile m_CommercialTile;
    private Tile m_IndustrialTile;

    // Start, not Awake: GameManager.Awake creates the GridData.
    private void Start()
    {
        if (m_GameManager == null || m_GameManager.Grid == null || m_Tilemap == null) return;

        m_ResidentialTile = CreateTile(m_ResidentialColor);
        m_CommercialTile = CreateTile(m_CommercialColor);
        m_IndustrialTile = CreateTile(m_IndustrialColor);

        m_Grid = m_GameManager.Grid;
        m_Grid.OnCellChanged += SyncCell;
    }

    private void OnDestroy()
    {
        if (m_Grid != null) m_Grid.OnCellChanged -= SyncCell;
        Destroy(m_ResidentialTile);
        Destroy(m_CommercialTile);
        Destroy(m_IndustrialTile);
    }

    private Tile CreateTile(Color color)
    {
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = m_Sprite;
        tile.color = color;
        return tile;
    }

    private void SyncCell(Vector2Int cell)
    {
        m_Tilemap.SetTile(m_GridSystem.LogicalToTileCell(cell), TileFor(m_Grid.GetZone(cell)));
    }

    private Tile TileFor(ZoneType zone)
    {
        switch (zone)
        {
            case ZoneType.Residential: return m_ResidentialTile;
            case ZoneType.Commercial: return m_CommercialTile;
            case ZoneType.Industrial: return m_IndustrialTile;
            default: return null;
        }
    }
}
