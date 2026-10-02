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
    public static event Action<float, Vector3> MoneySpent;        // amount, world position (floating text)
    public static event Action<GameSpeed> SpeedChanged;
    public static event Action<Vector2Int> CellChanged;
    public static event Action CityLoaded;                         // after a load or new game replaced all state
    public static event Action<string> Notification;               // short player-facing message (toast)

    public static void RaiseMoneyChanged(float money) => MoneyChanged?.Invoke(money);
    public static void RaisePopulationChanged(int population, int jobs) => PopulationChanged?.Invoke(population, jobs);
    public static void RaiseDateChanged(int day, int month, int year) => DateChanged?.Invoke(day, month, year);
    public static void RaiseDemandChanged(DemandSnapshot demand) => DemandChanged?.Invoke(demand);
    public static void RaiseHappinessChanged(float happiness) => HappinessChanged?.Invoke(happiness);
    public static void RaiseCashFlowChanged(float income, float expense) => CashFlowChanged?.Invoke(income, expense);
    public static void RaiseInsufficientFunds(float cost) => InsufficientFunds?.Invoke(cost);
    public static void RaiseMoneySpent(float amount, Vector3 worldPosition) => MoneySpent?.Invoke(amount, worldPosition);
    public static void RaiseSpeedChanged(GameSpeed speed) => SpeedChanged?.Invoke(speed);
    public static void RaiseCellChanged(Vector2Int cell) => CellChanged?.Invoke(cell);
    public static void RaiseCityLoaded() => CityLoaded?.Invoke();
    public static void RaiseNotification(string message) => Notification?.Invoke(message);

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
        MoneySpent = null;
        SpeedChanged = null;
        CellChanged = null;
        CityLoaded = null;
        Notification = null;
    }
}
