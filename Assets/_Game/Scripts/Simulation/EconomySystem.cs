using System;

public sealed class EconomySystem
{
    public float Money { get; private set; }
    public float IncomePerDay { get; private set; }
    public float ExpensePerDay { get; private set; }
    public float TaxResidential { get; set; }
    public float TaxCommercial { get; set; }
    public float TaxIndustrial { get; set; }

    public event Action<float> OnMoneyChanged;

    public EconomySystem(BalanceConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        Money = config.StartingMoney;
        TaxResidential = config.TaxResidential;
        TaxCommercial = config.TaxCommercial;
        TaxIndustrial = config.TaxIndustrial;
    }

    public bool CanAfford(float amount)
    {
        return Money >= amount;
    }

    public bool Spend(float amount)
    {
        if (!CanAfford(amount)) return false;
        SetMoney(Money - amount);
        return true;
    }

    public void Refund(float amount)
    {
        SetMoney(Money + amount);
    }

    // Money may go negative here (soft bankruptcy).
    public void ApplyDay(float income, float expense)
    {
        IncomePerDay = income;
        ExpensePerDay = expense;
        SetMoney(Money + income - expense);
    }

    private void SetMoney(float value)
    {
        Money = value;
        OnMoneyChanged?.Invoke(Money);
    }
}
