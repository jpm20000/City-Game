using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GridData
{
    public int Width { get; }
    public int Height { get; }

    private readonly ZoneType[] m_Zones;
    private readonly bool[] m_Roads;
    private readonly int[] m_Occupancy;
    private readonly byte[] m_BuildingLevel;

    public event Action<Vector2Int> OnCellChanged;

    public GridData(int width, int height)
    {
        Width = width;
        Height = height;
        m_Zones = new ZoneType[width * height];
        m_Roads = new bool[width * height];
        m_Occupancy = new int[width * height];
        m_BuildingLevel = new byte[width * height];
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

    public bool IsRoad(Vector2Int cell)
    {
        return m_Roads[Index(cell)];
    }

    public void SetRoad(Vector2Int cell, bool isRoad)
    {
        int i = Index(cell);
        if (m_Roads[i] == isRoad) return;
        m_Roads[i] = isRoad;
        OnCellChanged?.Invoke(cell);
    }

    public byte GetBuildingLevel(Vector2Int cell)
    {
        return m_BuildingLevel[Index(cell)];
    }

    public void SetBuildingLevel(Vector2Int cell, byte level)
    {
        if (level > 3) level = 3;
        int i = Index(cell);
        if (m_BuildingLevel[i] == level) return;
        m_BuildingLevel[i] = level;
        OnCellChanged?.Invoke(cell);
    }

    public int CountRoads()
    {
        int count = 0;
        for (int i = 0; i < m_Roads.Length; i++)
        {
            if (m_Roads[i]) count++;
        }
        return count;
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

    private int Index(Vector2Int cell)
    {
        return CellUtils.Index(cell, Width);
    }
}
