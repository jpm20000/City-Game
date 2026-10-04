using System;
using System.Collections.Generic;
using UnityEngine;

// Statistical commute flow (M16): every grown home puts its daily trips on its best frontage road, the
// trips travel to the nearest job (a road beside a grown commercial / industrial cell) or, as a
// fallback, out of town through a map-edge road (OutsideTripCost). One multi-source Dial's algorithm
// pass over the road cells gives each its distance to the nearest sink and a parent step towards it;
// loads then accumulate down that tree, so Load(road) = the trips through that cell. Congestion =
// load / the road tier's capacity. A home's commute congestion is the worst ratio on its way to its
// sink; a cell's local congestion is the worst ratio on the 4 roads beside it.
// Seeds go in row-major and neighbours in CellUtils.Neighbors4 order, so ties are deterministic.
// Derived, never saved: recomputed by Update (end of each tick, and after a load). Between updates the
// numbers are the last tick's.
public sealed class TrafficSystem
{
    private readonly GridData m_Grid;
    private readonly BalanceConfig m_Config;
    private readonly CapacityModel m_Capacity;
    private readonly RoadTiers m_Tiers;

    private int m_Width;
    private int m_Height;
    private byte[] m_Tier;               // snapshot of the road tiers for this Update
    private int[] m_Dist;
    private int[] m_Parent;
    private float[] m_Load;
    private float[] m_Congestion;       // load / capacity per road cell
    private float[] m_Worst;            // worst congestion from the cell to its sink
    private float[] m_HomeTrips;
    private int[] m_HomeRoad;           // the road a home loads (-1 = none)
    private int[] m_Order;              // road cells in the order they were settled (non-decreasing distance)
    private int m_OrderCount;
    private List<int>[] m_Buckets;

    // Neighbour offsets in CellUtils.Neighbors4 order (+x, -x, +y, -y): ties resolve the same way everywhere.
    private static readonly int[] DX = { 1, -1, 0, 0 };
    private static readonly int[] DY = { 0, 0, 1, -1 };

    // Tier lookups, refreshed per Update.
    private readonly float[] m_TierCapacity = new float[RoadTiers.Count + 1];
    private readonly int[] m_TierTravel = new int[RoadTiers.Count + 1];
    private readonly bool[] m_TierFrontage = new bool[RoadTiers.Count + 1];

    public float Trips { get; private set; }            // total daily trips put on the roads
    public int JammedRoads { get; private set; }        // road cells with load above capacity
    public float WorstCongestion { get; private set; }  // the busiest road's load / capacity

    public TrafficSystem(GridData grid, BalanceConfig config, CapacityModel capacity, RoadTiers tiers)
    {
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
        m_Capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
        m_Tiers = tiers ?? throw new ArgumentNullException(nameof(tiers));
        Allocate();
        grid.OnResized += Allocate;
    }

    private void Allocate()
    {
        m_Width = m_Grid.Width;
        m_Height = m_Grid.Height;
        int n = m_Width * m_Height;
        m_Tier = new byte[n];
        m_Dist = new int[n];
        m_Parent = new int[n];
        m_Load = new float[n];
        m_Congestion = new float[n];
        m_Worst = new float[n];
        m_HomeTrips = new float[n];
        m_HomeRoad = new int[n];
        for (int i = 0; i < n; i++) m_HomeRoad[i] = -1;
        m_Order = new int[n];
        m_OrderCount = 0;
        Trips = 0f;
        JammedRoads = 0;
        WorstCongestion = 0f;
    }

    private bool Fresh => m_Width == m_Grid.Width && m_Height == m_Grid.Height;

    // Trips through the road cell (0 for a cell that isn't a road, or out of range).
    public float Load(Vector2Int cell) => Fresh && m_Grid.InBounds(cell) ? m_Load[Index(cell)] : 0f;

    public float Capacity(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return 0f;
        byte tier = m_Grid.GetRoadTier(cell);
        return tier == 0 ? 0f : m_Tiers.Capacity(tier);
    }

    // load / capacity of a road cell, 0 elsewhere.
    public float Congestion(Vector2Int cell) => Fresh && m_Grid.InBounds(cell) ? m_Congestion[Index(cell)] : 0f;

