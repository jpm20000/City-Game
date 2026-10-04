using System;

// The twelve Medieval objectives (M19f), in order. Text is tunable here; the numbers are what a new player reaches
// comfortably (TutorialPlaythroughTests plays them with the harness player).
public static class TutorialContent
{
    public const string WellId = "well";
    public const string ParkId = "park";
    public const string MonasteryId = "monastery";
    public const string CommonsTech = "commons";

    private static (int, int) Of(int done, int needed) => (done, needed);

    public static readonly TutorialObjective[] Objectives =
    {
        new TutorialObjective
        {
            Id = "road", Title = "Lay a road",
            Body = "Pick the Road tool and drag from the edge of the map inward. Everything grows beside a road that reaches the edge.",
            Unit = "road cells", Highlight = "Road", Measure = s => Of(s.ConnectedRoads, 8),
        },
        new TutorialObjective
        {
            Id = "homes", Title = "Zone homes",
            Body = "Choose Residential and paint a strip of land right beside your road. Villagers will build houses there.",
            Unit = "cells", Highlight = "Residential", Measure = s => Of(s.ResidentialCells, 6),
        },
        new TutorialObjective
        {
            Id = "well", Title = "Dig a well",
            Body = "Medieval homes need water to grow past the first level. Place a Well within a few cells of your houses.",
            Highlight = "Well", Measure = s => Of(s.PlacedCount(WellId), 1),
        },
        new TutorialObjective
        {
            Id = "villagers", Title = "First villagers",
            Body = "Give the village a little time. Speed the clock up if you like, and click a zone to see what holds it back.",
            Unit = "villagers", Help = true, Measure = s => Of(s.Population, 10),
        },
        new TutorialObjective
        {
            Id = "work", Title = "Shops and crafts",
            Body = "People need work. Zone some Commercial land for shops and Industrial (crafts) land for workshops, beside the road.",
            Unit = "zoned cells", Highlight = "Commercial", Measure = s => Of(Math.Min(s.CommercialCells, 4) + Math.Min(s.IndustrialCells, 4), 8),
        },
        new TutorialObjective
        {
            Id = "books", Title = "Check the books",
            Body = "Open the Budget panel to see what the village earns and spends each day.",
            Highlight = "Budget", Measure = s => Of(s.BudgetOpened ? 1 : 0, 1),
        },
        new TutorialObjective
        {
            Id = "research", Title = "Start research",
            Body = "Open Research and pick any technology. Research points come from your people and buildings.",
            Highlight = "Research", Measure = s => Of(s.ResearchActive ? 1 : 0, 1),
        },
        new TutorialObjective
        {
            Id = "green", Title = "A village green",
            Body = "Research Commons, then place a Park. Parks make nearby homes happier.",
            Unit = "steps", Highlight = "Research",
            Measure = s => Of((s.Has(CommonsTech) ? 1 : 0) + (s.PlacedCount(ParkId) > 0 ? 1 : 0), 2),
        },
        new TutorialObjective
        {
            Id = "views", Title = "Read the land",
            Body = "Open any info view (Water, Parks, Pollution) to see how the village is doing. Click it again to close it.",
            Highlight = "Views", Measure = s => Of(s.ViewUsed ? 1 : 0, 1),
        },
        new TutorialObjective
        {
            Id = "growing", Title = "A growing village",
            Body = "Keep zoning and building. Homes, work and water have to grow together.",
            Unit = "villagers", Help = true, Measure = s => Of(s.Population, 60),
        },
        new TutorialObjective
        {
            Id = "learning", Title = "Seat of learning",
            Body = "Research Masonry and then Monasticism, and place a Monastery. It adds research points and happiness.",
            Highlight = "Research", Measure = s => Of(s.PlacedCount(MonasteryId), 1),
        },
        new TutorialObjective
        {
            Id = "age", Title = "A new age",
            Body = "Open Research and Advance to the Renaissance once it says you are ready: enough techs, research points and people.",
            Highlight = "Research", Measure = s => Of(s.Age >= 1 ? 1 : 0, 1),
        },
    };

    public static int Count => Objectives.Length;
}
