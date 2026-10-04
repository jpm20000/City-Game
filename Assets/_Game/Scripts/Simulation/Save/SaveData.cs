using System;
using System.Collections.Generic;

// Whole-city save (GamePlan §6). Public fields because JsonUtility serializes fields only.
// Grid arrays are row-major (index = y * Width + x). Older versions are upgraded on load by
// SaveMigrations; bump CurrentVersion together with a new migration step.
[Serializable]
public sealed class SaveData
{
    public const int CurrentVersion = 4;
    public const int NoAge = -1;

    public int Version = CurrentVersion;

    public int Day = 1;
    public int Month = 1;
    public int Year = 1;
    public int Speed = (int)GameSpeed.x1;

    public float Money;
    public float IncomePerDay;
    public float ExpensePerDay;
    public float TaxResidential;
    public float TaxCommercial;
    public float TaxIndustrial;

    public int Population;
    public float Happiness;

    public int Width;
    public int Height;
    public byte[] Zones;
    public byte[] Roads;
    public byte[] Levels;

    // v2 (M11): ages and research. Age = AgeDatabase index, NoAge for a city saved without ages
    // (adopted as Industrial when loaded with them). Research projects are TechSystem project Ids.
    public int Age = NoAge;
    public List<string> Researched = new();
    public string ActiveResearch = "";
    public float ResearchProgress;
    public List<string> ResearchQueue = new();
    public byte[] BuiltAges;
    public byte[] Historic;

    // v3 (M13): water pipes (1 = pipe under the cell).
    public byte[] Pipes;

    // v4 (M15): funding per BudgetLine (null / short = 100%), open loans and enacted ordinance ids.
    public float[] Funding;
    public List<LoanRecord> Loans = new();
    public List<string> Ordinances = new();

    public List<BuildingRecord> Buildings = new();
}

// An open loan (M15).
[Serializable]
public struct LoanRecord
{
    public float Amount;
    public float DailyPayment;
    public int DaysLeft;

    public LoanRecord(float amount, float dailyPayment, int daysLeft)
    {
        Amount = amount;
        DailyPayment = dailyPayment;
        DaysLeft = daysLeft;
    }
}

// A player-placed building, re-placed on load by definition id.
[Serializable]
public struct BuildingRecord
{
    public string Id;
    public int X;
    public int Y;
    public int Rotation;

    public BuildingRecord(string id, int x, int y, int rotation)
    {
        Id = id;
        X = x;
        Y = y;
        Rotation = rotation;
    }
}
