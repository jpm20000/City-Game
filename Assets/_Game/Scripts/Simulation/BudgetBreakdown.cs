// One day's money in and out (M15), for the ledger. Expense is what SimulationSystem.Tick charges;
// the parts add up to it ((UpkeepByLine + OtherUpkeep + Roads + Pipes) x TechUpkeepMultiplier + Loans + Ordinances).
public readonly struct BudgetBreakdown
{
    public readonly float IncomeResidential;
    public readonly float IncomeCommercial;
    public readonly float IncomeIndustrial;
    public readonly float[] UpkeepByLine;       // placed buildings per budget line, at the line's funding (indexed by BudgetLine)
    public readonly float OtherUpkeep;          // placed buildings on no budget line
    public readonly float Roads;
    public readonly float Pipes;
    public readonly float TechUpkeepMultiplier; // researched techs' upkeep multiplier (applies to all of the above)
    public readonly float Loans;                // daily loan payments (not scaled by the upkeep multiplier)
    public readonly float Ordinances;           // enacted ordinances' daily cost (not scaled either)
    public readonly float Expense;

    public BudgetBreakdown(float incomeResidential, float incomeCommercial, float incomeIndustrial, float[] upkeepByLine,
        float otherUpkeep, float roads, float pipes, float techUpkeepMultiplier, float loans, float ordinances, float expense)
    {
        IncomeResidential = incomeResidential;
        IncomeCommercial = incomeCommercial;
        IncomeIndustrial = incomeIndustrial;
        UpkeepByLine = upkeepByLine;
        OtherUpkeep = otherUpkeep;
        Roads = roads;
        Pipes = pipes;
        TechUpkeepMultiplier = techUpkeepMultiplier;
        Loans = loans;
        Ordinances = ordinances;
        Expense = expense;
    }

    public float Income => IncomeResidential + IncomeCommercial + IncomeIndustrial;
    public float Net => Income - Expense;
}
