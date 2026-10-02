using System;

// A research project: a tech, or advancing into the next age (paid in RP like a tech).
public sealed class ResearchProject : IEquatable<ResearchProject>
{
    public const string AdvancePrefix = "advance:";

    public TechDefinition Tech { get; }
    public AgeDefinition TargetAge { get; }
    public int TargetAgeIndex { get; }      // -1 for a tech

    public bool IsAdvance => Tech == null;
    public float Cost => IsAdvance ? TargetAge.AdvanceCost : Tech.Cost;
    public string Id => IsAdvance ? AdvancePrefix + TargetAge.Id : Tech.Id;
    public string DisplayName => IsAdvance ? "Advance to " + TargetAge.DisplayName : Tech.DisplayName;

    private ResearchProject(TechDefinition tech, AgeDefinition targetAge, int targetAgeIndex)
    {
        Tech = tech;
        TargetAge = targetAge;
        TargetAgeIndex = targetAgeIndex;
    }

    public static ResearchProject For(TechDefinition tech)
    {
        if (tech == null) throw new ArgumentNullException(nameof(tech));
        return new ResearchProject(tech, null, -1);
    }

    public static ResearchProject Advance(AgeDefinition age, int ageIndex)
    {
        if (age == null) throw new ArgumentNullException(nameof(age));
        return new ResearchProject(null, age, ageIndex);
    }

    public bool Equals(ResearchProject other)
    {
        if (other is null) return false;
        return IsAdvance ? other.IsAdvance && other.TargetAgeIndex == TargetAgeIndex : other.Tech == Tech;
    }

    public override bool Equals(object obj) => obj is ResearchProject other && Equals(other);
    public override int GetHashCode() => IsAdvance ? TargetAgeIndex : Tech.GetHashCode();
    public override string ToString() => Id;
}
