using System;
using System.Collections.Generic;
using UnityEngine;

// The building kit's palette and designs (M18b). One Spec describes a building per age, zone and level; the
// three variants of a slot differ by wall / roof colour picks, plan size, roof direction and a few
// features. Everything is data-driven and deterministic: re-running the generator rewrites the same
// meshes. Local space: pivot at the ground centre of a 1x1 cell, +Y up, the front (street side) is +Z.
public static class KitPalette
{
    public readonly struct Entry
    {
        public readonly string Name;
        public readonly Color Albedo;
        public readonly Color Emission;
        public Entry(string name, float r, float g, float b, float er = 0f, float eg = 0f, float eb = 0f)
        {
            Name = name;
            Albedo = new Color(r, g, b, 1f);
            Emission = new Color(er, eg, eb, 1f);
        }
    }

    public const int Columns = 16;          // 16 x 16 swatches of 4 px
    public const int SwatchPixels = 4;

    public static readonly Entry[] Entries =
    {
        // walls
        new("daub", .86f, .80f, .66f), new("daub2", .80f, .72f, .55f), new("stone", .60f, .58f, .54f), new("stone2", .52f, .50f, .47f),
        new("timber", .33f, .22f, .15f), new("woodlight", .55f, .40f, .26f), new("doorwood", .30f, .20f, .13f),
        new("ochre", .88f, .72f, .45f), new("sand", .86f, .78f, .62f), new("cream", .93f, .89f, .79f), new("rose", .85f, .64f, .56f),
        new("brickred", .60f, .28f, .22f), new("brickdark", .46f, .23f, .20f), new("brickyellow", .76f, .66f, .42f),
        new("concrete", .74f, .75f, .76f), new("concrete2", .60f, .62f, .64f), new("white", .92f, .93f, .94f),
        new("soot", .38f, .38f, .40f), new("iron", .25f, .27f, .30f), new("tin", .50f, .52f, .55f), new("steel", .66f, .69f, .73f),
        // roofs
        new("thatch", .74f, .60f, .32f), new("thatch2", .62f, .50f, .28f), new("shingle", .42f, .33f, .27f), new("shingle2", .34f, .28f, .24f),
        new("terracotta", .75f, .39f, .27f), new("terracotta2", .66f, .33f, .24f), new("slate", .30f, .33f, .38f), new("slate2", .24f, .26f, .31f),
        new("roofgrey", .45f, .47f, .50f), new("roofgreen", .35f, .50f, .40f), new("solar", .12f, .20f, .40f),
        // commercial accents
        new("awnteal", .20f, .52f, .64f), new("awnred", .72f, .25f, .22f), new("awnblue", .25f, .38f, .65f), new("awngreen", .28f, .55f, .40f),
        new("awnwhite", .92f, .90f, .85f), new("signteal", .15f, .45f, .58f), new("signgold", .80f, .65f, .25f),
        // glass, windows (emissive), dark openings
        new("glassteal", .35f, .62f, .72f, .35f, .55f, .65f), new("glassdark", .20f, .30f, .38f, .30f, .45f, .55f),
        new("win_warm", .18f, .22f, .28f, 1.00f, .72f, .35f), new("win_cool", .28f, .40f, .50f, .85f, .95f, 1.00f),
        new("dark", .10f, .10f, .12f), new("clock", .95f, .93f, .85f, .50f, .48f, .40f),
    };

    private static readonly Dictionary<string, int> s_Index = BuildIndex();

    private static Dictionary<string, int> BuildIndex()
    {
        var index = new Dictionary<string, int>();
        for (int i = 0; i < Entries.Length; i++) index.Add(Entries[i].Name, i);
        return index;
    }

    public static int Of(string name)
    {
        if (!s_Index.TryGetValue(name, out int i)) throw new ArgumentException("unknown palette colour '" + name + "'");
        return i;
    }

    // Centre of a swatch in the palette texture (row 0 at the bottom).
    public static Vector2 Uv(int swatch)
    {
        int size = Columns * SwatchPixels;
        int column = swatch % Columns, row = swatch / Columns;
        return new Vector2((column * SwatchPixels + SwatchPixels * 0.5f) / size, (row * SwatchPixels + SwatchPixels * 0.5f) / size);
    }
}

public enum KitRoof { Gable, Hip, Flat, Lean, SawTooth }

