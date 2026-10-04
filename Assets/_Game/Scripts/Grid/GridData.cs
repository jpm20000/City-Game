using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GridData
{
    public int Width { get; private set; }
    public int Height { get; private set; }

    private ZoneType[] m_Zones;
    private byte[] m_RoadTiers;     // 0 = no road, 1..MaxRoadTier = the street tier (M16)
    private int[] m_Occupancy;
    private byte[] m_BuildingLevel;
    private byte[] m_BuiltAge;      // age index a grown cell was (re)built in (M11)
    private bool[] m_Historic;      // "Keep historical building" (M11)
    private bool[] m_Pipes;         // water pipe under the cell (M13); never under a road (roads carry water anyway)

    public event Action<Vector2Int> OnCellChanged;

    // Raised by Resize after the map is replaced by an empty one. Anything caching per-cell state must
    // rebuild it; no OnCellChanged is raised for the cleared cells.
    public event Action OnResized;

    public GridData(int width, int height)
    {
        Allocate(width, height);
    }

    // Replaces the map with an empty one of the given size (new city / load). Release placed
    // buildings first: occupancy is cleared too.
    public void Resize(int width, int height)
    {
        Allocate(width, height);
        OnResized?.Invoke();
    }

    private void Allocate(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), $"Map size {width}x{height} must be positive.");

        Width = width;
        Height = height;
        m_Zones = new ZoneType[width * height];
        m_RoadTiers = new byte[width * height];
        m_Occupancy = new int[width * height];
        m_BuildingLevel = new byte[width * height];
        m_BuiltAge = new byte[width * height];
        m_Historic = new bool[width * height];
        m_Pipes = new bool[width * height];
    }

    public bool InBounds(Vector2Int cell)
    {
        return CellUtils.IsInBounds(cell, new Vector2Int(Width, Height));
    }

    public ZoneType GetZone(Vector2Int cell)
    {
        return m_Zones[Index(cell)];
    }

    public void SetZone(Vector2Int cell, ZoneType zone)
    {
        int i = Index(cell);
        if (m_Zones[i] == zone) return;
        m_Zones[i] = zone;
        OnCellChanged?.Invoke(cell);
    }

    public const int MaxRoadTier = 5;

    // The tier SetRoad(cell, true) lays: 3 = Paved, today's road (M16).
    public byte DefaultRoadTier { get; set; } = 3;

    public bool IsRoad(Vector2Int cell)
    {
        return m_RoadTiers[Index(cell)] != 0;
    }

    // 0 = no road, else the road's tier 1..MaxRoadTier.
    public byte GetRoadTier(Vector2Int cell)
    {
        return m_RoadTiers[Index(cell)];
    }

    // Tier 0 removes the road; a tier > 0 lays or changes it (a road laid over a pipe replaces it: the
    // road carries the water). Raises OnCellChanged on any change.
    public void SetRoadTier(Vector2Int cell, byte tier)
    {
        if (tier > MaxRoadTier) tier = MaxRoadTier;
        int i = Index(cell);
        if (m_RoadTiers[i] == tier) return;
        m_RoadTiers[i] = tier;
        if (tier > 0) m_Pipes[i] = false;
        OnCellChanged?.Invoke(cell);
    }

    // Lays DefaultRoadTier (an existing road keeps its tier) or removes the road.
    public void SetRoad(Vector2Int cell, bool isRoad)
    {
        if (isRoad)
        {
            if (!IsRoad(cell)) SetRoadTier(cell, DefaultRoadTier);
        }
        else SetRoadTier(cell, 0);
    }

    public bool IsPipe(Vector2Int cell)
    {
        return m_Pipes[Index(cell)];
    }

    // Pipes lie under anything but roads; setting one on a road is ignored.
    public void SetPipe(Vector2Int cell, bool isPipe)
    {
        int i = Index(cell);
        if (isPipe && m_RoadTiers[i] != 0) return;
        if (m_Pipes[i] == isPipe) return;
        m_Pipes[i] = isPipe;
        OnCellChanged?.Invoke(cell);
    }

    public int CountPipes()
    {
        int count = 0;
        for (int i = 0; i < m_Pipes.Length; i++)
        {
            if (m_Pipes[i]) count++;
        }
        return count;
    }

    public byte GetBuildingLevel(Vector2Int cell)
    {
        return m_BuildingLevel[Index(cell)];
    }

    // Level 0 (demolish, rezone) also clears the built age and the historic flag.
    public void SetBuildingLevel(Vector2Int cell, byte level)
    {
        if (level > 3) level = 3;
        int i = Index(cell);
        if (m_BuildingLevel[i] == level) return;
        m_BuildingLevel[i] = level;
        if (level == 0)
        {
            m_BuiltAge[i] = 0;
            m_Historic[i] = false;
        }
        OnCellChanged?.Invoke(cell);
    }

    public byte GetBuiltAge(Vector2Int cell)
    {
        return m_BuiltAge[Index(cell)];
    }

    public void SetBuiltAge(Vector2Int cell, byte age)
    {
        int i = Index(cell);
        if (m_BuiltAge[i] == age) return;
        m_BuiltAge[i] = age;
        OnCellChanged?.Invoke(cell);
    }

    public bool IsHistoric(Vector2Int cell)
    {
        return m_Historic[Index(cell)];
    }

    // Only grown cells can be kept; setting it on an undeveloped cell is ignored.
    public void SetHistoric(Vector2Int cell, bool historic)
    {
        int i = Index(cell);
        if (historic && m_BuildingLevel[i] == 0) return;
        if (m_Historic[i] == historic) return;
        m_Historic[i] = historic;
        OnCellChanged?.Invoke(cell);
    }

    public int CountRoads()
    {
        int count = 0;
        for (int i = 0; i < m_RoadTiers.Length; i++)
        {
            if (m_RoadTiers[i] != 0) count++;
        }
        return count;
    }

    // Fills counts[tier] with the number of roads of each tier (index 0 stays 0).
    public void CountRoadsByTier(int[] counts)
    {
        Array.Clear(counts, 0, counts.Length);
        for (int i = 0; i < m_RoadTiers.Length; i++)
        {
            int tier = m_RoadTiers[i];
            if (tier != 0 && tier < counts.Length) counts[tier]++;
        }
    }

    public bool IsOccupied(Vector2Int cell)
    {
        return m_Occupancy[Index(cell)] != 0;
    }

    public int GetOccupant(Vector2Int cell)
    {
        return m_Occupancy[Index(cell)];
    }

    public bool CanPlace(Vector2Int origin, Vector2Int size, int rotation)
    {
        foreach (Vector2Int cell in CellUtils.GetFootprint(origin, size, rotation))
        {
            if (!InBounds(cell) || IsRoad(cell) || IsOccupied(cell) || GetBuildingLevel(cell) > 0)
            {
                return false;
            }
        }
        return true;
    }

    public bool Occupy(Vector2Int origin, Vector2Int size, int rotation, int occupantId)
    {
        if (occupantId == 0) return false;
        if (!CanPlace(origin, size, rotation)) return false;

        foreach (Vector2Int cell in CellUtils.GetFootprint(origin, size, rotation))
        {
            m_Occupancy[Index(cell)] = occupantId;
            OnCellChanged?.Invoke(cell);
        }
        return true;
    }

    public void Release(Vector2Int origin, Vector2Int size, int rotation)
    {
        foreach (Vector2Int cell in CellUtils.GetFootprint(origin, size, rotation))
        {
            if (!InBounds(cell)) continue;
            m_Occupancy[Index(cell)] = 0;
            OnCellChanged?.Invoke(cell);
        }
    }

    public IEnumerable<Vector2Int> GetFootprint(Vector2Int origin, Vector2Int size, int rotation)
    {
        return CellUtils.GetFootprint(origin, size, rotation);
    }

    // Row-major copies of zones / roads / grown levels, for saving. Occupancy is not exported:
    // placed buildings are saved as records and re-placed on load.
    public byte[] ExportZones()
    {
        byte[] data = new byte[m_Zones.Length];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)m_Zones[i];
        return data;
    }

    public byte[] ExportRoads()
    {
        return (byte[])m_RoadTiers.Clone();
    }

    public byte[] ExportLevels()
    {
        return (byte[])m_BuildingLevel.Clone();
    }

    public byte[] ExportBuiltAges()
    {
        return (byte[])m_BuiltAge.Clone();
    }

    public byte[] ExportHistoric()
    {
        byte[] data = new byte[m_Historic.Length];
        for (int i = 0; i < data.Length; i++) data[i] = m_Historic[i] ? (byte)1 : (byte)0;
        return data;
    }

    public byte[] ExportPipes()
    {
        byte[] data = new byte[m_Pipes.Length];
        for (int i = 0; i < data.Length; i++) data[i] = m_Pipes[i] ? (byte)1 : (byte)0;
        return data;
    }

    // Replaces zones / roads / levels (and built ages / historic flags / pipes; null = all 0) and
    // clears occupancy (release buildings first). Raises OnCellChanged for every cell whose state
    // changed so views resync. Undeveloped cells never keep a built age or historic flag; roads never keep a pipe.
    public void Import(byte[] zones, byte[] roads, byte[] levels, byte[] builtAges = null, byte[] historic = null,
        byte[] pipes = null)
    {
        int count = Width * Height;
        if (zones == null || zones.Length != count) throw new ArgumentException("Zone data size mismatch.", nameof(zones));
        if (roads == null || roads.Length != count) throw new ArgumentException("Road data size mismatch.", nameof(roads));
        if (levels == null || levels.Length != count) throw new ArgumentException("Level data size mismatch.", nameof(levels));
        if (builtAges != null && builtAges.Length != count) throw new ArgumentException("Built age data size mismatch.", nameof(builtAges));
        if (historic != null && historic.Length != count) throw new ArgumentException("Historic data size mismatch.", nameof(historic));
        if (pipes != null && pipes.Length != count) throw new ArgumentException("Pipe data size mismatch.", nameof(pipes));

        for (int i = 0; i < count; i++)
        {
            ZoneType zone = (ZoneType)zones[i];
            byte roadTier = (byte)Mathf.Min(roads[i], (byte)MaxRoadTier);
            bool road = roadTier != 0;
            byte level = (byte)Mathf.Min(levels[i], (byte)3);
            byte builtAge = level > 0 && builtAges != null ? builtAges[i] : (byte)0;
            bool kept = level > 0 && historic != null && historic[i] != 0;
            bool pipe = !road && pipes != null && pipes[i] != 0;
            if (m_Zones[i] == zone && m_RoadTiers[i] == roadTier && m_BuildingLevel[i] == level && m_Occupancy[i] == 0
                && m_BuiltAge[i] == builtAge && m_Historic[i] == kept && m_Pipes[i] == pipe) continue;

            m_Zones[i] = zone;
            m_RoadTiers[i] = roadTier;
            m_BuildingLevel[i] = level;
            m_BuiltAge[i] = builtAge;
            m_Historic[i] = kept;
            m_Pipes[i] = pipe;
            m_Occupancy[i] = 0;
            OnCellChanged?.Invoke(new Vector2Int(i % Width, i / Width));
        }
    }

    private int Index(Vector2Int cell)
    {
        return CellUtils.Index(cell, Width);
    }
}
