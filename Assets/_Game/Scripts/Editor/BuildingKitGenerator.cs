using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Generates the M18b building kit: the palette textures, the shared Kit material, one mesh and one prefab
// per variant (4 ages x R/C/I x levels 1-3 x 3 variants) and wires the prefabs into the four
// AgeVisualSets. Menu: CityBuilder > Generate Building Kit. Deterministic and in place: a re-run rewrites
// the same assets and keeps their GUIDs. Hand-made art replaces a slot by editing the AgeVisualSet;
// re-running the generator overwrites the slots it owns, so keep hand-made prefabs out of a re-run
// (or untick the slot) once you start replacing them.
public static class BuildingKitGenerator
{
    public const string KitFolder = "Assets/_Game/Art/Kit";
    public const string PrefabFolder = "Assets/_Game/Prefabs/Kit";
    public const string MaterialPath = KitFolder + "/Kit.mat";
    public const int Variants = 3;

    private static readonly string[] s_Ages = { "medieval", "renaissance", "industrial", "modern" };
    private static readonly string[] s_ZoneFields = { "m_Residential", "m_Commercial", "m_Industrial" };
    private static readonly string[] s_ZoneLetters = { "R", "C", "I" };

    [MenuItem("CityBuilder/Generate Building Kit")]
    public static void Generate()
    {
        EnsureFolder(KitFolder);
        EnsureFolder(KitFolder + "/Meshes");
        EnsureFolder(PrefabFolder);

        Material material = BuildMaterial();
        var problems = new List<string>();
        int prefabs = 0, triangles = 0;

        foreach (string age in s_Ages)
        {
            string ageName = char.ToUpper(age[0]) + age.Substring(1);
            EnsureFolder(PrefabFolder + "/" + ageName);
            KitSpec[] specs = KitDesigns.ForAge(age);
            var set = LoadSet(age);
            var so = new SerializedObject(set);

            for (int slot = 0; slot < specs.Length; slot++)
            {
                int zone = slot / 3, level = slot % 3 + 1;
                var slotPrefabs = new List<GameObject>();
                for (int v = 0; v < Variants; v++)
                {
                    string name = $"{ageName}_{s_ZoneLetters[zone]}{level}{(char)('a' + v)}";
                    var builder = new KitMeshBuilder(KitPalette.Uv);
                    Compose(builder, specs[slot], v);
                    Mesh mesh = SaveMesh(builder, KitFolder + "/Meshes/" + name + ".asset");
                    triangles += mesh.triangles.Length / 3;
                    GameObject prefab = SavePrefab(PrefabFolder + "/" + ageName + "/" + name + ".prefab", name, mesh, material);
                    slotPrefabs.Add(prefab);
                    prefabs++;

                    var errors = new List<string>();
                    if (!ArtContract.Validate(prefab, level, errors)) problems.AddRange(errors);
                }

                SerializedProperty array = so.FindProperty(s_ZoneFields[zone]).GetArrayElementAtIndex(level - 1).FindPropertyRelative("Prefabs");
                array.arraySize = slotPrefabs.Count;
                for (int i = 0; i < slotPrefabs.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = slotPrefabs[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
        }

        int vehicles = BuildVehicles(material);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        string summary = $"Building kit: {prefabs} prefabs, {triangles} triangles, {vehicles} vehicles.";
        if (problems.Count == 0) Debug.Log(summary + " Art contract OK.");
        else Debug.LogWarning(summary + $" {problems.Count} contract problem(s):\n" + string.Join("\n", problems));
    }

    // The cosmetic vehicles (M18f): one mesh + prefab per vehicle (no collider, default layer: not selectable) and the
    // VehicleSet asset the runtime reads, one list per age.
    private static int BuildVehicles(Material material)
    {
        EnsureFolder(PrefabFolder + "/Vehicles");
        EnsureFolder("Assets/_Game/Scriptables/Art");
        const string setPath = "Assets/_Game/Scriptables/Art/VehicleSet.asset";
        var set = AssetDatabase.LoadAssetAtPath<VehicleSet>(setPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<VehicleSet>();
            AssetDatabase.CreateAsset(set, setPath);
        }

        var made = new Dictionary<string, GameObject>();
        var so = new SerializedObject(set);
        SerializedProperty ages = so.FindProperty("m_Ages");
        ages.arraySize = KitVehicles.ByAge.Length;
        for (int age = 0; age < KitVehicles.ByAge.Length; age++)
        {
            string[] names = KitVehicles.ByAge[age];
            SerializedProperty list = ages.GetArrayElementAtIndex(age).FindPropertyRelative("Prefabs");
            list.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                if (!made.TryGetValue(names[i], out GameObject prefab))
                {
                    var builder = new KitMeshBuilder(KitPalette.Uv);
                    KitVehicles.Build(names[i], builder);
                    Mesh mesh = SaveMesh(builder, KitFolder + "/Meshes/Vehicle_" + names[i] + ".asset");
                    var root = new GameObject("Vehicle_" + names[i]);
                    root.AddComponent<MeshFilter>().sharedMesh = mesh;
                    root.AddComponent<MeshRenderer>().sharedMaterial = material;
                    prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/Vehicles/Vehicle_" + names[i] + ".prefab");
                    Object.DestroyImmediate(root);
                    made[names[i]] = prefab;
                }
                list.GetArrayElementAtIndex(i).objectReferenceValue = prefab;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(set);
        return made.Count;
    }

    private static ScriptableObject LoadSet(string age)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AgeVisualSet"))
        {
            var set = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (new SerializedObject(set).FindProperty("m_AgeId").stringValue == age) return set;
        }
        throw new System.InvalidOperationException("no AgeVisualSet for " + age);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ---- palette, material ------------------------------------------------------------------------------

    private static Material BuildMaterial()
    {
        int size = KitPalette.Columns * KitPalette.SwatchPixels;
        Texture2D albedo = WritePalette(KitFolder + "/KitPalette.png", size, e => e.Albedo);
        Texture2D emission = WritePalette(KitFolder + "/KitEmission.png", size, e => e.Emission);

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")) { name = "Kit" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetTexture("_BaseMap", albedo);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0f);
        material.SetColor("_SpecColor", new Color(0.08f, 0.08f, 0.08f));
        material.SetTexture("_EmissionMap", emission);
        material.SetColor("_EmissionColor", Color.black);       // the day/night cycle (M18d) raises it at night
        material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D WritePalette(string path, int size, System.Func<KitPalette.Entry, Color> pick)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.black;
        for (int s = 0; s < KitPalette.Entries.Length; s++)
        {
            Color color = pick(KitPalette.Entries[s]);
            int column = s % KitPalette.Columns, row = s / KitPalette.Columns;
            for (int y = 0; y < KitPalette.SwatchPixels; y++)
            {
                for (int x = 0; x < KitPalette.SwatchPixels; x++)
                {
                    pixels[(row * KitPalette.SwatchPixels + y) * size + column * KitPalette.SwatchPixels + x] = color;
                }
            }
        }
        texture.SetPixels32(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Mesh SaveMesh(KitMeshBuilder builder, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            builder.ToMesh(existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        Mesh mesh = builder.ToMesh();
        mesh.name = Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static GameObject SavePrefab(string path, string name, Mesh mesh, Material material)
    {
        var root = new GameObject(name) { layer = ArtContract.BuildingsLayer };
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        var collider = root.AddComponent<BoxCollider>();
        collider.center = mesh.bounds.center;
        collider.size = mesh.bounds.size;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ---- composing a building ---------------------------------------------------------------------------

    private static readonly float[] s_WidthScale = { 1.00f, 0.92f, 1.04f };
    private static readonly float[] s_DepthScale = { 1.00f, 1.05f, 0.94f };

    private static int C(string name) => KitPalette.Of(name);

    private static string Pick(string[] names, int variant) => names[variant % names.Length];

    private static void Compose(KitMeshBuilder m, KitSpec s, int v)
    {
        float w = Mathf.Min(s.W * s_WidthScale[v], 0.90f), d = Mathf.Min(s.D * s_DepthScale[v], 0.84f);
        float x0 = -w / 2f, x1 = w / 2f, z0 = -d / 2f, z1 = d / 2f;
        float fh = s.FloorH, wallH = s.Floors * fh;
        int wall = C(Pick(s.Wall, v)), roofCol = C(Pick(s.RoofCol, v));
        int win = C(s.Win), dark = C("dark"), timber = C("timber"), cream = C("cream");
        bool alongX = s.AlongX ^ (v == 2 && s.FlipV2);
        float over = s.Roof == KitRoof.Flat ? 0.012f : s.Over;
        float jet = s.Jetty && s.Floors >= 2 ? 0.035f : 0f;

        // Body (towers with a setback narrow above 60% of their height).
        float wx0 = x0, wx1 = x1, wz0 = z0, wz1 = z1;      // plan of the top storey (roof, rooftop gear)
        if (s.Setback)
        {
            int lowerFloors = Mathf.Max(1, Mathf.RoundToInt(s.Floors * 0.6f));
            float inset = 0.06f;
            m.Box(new Vector3(x0, 0f, z0), new Vector3(x1, lowerFloors * fh, z1), wall, wall);
            wx0 += inset; wx1 -= inset; wz0 += inset; wz1 -= inset;
            m.Box(new Vector3(wx0, lowerFloors * fh, wz0), new Vector3(wx1, wallH, wz1), wall, roofCol);
            Ribbons(m, s, win, wx0, wx1, wz0, wz1, lowerFloors, s.Floors, fh);
            Ribbons(m, s, win, x0, x1, z0, z1, 0, lowerFloors, fh);
        }
        else if (jet > 0f)
        {
            m.Box(new Vector3(x0, 0f, z0), new Vector3(x1, fh, z1), wall, wall);
            wx0 -= jet; wx1 += jet; wz1 += jet;
            m.Box(new Vector3(wx0, fh, wz0), new Vector3(wx1, wallH, wz1), wall, roofCol);
        }
        else
        {
            m.Box(new Vector3(x0, 0f, z0), new Vector3(x1, wallH, z1), wall, s.Roof == KitRoof.Flat || s.Roof == KitRoof.SawTooth ? roofCol : wall);
        }

        // Windows, doors, trim
        if (!s.Setback)
        {
            if (s.Ribbon) Ribbons(m, s, win, wx0, wx1, wz0, wz1, 0, s.Floors, fh);
            else WindowsAndDoors(m, s, win, dark, wx0, wx1, wz0, wz1, x0, x1, z1, fh, jet);
        }
        else
        {
            WindowsAndDoors(m, s, win, dark, x0, x1, z0, z1, x0, x1, z1, fh, 0f, groundOnly: true);
        }
        if (s.Timber) Beams(m, timber, wx0, wx1, wz0, wz1, s.Floors, fh, jet);
        if (s.Balcony && s.Floors >= 2) m.Box(new Vector3(-0.15f, fh * 1f, wz1), new Vector3(0.15f, fh * 1f + 0.025f, wz1 + 0.07f), cream, cream);
        if (s.Cornice)
        {
            float t = 0.03f;
            m.Box(new Vector3(wx0 - t, wallH - 0.04f, wz0 - t), new Vector3(wx1 + t, wallH, wz1 + t), cream, cream);
        }
        if (s.Arcade && !s.Ribbon) Arcade(m, s, dark, x0, x1, z1, fh);
        if (s.Awning != null) Awning(m, C("awnwhite"), C(Pick(s.Awning, v)), x0 + 0.02f, x1 - 0.02f, z1, fh);
        if (s.Sign != null) m.Box(new Vector3(-0.14f, fh * 0.82f, z1 + (jet > 0f ? 0f : 0f)), new Vector3(0.14f, fh * 0.97f, z1 + 0.025f), C(s.Sign), C("signgold"));

        // Roof
        float roofY = wallH;
        switch (s.Roof)
        {
            case KitRoof.Gable:
                m.Gable(wx0, wx1, wz0, wz1, roofY, s.Rise, alongX, roofCol, wall, over);
                break;
            case KitRoof.Hip:
                m.Hip(wx0, wx1, wz0, wz1, roofY, s.Rise, roofCol, over);
                break;
            case KitRoof.Lean:
                m.Lean(wx0, wx1, wz0, wz1, roofY, s.Rise, roofCol, wall, over);
                break;
            case KitRoof.SawTooth:
                m.SawTooth(wx0, wx1, wz0, wz1, roofY, s.Rise, s.Teeth, roofCol, win, wall);
                break;
            default:
                m.Box(new Vector3(wx0 - over, roofY, wz0 - over), new Vector3(wx1 + over, roofY + 0.03f, wz1 + over), wall, roofCol);
                RooftopGear(m, s, wx0, wx1, wz0, wz1, roofY + 0.03f, C("concrete2"));
                break;
        }

        // Chimneys on the back slope
        for (int i = 0; i < s.Chimneys; i++)
        {
            float fx = s.Chimneys == 1 ? 0.3f : Mathf.Lerp(-0.6f, 0.6f, i / (float)(s.Chimneys - 1));
            float cx = fx * (wx1 - wx0) * 0.5f, cz = z0 + d * 0.22f;
            float top = roofY + (s.Roof == KitRoof.Flat ? 0.03f : s.Rise * 0.55f) + s.ChimneyH;
            m.Box(new Vector3(cx - 0.025f, roofY, cz - 0.025f), new Vector3(cx + 0.025f, top, cz + 0.025f), C(s.ChimneyCol), C("dark"));
        }
        // Stacks
        foreach (float fx in s.Stacks)
        {
            float cx = fx * w * 0.5f, cz = s.Cone ? 0f : z0 + d * 0.22f;
            if (s.Cone) m.Cylinder(cx, cz, s.StackR, 0f, s.StackH, s.StackR * 0.45f, 10, C(s.StackCol), C("dark"));
            else m.Cylinder(cx, cz, s.StackR, roofY - 0.02f, s.StackH, s.StackR * 0.8f, 8, C(s.StackCol), C("dark"));
        }
        // Tower
        if (s.TowerSize > 0f)
        {
            float tx = s.TowerX * w * 0.5f, tz = s.TowerZ * d * 0.5f, hs = s.TowerSize * 0.5f;
            float th = s.TowerH;
            if (s.TowerCone)
            {
                m.Box(new Vector3(tx - hs, 0f, tz - hs), new Vector3(tx + hs, th, tz + hs), wall, wall);
                m.Hip(tx - hs, tx + hs, tz - hs, tz + hs, th, hs * 2.2f, roofCol, 0.02f);
                m.Quad(new Vector3(tx - 0.03f, th * 0.7f, tz + hs + 0.004f), new Vector3(tx + 0.03f, th * 0.7f, tz + hs + 0.004f), new Vector3(tx + 0.03f, th * 0.7f + 0.07f, tz + hs + 0.004f), new Vector3(tx - 0.03f, th * 0.7f + 0.07f, tz + hs + 0.004f), win, Vector3.forward);
            }
            else
            {
                float top = wallH + th;
                m.Box(new Vector3(tx - hs, wallH, tz - hs), new Vector3(tx + hs, top, tz + hs), wall, roofCol);
                if (s.Clock)
                {
                    float cy = wallH + th * 0.55f;
                    m.Quad(new Vector3(tx - 0.045f, cy - 0.045f, tz + hs + 0.004f), new Vector3(tx + 0.045f, cy - 0.045f, tz + hs + 0.004f), new Vector3(tx + 0.045f, cy + 0.045f, tz + hs + 0.004f), new Vector3(tx - 0.045f, cy + 0.045f, tz + hs + 0.004f), C("clock"), Vector3.forward);
                }
            }
        }
        // Special features
        if (s.Tanks > 0)
        {
            for (int i = 0; i < s.Tanks; i++)
            {
                float cx = Mathf.Lerp(x0 + 0.17f, x1 - 0.17f, s.Tanks == 1 ? 0.5f : i / (float)(s.Tanks - 1));
                m.Cylinder(cx, z0 + 0.14f, 0.09f, wallH + 0.03f, wallH + 0.40f, 0.09f, 10, C("steel"), C("concrete"));
            }
        }
        if (s.Solar)
        {
            for (int r = 0; r < 3; r++)
            {
                float zz = Mathf.Lerp(z0 + 0.14f, z1 - 0.16f, r / 2f), y = wallH + 0.03f;
                Vector3 a = new(x0 + 0.1f, y, zz), b = new(x1 - 0.1f, y, zz), c = new(x1 - 0.1f, y + 0.07f, zz - 0.1f), dd = new(x0 + 0.1f, y + 0.07f, zz - 0.1f);
                m.Quad(a, b, c, dd, C("solar"), new Vector3(0f, 1f, 1f));
            }
        }
        if (s.Carport)
        {
            float cx0 = x1 - 0.02f, cx1 = x1 + 0.12f;
            m.Box(new Vector3(cx0, 0.19f, z0 + 0.04f), new Vector3(cx1, 0.215f, z1 - 0.02f), C("concrete2"), C("roofgrey"));
            m.Box(new Vector3(cx1 - 0.02f, 0f, z1 - 0.05f), new Vector3(cx1, 0.19f, z1 - 0.02f), C("steel"));
            m.Box(new Vector3(cx1 - 0.02f, 0f, z0 + 0.04f), new Vector3(cx1, 0.19f, z0 + 0.07f), C("steel"));
        }
        if (s.Crown)
        {
            float cx = 0f, cz = 0f;
            m.Box(new Vector3(wx0 + 0.07f, wallH, wz0 + 0.07f), new Vector3(wx1 - 0.07f, wallH + 0.09f, wz1 - 0.07f), C("steel"), C("roofgrey"));
            m.Cylinder(cx, cz, 0.012f, wallH + 0.09f, wallH + 0.30f, 0.008f, 6, C("steel"));
        }
        if (s.Hoist)
        {
            float top = wallH + s.Rise * 0.7f;
            m.Box(new Vector3(-0.02f, top - 0.03f, z1 - 0.01f), new Vector3(0.02f, top + 0.01f, z1 + 0.14f), C("timber"));
            m.Box(new Vector3(-0.012f, top - 0.14f, z1 + 0.12f), new Vector3(0.012f, top - 0.03f, z1 + 0.14f), C("iron"));
        }
    }

    private static void RooftopGear(KitMeshBuilder m, KitSpec s, float x0, float x1, float z0, float z1, float y, int swatch)
    {
        float w = x1 - x0;
        m.Box(new Vector3(x0 + w * 0.15f, y, z0 + 0.06f), new Vector3(x0 + w * 0.15f + 0.09f, y + 0.045f, z0 + 0.13f), swatch);
        if (w > 0.5f) m.Box(new Vector3(x1 - w * 0.3f, y, z0 + 0.08f), new Vector3(x1 - w * 0.3f + 0.07f, y + 0.035f, z0 + 0.14f), swatch);
    }

    // Window grid on the front (with door gaps on the ground floor) and the two sides.
    private static void WindowsAndDoors(KitMeshBuilder m, KitSpec s, int win, int dark, float x0, float x1, float z0, float z1,
        float baseX0, float baseX1, float baseZ1, float fh, float jet, bool groundOnly = false)
    {
        float width = x1 - x0;
        float winW = Mathf.Min(0.085f, width / Mathf.Max(1, s.Cols) * 0.5f), winH = fh * 0.42f;
        int doorCol = C("doorwood");
        float doorW = 0.075f, doorH = Mathf.Min(fh * 0.7f, 0.2f);

        var doorXs = new List<float>();
        for (int i = 0; i < s.Doors; i++) doorXs.Add(s.Doors == 1 ? 0f : Mathf.Lerp(-0.34f, 0.34f, i / (float)(s.Doors - 1)) * width);
        int floors = groundOnly ? 1 : s.Floors;

        for (int floor = 0; floor < floors; floor++)
        {
            // upper floors of a jettied house sit `jet` further out
            float zf = (floor > 0 && jet > 0f) ? z1 : baseZ1;
            float y = floor * fh + fh * 0.30f;
            for (int c = 0; c < s.Cols; c++)
            {
                float cx = Mathf.Lerp(x0 + width * 0.18f, x1 - width * 0.18f, s.Cols == 1 ? 0.5f : c / (float)(s.Cols - 1));
                if (floor == 0)
                {
                    bool blocked = false;
                    foreach (float dx in doorXs) blocked |= Mathf.Abs(cx - dx) < doorW + winW * 0.6f;
                    if (blocked || s.Arcade) continue;
                }
                Window(m, win, cx, y, zf + 0.004f, winW, winH);
            }
        }
        // front door(s)
        foreach (float dx in doorXs) m.Quad(new Vector3(dx - doorW / 2f, 0f, baseZ1 + 0.005f), new Vector3(dx + doorW / 2f, 0f, baseZ1 + 0.005f), new Vector3(dx + doorW / 2f, doorH, baseZ1 + 0.005f), new Vector3(dx - doorW / 2f, doorH, baseZ1 + 0.005f), doorCol, Vector3.forward);

        // side windows on both walls
        float depth = z1 - z0;
        for (int floor = 0; floor < floors; floor++)
        {
            float y = floor * fh + fh * 0.30f;
            for (int c = 0; c < s.SideCols; c++)
            {
                float cz = Mathf.Lerp(z0 + depth * 0.28f, z1 - depth * 0.28f, s.SideCols == 1 ? 0.5f : c / (float)(s.SideCols - 1));
                float sw = Mathf.Min(0.08f, depth / Mathf.Max(1, s.SideCols) * 0.4f);
                m.Quad(new Vector3(x1 + 0.004f, y, cz - sw / 2f), new Vector3(x1 + 0.004f, y, cz + sw / 2f), new Vector3(x1 + 0.004f, y + winH, cz + sw / 2f), new Vector3(x1 + 0.004f, y + winH, cz - sw / 2f), win, Vector3.right);
                m.Quad(new Vector3(x0 - 0.004f, y, cz - sw / 2f), new Vector3(x0 - 0.004f, y, cz + sw / 2f), new Vector3(x0 - 0.004f, y + winH, cz + sw / 2f), new Vector3(x0 - 0.004f, y + winH, cz - sw / 2f), win, Vector3.left);
            }
        }
    }

    private static void Window(KitMeshBuilder m, int swatch, float cx, float y, float z, float w, float h)
    {
        m.Quad(new Vector3(cx - w / 2f, y, z), new Vector3(cx + w / 2f, y, z), new Vector3(cx + w / 2f, y + h, z), new Vector3(cx - w / 2f, y + h, z), swatch, Vector3.forward);
    }

    // Continuous glass bands round a storey range (modern towers and shops).
    private static void Ribbons(KitMeshBuilder m, KitSpec s, int win, float x0, float x1, float z0, float z1, int firstFloor, int lastFloor, float fh)
    {
        for (int f = firstFloor; f < lastFloor; f++)
        {
            float y0 = f * fh + fh * 0.18f, y1 = f * fh + fh * 0.80f;
            float e = 0.004f;
            m.Quad(new Vector3(x0 + 0.03f, y0, z1 + e), new Vector3(x1 - 0.03f, y0, z1 + e), new Vector3(x1 - 0.03f, y1, z1 + e), new Vector3(x0 + 0.03f, y1, z1 + e), win, Vector3.forward);
            m.Quad(new Vector3(x1 + e, y0, z0 + 0.03f), new Vector3(x1 + e, y0, z1 - 0.03f), new Vector3(x1 + e, y1, z1 - 0.03f), new Vector3(x1 + e, y1, z0 + 0.03f), win, Vector3.right);
            m.Quad(new Vector3(x0 - e, y0, z0 + 0.03f), new Vector3(x0 - e, y0, z1 - 0.03f), new Vector3(x0 - e, y1, z1 - 0.03f), new Vector3(x0 - e, y1, z0 + 0.03f), win, Vector3.left);
        }
    }

    // Dark timber frame: a rail at every floor line and posts at the corners and between windows.
    private static void Beams(KitMeshBuilder m, int timber, float x0, float x1, float z0, float z1, int floors, float fh, float jet)
    {
        float e = 0.005f, t = 0.016f, top = floors * fh;
        int posts = 4;
        for (int i = 0; i < posts; i++)
        {
            float x = Mathf.Lerp(x0 + t, x1 - t, i / (float)(posts - 1));
            // a jettied house has its ground floor `jet` behind the upper floors, so its posts sit further back
            float zLow = z1 - jet + e;
            float lowTop = jet > 0f ? fh : top;
            m.Quad(new Vector3(x - t / 2f, 0f, zLow), new Vector3(x + t / 2f, 0f, zLow), new Vector3(x + t / 2f, lowTop, zLow), new Vector3(x - t / 2f, lowTop, zLow), timber, Vector3.forward);
            if (jet > 0f)
            {
                m.Quad(new Vector3(x - t / 2f, fh, z1 + e), new Vector3(x + t / 2f, fh, z1 + e), new Vector3(x + t / 2f, top, z1 + e), new Vector3(x - t / 2f, top, z1 + e), timber, Vector3.forward);
            }
        }
        for (int f = 1; f <= floors; f++)
        {
            float y = f * fh;
            m.Quad(new Vector3(x0, y - t, z1 + e), new Vector3(x1, y - t, z1 + e), new Vector3(x1, y, z1 + e), new Vector3(x0, y, z1 + e), timber, Vector3.forward);
        }
    }

    // Open arches / dark bays along the ground floor of the front.
    private static void Arcade(KitMeshBuilder m, KitSpec s, int dark, float x0, float x1, float z1, float fh)
    {
        int n = Mathf.Max(3, s.Cols);
        float width = x1 - x0, bay = width / n, open = bay * 0.55f, h = fh * 0.72f;
        for (int i = 0; i < n; i++)
        {
            float cx = x0 + bay * (i + 0.5f);
            m.Quad(new Vector3(cx - open / 2f, 0f, z1 + 0.005f), new Vector3(cx + open / 2f, 0f, z1 + 0.005f), new Vector3(cx + open / 2f, h, z1 + 0.005f), new Vector3(cx - open / 2f, h, z1 + 0.005f), dark, Vector3.forward);
        }
    }

    // A slanted striped awning over the shop front.
    private static void Awning(KitMeshBuilder m, int cream, int colour, float x0, float x1, float z1, float fh)
    {
        int stripes = 6;
        float step = (x1 - x0) / stripes, yHigh = fh * 0.80f, yLow = fh * 0.60f, reach = 0.13f;
        for (int i = 0; i < stripes; i++)
        {
            float xa = x0 + i * step, xb = xa + step;
            m.Quad(new Vector3(xa, yHigh, z1), new Vector3(xb, yHigh, z1), new Vector3(xb, yLow, z1 + reach), new Vector3(xa, yLow, z1 + reach), i % 2 == 0 ? colour : cream, new Vector3(0f, 1f, 1f));
        }
    }
}
