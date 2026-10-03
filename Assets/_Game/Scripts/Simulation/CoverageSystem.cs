using System;
using System.Collections.Generic;
using UnityEngine;

// How many services reach each cell. Recomputed only when the placed services change. The radius
// selector picks which reach counts (parks' CoverageRadius by default; wells' WaterRadius in M13).
public sealed class CoverageSystem
{
    private readonly Func<ServiceSource, int> m_Radius;
    private int m_Width;
    private int m_Height;
    private byte[] m_Count;

    public CoverageSystem(int width, int height, Func<ServiceSource, int> radius = null)
    {
        m_Radius = radius ?? (source => source.CoverageRadius);
        Resize(width, height);
    }

    // Clears all coverage; call Recompute afterwards.
    public void Resize(int width, int height)
    {
        m_Width = width;
        m_Height = height;
        m_Count = new byte[width * height];
    }

    public void Recompute(IReadOnlyList<ServiceSource> sources)
    {
        Array.Clear(m_Count, 0, m_Count.Length);
        if (sources == null) return;

        foreach (ServiceSource source in sources)
        {
            int r = m_Radius(source);
            if (r <= 0) continue;

            int minX = Mathf.Max(0, source.Origin.x - r);
            int minY = Mathf.Max(0, source.Origin.y - r);
            int maxX = Mathf.Min(m_Width - 1, source.Origin.x + source.Size.x - 1 + r);
            int maxY = Mathf.Min(m_Height - 1, source.Origin.y + source.Size.y - 1 + r);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int i = y * m_Width + x;
                    if (m_Count[i] < byte.MaxValue) m_Count[i]++;
                }
            }
        }
    }

    public int GetCoverage(Vector2Int cell)
    {
        if (!CellUtils.IsInBounds(cell, new Vector2Int(m_Width, m_Height))) return 0;
        return m_Count[CellUtils.Index(cell, m_Width)];
    }
}
