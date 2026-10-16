using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Import settings for the UI art (M32b) that Tools/gen_ui_art.py writes under Art/UI/<kind>/: Sprite at 300 pixels per
// unit (art is 3x design size, so a 9-slice border draws at its design size on the 100 PPU canvas), the 9-slice border
// from Art/UI/ui_art.json, mipmaps for icons only (they shrink up to 3.75x at the smallest UI scale), cursors as Cursor
// textures. The small kinds are packed in Art/UI/UiAtlas.spriteatlasv2 (mipmapped, compressed there). Re-running the script re-imports the PNGs; a border-only change re-imports through the json. UiArtTests
// checks every import against the json.
public sealed class UiArtImporter : AssetPostprocessor
{
    public const string Root = "Assets/_Game/Art/UI/";
    public const string ManifestPath = Root + "ui_art.json";
    public const float PixelsPerUnit = 300f;
    private static readonly HashSet<string> IconKinds = new HashSet<string> { "hud", "tools", "buildings", "controls" };
    // The kinds packed in UiAtlas.spriteatlasv2 (the folders it holds); backgrounds, events and the logo are too big.
    public static readonly HashSet<string> AtlasKinds = new HashSet<string>
        { "hud", "tools", "buildings", "controls", "buttons", "frames", "meters", "hudbar" };

    [Serializable]
    public sealed class Entry
    {
        public string name;
        public string kind;
        public int[] size;
        public int[] border;    // left, bottom, right, top (Unity's spriteBorder order); empty = no border
        public int[] hotspot;
    }

    [Serializable]
    private sealed class Manifest
    {
        public Entry[] sprites;
    }

    public override uint GetVersion() => 2;

    public static Dictionary<string, Entry> LoadManifest()
    {
        var entries = new Dictionary<string, Entry>();
        if (!File.Exists(ManifestPath)) return entries;
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        if (manifest?.sprites == null) return entries;
        foreach (Entry e in manifest.sprites) entries[e.name] = e;
        return entries;
    }

    public static Vector4 BorderOf(Entry entry) =>
        entry?.border == null || entry.border.Length != 4
            ? Vector4.zero
            : new Vector4(entry.border[0], entry.border[1], entry.border[2], entry.border[3]);

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Root, StringComparison.Ordinal)) return;
        var importer = (TextureImporter)assetImporter;
        string kind = Path.GetFileName(Path.GetDirectoryName(assetPath));
        LoadManifest().TryGetValue(Path.GetFileNameWithoutExtension(assetPath), out Entry entry);

        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.isReadable = false;
        if (kind == "cursors")
        {
            importer.textureType = TextureImporterType.Cursor;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            return;
        }

        bool icon = IconKinds.Contains(kind);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;   // 9-slicing wants the full rect
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.spriteBorder = BorderOf(entry);
        importer.mipmapEnabled = icon;
        importer.filterMode = icon ? FilterMode.Trilinear : FilterMode.Bilinear;
        // Packed kinds stay uncompressed (the atlas compresses once, from clean pixels); the big pictures compress here.
        importer.textureCompression = AtlasKinds.Contains(kind)
            ? TextureImporterCompression.Uncompressed
            : TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = 2048;
    }

    // (M32c) Puts the art where the game reads it: the theme's icon list gets every sprite in the icon folders below
    // (found by name through UiIcons), and each building whose Id has Art/UI/buildings/<Id>.png gets it as its Icon.
    // Run after Tools/gen_ui_art.py adds or renames sprites; UiArtTests fails until it has been.
    public static readonly string[] ThemeIconKinds = { "hud", "tools", "controls" };

    [MenuItem("CityBuilder/Sync UI Art")]
    public static void SyncUiArt()
    {
        var theme = AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/_Game/Resources/UiTheme.asset");
        if (theme == null)
        {
            Debug.LogError("Sync UI Art: Assets/_Game/Resources/UiTheme.asset not found.");
            return;
        }
        var icons = new List<Sprite>();
        foreach (Entry entry in LoadManifest().Values)
        {
            if (Array.IndexOf(ThemeIconKinds, entry.kind) < 0) continue;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}{entry.kind}/{entry.name}.png");
            if (sprite != null) icons.Add(sprite);
        }
        icons.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        var themeObject = new SerializedObject(theme);
        SerializedProperty list = themeObject.FindProperty(nameof(UiTheme.Icons));
        list.arraySize = icons.Count;
        for (int i = 0; i < icons.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = icons[i];
        themeObject.ApplyModifiedProperties();

        int buildings = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:BuildingDefinition", new[] { "Assets/_Game/Scriptables/Buildings" }))
        {
            var def = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (def == null || string.IsNullOrEmpty(def.Id)) continue;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Root}buildings/{def.Id}.png");
            if (sprite == null || def.Icon == sprite) continue;
            var so = new SerializedObject(def);
            so.FindProperty("m_Icon").objectReferenceValue = sprite;
            so.ApplyModifiedProperties();
            buildings++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Sync UI Art: {icons.Count} theme icons, {buildings} building icons set.");
    }

    // The json changed on its own (a border tweak): re-import the sprites whose border no longer matches.
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (Array.IndexOf(imported, ManifestPath) < 0) return;
        foreach (Entry entry in LoadManifest().Values)
        {
            string path = $"{Root}{entry.kind}/{entry.name}.png";
            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                importer.textureType == TextureImporterType.Sprite && importer.spriteBorder != BorderOf(entry))
                importer.SaveAndReimport();
        }
    }
}