public sealed class KitSpec
{
    public float W = 0.6f, D = 0.5f;                // footprint
    public int Floors = 1;
    public float FloorH = 0.25f;
    public string[] Wall = { "daub" };
    public KitRoof Roof = KitRoof.Gable;
    public string[] RoofCol = { "thatch" };
    public float Rise = 0.2f;
    public float Over = 0.03f;
    public bool AlongX;                             // gable ridge parallel to the street
    public string Win = "win_warm";
    public int Cols = 2;                            // windows per floor on the front
    public int SideCols = 1;
    public bool Ribbon;                             // continuous glass bands instead of single windows
    public bool Timber;                             // dark frame beams on the walls
    public bool Jetty;                              // upper floors overhang the street
    public int Doors = 1;
    public string[] Awning;                         // striped awning colours (cream + one)
    public string Sign;                             // board above the awning
    public bool Arcade;                             // dark openings along the ground floor
    public bool Balcony;
    public bool Cornice;
    public int Chimneys;
    public float ChimneyH = 0.12f;
    public string ChimneyCol = "stone";
    public int Teeth = 3;                           // saw-tooth roofs
    public float[] Stacks = Array.Empty<float>();   // x fractions of the half width; one stack each (back of the plan)
    public float StackR = 0.045f, StackH = 0.9f;
    public string StackCol = "brickdark";
    public bool Cone;                               // conical kiln chimney instead of straight stacks
    public int Tanks;
    public bool Solar, Carport, Crown, Hoist, Clock;
    public float TowerX = 0f, TowerZ = 0f, TowerSize = 0f, TowerH = 0f;   // fractions of the half plan; size / height in cells
    public bool TowerCone;
    public bool Setback;                            // towers: the upper half is narrower
    public bool FlipV2 = true;                      // variant c turns the ridge
}

public static class KitDesigns
{
    private static KitSpec S(Action<KitSpec> set)
    {
        var spec = new KitSpec();
        set(spec);
        return spec;
    }

    private static readonly string[] WarmWin = { "win_warm" };

    // slot index = zone (0 R, 1 C, 2 I) * 3 + level - 1
    public static KitSpec[] ForAge(string age)
    {
        switch (age)
        {
            case "medieval": return Medieval();
            case "renaissance": return Renaissance();
            case "industrial": return Industrial();
            default: return Modern();
        }
    }

    private static KitSpec[] Medieval() => new[]
    {
        // Residential: cottage, jettied house, row house
        S(s => { s.W = .52f; s.D = .48f; s.Floors = 1; s.FloorH = .24f; s.Wall = new[] { "daub", "daub2", "stone" }; s.RoofCol = new[] { "thatch", "thatch2", "thatch" }; s.Rise = .22f; s.Chimneys = 1; s.ChimneyH = .06f; s.Cols = 2; s.Timber = true; }),
        S(s => { s.W = .58f; s.D = .50f; s.Floors = 2; s.FloorH = .27f; s.Wall = new[] { "daub", "daub2", "cream" }; s.RoofCol = new[] { "thatch", "shingle", "thatch2" }; s.Rise = .24f; s.Jetty = true; s.Timber = true; s.Cols = 2; s.Chimneys = 1; }),
        S(s => { s.W = .80f; s.D = .52f; s.Floors = 3; s.FloorH = .28f; s.Wall = new[] { "daub", "cream", "daub2" }; s.RoofCol = new[] { "shingle", "thatch2", "shingle2" }; s.Rise = .28f; s.AlongX = true; s.Jetty = true; s.Timber = true; s.Cols = 3; s.Chimneys = 2; s.Doors = 2; }),
        // Commercial: market stall, shop-house, guild hall
        S(s => { s.W = .68f; s.D = .52f; s.Floors = 1; s.FloorH = .24f; s.Wall = new[] { "woodlight", "daub2", "timber" }; s.Roof = KitRoof.Lean; s.RoofCol = new[] { "shingle", "thatch2", "shingle2" }; s.Rise = .10f; s.Awning = new[] { "awnteal", "awnred", "awnblue" }; s.Sign = "signteal"; s.Cols = 2; s.Timber = true; }),
        S(s => { s.W = .62f; s.D = .54f; s.Floors = 2; s.FloorH = .28f; s.Wall = new[] { "daub", "cream", "daub2" }; s.RoofCol = new[] { "shingle", "shingle2", "thatch2" }; s.Rise = .22f; s.Jetty = true; s.Timber = true; s.Awning = new[] { "awnteal", "awnblue", "awnred" }; s.Sign = "signteal"; s.Cols = 2; }),
        S(s => { s.W = .80f; s.D = .60f; s.Floors = 2; s.FloorH = .34f; s.Wall = new[] { "stone", "daub", "stone2" }; s.RoofCol = new[] { "shingle2", "slate", "shingle" }; s.Rise = .26f; s.AlongX = true; s.Timber = true; s.Cols = 3; s.Sign = "signteal"; s.TowerX = -.55f; s.TowerZ = -.2f; s.TowerSize = .22f; s.TowerH = .62f; s.TowerCone = true; s.FlipV2 = false; }),
        // Industrial: smithy, workshop yard, mill shed
        S(s => { s.W = .66f; s.D = .58f; s.Floors = 1; s.FloorH = .26f; s.Wall = new[] { "stone", "stone2", "timber" }; s.Roof = KitRoof.Lean; s.RoofCol = new[] { "shingle2", "slate2", "shingle2" }; s.Rise = .12f; s.Cols = 1; s.Chimneys = 1; s.ChimneyH = .22f; s.ChimneyCol = "stone2"; }),
        S(s => { s.W = .82f; s.D = .62f; s.Floors = 1; s.FloorH = .32f; s.Wall = new[] { "timber", "stone2", "woodlight" }; s.RoofCol = new[] { "shingle2", "shingle", "slate2" }; s.Rise = .22f; s.AlongX = true; s.Cols = 2; s.Stacks = new[] { .5f }; s.StackH = .72f; s.StackCol = "stone"; }),
        S(s => { s.W = .86f; s.D = .66f; s.Floors = 2; s.FloorH = .30f; s.Wall = new[] { "woodlight", "stone2", "timber" }; s.RoofCol = new[] { "shingle", "shingle2", "slate2" }; s.Rise = .30f; s.AlongX = true; s.Cols = 3; s.Chimneys = 1; s.ChimneyH = .14f; s.Hoist = true; }),
    };

