using System;
using UnityEngine;

// One day of the city's goods (M24). Supply is the share of demand that was delivered (stock + production + imports);
// it is 1 when goods are not in play.
public readonly struct GoodsReport
{
    public static readonly GoodsReport Off = new GoodsReport(0f, 0f, 0f, 0f, 0f, 1f, 0f);

    public readonly float Produced;
    public readonly float Demanded;
    public readonly float Delivered;
    public readonly float Imported;
    public readonly float Exported;
    public readonly float Supply;       // 0..1
    public readonly float StockAfter;

    public GoodsReport(float produced, float demanded, float delivered, float imported, float exported, float supply, float stockAfter)
    {
        Produced = produced;
        Demanded = demanded;
        Delivered = delivered;
        Imported = imported;
        Exported = exported;
        Supply = supply;
        StockAfter = stockAfter;
    }

    public float Shortage => 1f - Supply;
}

// The city-wide goods pool (M24): industry produces, shops and homes consume, a stock buffers the difference, a deficit
// is imported (up to a share of demand) and a surplus above the stock cap is exported. Pure arithmetic on the numbers
// SimulationSystem hands it; the stock is the only state (saved). Inactive = the identity (no stock, supply 1).
public sealed class GoodsSystem
{
    private readonly BalanceConfig m_Config;

    public float Stock { get; private set; }
    // What the last Step did (Off before the first one / while inactive).
    public GoodsReport Last { get; private set; } = GoodsReport.Off;

    public GoodsSystem(BalanceConfig config)
    {
        m_Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    // What a day with this production and demand would do from the current stock, without changing anything. Step
    // applies exactly this, so Evaluate(...).Supply is also "how supplied the city is right now".
    public GoodsReport Evaluate(float production, float demand, bool active)
    {
        if (!active) return GoodsReport.Off;

        production = Mathf.Max(0f, production);
        demand = Mathf.Max(0f, demand);
        float available = Stock + production;
        float sold = Mathf.Min(demand, available);
        float stock = available - sold;

        float imported = Mathf.Min(demand - sold, m_Config.GoodsMaxImportShare * demand);
        float delivered = sold + imported;

        float exported = Mathf.Max(0f, stock - m_Config.GoodsStockDays * demand);
        stock -= exported;

        float supply = demand > 0f ? Mathf.Clamp01(delivered / demand) : 1f;
        return new GoodsReport(production, demand, delivered, imported, exported, supply, stock);
    }

    // One day: applies Evaluate and keeps its stock. Inactive clears the stock (goods start from nothing when they begin).
    public GoodsReport Step(float production, float demand, bool active)
    {
        GoodsReport report = Evaluate(production, demand, active);
        Stock = active ? report.StockAfter : 0f;
        Last = report;
        return report;
    }

    // Load / new game: the saved stock; Last is the report the next day would produce (so the HUD is right before the tick).
    public void Restore(float stock, float production, float demand, bool active)
    {
        Stock = active && stock > 0f && !float.IsNaN(stock) && !float.IsInfinity(stock) ? stock : 0f;
        Last = Evaluate(production, demand, active);
    }

    // Money for the day's flows (ledger lines).
    public float ImportCost(GoodsReport report) => report.Imported * m_Config.GoodsImportPrice;
    public float ExportIncome(GoodsReport report) => report.Exported * m_Config.GoodsExportPrice;
}
