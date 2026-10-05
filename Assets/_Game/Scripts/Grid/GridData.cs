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
    private byte[] m_RoadDirections;    // RoadLayout code: a Highway's one-way direction (0 = two-way); 0 elsewhere (M22)
    private byte[] m_RoadPairs;         // RoadLayout code: the side of an Avenue cell's partner lane (0 = single lane); 0 elsewhere (M22)

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
        m_RoadDirections = new byte[width * height];
        m_RoadPairs = new byte[width * height];
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
    public const byte AvenueTier = 4, HighwayTier = 5;      // the tiers that carry a layout byte (RoadTiers.Avenue / Highway)

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
        m_RoadDirections[i] = 0;
        ReleasePartner(cell, i);
        OnCellChanged?.Invoke(cell);
    }

    // 0 for anything but a Highway: the way a vehicle may leave the cell (RoadLayout codes), 0 = two-way (M22).
    public byte GetRoadDirection(Vector2Int cell)
    {
        return m_RoadDirections[Index(cell)];
    }

    // Sets a Highway cell's one-way direction (0 = two-way); ignored on any other cell or an invalid code.
    public void SetRoadDirection(Vector2Int cell, byte direction)
    {
        int i = Index(cell);
        if (m_RoadTiers[i] != HighwayTier || !RoadLayout.IsCode(direction)) return;
        if (m_RoadDirections[i] == direction) return;
        m_RoadDirections[i] = direction;
        OnCellChanged?.Invoke(cell);
    }

    // 0 for anything but a paired Avenue cell: the side its partner lane is on (RoadLayout codes) (M22).
    public byte GetRoadPair(Vector2Int cell)
    {
        return m_RoadPairs[Index(cell)];
    }

    // Pairs an Avenue cell with its neighbour on `side` (both cells point at each other, and any older pair of
    // either is released); side 0 unpairs it and its partner. Ignored unless both cells are Avenue road.
    public void SetRoadPair(Vector2Int cell, byte side)
    {
        int i = Index(cell);
        if (m_RoadTiers[i] != AvenueTier || !RoadLayout.IsCode(side)) return;
        if (side == 0)
        {
            ReleasePartner(cell, i);
            OnCellChanged?.Invoke(cell);
            return;
        }
        Vector2Int partner = cell + RoadLayout.Offset(side);
        if (!InBounds(partner) || m_RoadTiers[Index(partner)] != AvenueTier) return;
        int j = Index(partner);
        if (m_RoadPairs[i] == side && m_RoadPairs[j] == RoadLayout.Opposite(side)) return;
        ReleasePartner(cell, i);
        ReleasePartner(partner, j);
        m_RoadPairs[i] = side;
        m_RoadPairs[j] = RoadLayout.Opposite(side);
        OnCellChanged?.Invoke(cell);
        OnCellChanged?.Invoke(partner);
    }

    // Drops the cell's pair byte and, when its partner points back, the partner's (the partner stays a single lane).
    private void ReleasePartner(Vector2Int cell, int i)
    {
        byte side = m_RoadPairs[i];
        if (side == 0) return;
        m_RoadPairs[i] = 0;
        Vector2Int partner = cell + RoadLayout.Offset(side);
        if (!InBounds(partner)) return;
        int j = Index(partner);
        if (m_RoadPairs[j] != RoadLayout.Opposite(side)) return;
        m_RoadPairs[j] = 0;
        OnCellChanged?.Invoke(partner);
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

    // Copies the row-major tier bytes into dest (hot paths: no allocation).
    public void CopyRoadTiersTo(byte[] dest)
    {
        Buffer.BlockCopy(m_RoadTiers, 0, dest, 0, m_RoadTiers.Length);
    }

    // The same for the highway direction codes (M22).
    public void CopyRoadDirectionsTo(byte[] dest)
    {
        Buffer.BlockCopy(m_RoadDirections, 0, dest, 0, m_RoadDirections.Length);
    }

    // True when any road cell is one-way (lets traffic skip the direction checks in an all-two-way city).
    public bool AnyOneWay()
    {
        for (int i = 0; i < m_RoadDirections.Length; i++)
        {
            if (m_RoadDirections[i] != 0) return true;
        }
        return false;
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

    public byte[] ExportRoadDirections()
    {
        return (byte[])m_RoadDirections.Clone();
    }

    public byte[] ExportRoadPairs()
    {
        return (byte[])m_RoadPairs.Clone();
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
    // roadDirections / roadPairs (M22, null = all 0): a direction only sticks on a Highway cell, a pair only on an
    // Avenue cell whose partner is an Avenue cell pointing back; anything else is dropped.
    public void Import(byte[] zones, byte[] roads, byte[] levels, byte[] builtAges = null, byte[] historic = null,
        byte[] pipes = null, byte[] roadDirections = null, byte[] roadPairs = null)
    {
        int count = Width * Height;
        if (zones == null || zones.Length != count) throw new ArgumentException("Zone data size mismatch.", nameof(zones));
        if (roads == null || roads.Length != count) throw new ArgumentException("Road data size mismatch.", nameof(roads));
        if (levels == null || levels.Length != count) throw new ArgumentException("Level data size mismatch.", nameof(levels));
        if (builtAges != null && builtAges.Length != count) throw new ArgumentException("Built age data size mismatch.", nameof(builtAges));
        if (historic != null && historic.Length != count) throw new ArgumentException("Historic data size mismatch.", nameof(historic));
        if (pipes != null && pipes.Length != count) throw new ArgumentException("Pipe data size mismatch.", nameof(pipes));
        if (roadDirections != null && roadDirections.Length != count) throw new ArgumentException("Road direction data size mismatch.", nameof(roadDirections));
        if (roadPairs != null && roadPairs.Length != count) throw new ArgumentException("Road pair data size mismatch.", nameof(roadPairs));

        for (int i = 0; i < count; i++)
        {
            ZoneType zone = (ZoneType)zones[i];
            byte roadTier = (byte)Mathf.Min(roads[i], (byte)MaxRoadTier);
            bool road = roadTier != 0;
            byte level = (byte)Mathf.Min(levels[i], (byte)3);
            byte builtAge = level > 0 && builtAges != null ? builtAges[i] : (byte)0;
            bool kept = level > 0 && historic != null && historic[i] != 0;
            bool pipe = !road && pipes != null && pipes[i] != 0;
            byte direction = roadTier == HighwayTier && roadDirections != null && RoadLayout.IsCode(roadDirections[i]) ? roadDirections[i] : (byte)0;
            byte pair = ImportedPair(roads, roadPairs, i);
            if (m_Zones[i] == zone && m_RoadTiers[i] == roadTier && m_BuildingLevel[i] == level && m_Occupancy[i] == 0
                && m_BuiltAge[i] == builtAge && m_Historic[i] == kept && m_Pipes[i] == pipe
                && m_RoadDirections[i] == direction && m_RoadPairs[i] == pair) continue;

            m_Zones[i] = zone;
            m_RoadTiers[i] = roadTier;
            m_BuildingLevel[i] = level;
            m_BuiltAge[i] = builtAge;
            m_Historic[i] = kept;
            m_Pipes[i] = pipe;
            m_RoadDirections[i] = direction;
            m_RoadPairs[i] = pair;
            m_Occupancy[i] = 0;
            OnCellChanged?.Invoke(new Vector2Int(i % Width, i / Width));
        }
    }

    // The imported pair byte of cell i, or 0 when it is not an Avenue cell whose partner is an Avenue cell pointing back.
    private byte ImportedPair(byte[] roads, byte[] pairs, int i)
    {
        if (pairs == null || roads[i] != AvenueTier || pairs[i] == 0 || !RoadLayout.IsCode(pairs[i])) return 0;
        Vector2Int cell = new Vector2Int(i % Width, i / Width);
        Vector2Int partner = cell + RoadLayout.Offset(pairs[i]);
        if (!InBounds(partner)) return 0;
        int j = Index(partner);
        return roads[j] == AvenueTier && pairs[j] == RoadLayout.Opposite(pairs[i]) ? pairs[i] : (byte)0;
    }

    private int Index(Vector2Int cell)
    {
        return CellUtils.Index(cell, Width);
    }
}
