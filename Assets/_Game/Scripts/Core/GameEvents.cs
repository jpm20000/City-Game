using System;
using UnityEngine;

// Central event bus. Presentation subscribes here instead of polling systems.
public static class GameEvents
{
    public static event Action<float> MoneyChanged;
    public static event Action<int, int> PopulationChanged;   // population, jobs
    public static event Action<int, int, int> DateChanged;    // day, month, year
    public static event Action<DemandSnapshot> DemandChanged;
    public static event Action<float> HappinessChanged;            // 0..1
    public static event Action<float, float> CashFlowChanged;      // income, expense per day
    public static event Action<float> InsufficientFunds;          // cost that couldn't be paid
    public static event Action<GameSpeed> SpeedChanged;
    public static event Action<BuildingInstance> BuildingPlaced;
    public static event Action<BuildingInstance> BuildingRemoved;
    public static event Action<Vector2Int> CellChanged;

    public static void RaiseMoneyChanged(float money) => MoneyChanged?.Invoke(money);
    public static void RaisePopulationChanged(int population, int jobs) => PopulationChanged?.Invoke(population, jobs);
    public static void RaiseDateChanged(int day, int month, int year) => DateChanged?.Invoke(day, month, year);
    public static void RaiseDemandChanged(DemandSnapshot demand) => DemandChanged?.Invoke(demand);
    public static void RaiseHappinessChanged(float happiness) => HappinessChanged?.Invoke(happiness);
    public static void RaiseCashFlowChanged(float income, float expense) => CashFlowChanged?.Invoke(income, expense);
    public static void RaiseInsufficientFunds(float cost) => InsufficientFunds?.Invoke(cost);
    public static void RaiseSpeedChanged(GameSpeed speed) => SpeedChanged?.Invoke(speed);
    public static void RaiseBuildingPlaced(BuildingInstance building) => BuildingPlaced?.Invoke(building);
    public static void RaiseBuildingRemoved(BuildingInstance building) => BuildingRemoved?.Invoke(building);
    public static void RaiseCellChanged(Vector2Int cell) => CellChanged?.Invoke(cell);

    // Static events survive play sessions when domain reload is disabled; drop stale subscribers.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSubscribers()
    {
        MoneyChanged = null;
        PopulationChanged = null;
        DateChanged = null;
        DemandChanged = null;
        HappinessChanged = null;
        CashFlowChanged = null;
        InsufficientFunds = null;
        SpeedChanged = null;
        BuildingPlaced = null;
        BuildingRemoved = null;
        CellChanged = null;
    }
}
