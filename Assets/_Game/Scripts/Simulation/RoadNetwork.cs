using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class RoadNetwork
{
    private readonly GridData m_Grid;
    private int m_Width;
    private int m_Height;
    private bool[] m_ConnectedToEntry;
    private bool m_Dirty = true;
    private Func<byte, bool> m_Frontage;

    // frontage(tier) = whether land beside a road of that tier gets road access (M16: highways don't);
    // null = every road gives access. Connectivity (the map-edge entry) counts every tier.
    public RoadNetwork(GridData grid, Func<byte, bool> frontage = null)
    {
        if (grid == null) throw new ArgumentNullException(nameof(grid));

        m_Grid = grid;
        m_Frontage = frontage;
        Allocate();
        grid.OnCellChanged += _ => m_Dirty = true;
        grid.OnResized += Allocate;
    }

    private void Allocate()
    {
        m_Width = m_Grid.Width;
        m_Height = m_Grid.Height;
        m_ConnectedToEntry = new bool[m_Width * m_Height];
        m_Dirty = true;
    }

    public bool HasRoadAccess(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return false;

        EnsureFresh();

        foreach (Vector2Int offset in CellUtils.Neighbors4)
        {
            Vector2Int neighbor = cell + offset;
            if (m_Grid.InBounds(neighbor)
                && m_Grid.IsRoad(neighbor)
                && m_ConnectedToEntry[Index(neighbor)]
                && (m_Frontage == null || m_Frontage(m_Grid.GetRoadTier(neighbor))))
            {
                return true;
            }
        }
        return false;
    }

    // Set by SimulationSystem once the tier table exists.
    public void SetFrontage(Func<byte, bool> frontage)
    {
        m_Frontage = frontage;
        m_Dirty = true;
    }

    public bool IsConnectedToEntry(Vector2Int roadCell)
    {
        if (!m_Grid.InBounds(roadCell)) return false;
        if (!m_Grid.IsRoad(roadCell)) return false;

        EnsureFresh();
        return m_ConnectedToEntry[Index(roadCell)];
    }

    private void EnsureFresh()
    {
        if (!m_Dirty) return;
        Recompute();
        m_Dirty = false;
    }

    private void Recompute()
    {
        Array.Clear(m_ConnectedToEntry, 0, m_ConnectedToEntry.Length);

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();

        for (int y = 0; y < m_Height; y++)
        {
            for (int x = 0; x < m_Width; x++)
            {
                if (!IsEdge(x, y)) continue;

                Vector2Int cell = new Vector2Int(x, y);
                if (!m_Grid.IsRoad(cell)) continue;

                int i = Index(cell);
                if (m_ConnectedToEntry[i]) continue;

                m_ConnectedToEntry[i] = true;
                frontier.Enqueue(cell);
            }
        }

        while (frontier.Count > 0)
        {
            Vector2Int cell = frontier.Dequeue();
            foreach (Vector2Int offset in CellUtils.Neighbors4)
            {
                Vector2Int neighbor = cell + offset;
                if (!m_Grid.InBounds(neighbor)) continue;
                if (!m_Grid.IsRoad(neighbor)) continue;

                int i = Index(neighbor);
                if (m_ConnectedToEntry[i]) continue;

                m_ConnectedToEntry[i] = true;
                frontier.Enqueue(neighbor);
            }
        }
    }

    private bool IsEdge(int x, int y)
    {
        return x == 0 || y == 0 || x == m_Width - 1 || y == m_Height - 1;
    }

    private int Index(Vector2Int cell)
    {
        return CellUtils.Index(cell, m_Width);
    }
}