    private static KitSpec[] Renaissance() => new[]
    {
        S(s => { s.W = .58f; s.D = .50f; s.Floors = 1; s.FloorH = .30f; s.Wall = new[] { "ochre", "sand", "rose" }; s.Roof = KitRoof.Hip; s.RoofCol = new[] { "terracotta", "terracotta2", "terracotta" }; s.Rise = .22f; s.Cols = 2; s.Chimneys = 1; s.ChimneyH = .08f; }),
        S(s => { s.W = .60f; s.D = .54f; s.Floors = 2; s.FloorH = .34f; s.Wall = new[] { "sand", "ochre", "cream" }; s.Roof = KitRoof.Hip; s.RoofCol = new[] { "terracotta", "terracotta2", "terracotta" }; s.Rise = .26f; s.Balcony = true; s.Cols = 2; s.Cornice = true; s.Chimneys = 1; }),
        S(s => { s.W = .84f; s.D = .66f; s.Floors = 3; s.FloorH = .38f; s.Wall = new[] { "sand", "ochre", "rose" }; s.Roof = KitRoof.Hip; s.RoofCol = new[] { "terracotta2", "terracotta", "terracotta2" }; s.Rise = .30f; s.Arcade = true; s.Cornice = true; s.Cols = 4; s.Chimneys = 2; s.Balcony = true; }),
        S(s => { s.W = .72f; s.D = .54f; s.Floors = 1; s.FloorH = .32f; s.Wall = new[] { "cream", "sand", "ochre" }; s.Roof = KitRoof.Hip; s.RoofCol = new[] { "terracotta", "terracotta2", "terracotta" }; s.Rise = .18f; s.Arcade = true; s.Awning = new[] { "awnteal", "awnred", "awnblue" }; s.Sign = "signteal"; s.Cols = 3; }),
        S(s => { s.W = .62f; s.D = .60f; s.Floors = 3; s.FloorH = .32f; s.Wall = new[] { "brickyellow", "sand", "rose" }; s.RoofCol = new[] { "terracotta2", "slate", "terracotta" }; s.Rise = .34f; s.Cols = 2; s.Sign = "signteal"; s.Awning = new[] { "awnteal", "awnred", "awngreen" }; s.Cornice = true; }),
        S(s => { s.W = .84f; s.D = .68f; s.Floors = 2; s.FloorH = .46f; s.Wall = new[] { "cream", "sand", "white" }; s.Roof = KitRoof.Hip; s.RoofCol = new[] { "roofgreen", "terracotta", "roofgreen" }; s.Rise = .20f; s.Cornice = true; s.Arcade = true; s.Cols = 4; s.Sign = "signteal"; s.TowerX = 0f; s.TowerZ = -.1f; s.TowerSize = .30f; s.TowerH = .55f; s.TowerCone = true; s.FlipV2 = false; }),
        S(s => { s.W = .72f; s.D = .60f; s.Floors = 1; s.FloorH = .32f; s.Wall = new[] { "brickred", "brickyellow", "brickdark" }; s.RoofCol = new[] { "slate", "terracotta2", "slate2" }; s.Rise = .20f; s.AlongX = true; s.Cols = 2; s.Chimneys = 1; s.ChimneyH = .18f; s.ChimneyCol = "brickdark"; }),
        S(s => { s.W = .84f; s.D = .64f; s.Floors = 2; s.FloorH = .32f; s.Wall = new[] { "brickdark", "brickred", "stone" }; s.RoofCol = new[] { "terracotta2", "slate2", "terracotta" }; s.Rise = .24f; s.AlongX = true; s.Cols = 3; s.Hoist = true; }),
        S(s => { s.W = .74f; s.D = .74f; s.Floors = 1; s.FloorH = .50f; s.Wall = new[] { "brickred", "brickdark", "brickyellow" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "slate2", "soot", "slate2" }; s.Cols = 1; s.Stacks = new[] { 0f }; s.StackR = .17f; s.StackH = 1.45f; s.Cone = true; s.StackCol = "brickred"; }),
    };

