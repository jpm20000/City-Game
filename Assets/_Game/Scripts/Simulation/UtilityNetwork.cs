using System;
using System.Collections.Generic;
using UnityEngine;

// A utility that flows from sources into the network cells they touch and along every network cell
// 4-connected to them (the network needn't reach the map edge). Sources on the same network pool
// their supply. Grown cells beside a carrying cell draw DrawFor(capacity); supply is handed out in
// BFS order from the sources (seeds sorted row-major, so the result doesn't depend on build order),
// so the cells furthest out go without first. A carrying cell that is itself a grown cell (water: a
// pipe under it) is served too. Recomputes lazily after any grid or source change.
// Power (M9) and piped water (M13) are the two subclasses.
public abstract class UtilityNetwork
{
    protected readonly GridData m_Grid;
    private readonly CapacityModel m_Capacity;
    private int m_Width;
    private int[] m_RoadNetwork;      // network id per carrying cell, -1 = dry
    private int[] m_CellNetwork;      // network feeding each served cell, -1 = unserved
    private bool[] m_Visited;
    private int[] m_Draw;             // each cell's draw, filled once per recompute (M13e: the BFS asks up to 5x per cell)
    private bool[] m_Carries;         // Carries(cell), filled once per recompute
    private readonly List<int> m_Remaining = new();
    private readonly Queue<Vector2Int> m_Frontier = new();
    private IReadOnlyList<ServiceSource> m_Sources = Array.Empty<ServiceSource>();
    private bool m_Dirty = true;

    private int m_Supply;
    private int m_Demand;
    private int m_Load;
    private int m_UnservedCells;

    protected UtilityNetwork(GridData grid, BalanceConfig config, CapacityModel capacity)
    {
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        if (config == null) throw new ArgumentNullException(nameof(config));
        m_Capacity = capacity ?? new CapacityModel(config);
        Allocate();
        grid.OnCellChanged += _ => m_Dirty = true;
        grid.OnResized += Allocate;
    }

    // Units a source feeds into this network; 0 = not a source of it.
    protected abstract int SupplyOf(ServiceSource source);

    // Whether the utility flows through this cell (roads for power; roads and pipes for water).
    protected virtual bool Carries(Vector2Int cell) => m_Grid.IsRoad(cell);

    // Units a cell of the given capacity draws.
    public virtual int DrawFor(int capacity) => capacity;

    // Extra draw of a cell going from one capacity to another (never negative).
    public int ExtraDraw(int fromCapacity, int toCapacity) => Math.Max(0, DrawFor(toCapacity) - DrawFor(fromCapacity));

    private void Allocate()
    {
        m_Width = m_Grid.Width;
        int count = m_Grid.Width * m_Grid.Height;
        m_RoadNetwork = new int[count];
        m_CellNetwork = new int[count];
        m_Visited = new bool[count];
        m_Draw = new int[count];
        m_Carries = new bool[count];
        m_Dirty = true;
    }

    // Supply of sources that touch the network.
    public int Supply { get { EnsureFresh(); return m_Supply; } }
    // What every grown cell would draw if all were served.
    public int Demand { get { EnsureFresh(); return m_Demand; } }
    // What served cells draw, including upgrades reserved this tick.
    public int Load { get { EnsureFresh(); return m_Load; } }
    // Grown cells left without (no source on their network, or their network ran out).
    public int UnservedCells { get { EnsureFresh(); return m_UnservedCells; } }

    public void SetSources(IReadOnlyList<ServiceSource> sources)
    {
        m_Sources = sources ?? Array.Empty<ServiceSource>();
        m_Dirty = true;
    }

    public bool IsServed(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return false;
        EnsureFresh();
        return m_CellNetwork[Index(cell)] >= 0;
    }

    public bool IsCarrying(Vector2Int cell)
    {
        if (!m_Grid.InBounds(cell)) return false;
        EnsureFresh();
        return m_RoadNetwork[Index(cell)] >= 0;
    }

