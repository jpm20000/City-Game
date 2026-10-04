using UnityEngine;

// The cosmetic vehicles of the building kit (M18f): carts, carriages, trams, trucks and cars as small meshes on
// the shared Kit palette. Local space: on the ground (y = 0), centred, front toward +Z, about 0.2-0.35 cell long,
// so a road cell (1 unit) reads as a real street at game zoom. Headlight and tail-light swatches are emissive,
// so they glow with the day/night cycle's window light.
public static class KitVehicles
{
    // age index -> vehicle names (each one is built by Build)
    public static readonly string[][] ByAge =
    {
        new[] { "handcart", "oxcart" },                  // Medieval
        new[] { "carriage", "oxcart", "handcart" },      // Renaissance
        new[] { "horse_tram", "truck", "carriage" },     // Industrial
        new[] { "car_red", "car_blue", "car_white", "bus", "van" },   // Modern
    };

    private static int C(string name) => KitPalette.Of(name);

    private static void Box(KitMeshBuilder m, float x0, float x1, float y0, float y1, float z0, float z1, string side, string top = null)
    {
        m.Box(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), C(side), top != null ? C(top) : -1);
    }

    private static void Wheels(KitMeshBuilder m, float halfWidth, float[] zs, float radius, string colour)
    {
        foreach (float z in zs)
        {
            Box(m, halfWidth - 0.012f, halfWidth + 0.012f, 0f, radius * 2f, z - radius, z + radius, colour);
            Box(m, -halfWidth - 0.012f, -halfWidth + 0.012f, 0f, radius * 2f, z - radius, z + radius, colour);
        }
    }

    private static void Quad(KitMeshBuilder m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, string swatch, Vector3 outward)
    {
        m.Quad(a, b, c, d, C(swatch), outward);
    }

    // Side windows: a quad on each flank.
    private static void FlankWindows(KitMeshBuilder m, float x, float y0, float y1, float zFrom, float zTo, int count, string swatch)
    {
        float step = (zTo - zFrom) / count;
        for (int i = 0; i < count; i++)
        {
            float z0 = zFrom + i * step + step * 0.15f, z1 = zFrom + (i + 1) * step - step * 0.15f;
            Quad(m, new Vector3(x + 0.002f, y0, z0), new Vector3(x + 0.002f, y0, z1), new Vector3(x + 0.002f, y1, z1), new Vector3(x + 0.002f, y1, z0), swatch, Vector3.right);
            Quad(m, new Vector3(-x - 0.002f, y0, z0), new Vector3(-x - 0.002f, y0, z1), new Vector3(-x - 0.002f, y1, z1), new Vector3(-x - 0.002f, y1, z0), swatch, Vector3.left);
        }
    }

    private static void Lights(KitMeshBuilder m, float halfWidth, float y, float zFront, float zBack)
    {
        foreach (float x in new[] { -halfWidth * 0.6f, halfWidth * 0.6f })
        {
            Quad(m, new Vector3(x - 0.012f, y, zFront + 0.002f), new Vector3(x + 0.012f, y, zFront + 0.002f), new Vector3(x + 0.012f, y + 0.014f, zFront + 0.002f), new Vector3(x - 0.012f, y + 0.014f, zFront + 0.002f), "headlight", Vector3.forward);
            Quad(m, new Vector3(x - 0.012f, y, zBack - 0.002f), new Vector3(x + 0.012f, y, zBack - 0.002f), new Vector3(x + 0.012f, y + 0.014f, zBack - 0.002f), new Vector3(x - 0.012f, y + 0.014f, zBack - 0.002f), "taillight", Vector3.back);
        }
    }

    private static void Animal(KitMeshBuilder m, float zBack, string colour)
    {
        // body, neck and head of a draught animal pulling from the front
        Box(m, -0.028f, 0.028f, 0.035f, 0.085f, zBack, zBack + 0.10f, colour);
        Box(m, -0.02f, 0.02f, 0.07f, 0.115f, zBack + 0.09f, zBack + 0.125f, colour);
        Box(m, -0.016f, 0.016f, 0.085f, 0.108f, zBack + 0.12f, zBack + 0.155f, colour);
        Box(m, -0.026f, -0.014f, 0f, 0.036f, zBack + 0.01f, zBack + 0.03f, "shingle2");
        Box(m, 0.014f, 0.026f, 0f, 0.036f, zBack + 0.01f, zBack + 0.03f, "shingle2");
        Box(m, -0.026f, -0.014f, 0f, 0.036f, zBack + 0.07f, zBack + 0.09f, "shingle2");
        Box(m, 0.014f, 0.026f, 0f, 0.036f, zBack + 0.07f, zBack + 0.09f, "shingle2");
    }

    public static void Build(string name, KitMeshBuilder m)
    {
        switch (name)
        {
            case "handcart":
                Box(m, -0.045f, 0.045f, 0.035f, 0.065f, -0.08f, 0.06f, "woodlight");
                Box(m, -0.03f, 0.03f, 0.065f, 0.095f, -0.07f, 0.0f, "daub2");
                Box(m, -0.05f, -0.043f, 0.035f, 0.075f, -0.08f, 0.06f, "timber");
                Box(m, 0.043f, 0.05f, 0.035f, 0.075f, -0.08f, 0.06f, "timber");
                Wheels(m, 0.05f, new[] { 0f }, 0.028f, "timber");
                Box(m, -0.035f, -0.028f, 0.05f, 0.058f, 0.06f, 0.15f, "timber");
                Box(m, 0.028f, 0.035f, 0.05f, 0.058f, 0.06f, 0.15f, "timber");
                break;
            case "oxcart":
                Box(m, -0.05f, 0.05f, 0.04f, 0.07f, -0.13f, 0.05f, "woodlight");
                Box(m, -0.055f, -0.047f, 0.07f, 0.1f, -0.13f, 0.05f, "timber");
                Box(m, 0.047f, 0.055f, 0.07f, 0.1f, -0.13f, 0.05f, "timber");
                Box(m, -0.04f, 0.04f, 0.07f, 0.12f, -0.12f, -0.03f, "thatch");
                Wheels(m, 0.055f, new[] { -0.07f }, 0.032f, "timber");
                Animal(m, 0.08f, "woodlight");
                break;
            case "carriage":
                Box(m, -0.045f, 0.045f, 0.04f, 0.105f, -0.11f, 0.02f, "terracotta2");
                Box(m, -0.047f, 0.047f, 0.105f, 0.115f, -0.115f, 0.025f, "timber");
                FlankWindows(m, 0.045f, 0.065f, 0.095f, -0.10f, 0.01f, 2, "win_warm");
                Wheels(m, 0.05f, new[] { -0.08f, 0.0f }, 0.03f, "timber");
                Animal(m, 0.05f, "doorwood");
                break;
            case "horse_tram":
                Box(m, -0.05f, 0.05f, 0.03f, 0.105f, -0.16f, 0.08f, "awnwhite");
                Box(m, -0.052f, 0.052f, 0.03f, 0.05f, -0.16f, 0.08f, "awnred");
                Box(m, -0.052f, 0.052f, 0.105f, 0.118f, -0.165f, 0.085f, "roofgrey");
                FlankWindows(m, 0.05f, 0.06f, 0.095f, -0.15f, 0.07f, 4, "win_warm");
                Wheels(m, 0.05f, new[] { -0.12f, 0.04f }, 0.022f, "iron");
                Animal(m, 0.08f, "woodlight");
                break;
            case "truck":
                Box(m, -0.045f, 0.045f, 0.03f, 0.095f, 0.03f, 0.115f, "iron");
                Box(m, -0.04f, 0.04f, 0.07f, 0.09f, 0.095f, 0.116f, "glassdark");
                Box(m, -0.05f, 0.05f, 0.035f, 0.075f, -0.14f, 0.03f, "woodlight");
                Box(m, -0.052f, -0.046f, 0.075f, 0.1f, -0.14f, 0.03f, "timber");
                Box(m, 0.046f, 0.052f, 0.075f, 0.1f, -0.14f, 0.03f, "timber");
                Wheels(m, 0.048f, new[] { -0.09f, 0.075f }, 0.025f, "dark");
                Lights(m, 0.045f, 0.05f, 0.115f, -0.14f);
                break;
            case "car_red":
            case "car_blue":
            case "car_white":
                {
                    string body = name == "car_red" ? "awnred" : name == "car_blue" ? "awnblue" : "white";
                    Box(m, -0.045f, 0.045f, 0.02f, 0.058f, -0.1f, 0.1f, body);
                    Box(m, -0.04f, 0.04f, 0.058f, 0.088f, -0.05f, 0.05f, "glassdark", body);
                    Wheels(m, 0.046f, new[] { -0.065f, 0.065f }, 0.02f, "dark");
                    Lights(m, 0.045f, 0.035f, 0.1f, -0.1f);
                    break;
                }
            case "bus":
                Box(m, -0.05f, 0.05f, 0.02f, 0.105f, -0.17f, 0.17f, "awnteal", "roofgrey");
                Box(m, -0.052f, 0.052f, 0.02f, 0.04f, -0.17f, 0.17f, "awnwhite");
                FlankWindows(m, 0.05f, 0.06f, 0.095f, -0.15f, 0.15f, 6, "win_cool");
                Box(m, -0.045f, 0.045f, 0.06f, 0.095f, 0.169f, 0.172f, "glassdark");
                Wheels(m, 0.052f, new[] { -0.11f, 0.11f }, 0.022f, "dark");
                Lights(m, 0.05f, 0.032f, 0.172f, -0.17f);
                break;
            default:        // van
                Box(m, -0.045f, 0.045f, 0.02f, 0.09f, -0.12f, 0.1f, "white");
                Box(m, -0.04f, 0.04f, 0.06f, 0.085f, 0.05f, 0.101f, "glassdark");
                Box(m, -0.047f, 0.047f, 0.02f, 0.035f, -0.12f, 0.1f, "steel");
                Wheels(m, 0.046f, new[] { -0.07f, 0.07f }, 0.021f, "dark");
                Lights(m, 0.045f, 0.036f, 0.1f, -0.12f);
                break;
        }
    }
}