    private static KitSpec[] Industrial() => new[]
    {
        S(s => { s.W = .54f; s.D = .50f; s.Floors = 1; s.FloorH = .28f; s.Wall = new[] { "brickred", "brickyellow", "brickdark" }; s.RoofCol = new[] { "slate", "slate2", "terracotta2" }; s.Rise = .20f; s.Cols = 2; s.Chimneys = 1; s.ChimneyH = .10f; s.ChimneyCol = "brickdark"; }),
        S(s => { s.W = .84f; s.D = .54f; s.Floors = 2; s.FloorH = .32f; s.Wall = new[] { "brickred", "brickyellow", "brickdark" }; s.RoofCol = new[] { "slate", "slate2", "slate" }; s.Rise = .22f; s.AlongX = true; s.Cols = 3; s.Doors = 3; s.Chimneys = 3; s.ChimneyH = .10f; s.ChimneyCol = "brickdark"; s.FlipV2 = false; }),
        S(s => { s.W = .80f; s.D = .64f; s.Floors = 4; s.FloorH = .34f; s.Wall = new[] { "brickred", "brickdark", "brickyellow" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "slate2", "roofgrey", "slate2" }; s.Cols = 4; s.SideCols = 2; s.Cornice = true; s.Chimneys = 2; s.ChimneyH = .18f; s.ChimneyCol = "brickdark"; }),
        S(s => { s.W = .64f; s.D = .54f; s.Floors = 1; s.FloorH = .34f; s.Wall = new[] { "brickyellow", "brickred", "cream" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "slate2", "roofgrey", "slate" }; s.Awning = new[] { "awnblue", "awnteal", "awnred" }; s.Sign = "signteal"; s.Cols = 2; s.Cornice = true; }),
        S(s => { s.W = .84f; s.D = .70f; s.Floors = 3; s.FloorH = .36f; s.Wall = new[] { "brickyellow", "sand", "brickred" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "slate2", "roofgrey", "slate" }; s.Cols = 5; s.SideCols = 3; s.Awning = new[] { "awnteal", "awnblue", "awnred" }; s.Sign = "signteal"; s.Cornice = true; }),
        S(s => { s.W = .70f; s.D = .64f; s.Floors = 5; s.FloorH = .37f; s.Wall = new[] { "brickred", "concrete", "brickyellow" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "slate2", "roofgrey", "slate2" }; s.Cols = 4; s.SideCols = 3; s.Cornice = true; s.Clock = true; s.TowerX = .45f; s.TowerZ = .1f; s.TowerSize = .16f; s.TowerH = .35f; s.Sign = "signteal"; s.FlipV2 = false; }),
        S(s => { s.W = .90f; s.D = .74f; s.Floors = 1; s.FloorH = .34f; s.Wall = new[] { "brickdark", "brickred", "soot" }; s.Roof = KitRoof.SawTooth; s.RoofCol = new[] { "slate2", "tin", "slate" }; s.Rise = .20f; s.Teeth = 3; s.Win = "win_cool"; s.Cols = 3; s.SideCols = 2; s.Stacks = new[] { .7f }; s.StackH = .90f; }),
        S(s => { s.W = .84f; s.D = .68f; s.Floors = 3; s.FloorH = .28f; s.Wall = new[] { "brickred", "brickdark", "brickyellow" }; s.RoofCol = new[] { "slate2", "slate", "tin" }; s.Rise = .20f; s.AlongX = true; s.Win = "win_cool"; s.Cols = 4; s.SideCols = 2; s.Stacks = new[] { -.6f }; s.StackR = .06f; s.StackH = 1.60f; s.FlipV2 = false; }),
        S(s => { s.W = .92f; s.D = .76f; s.Floors = 1; s.FloorH = .62f; s.Wall = new[] { "iron", "soot", "brickdark" }; s.RoofCol = new[] { "tin", "slate2", "roofgrey" }; s.Rise = .16f; s.AlongX = true; s.Win = "win_cool"; s.Cols = 5; s.SideCols = 3; s.Stacks = new[] { -.7f, .55f }; s.StackR = .06f; s.StackH = 1.75f; s.StackCol = "soot"; s.FlipV2 = false; }),
    };

