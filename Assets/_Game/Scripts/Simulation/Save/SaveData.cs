using System;
using System.Collections.Generic;

// Whole-city save (GamePlan §6). Public fields because JsonUtility serializes fields only.
// Grid arrays are row-major (index = y * Width + x).
[Serializable]
public sealed class SaveData
{
    public const int CurrentVersion = 1;

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

    public List<BuildingRecord> Buildings = new();
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