    // The home's commute: the worst load / capacity on the way from its road to the nearest job.
    public float CommuteCongestion(Vector2Int home)
    {
        if (!Fresh || !m_Grid.InBounds(home)) return 0f;
        int road = m_HomeRoad[Index(home)];
        return road < 0 ? 0f : m_Worst[road];
    }

    // Daily trips the home put on the road in the last Update.
    public float HomeTrips(Vector2Int home) => Fresh && m_Grid.InBounds(home) ? m_HomeTrips[Index(home)] : 0f;

    // The busiest of the 4 roads beside the cell (noise and fumes from the street outside).
    public float LocalCongestion(Vector2Int cell)
    {
        if (!Fresh || !m_Grid.InBounds(cell)) return 0f;
        float worst = 0f;
        foreach (Vector2Int offset in CellUtils.Neighbors4)
        {
            Vector2Int neighbor = cell + offset;
            if (!m_Grid.InBounds(neighbor)) continue;
            float c = m_Congestion[Index(neighbor)];
            if (c > worst) worst = c;
        }
        return worst;
    }

    // One home's happiness loss from its commute (positive, capped): free up to CongestionFree.
    public static float TrafficPenaltyAt(BalanceConfig config, float congestion)
    {
        return Mathf.Min(Mathf.Max(0f, congestion - config.CongestionFree) * config.TrafficPenalty, config.TrafficPenaltyCap);
    }

    // One cell's land value loss from the jam on the roads beside it (positive, capped).
    public static float LandValueLossAt(BalanceConfig config, float localCongestion)
    {
        return Mathf.Min(Mathf.Max(0f, localCongestion - config.CongestionFree) * config.LandValuePerCongestion,
            config.LandValueCongestionCap);
    }

    // occupancy = residents / housing (0..1), employment = employed / workers (0..1), multiplier =
    // TechModifiers.TrafficMultiplier. Trips per home = capacity x occupancy x employment x TripsPerWorker x multiplier.
    public void Update(float occupancy, float employment, float multiplier)
    {
        if (!Fresh) Allocate();
        int n = m_Width * m_Height;
        m_Grid.CopyRoadTiersTo(m_Tier);
        Array.Clear(m_Load, 0, n);
        Array.Clear(m_Congestion, 0, n);
        Array.Clear(m_Worst, 0, n);
        Array.Clear(m_HomeTrips, 0, n);
        for (int i = 0; i < n; i++)
        {
            m_Dist[i] = int.MaxValue;
            m_Parent[i] = -1;
            m_HomeRoad[i] = -1;
        }
        Trips = 0f;
        JammedRoads = 0;
        WorstCongestion = 0f;
        m_OrderCount = 0;

        int maxTravel = 1;
        for (int t = 1; t <= RoadTiers.Count; t++)
        {
            m_TierCapacity[t] = Mathf.Max(0.0001f, m_Tiers.Capacity(t));
            m_TierTravel[t] = Mathf.Max(1, m_Tiers.TravelCost(t));
            m_TierFrontage[t] = m_Tiers.Frontage(t);
            if (m_TierTravel[t] > maxTravel) maxTravel = m_TierTravel[t];
        }
        int outside = Mathf.Max(1, m_Config.OutsideTripCost);
        int size = Mathf.Max(outside, maxTravel) + 2;
        if (m_Buckets == null || m_Buckets.Length != size)
        {
            m_Buckets = new List<int>[size];
            for (int i = 0; i < size; i++) m_Buckets[i] = new List<int>();
        }
        else
        {
            for (int i = 0; i < size; i++) m_Buckets[i].Clear();
        }

        SeedSinks(outside);
        Settle(size);

        float tripsPer = Mathf.Max(0f, occupancy) * Mathf.Max(0f, employment) * m_Config.TripsPerWorker * Mathf.Max(0f, multiplier);
        LoadHomes(tripsPer);
        Accumulate();
    }

