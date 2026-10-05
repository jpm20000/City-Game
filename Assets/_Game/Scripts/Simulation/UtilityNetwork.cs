using System;
using System.Collections.Generic;
using UnityEngine;

// A utility that flows from sources into the network cells they touch and along every network cell
// 4-connected to them (the network needn't reach the map edge). Sources on the same network pool
// their supply. Grown cells beside a carrying cell draw DrawFor(capacity); supply is handed out in
// BFS order from the sources (seeds sorted row-major, so the result doesn't depend on build order),
// so the cells furthest out go without first. A carrying cell that is itself a grown cell (water: a
// pipe under it) is served too. Power (M9) and piped water (M13) are the two subclasses.
//
// Kept cheap on big maps (M13 follow-up): each cell's draw and carrying flag are updated as the
// grid changes, and the work is split in two lazy stages. The topology (which cells carry, the
// networks, the sources' shares and the BFS feed order, recorded as a list of feed attempts) is
// rebuilt only when a carrying flag or the sources change. Any other change (levels, zones, built
// ages) only replays the recorded attempts over flat arrays, which serves exactly the same cells as
// a full BFS would.
public abstract class UtilityNetwork
{
    protected readonly GridData m_Grid;
    private readonly CapacityModel m_Capacity;
    private int m_Width;
    private int m_Height;
    private int[] m_RoadNetwork;      // network id per carrying cell, -1 = dry
    private int[] m_CellNetwork;      // network feeding each served cell, -1 = unserved
    private int[] m_Draw;             // each cell's draw, kept up to date as cells change
    private bool[] m_Carries;         // Carries(cell), kept up to date as cells change
    private int[] m_Queue;            // BFS frontier (each cell enters at most once per pass)
    private bool[] m_Visited;
    private int[] m_Attempts = new int[64];   // (cell, network) pairs in feed order
    private int m_AttemptCount;
    private int[] m_InitialRemaining = Array.Empty<int>();   // each network's supply before anything is served
    private int[] m_Remaining = Array.Empty<int>();
    private int m_NetworkCount;
    private IReadOnlyList<ServiceSource> m_Sources = Array.Empty<ServiceSource>();

