using System;
using System.Collections.Generic;
using UnityEngine;

// Power flows from each plant into the roads it touches and along every road 4-connected to them
// (the road needn't reach the map edge). Plants on the same road network pool their supply. Grown
// cells beside an energised road draw their level's capacity; supply is handed out in BFS order from
// the plants (seeds sorted row-major, so the result doesn't depend on build order), so the cells
// furthest out go dark first. Recomputes lazily after any grid or plant change.
public sealed class PowerSystem
{
    private readonly GridData m_Grid;
    private readonly CapacityModel m_Capacity;
    private int m_Width;
    private int[] m_RoadNetwork;      // network id per energised road cell, -1 = dark
    private int[] m_CellNetwork;      // network feeding each powered cell, -1 = unpowered
    private bool[] m_Visited;
    private readonly List<int> m_Remaining = new();
    private readonly Queue<Vector2Int> m_Frontier = new();
    private IReadOnlyList<ServiceSource> m_Sources = Array.Empty<ServiceSource>();
    private bool m_Dirty = true;

    private int m_Supply;
    private int m_Demand;
    private int m_Load;
    private int m_UnpoweredCells;

    public PowerSystem(GridData grid, BalanceConfig config, CapacityModel capacity = null)
    {
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        if (config == null) throw new ArgumentNullException(nameof(config));
        m_Capacity = capacity ?? new CapacityModel(config);
        Allocate();
        grid.OnCellChanged += _ => m_Dirty = true;
        grid.OnResized += Allocate;
    }

    private void Allocate()
    {
        m_Width = m_Grid.Width;
        int count = m_Grid.Width * m_Grid.Height;
        m_RoadNetwork = new int[count];
        m_CellNetwork = new int[count];
        m_Visited = new bool[count];
        m_Dirty = true;
    }

    // Supply of plants that touch a road.
    public int Supply { get { EnsureFresh(); return m_Supply; } }
    // What every grown cell would draw if all were powered.
    public int Demand { get { EnsureFresh(); return m_Demand; } }
    // What powered cells draw, including upgrades reserved this tick.
    public int Load { get { EnsureFresh(); return m_Load; } }
    // Grown cells left without power (no plant on their road, or their network ran out).
    public int UnpoweredCells { get { EnsureFresh(); return m_UnpoweredCells; } }

    public void SetSources(IReadOnlyList<ServiceSource> sources)
    {
        m_Sources = sources ?? Array.Empty<ServiceSource>();
        m_Dirty = true;
    }

    public bool IsPowered(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return false;
        EnsureFresh();
        return m_CellNetwork[Index(cell)] >= 0;
    }

    public bool IsEnergisedRoad(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return false;
        EnsureFresh();
        return m_RoadNetwork[Index(cell)] >= 0;
    }

    // Whether a powered cell's network can supply `extra` more units.
    public bool HasHeadroom(Vector2Int cell, int extra)
    {
        if (!IsPowered(cell)) return false;
        return m_Remaining[m_CellNetwork[Index(cell)]] >= extra;
    }

    // Growth reserves an upgrade's extra draw so several upgrades in one tick can't overdraw a network.
    public bool TryReserve(Vector2Int cell, int extra)
    {
        if (!HasHeadroom(cell, extra)) return false;
        m_Remaining[m_CellNetwork[Index(cell)]] -= extra;
        m_Load += extra;
        return true;
    }

    private void EnsureFresh()
    {
        if (!m_Dirty) return;
        m_Dirty = false;
        Recompute();
    }