    // Jobs: roads (with frontage) beside grown commercial / industrial cells, distance 0. Map-edge roads
    // lead out of town at OutsideTripCost.
    private void SeedSinks(int outside)
    {
        for (int y = 0; y < m_Height; y++)
        {
            for (int x = 0; x < m_Width; x++)
            {
                int i = y * m_Width + x;
                byte tier = m_Tier[i];
                if (tier != 0)
                {
                    if (x == 0 || y == 0 || x == m_Width - 1 || y == m_Height - 1) Seed(i, outside);
                    continue;
                }
                var cell = new Vector2Int(x, y);
                if (m_Grid.GetBuildingLevel(cell) == 0) continue;
                ZoneType zone = m_Grid.GetZone(cell);
                if ((zone != ZoneType.Commercial && zone != ZoneType.Industrial) || m_Capacity.CapacityOf(m_Grid, cell) == 0) continue;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + DX[k];
                    int ny = y + DY[k];
                    if (nx < 0 || ny < 0 || nx >= m_Width || ny >= m_Height) continue;
                    int nb = ny * m_Width + nx;
                    byte nt = m_Tier[nb];
                    if (nt != 0 && m_TierFrontage[nt]) Seed(nb, 0);
                }
            }
        }
    }

    private void Seed(int index, int distance)
    {
        if (distance >= m_Dist[index]) return;
        m_Dist[index] = distance;
        m_Buckets[distance % m_Buckets.Length].Add(index);
    }

    // Dial's algorithm over a circular bucket queue (every step costs at least 1 and at most size - 2).
    private void Settle(int size)
    {
        int remaining = 0;
        for (int i = 0; i < size; i++) remaining += m_Buckets[i].Count;
        for (int d = 0; remaining > 0; d++)
        {
            List<int> bucket = m_Buckets[d % size];
            for (int k = 0; k < bucket.Count; k++)
            {
                int c = bucket[k];
                remaining--;
                if (m_Dist[c] != d) continue;      // stale: reached cheaper after being queued
                m_Order[m_OrderCount++] = c;
                int cx = c % m_Width;
                int cy = c / m_Width;
                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = cx + DX[dir];
                    int ny = cy + DY[dir];
                    if (nx < 0 || ny < 0 || nx >= m_Width || ny >= m_Height) continue;
                    int nb = ny * m_Width + nx;
                    byte tier = m_Tier[nb];
                    if (tier == 0) continue;
                    int nd = d + m_TierTravel[tier];
                    if (nd >= m_Dist[nb]) continue;
                    m_Dist[nb] = nd;
                    m_Parent[nb] = c;
                    m_Buckets[nd % size].Add(nb);
                    remaining++;
                }
            }
            bucket.Clear();
        }
    }

    private void LoadHomes(float tripsPer)
    {
        for (int y = 0; y < m_Height; y++)
        {
            for (int x = 0; x < m_Width; x++)
            {
                var cell = new Vector2Int(x, y);
                if (m_Grid.GetBuildingLevel(cell) == 0 || m_Grid.GetZone(cell) != ZoneType.Residential) continue;
                int capacity = m_Capacity.CapacityOf(m_Grid, cell);
                if (capacity == 0) continue;

                int best = -1;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + DX[k];
                    int ny = y + DY[k];
                    if (nx < 0 || ny < 0 || nx >= m_Width || ny >= m_Height) continue;
                    int index = ny * m_Width + nx;
                    byte tier = m_Tier[index];
                    if (tier == 0 || !m_TierFrontage[tier]) continue;
                    if (m_Dist[index] == int.MaxValue) continue;
                    if (best < 0 || m_Dist[index] < m_Dist[best]) best = index;
                }
                if (best < 0) continue;

                float trips = capacity * tripsPer;
                int home = y * m_Width + x;
                m_HomeRoad[home] = best;
                m_HomeTrips[home] = trips;
                m_Load[best] += trips;
                Trips += trips;
            }
        }
    }

    // Loads flow down the tree (decreasing distance), then the worst ratio on the way to the sink flows
    // back up (increasing distance).
    private void Accumulate()
    {
        for (int k = m_OrderCount - 1; k >= 0; k--)
        {
            int c = m_Order[k];
            int parent = m_Parent[c];
            if (parent >= 0) m_Load[parent] += m_Load[c];
        }
        for (int k = 0; k < m_OrderCount; k++)
        {
            int c = m_Order[k];
            byte tier = m_Tier[c];
            float congestion = m_Load[c] / m_TierCapacity[tier];
            m_Congestion[c] = congestion;
            int parent = m_Parent[c];
            m_Worst[c] = parent >= 0 ? Mathf.Max(congestion, m_Worst[parent]) : congestion;
            if (congestion > 1f) JammedRoads++;
            if (congestion > WorstCongestion) WorstCongestion = congestion;
        }
    }

    private int Index(Vector2Int cell) => cell.y * m_Width + cell.x;
}
