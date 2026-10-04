using System;
using System.Collections.Generic;
using UnityEngine;

// How well each civic service line covers each cell (M14): the best CivicStrength among the
// sources of that line whose CivicRadius reaches the cell (Chebyshev from the footprint). Unlike
// park coverage this never stacks; a stronger (newer) building simply overrides. Recomputed only
// when the placed services change.
public sealed class CivicCoverage
{
    private static readonly int KindCount = Enum.GetValues(typeof(ServiceKind)).Length;

    private int m_Width;
    private int m_Height;
    private float[][] m_Strength;   // [kind][cell]; ServiceKind.None stays empty

    public CivicCoverage(int width, int height)
    {
        Resize(width, height);
    }

    // Clears all cover; call Recompute afterwards.
    public void Resize(int width, int height)
    {
        m_Width = width;
        m_Height = height;
        m_Strength = new float[KindCount][];
        for (int k = 1; k < KindCount; k++) m_Strength[k] = new float[width * height];
    }

    public void Recompute(IReadOnlyList<ServiceSource> sources)
    {
        for (int k = 1; k < KindCount; k++) Array.Clear(m_Strength[k], 0, m_Strength[k].Length);
        if (sources == null) return;
        foreach (ServiceSource source in sources)
        {
            int kind = (int)source.CivicKind;
            int r = source.CivicRadius;
            float strength = Mathf.Clamp01(source.CivicStrength);
            if (kind <= 0 || kind >= KindCount || r <= 0 || strength <= 0f) continue;
            float[] cells = m_Strength[kind];
            int minX = Mathf.Max(0, source.Origin.x - r);
            int minY = Mathf.Max(0, source.Origin.y - r);
            int maxX = Mathf.Min(m_Width - 1, source.Origin.x + source.Size.x - 1 + r);
            int maxY = Mathf.Min(m_Height - 1, source.Origin.y + source.Size.y - 1 + r);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int i = y * m_Width + x;
                    if (cells[i] < strength) cells[i] = strength;
                }
            }
        }
    }

    // By flat index (y * width + x), unchecked: the hot path for CivicSystem.HomeNeeds.
    internal float StrengthAt(ServiceKind kind, int index) => m_Strength[(int)kind][index];

    // 0..1; 0 off-map or for ServiceKind.None.
    public float GetStrength(ServiceKind kind, Vector2Int cell)
    {
        int k = (int)kind;
        if (k <= 0 || k >= KindCount || !CellUtils.IsInBounds(cell, new Vector2Int(m_Width, m_Height))) return 0f;
        return m_Strength[k][cell.y * m_Width + cell.x];
    }
}
