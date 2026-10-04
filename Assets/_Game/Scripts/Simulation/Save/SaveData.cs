using System;
using System.Collections.Generic;

// Whole-city save (GamePlan §6). Public fields because JsonUtility serializes fields only.
// Grid arrays are row-major (index = y * Width + x). Older versions are upgraded on load by
// SaveMigrations; bump CurrentVersion together with a new migration step.
[Serializable]
public sealed class SaveData
{
    public const int CurrentVersion = 7;
    public const int NoAge = -1;
    public const int NoTutorial = -1;

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
    public byte[] Roads;     // v5 (M16): 0 = none, 1..5 = the road's tier (v1-v4: 1 = a road)
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

    // v6 (M17): the Disasters & events switch, the RNG state (16 hex digits) and every hazard's state. Fires, Rubble
    // and Plague are row-major bytes (fire days, rubble days left, plague days or 255 = recovered).
    public bool Disasters;
    public string RandomState = "";
    public byte[] Fires;
    public byte[] Rubble;
    public byte[] Plague;
    public int PlagueCooldown;
    public float PlagueRemainder;
    public int PlagueDeaths;
    public List<BrokenRecord> Broken = new();
    public string PendingEvent = "";
    public int PendingDays;
    public int DaysToNextEvent;
    public List<EventRecord> ActiveEvents = new();
    public List<EventRecord> RecentEvents = new();

    // v7 (M19): the city's name ("" = unnamed) and the tutorial's progress (-1 = no tutorial, else the index of the
    // current objective; past the last = finished).
    public string CityName = "";
    public int Tutorial = NoTutorial;

    public List<BuildingRecord> Buildings = new();
}

// A power plant, water tower or pumping station that is down (M17), by origin cell.
[Serializable]
public struct BrokenRecord
{
    public int X;
    public int Y;
    public int DaysLeft;

    public BrokenRecord(int x, int y, int daysLeft)
    {
        X = x;
        Y = y;
        DaysLeft = daysLeft;
    }
}

// A random event's id with the choice taken (active effects) and the days left: of its effects, or
// until it may be offered again (recent list). Choice is -1 in the recent list. (M17)
[Serializable]
public struct EventRecord
{
    public string Id;
    public int Choice;
    public int DaysLeft;

    public EventRecord(string id, int choice, int daysLeft)
    {
        Id = id;
        Choice = choice;
        DaysLeft = daysLeft;
    }
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