    private static KitSpec[] Modern() => new[]
    {
        S(s => { s.W = .66f; s.D = .50f; s.Floors = 1; s.FloorH = .30f; s.Wall = new[] { "white", "concrete", "cream" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "roofgrey", "roofgreen", "concrete2" }; s.Win = "win_cool"; s.Cols = 3; s.Carport = true; }),
        S(s => { s.W = .76f; s.D = .60f; s.Floors = 3; s.FloorH = .32f; s.Wall = new[] { "concrete", "white", "cream" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "roofgrey", "roofgreen", "concrete2" }; s.Win = "win_cool"; s.Cols = 4; s.SideCols = 2; s.Balcony = true; }),
        S(s => { s.W = .50f; s.D = .50f; s.Floors = 8; s.FloorH = .34f; s.Wall = new[] { "concrete", "white", "concrete2" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "roofgrey", "concrete2", "roofgrey" }; s.Win = "win_cool"; s.Cols = 3; s.SideCols = 3; s.Setback = true; s.Crown = true; }),
        S(s => { s.W = .76f; s.D = .60f; s.Floors = 1; s.FloorH = .36f; s.Wall = new[] { "glassteal", "glassdark", "white" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "roofgrey", "concrete2", "roofgrey" }; s.Win = "glassteal"; s.Ribbon = true; s.Sign = "signteal"; s.Awning = new[] { "awnteal", "awnblue", "awnwhite" }; }),
        S(s => { s.W = .82f; s.D = .56f; s.Floors = 5; s.FloorH = .34f; s.Wall = new[] { "glassteal", "glassdark", "concrete" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "roofgrey", "concrete2", "roofgrey" }; s.Win = "glassdark"; s.Ribbon = true; s.Cornice = true; s.FlipV2 = false; }),
        S(s => { s.W = .56f; s.D = .56f; s.Floors = 9; s.FloorH = .335f; s.Wall = new[] { "glassteal", "glassdark", "steel" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "roofgrey", "concrete2", "roofgrey" }; s.Win = "glassdark"; s.Ribbon = true; s.Setback = true; s.Crown = true; }),
        S(s => { s.W = .90f; s.D = .78f; s.Floors = 1; s.FloorH = .42f; s.Wall = new[] { "tin", "steel", "concrete2" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "roofgrey", "concrete2", "roofgrey" }; s.Win = "win_cool"; s.Cols = 4; s.SideCols = 3; s.Doors = 3; }),
        S(s => { s.W = .90f; s.D = .80f; s.Floors = 1; s.FloorH = .36f; s.Wall = new[] { "steel", "concrete", "tin" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "roofgrey", "concrete2", "roofgrey" }; s.Win = "win_cool"; s.Cols = 3; s.SideCols = 3; s.Tanks = 3; }),
        S(s => { s.W = .90f; s.D = .80f; s.Floors = 1; s.FloorH = .64f; s.Wall = new[] { "white", "concrete", "steel" }; s.Roof = KitRoof.Flat; s.RoofCol = new[] { "roofgrey", "concrete2", "roofgrey" }; s.Win = "win_cool"; s.Cols = 4; s.SideCols = 3; s.Solar = true; s.Doors = 2; }),
    };
}