    // Whether a served cell's network can supply `extra` more units.
    public bool HasHeadroom(Vector2Int cell, int extra)
    {
        if (!IsServed(cell)) return false;
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

    // Marks the network for a recompute (subclasses whose carrying cells change outside the grid's events).
    protected void MarkDirty() => m_Dirty = true;

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
        m_UnservedCells = 0;
        m_Demand = CountDemand();   // also fills m_Draw and m_Carries

        // Seeds: network cells touching a source.
        SortedSet<int> seeds = new SortedSet<int>();
        List<(int supply, List<int> roads)> plants = new();
        foreach (ServiceSource source in m_Sources)
        {
            int supply = SupplyOf(source);
            if (supply <= 0) continue;

            List<int> roads = CollectAdjacentRoads(source);
            if (roads.Count == 0) continue;

            seeds.UnionWith(roads);
            plants.Add((supply, roads));
            m_Supply += supply;
        }

        // Label networks, flooding from the seeds in row-major order.
        foreach (int seed in seeds)
        {
            if (m_RoadNetwork[seed] >= 0) continue;
            Flood(seed, m_Remaining.Count);
            m_Remaining.Add(0);
        }

        // A source touching several separate networks splits its supply evenly between them.
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

        // Multi-source BFS from every seed: cells nearest a source are served first.
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
                if (!m_Grid.InBounds(next)) continue;
                int i = Index(next);
                if (!m_Carries[i] || m_Visited[i]) continue;
                m_Visited[i] = true;
                m_Frontier.Enqueue(next);
            }
        }
    }

    private static readonly Vector2Int[] s_SelfAndNeighbors =
        { Vector2Int.zero, new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };   // self, then CellUtils.Neighbors4's order

    // Serves the grown cells beside one carrying cell (and the cell itself, when it is a grown cell
    // with a pipe under it) while its network has supply left. Roads draw nothing, so power is unchanged.
    private void FeedCellsBeside(Vector2Int road)
    {
        int network = m_RoadNetwork[Index(road)];
        foreach (Vector2Int offset in s_SelfAndNeighbors)
        {
            Vector2Int cell = road + offset;
            if (!m_Grid.InBounds(cell)) continue;
            int i = Index(cell);
            if (m_CellNetwork[i] >= 0) continue;

            int draw = m_Draw[i];
            if (draw == 0 || m_Remaining[network] < draw) continue;

            m_Remaining[network] -= draw;
            m_Load += draw;
            m_CellNetwork[i] = network;
            m_UnservedCells--;
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
                if (!m_Grid.InBounds(next)) continue;
                int i = Index(next);
                if (!m_Carries[i] || m_RoadNetwork[i] >= 0) continue;
                m_RoadNetwork[i] = network;
                m_Frontier.Enqueue(next);
            }
        }
    }

    // Carrying cells sharing an edge with the footprint (diagonals don't count, like road access).
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
                if (m_Grid.InBounds(cell) && m_Carries[Index(cell)]) roads.Add(Index(cell));
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
                Vector2Int cell = new Vector2Int(x, y);
                int i = Index(cell);
                int draw = Draw(cell);
                m_Draw[i] = draw;
                m_Carries[i] = Carries(cell);
                demand += draw;
                if (draw > 0) m_UnservedCells++;   // counted down as cells get served
            }
        }
        return demand;
    }

    // A grown zone cell draws for its capacity; everything else draws nothing.
    private int Draw(Vector2Int cell)
    {
        if (m_Grid.GetZone(cell) == ZoneType.None || m_Grid.IsRoad(cell)) return 0;
        int capacity = m_Capacity.CapacityOf(m_Grid, cell);
        return capacity == 0 ? 0 : DrawFor(capacity);
    }

    private int Index(Vector2Int cell) => CellUtils.Index(cell, m_Width);

    private Vector2Int CellOf(int index) => new Vector2Int(index % m_Width, index / m_Width);
}
