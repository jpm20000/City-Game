using System;
using System.Collections.Generic;

// Fires, plague, breakdowns and random events (M17) behind one switch. This is the state holder and the daily
// Step; 17a only carries the state (and saves it), the hazards fill Step in 17b-17d. Without age data the sim
// never runs it (Enabled stays false), so the age-less baseline cannot move. All per-cell layers are row-major
// bytes the size of the map, reallocated empty on GridData.Resize like every other per-cell state.
public sealed class DisasterSystem
{
    private readonly GridData m_Grid;
    private readonly bool m_HasAges;
    private bool m_Enabled;

    public SimRandom Random { get; } = new SimRandom();

    // Fire days of each burning cell (0 = not burning), rubble days left, and plague (0 = healthy,
    // 1..PlagueDays = days infected, PlagueRecovered = immune until the outbreak ends).
    public byte[] Fires { get; private set; }
    public byte[] Rubble { get; private set; }
    public byte[] Plague { get; private set; }
    public const byte PlagueRecovered = 255;

    public int PlagueCooldown { get; set; }
    public float PlagueRemainder { get; set; }

    // Plants, towers and pumps that are down, keyed by origin cell.
    public List<BrokenRecord> Broken { get; } = new();

    // Random events: the one waiting for an answer, the days until the next, effects still running and
    // the events offered lately (DaysLeft = days until one may repeat).
    public string PendingEvent { get; set; } = "";
    public int PendingDays { get; set; }
    public int DaysToNextEvent { get; set; }
    public List<EventRecord> ActiveEvents { get; } = new();
    public List<EventRecord> RecentEvents { get; } = new();

    public DisasterSystem(GridData grid, bool hasAges)
    {
        m_Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        m_HasAges = hasAges;
        Allocate();
        grid.OnResized += () =>
        {
            Allocate();
            Clear();
        };
    }

    // The player's switch, but never on without age data (the legacy sim has no hazards or events).
    public bool Enabled
    {
        get => m_Enabled && m_HasAges;
        set => m_Enabled = value;
    }

    private void Allocate()
    {
        int count = m_Grid.Width * m_Grid.Height;
        Fires = new byte[count];
        Rubble = new byte[count];
        Plague = new byte[count];
    }

    // Forgets every hazard and pending event (a new city, a resize). The switch and the RNG stay.
    public void Clear()
    {
        Array.Clear(Fires, 0, Fires.Length);
        Array.Clear(Rubble, 0, Rubble.Length);
        Array.Clear(Plague, 0, Plague.Length);
        PlagueCooldown = 0;
        PlagueRemainder = 0f;
        Broken.Clear();
        PendingEvent = "";
        PendingDays = 0;
        DaysToNextEvent = 0;
        ActiveEvents.Clear();
        RecentEvents.Clear();
    }

    // The daily step, called by SimulationSystem.Tick after research and before the traffic flow. The hazards
    // arrive in 17b (fire), 17c (plague, breakdowns) and 17d (events), drawing from Random in that fixed order.
    public void Step()
    {
        if (!Enabled) return;
    }

    // Writes the switch, the RNG state and every hazard layer into a save.
    public void Export(SaveData data)
    {
        data.Disasters = m_Enabled;
        data.RandomState = Random.StateString;
        data.Fires = (byte[])Fires.Clone();
        data.Rubble = (byte[])Rubble.Clone();
        data.Plague = (byte[])Plague.Clone();
        data.PlagueCooldown = PlagueCooldown;
        data.PlagueRemainder = PlagueRemainder;
        data.Broken = new List<BrokenRecord>(Broken);
        data.PendingEvent = PendingEvent ?? "";
        data.PendingDays = PendingDays;
        data.DaysToNextEvent = DaysToNextEvent;
        data.ActiveEvents = new List<EventRecord>(ActiveEvents);
        data.RecentEvents = new List<EventRecord>(RecentEvents);
    }

    // Restores a save's hazard state (the grid has already been resized to the save's size). Layers of the wrong
    // size, an unreadable RNG state and out-of-range numbers fall back to empty / defaults instead of failing.
    public void Restore(SaveData data)
    {
        Clear();
        m_Enabled = data.Disasters;
        if (!Random.TryRestore(data.RandomState)) Random.Seed(1);

        CopyLayer(data.Fires, Fires);
        CopyLayer(data.Rubble, Rubble);
        CopyLayer(data.Plague, Plague);
        PlagueCooldown = Math.Max(0, data.PlagueCooldown);
        PlagueRemainder = data.PlagueRemainder >= 0f && data.PlagueRemainder < 1f ? data.PlagueRemainder : 0f;

        if (data.Broken != null)
        {
            foreach (BrokenRecord record in data.Broken)
            {
                if (record.DaysLeft > 0 && m_Grid.InBounds(new UnityEngine.Vector2Int(record.X, record.Y))) Broken.Add(record);
            }
        }
        PendingEvent = data.PendingEvent ?? "";
        PendingDays = Math.Max(0, data.PendingDays);
        DaysToNextEvent = Math.Max(0, data.DaysToNextEvent);
        if (data.ActiveEvents != null) ActiveEvents.AddRange(data.ActiveEvents);
        if (data.RecentEvents != null) RecentEvents.AddRange(data.RecentEvents);
    }

    private static void CopyLayer(byte[] source, byte[] target)
    {
        if (source != null && source.Length == target.Length) Array.Copy(source, target, target.Length);
    }
}
