using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Data views on the Info Tilemap: Power (energised roads, powered / dark buildings and zones) and
// Coverage (how much park bonus each cell gets). The ground tint is mostly hidden under grown
// buildings, so those are recoloured through GrowthVisuals too. V cycles Off -> Power -> Coverage.
// Holding the Power Plant or Park tool switches to its view and previews the building under the
// cursor: what would be powered / covered if it were placed there.
public sealed class InfoOverlay : GridTilemapView
{
    public enum View { Off, Power, Coverage }

    [SerializeField] private Sprite m_Sprite;
    [SerializeField] private InputReader m_InputReader;
    [SerializeField] private PlacementController m_Placement;
    [SerializeField] private GrowthVisuals m_GrowthVisuals;
    [Tooltip("Hidden while a view is shown so zone tints don't mix with the view's colours.")]
    [SerializeField] private Renderer m_ZoneRenderer;

    [Header("Power")]
    [SerializeField] private Color m_EnergisedRoad = new Color(1f, 0.82f, 0.20f, 0.80f);
    [SerializeField] private Color m_Powered = new Color(0.35f, 0.85f, 0.40f, 0.60f);
    [SerializeField] private Color m_Unpowered = new Color(0.95f, 0.30f, 0.25f, 0.60f);
    [SerializeField, Range(0f, 1f)] private float m_UndevelopedAlpha = 0.28f;

    [Header("Coverage")]
    [SerializeField] private Color m_Covered = new Color(0.35f, 0.85f, 0.40f, 0.65f);
    [SerializeField] private Color m_NewlyCovered = new Color(0.30f, 0.75f, 1f, 0.60f);
    [SerializeField] private Color m_Uncovered = new Color(0.66f, 0.66f, 0.70f, 1f);
    [Tooltip("Non-residential buildings in the coverage view (coverage only helps homes).")]
    [SerializeField] private Color m_NotAHome = new Color(0.34f, 0.35f, 0.39f, 1f);
    [SerializeField, Range(0f, 1f)] private float m_MinCoverageAlpha = 0.18f;
    [Tooltip("How green a home covered by a single service is (1 = fully green at the cap).")]
    [SerializeField, Range(0f, 1f)] private float m_MinCoveredTint = 0.45f;

    private View m_Chosen;
    private View m_Shown;
    private bool m_Previewing;
    private PowerSystem m_PreviewPower;
    private CoverageSystem m_PreviewCoverage;
    private readonly List<ServiceSource> m_PreviewSources = new();
    private Func<Vector2Int, Color?> m_BuildingColor;
    private bool m_BuildingsTinted;

    private Tile m_EnergisedTile;
    private Tile m_PoweredTile;
    private Tile m_UnpoweredTile;
    private Tile m_PowerReachableTile;
    private Tile m_PowerUnreachableTile;
    private Tile m_NewlyCoveredTile;
    private Tile[] m_CoverageTiles;   // [count], 0 = none

    // The view the player picked (V / toolbar); Shown can differ while a Plant or Park tool is held.
    public View Chosen => m_Chosen;
    public View Shown => m_Shown;
    public event Action ViewChanged;

    private SimulationSystem Simulation => GameManager.Simulation;
    private PowerSystem Power => m_Previewing ? m_PreviewPower : Simulation.Power;
    private CoverageSystem Coverage => m_Previewing ? m_PreviewCoverage : Simulation.Coverage;

    public void SetView(View view)
    {
        m_Chosen = view;
        Refresh();
    }

    public void Cycle()
    {
        SetView(m_Chosen == View.Coverage ? View.Off : m_Chosen + 1);
    }

    private void OnEnable()
    {
        if (m_Placement == null) return;
        m_Placement.ModeChanged += Refresh;
        m_Placement.PreviewChanged += Refresh;
    }

    private void OnDisable()
    {
        if (m_Placement == null) return;
        m_Placement.ModeChanged -= Refresh;
        m_Placement.PreviewChanged -= Refresh;
    }

    private void Update()
    {
        if (m_InputReader != null && Grid != null && m_InputReader.CycleOverlayPressed) Cycle();
    }

    // Works out what to show (tool view overrides the chosen one) and sets up the what-if preview.
    private void Refresh()
    {
        if (Grid == null) return;

        BuildingDefinition tool = m_Placement != null && m_Placement.CurrentMode == PlacementController.Mode.Building
            ? m_Placement.SelectedBuilding
            : null;
        View toolView = tool == null ? View.Off
            : tool.PowerSupply > 0 ? View.Power
            : tool.CoverageRadius > 0 ? View.Coverage
            : View.Off;
        View shown = toolView != View.Off ? toolView : m_Chosen;

        m_Previewing = toolView != View.Off && m_Placement.HasBuildingPreview;
        if (m_Previewing)
        {
            m_PreviewSources.Clear();
            m_PreviewSources.AddRange(Simulation.Sources);
            m_PreviewSources.Add(new ServiceSource(m_Placement.PreviewOrigin,
                CellUtils.EffectiveSize(tool.Size, m_Placement.PreviewRotation), tool.CoverageRadius, tool.PowerSupply));
            m_PreviewPower.SetSources(m_PreviewSources);
            m_PreviewCoverage.Recompute(m_PreviewSources);
        }

        if (shown != m_Shown)
        {
            m_Shown = shown;
            if (m_ZoneRenderer != null) m_ZoneRenderer.enabled = shown == View.Off;
            ViewChanged?.Invoke();
        }
        MarkDirty();
    }