    private void Recompute()
    {
        Array.Fill(m_RoadNetwork, -1);
        Array.Fill(m_CellNetwork, -1);
        Array.Clear(m_Visited, 0, m_Visited.Length);
        m_Remaining.Clear();
        m_Supply = 0;
        m_Load = 0;
        m_UnpoweredCells = 0;
        m_Demand = CountDemand();

        // Seeds: road cells touching a plant.
        SortedSet<int> seeds = new SortedSet<int>();
        List<(int supply, List<int> roads)> plants = new();
        foreach (ServiceSource source in m_Sources)
        {
            if (source.PowerSupply <= 0) continue;

            List<int> roads = CollectAdjacentRoads(source);
            if (roads.Count == 0) continue;

            seeds.UnionWith(roads);
            plants.Add((source.PowerSupply, roads));
            m_Supply += source.PowerSupply;
        }

        // Label networks, flooding from the seeds in row-major order.
        foreach (int seed in seeds)
        {
            if (m_RoadNetwork[seed] >= 0) continue;
            Flood(seed, m_Remaining.Count);
            m_Remaining.Add(0);
        }

        // A plant touching several separate networks splits its supply evenly between them.
        foreach ((int supply, List<int> roads) in plants)
        {
            List<int> networks = new List<int>();
            foreach (int road in roads)
            {
                if (!networks.Contains(m_RoadNetwork[road])) networks.Add(m_RoadNetwork[road]);
            }
            networks.Sort();
            int share = supply / networks.Count;
            for (int k = 0; k < networks.Count; k++)
            {
                m_Remaining[networks[k]] += k == 0 ? supply - share * (networks.Count - 1) : share;
            }
        }

        // Multi-source BFS from every seed: roads nearest a plant hand out supply first.
        m_Frontier.Clear();
        foreach (int seed in seeds)
        {
            m_Visited[seed] = true;
            m_Frontier.Enqueue(CellOf(seed));
        }
        while (m_Frontier.Count > 0)
        {
            Vector2Int road = m_Frontier.Dequeue();
            FeedCellsBeside(road);
            foreach (Vector2Int offset in CellUtils.Neighbors4)
            {
                Vector2Int next = road + offset;
                if (!m_Grid.InBounds(next) || !m_Grid.IsRoad(next)) continue;
                int i = Index(next);
                if (m_Visited[i]) continue;
                m_Visited[i] = true;
                m_Frontier.Enqueue(next);
            }
        }
    }

    // Powers the grown cells beside one energised road while its network has supply left.
    private void FeedCellsBeside(Vector2Int road)
    {
        int network = m_RoadNetwork[Index(road)];
        foreach (Vector2Int offset in CellUtils.Neighbors4)
        {
            Vector2Int cell = road + offset;
            if (!m_Grid.InBounds(cell)) continue;
            int i = Index(cell);
            if (m_CellNetwork[i] >= 0) continue;

            int draw = Draw(cell);
            if (draw == 0 || m_Remaining[network] < draw) continue;

            m_Remaining[network] -= draw;
            m_Load += draw;
            m_CellNetwork[i] = network;
            m_UnpoweredCells--;
        }
    }

    private void Flood(int seed, int network)
    {
        m_Frontier.Clear();
        m_RoadNetwork[seed] = network;
        m_Frontier.Enqueue(CellOf(seed));
        while (m_Frontier.Count > 0)
        {
            Vector2Int cell = m_Frontier.Dequeue();
            foreach (Vector2Int offset in CellUtils.Neighbors4)
            {
                Vector2Int next = cell + offset;
                if (!m_Grid.InBounds(next) || !m_Grid.IsRoad(next)) continue;
                int i = Index(next);
                if (m_RoadNetwork[i] >= 0) continue;
                m_RoadNetwork[i] = network;
                m_Frontier.Enqueue(next);
            }
        }
    }

    // Road cells sharing an edge with the footprint (diagonals don't count, like road access).
    private List<int> CollectAdjacentRoads(ServiceSource source)
    {
        List<int> roads = new List<int>();
        for (int y = source.Origin.y - 1; y <= source.Origin.y + source.Size.y; y++)
        {
            for (int x = source.Origin.x - 1; x <= source.Origin.x + source.Size.x; x++)
            {
                bool insideX = x >= source.Origin.x && x < source.Origin.x + source.Size.x;
                bool insideY = y >= source.Origin.y && y < source.Origin.y + source.Size.y;
                if (insideX == insideY) continue;   // the footprint itself, or a diagonal corner

                Vector2Int cell = new Vector2Int(x, y);
                if (m_Grid.InBounds(cell) && m_Grid.IsRoad(cell)) roads.Add(Index(cell));
            }
        }
        return roads;
    }

    private int CountDemand()
    {
        int demand = 0;
        for (int y = 0; y < m_Grid.Height; y++)
        {
            for (int x = 0; x < m_Width; x++)
            {
                int draw = Draw(new Vector2Int(x, y));
                demand += draw;
                if (draw > 0) m_UnpoweredCells++;   // counted down as cells get power
            }
        }
        return demand;
    }

    // A grown zone cell draws its capacity; everything else draws nothing.
    private int Draw(Vector2Int cell)
    {
        if (m_Grid.GetZone(cell) == ZoneType.None || m_Grid.IsRoad(cell)) return 0;
        return m_Capacity.CapacityOf(m_Grid, cell);
    }

    private int Index(Vector2Int cell) => CellUtils.Index(cell, m_Width);

    private Vector2Int CellOf(int index) => new Vector2Int(index % m_Width, index / m_Width);
}