    private bool m_CellsStale = true;     // every cell's draw / carrying flag must be re-read (new map, new subclass state)
    private bool m_TopologyDirty = true;
    private bool m_AllocationDirty = true;

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
        grid.OnCellChanged += OnCellChanged;
        grid.OnResized += Allocate;
    }

    // Units a source feeds into this network; 0 = not a source of it.
    protected abstract int SupplyOf(ServiceSource source);

    // Whether the utility flows through this cell (roads for power; roads and pipes for water).
    // Must depend only on the cell's own grid state.
    protected virtual bool Carries(Vector2Int cell) => m_Grid.IsRoad(cell);

    // Units a cell of the given capacity draws.
    public virtual int DrawFor(int capacity) => capacity;

    // Extra draw of a cell going from one capacity to another (never negative).
    public int ExtraDraw(int fromCapacity, int toCapacity) => Math.Max(0, DrawFor(toCapacity) - DrawFor(fromCapacity));

    private void Allocate()
    {
        m_Width = m_Grid.Width;
        m_Height = m_Grid.Height;
        int count = m_Width * m_Height;
        m_RoadNetwork = new int[count];
        m_CellNetwork = new int[count];
        m_Draw = new int[count];
        m_Carries = new bool[count];
        m_Queue = new int[count];
        m_Visited = new bool[count];
        m_CellsStale = true;
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
        m_TopologyDirty = true;
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

    // M23: an undeveloped cell draws nothing, so it is never "served"; a block about to start (High density needs
    // utilities from its first level) is fed through the carrying cell it touches (itself, then +x, -x, +y, -y).
    public bool IsCarryingNear(Vector2Int cell)
    {
        return m_Grid.InBounds(cell) && NearNetwork(cell, 0) >= 0;
    }

    public bool HasHeadroomNear(Vector2Int cell, int extra)
    {
        return m_Grid.InBounds(cell) && NearNetwork(cell, extra) >= 0;
    }

    public bool TryReserveNear(Vector2Int cell, int extra)
    {
        if (!m_Grid.InBounds(cell)) return false;
        int network = NearNetwork(cell, extra);
        if (network < 0) return false;
        m_Remaining[network] -= extra;
        m_Load += extra;
        return true;
    }

    private int NearNetwork(Vector2Int cell, int extra)
    {
        EnsureFresh();
        for (int k = 0; k < 5; k++)
        {
            Vector2Int c = k switch
            {
                0 => cell,
                1 => new Vector2Int(cell.x + 1, cell.y),
                2 => new Vector2Int(cell.x - 1, cell.y),
                3 => new Vector2Int(cell.x, cell.y + 1),
                _ => new Vector2Int(cell.x, cell.y - 1),
            };
            if (!m_Grid.InBounds(c)) continue;
            int network = m_RoadNetwork[Index(c)];
            if (network >= 0 && m_Remaining[network] >= extra) return network;
        }
        return -1;
    }

    // Growth reserves an upgrade's extra draw so several upgrades in one tick can't overdraw a network.
    public bool TryReserve(Vector2Int cell, int extra)
    {
        if (!HasHeadroom(cell, extra)) return false;
        m_Remaining[m_CellNetwork[Index(cell)]] -= extra;
        m_Load += extra;
        return true;
    }

    // Marks the whole network for a rebuild (subclasses whose carrying cells or draws change outside the grid's events).
    protected void MarkDirty() => m_CellsStale = true;

    // A changed cell can only change its own draw and carrying flag. A carrying change means new
    // networks (topology); anything else only needs the supply handed out again.
    private void OnCellChanged(Vector2Int cell)
    {
        m_AllocationDirty = true;
        if (m_CellsStale) return;   // everything is re-read anyway
        int i = Index(cell);
        m_Draw[i] = Draw(cell);
        bool carries = Carries(cell);
        if (carries != m_Carries[i])
        {
            m_Carries[i] = carries;
            m_TopologyDirty = true;
        }
    }

    private void EnsureFresh()
    {
        if (m_CellsStale)
        {
            m_CellsStale = false;
            ReadAllCells();
            m_TopologyDirty = true;
        }
        if (m_TopologyDirty)
        {
            m_TopologyDirty = false;
            RebuildTopology();
            m_AllocationDirty = true;
        }
        if (m_AllocationDirty)
        {
            m_AllocationDirty = false;
            Distribute();
        }
    }

    private void ReadAllCells()
    {
        for (int y = 0; y < m_Height; y++)
        {
            for (int x = 0; x < m_Width; x++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                int i = y * m_Width + x;
                m_Draw[i] = Draw(cell);
                m_Carries[i] = Carries(cell);
            }
        }
    }

    // Networks, the sources' shares of them and the feed order (a multi-source BFS from every seed:
    // cells nearest a source are fed first). Depends only on the carrying flags and the sources.
    private void RebuildTopology()
    {
        Array.Fill(m_RoadNetwork, -1);
        m_Supply = 0;
        m_NetworkCount = 0;
        m_AttemptCount = 0;

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
            Flood(seed, m_NetworkCount++);
        }

        // A source touching several separate networks splits its supply evenly between them.
        if (m_InitialRemaining.Length < m_NetworkCount)
        {
            m_InitialRemaining = new int[m_NetworkCount];
            m_Remaining = new int[m_NetworkCount];
        }
        Array.Clear(m_InitialRemaining, 0, m_NetworkCount);
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
                m_InitialRemaining[networks[k]] += k == 0 ? supply - share * (networks.Count - 1) : share;
            }
        }

        // Record the feed attempts in BFS order: for each carrying cell as it is dequeued, the cell
        // itself and then its neighbours in CellUtils.Neighbors4's order (+x, -x, +y, -y).
        Array.Clear(m_Visited, 0, m_Visited.Length);
        int head = 0, tail = 0;
        foreach (int seed in seeds)
        {
            m_Visited[seed] = true;
            m_Queue[tail++] = seed;
        }
        while (head < tail)
        {
            int road = m_Queue[head++];
            int network = m_RoadNetwork[road];
            int x = road % m_Width, y = road / m_Width;
            AddAttempt(road, network);
            if (x + 1 < m_Width) AddAttempt(road + 1, network);
            if (x > 0) AddAttempt(road - 1, network);
            if (y + 1 < m_Height) AddAttempt(road + m_Width, network);
            if (y > 0) AddAttempt(road - m_Width, network);

            if (x + 1 < m_Width) Visit(road + 1, ref tail);
            if (x > 0) Visit(road - 1, ref tail);
            if (y + 1 < m_Height) Visit(road + m_Width, ref tail);
            if (y > 0) Visit(road - m_Width, ref tail);
        }
    }

    private void AddAttempt(int cell, int network)
    {
        if (m_AttemptCount + 2 > m_Attempts.Length) Array.Resize(ref m_Attempts, m_Attempts.Length * 2);
        m_Attempts[m_AttemptCount++] = cell;
        m_Attempts[m_AttemptCount++] = network;
    }

    private void Visit(int i, ref int tail)
    {
        if (!m_Carries[i] || m_Visited[i]) return;
        m_Visited[i] = true;
        m_Queue[tail++] = i;
    }

    // Hands out the supply: replays the recorded attempts, serving each cell the first time its
    // network still has room for its draw.
    private void Distribute()
    {
        Array.Fill(m_CellNetwork, -1);
        Array.Copy(m_InitialRemaining, m_Remaining, m_NetworkCount);
        m_Load = 0;
        int demand = 0, unserved = 0;
        for (int i = 0; i < m_Draw.Length; i++)
        {
            int draw = m_Draw[i];
            demand += draw;
            if (draw > 0) unserved++;
        }

        for (int k = 0; k < m_AttemptCount; k += 2)
        {
            int cell = m_Attempts[k];
            if (m_CellNetwork[cell] >= 0) continue;
            int draw = m_Draw[cell];
            int network = m_Attempts[k + 1];
            if (draw == 0 || m_Remaining[network] < draw) continue;

            m_Remaining[network] -= draw;
            m_Load += draw;
            m_CellNetwork[cell] = network;
            unserved--;
        }
        m_Demand = demand;
        m_UnservedCells = unserved;
    }

    private void Flood(int seed, int network)
    {
        int head = 0, tail = 0;
        m_RoadNetwork[seed] = network;
        m_Queue[tail++] = seed;
        while (head < tail)
        {
            int cell = m_Queue[head++];
            int x = cell % m_Width, y = cell / m_Width;
            if (x + 1 < m_Width) Label(cell + 1, network, ref tail);
            if (x > 0) Label(cell - 1, network, ref tail);
            if (y + 1 < m_Height) Label(cell + m_Width, network, ref tail);
            if (y > 0) Label(cell - m_Width, network, ref tail);
        }
    }

    private void Label(int i, int network, ref int tail)
    {
        if (!m_Carries[i] || m_RoadNetwork[i] >= 0) return;
        m_RoadNetwork[i] = network;
        m_Queue[tail++] = i;
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

    // A grown zone cell draws for its capacity; everything else draws nothing.
    private int Draw(Vector2Int cell)
    {
        if (m_Grid.GetZone(cell) == ZoneType.None || m_Grid.IsRoad(cell)) return 0;
        int capacity = m_Capacity.CapacityOf(m_Grid, cell);
        return capacity == 0 ? 0 : DrawFor(capacity);
    }

    private int Index(Vector2Int cell) => CellUtils.Index(cell, m_Width);
}
