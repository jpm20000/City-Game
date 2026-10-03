using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Data views on the Info Tilemap: Power (energised roads, powered / dark buildings and zones),
// Coverage (how much park bonus each cell gets), Pollution and Land value (M12: pollution reaching
// each cell, polluters dark; land value red -> green, homes and shops held at level 2 by it striped)
// and Age (M11: the age each grown cell was built in, warm = old to cool = new; kept historic cells
// gold; outdated cells striped and darkened). The ground tint is mostly hidden under grown
// buildings, so those are recoloured through GrowthVisuals too. V cycles Off -> Power -> Coverage
// -> Pollution -> Land value -> Age, skipping views that aren't available yet (Power until a power
// source is unlocked, Age without age data).
// Holding the Power Plant or Park tool switches to its view and previews the building under the
// cursor: what would be powered / covered if it were placed there.
public sealed class InfoOverlay : GridTilemapView
{
    public enum View { Off, Power, Coverage, Pollution, LandValue, Age }

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

    [Header("Pollution")]
    [SerializeField] private Color m_PollutionLow = new Color(0.86f, 0.78f, 0.36f, 0.30f);
    [SerializeField] private Color m_PollutionHigh = new Color(0.42f, 0.18f, 0.40f, 0.80f);
    [Tooltip("Pollution points drawn at full strength (homes pay the full penalty from about 4).")]
    [SerializeField] private float m_PollutionFull = 6f;
    [SerializeField] private Color m_CleanBuilding = new Color(0.72f, 0.74f, 0.78f, 1f);
    [Tooltip("Polluters (industry, plants) in the pollution view.")]
    [SerializeField] private Color m_Polluter = new Color(0.30f, 0.22f, 0.20f, 1f);

    [Header("Land value")]
    [SerializeField] private Color m_LandValueLow = new Color(0.90f, 0.30f, 0.25f, 0.60f);
    [SerializeField] private Color m_LandValueMid = new Color(0.95f, 0.80f, 0.30f, 0.55f);
    [SerializeField] private Color m_LandValueHigh = new Color(0.30f, 0.80f, 0.40f, 0.65f);
    [Tooltip("Industry in the land value view (it doesn't need land value).")]
    [SerializeField] private Color m_NotGated = new Color(0.34f, 0.35f, 0.39f, 1f);