    protected override void CreateTiles()
    {
        m_PreviewPower = new PowerSystem(Grid, GameManager.Balance);
        m_PreviewCoverage = new CoverageSystem(Grid.Width, Grid.Height);
        m_BuildingColor = BuildingColor;

        m_EnergisedTile = CreateTile(m_Sprite, m_EnergisedRoad);
        m_PoweredTile = CreateTile(m_Sprite, m_Powered);
        m_UnpoweredTile = CreateTile(m_Sprite, m_Unpowered);
        m_PowerReachableTile = CreateTile(m_Sprite, WithAlpha(m_Powered, m_UndevelopedAlpha));
        m_PowerUnreachableTile = CreateTile(m_Sprite, WithAlpha(m_Unpowered, m_UndevelopedAlpha));
        m_NewlyCoveredTile = CreateTile(m_Sprite, m_NewlyCovered);

        BalanceConfig balance = GameManager.Balance;
        int levels = Mathf.Max(1, Mathf.CeilToInt(balance.ServiceBonusCap / Mathf.Max(balance.ServiceBonusEach, 0.0001f)));
        m_CoverageTiles = new Tile[levels + 1];
        for (int count = 1; count <= levels; count++)
        {
            float alpha = Mathf.Lerp(m_MinCoverageAlpha, m_Covered.a, (float)count / levels);
            m_CoverageTiles[count] = CreateTile(m_Sprite, WithAlpha(m_Covered, alpha));
        }

        Refresh();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (m_GrowthVisuals != null && m_BuildingsTinted) m_GrowthVisuals.SetColorOverride(null);
        foreach (Tile tile in new[] { m_EnergisedTile, m_PoweredTile, m_UnpoweredTile, m_PowerReachableTile, m_PowerUnreachableTile, m_NewlyCoveredTile })
        {
            if (tile != null) Destroy(tile);
        }
        if (m_CoverageTiles != null) foreach (Tile tile in m_CoverageTiles) if (tile != null) Destroy(tile);
    }

    protected override Tile TileFor(Vector2Int cell)
    {
        switch (m_Shown)
        {
            case View.Power: return PowerTile(cell);
            case View.Coverage: return CoverageTile(cell);
            default: return null;
        }
    }

    protected override void OnRepainted()
    {
        if (m_GrowthVisuals == null) return;

        if (m_Shown != View.Off)
        {
            m_GrowthVisuals.SetColorOverride(m_BuildingColor);
            m_BuildingsTinted = true;
        }
        else if (m_BuildingsTinted)
        {
            m_GrowthVisuals.SetColorOverride(null);
            m_BuildingsTinted = false;
        }
    }

    private Tile PowerTile(Vector2Int cell)
    {
        PowerSystem power = Power;
        if (Grid.IsRoad(cell)) return power.IsEnergisedRoad(cell) ? m_EnergisedTile : null;
        if (Grid.GetZone(cell) == ZoneType.None) return null;
        if (Grid.GetBuildingLevel(cell) > 0) return power.IsPowered(cell) ? m_PoweredTile : m_UnpoweredTile;
        return BesideEnergisedRoad(cell, power) ? m_PowerReachableTile : m_PowerUnreachableTile;
    }

    private Tile CoverageTile(Vector2Int cell)
    {
        if (Grid.IsRoad(cell)) return null;
        int count = Coverage.GetCoverage(cell);
        if (m_Previewing && count > Simulation.Coverage.GetCoverage(cell)) return m_NewlyCoveredTile;
        return m_CoverageTiles[Mathf.Min(count, m_CoverageTiles.Length - 1)];
    }

    // Grown buildings: green / red for power; homes shaded by their service bonus for coverage.
    private Color? BuildingColor(Vector2Int cell)
    {
        if (m_Shown == View.Power)
        {
            return WithAlpha(Power.IsPowered(cell) ? m_Powered : m_Unpowered, 1f);
        }
        if (m_Shown == View.Coverage)
        {
            if (Grid.GetZone(cell) != ZoneType.Residential) return m_NotAHome;   // coverage only helps homes
            int count = Coverage.GetCoverage(cell);
            if (m_Previewing && count > Simulation.Coverage.GetCoverage(cell)) return WithAlpha(m_NewlyCovered, 1f);
            if (count == 0) return m_Uncovered;
            float t = (float)Mathf.Min(count, m_CoverageTiles.Length - 1) / (m_CoverageTiles.Length - 1);
            return Color.Lerp(m_Uncovered, WithAlpha(m_Covered, 1f), Mathf.Lerp(m_MinCoveredTint, 1f, t));
        }
        return null;
    }

    private bool BesideEnergisedRoad(Vector2Int cell, PowerSystem power)
    {
        foreach (Vector2Int offset in CellUtils.Neighbors4)
        {
            if (power.IsEnergisedRoad(cell + offset)) return true;
        }
        return false;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }
}