    [Header("Age")]
    [SerializeField] private Color m_OldestAge = new Color(0.90f, 0.52f, 0.22f, 0.65f);
    [SerializeField] private Color m_NewestAge = new Color(0.32f, 0.60f, 0.96f, 0.65f);
    [SerializeField] private Color m_Historic = new Color(0.95f, 0.80f, 0.30f, 0.80f);
    [Tooltip("How much darker outdated buildings are drawn (their ground tile is striped).")]
    [SerializeField, Range(0f, 1f)] private float m_OutdatedShade = 0.6f;

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
    private Tile[] m_AgeTiles;        // [built age]
    private Tile[] m_OutdatedTiles;   // [built age], striped
    private Tile m_HistoricTile;
    private Tile[] m_PollutionTiles;  // [bucket], 0 = none
    private Tile[] m_LandValueTiles;  // [bucket]
    private Tile[] m_HeldTiles;       // [bucket], striped: held at level 2 by land value
    private Sprite m_StripeSprite;

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
        View next = m_Chosen;
        do next = next == View.Age ? View.Off : next + 1;   // Age is last
        while (next != View.Off && !IsAvailable(next));
        SetView(next);
    }

    // Power appears once a power source is unlocked; Age only with age data.
    public bool IsAvailable(View view)
    {
        if (GameManager == null || GameManager.Simulation == null) return view != View.Age;
        switch (view)
        {
            case View.Power: return GameManager.PowerUnlocked;
            case View.Age: return GameManager.Simulation.Tech != null;
            default: return true;
        }
    }

    private void OnEnable()
    {
        GameEvents.CityLoaded += Refresh;
        GameEvents.TechCompleted += OnTechCompleted;
        GameEvents.AgeChanged += OnAgeChanged;
        if (m_Placement == null) return;
        m_Placement.ModeChanged += Refresh;
        m_Placement.PreviewChanged += Refresh;
    }

    private void OnDisable()
    {
        GameEvents.CityLoaded -= Refresh;
        GameEvents.TechCompleted -= OnTechCompleted;
        GameEvents.AgeChanged -= OnAgeChanged;
        if (m_Placement == null) return;
        m_Placement.ModeChanged -= Refresh;
        m_Placement.PreviewChanged -= Refresh;
    }

    private void OnTechCompleted(string techId) => Refresh();

    // Outdated cells depend on the current age.
    private void OnAgeChanged(int age) => Refresh();

    private void Update()
    {
        if (m_InputReader != null && Grid != null && m_InputReader.CycleOverlayPressed) Cycle();
    }

    // Works out what to show (tool view overrides the chosen one) and sets up the what-if preview.
    private void Refresh()
    {
        if (Grid == null) return;
        if (!IsAvailable(m_Chosen)) m_Chosen = View.Off;   // e.g. loaded a city from before power

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
        m_PreviewPower = new PowerSystem(Grid, GameManager.Balance, GameManager.Simulation?.Capacity);
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

        int ages = GameManager.Ages != null ? GameManager.Ages.Count : 1;
        m_StripeSprite = CreateStripeSprite(m_Sprite, "OutdatedStripes");
        m_AgeTiles = new Tile[ages];
        m_OutdatedTiles = new Tile[ages];
        for (int age = 0; age < ages; age++)
        {
            m_AgeTiles[age] = CreateTile(m_Sprite, AgeColor(age));
            m_OutdatedTiles[age] = CreateTile(m_StripeSprite, WithAlpha(AgeColor(age), 0.9f));
        }
        m_HistoricTile = CreateTile(m_Sprite, m_Historic);

        m_PollutionTiles = new Tile[Buckets + 1];
        for (int i = 1; i <= Buckets; i++) m_PollutionTiles[i] = CreateTile(m_Sprite, PollutionColor((float)i / Buckets));
        m_LandValueTiles = new Tile[Buckets + 1];
        m_HeldTiles = new Tile[Buckets + 1];
        for (int i = 0; i <= Buckets; i++)
        {
            Color color = LandValueColor((float)i / Buckets);
            m_LandValueTiles[i] = CreateTile(m_Sprite, color);
            m_HeldTiles[i] = CreateTile(m_StripeSprite, WithAlpha(color, 0.9f));
        }

        Refresh();
    }

    protected override void OnGridResized()
    {
        m_PreviewCoverage.Resize(Grid.Width, Grid.Height);
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
        if (m_AgeTiles != null) foreach (Tile tile in m_AgeTiles) if (tile != null) Destroy(tile);
        if (m_OutdatedTiles != null) foreach (Tile tile in m_OutdatedTiles) if (tile != null) Destroy(tile);
        if (m_HistoricTile != null) Destroy(m_HistoricTile);
        foreach (Tile[] tiles in new[] { m_PollutionTiles, m_LandValueTiles, m_HeldTiles })
        {
            if (tiles != null) foreach (Tile tile in tiles) if (tile != null) Destroy(tile);
        }
        DestroyStripeSprite(m_StripeSprite);
    }

    protected override Tile TileFor(Vector2Int cell)
    {
        switch (m_Shown)
        {
            case View.Power: return PowerTile(cell);
            case View.Coverage: return CoverageTile(cell);
            case View.Age: return AgeTile(cell);
            case View.Pollution: return PollutionTile(cell);
            case View.LandValue: return LandValueTile(cell);
            default: return null;
        }
    }

    private const int Buckets = 20;

    private static int Bucket(float t) => Mathf.Clamp(Mathf.RoundToInt(t * Buckets), 0, Buckets);

    private float PollutionShare(Vector2Int cell) => Mathf.Clamp01(Simulation.Pollution.GetPollution(cell) / Mathf.Max(m_PollutionFull, 0.01f));

    private Color PollutionColor(float t) => Color.Lerp(m_PollutionLow, m_PollutionHigh, t);

    // Centred on the level-3 threshold: red well below it, yellow at it, green well above.
    private Color LandValueColor(float value)
    {
        float gate = GameManager.Balance.LandValueForLevel3;
        return value < gate
            ? Color.Lerp(m_LandValueLow, m_LandValueMid, Mathf.InverseLerp(gate - 0.3f, gate, value))
            : Color.Lerp(m_LandValueMid, m_LandValueHigh, Mathf.InverseLerp(gate, gate + 0.35f, value));
    }

    private Tile PollutionTile(Vector2Int cell)
    {
        if (Grid.IsRoad(cell)) return null;
        float points = Simulation.Pollution.GetPollution(cell);
        if (points <= 0.01f) return null;
        return m_PollutionTiles[Mathf.Max(1, Bucket(PollutionShare(cell)))];
    }

    private Tile LandValueTile(Vector2Int cell)
    {
        if (Grid.IsRoad(cell)) return null;
        // Rounded down so a cell just below the threshold never shows the threshold's colour.
        int bucket = Mathf.Clamp(Mathf.FloorToInt(Simulation.LandValue.GetLandValue(cell) * Buckets + 1e-4f), 0, Buckets);
        return Simulation.Growth.IsHeldByLandValue(cell) ? m_HeldTiles[bucket] : m_LandValueTiles[bucket];
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

    private Tile AgeTile(Vector2Int cell)
    {
        if (Grid.GetBuildingLevel(cell) == 0) return null;
        if (Grid.IsHistoric(cell)) return m_HistoricTile;
        int age = Mathf.Clamp(Grid.GetBuiltAge(cell), 0, m_AgeTiles.Length - 1);
        return Simulation.Growth.IsOutdated(cell) ? m_OutdatedTiles[age] : m_AgeTiles[age];
    }

    // Oldest age warm, newest cool.
    private Color AgeColor(int age)
    {
        int last = m_AgeTiles != null ? m_AgeTiles.Length - 1 : 0;
        return Color.Lerp(m_OldestAge, m_NewestAge, last > 0 ? (float)age / last : 1f);
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
        if (m_Shown == View.Pollution)
        {
            if (Simulation.Pollution.EmissionOf(cell) > 0f) return m_Polluter;
            return Color.Lerp(m_CleanBuilding, WithAlpha(m_PollutionHigh, 1f), PollutionShare(cell));
        }
        if (m_Shown == View.LandValue)
        {
            ZoneType zone = Grid.GetZone(cell);
            if (zone != ZoneType.Residential && zone != ZoneType.Commercial) return m_NotGated;
            Color color = WithAlpha(LandValueColor(Simulation.LandValue.GetLandValue(cell)), 1f);
            return Simulation.Growth.IsHeldByLandValue(cell) ? WithAlpha(color * m_OutdatedShade, 1f) : color;
        }
        if (m_Shown == View.Age)
        {
            if (Grid.IsHistoric(cell)) return WithAlpha(m_Historic, 1f);
            Color color = WithAlpha(AgeColor(Mathf.Clamp(Grid.GetBuiltAge(cell), 0, m_AgeTiles.Length - 1)), 1f);
            return Simulation.Growth.IsOutdated(cell) ? WithAlpha(color * m_OutdatedShade, 1f) : color;
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
