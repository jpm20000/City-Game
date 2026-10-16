using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

// M32b: the UI art Tools/gen_ui_art.py writes under Art/UI. Every PNG there is listed in ui_art.json and imported as
// specified (Sprite at 300 PPU with the json's 9-slice border, mipmaps for icons only, cursors as Cursor textures), and
// the small sprites are packed in the UI atlas. The rules are restated here, not read from UiArtImporter (an editor
// type this assembly can't reference), so the test is an independent check of the importer.
public sealed class UiArtTests
{
    private const string Root = "Assets/_Game/Art/UI/";
    private const string AtlasPath = Root + "UiAtlas.spriteatlasv2";
    private static readonly string[] IconKinds = { "hud", "tools", "buildings", "controls" };
    private static readonly string[] AtlasKinds = { "hud", "tools", "buildings", "controls", "buttons", "frames", "meters", "hudbar" };

    [Serializable]
    private sealed class Entry
    {
        public string name;
        public string kind;
        public int[] size;
        public int[] border;
        public int[] hotspot;
    }

    [Serializable]
    private sealed class Manifest
    {
        public Entry[] sprites;
    }

    private static Entry[] Entries()
    {
        string path = Root + "ui_art.json";
        Assert.IsTrue(File.Exists(path), path);
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
        Assert.IsNotNull(manifest?.sprites, "ui_art.json has a sprites list");
        Assert.IsNotEmpty(manifest.sprites);
        return manifest.sprites;
    }

    private static string PathOf(Entry e) => $"{Root}{e.kind}/{e.name}.png";

    [Test]
    public void EveryPng_IsListed_AndEveryEntryExists()
    {
        Entry[] entries = Entries();
        var listed = new HashSet<string>(entries.Select(PathOf));
        Assert.AreEqual(entries.Length, listed.Count, "names are unique");
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".png")) Assert.IsTrue(listed.Contains(path), $"{path} is not in ui_art.json (re-run Tools/gen_ui_art.py)");
        }
        foreach (string path in listed) Assert.IsTrue(File.Exists(path), $"{path} is listed but missing");
    }

    [Test]
    public void EverySprite_IsImportedAsSpecified()
    {
        int checkedSprites = 0;
        foreach (Entry e in Entries())
        {
            string path = PathOf(e);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(importer, path);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.AreEqual(e.size[0], texture.width, $"{path} width");
            Assert.AreEqual(e.size[1], texture.height, $"{path} height");
            Assert.IsTrue(importer.alphaIsTransparency, $"{path} alpha is transparency");
            Assert.AreEqual(TextureImporterNPOTScale.None, importer.npotScale, $"{path} keeps its size");
            if (e.kind == "cursors")
            {
                Assert.AreEqual(TextureImporterType.Cursor, importer.textureType, path);
                Assert.IsNotNull(e.hotspot, $"{path} hotspot");
                continue;
            }
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, path);
            Assert.AreEqual(SpriteImportMode.Single, importer.spriteImportMode, path);
            Assert.AreEqual(300f, importer.spritePixelsPerUnit, $"{path} pixels per unit");
            Assert.AreEqual(Array.IndexOf(IconKinds, e.kind) >= 0, importer.mipmapEnabled, $"{path} mipmaps (icons only)");
            bool packed = Array.IndexOf(AtlasKinds, e.kind) >= 0;
            Assert.AreEqual(packed, importer.textureCompression == TextureImporterCompression.Uncompressed,
                $"{path} uncompressed when the atlas packs it, compressed otherwise");
            Vector4 border = e.border == null || e.border.Length != 4
                ? Vector4.zero
                : new Vector4(e.border[0], e.border[1], e.border[2], e.border[3]);
            Assert.AreEqual(border, importer.spriteBorder, $"{path} 9-slice border");
            Assert.Less(border.x + border.z, e.size[0], $"{path} left + right border fit");
            Assert.Less(border.y + border.w, e.size[1], $"{path} bottom + top border fit");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsNotNull(sprite, $"{path} has a sprite");
            Assert.AreEqual(border, sprite.border, $"{path} sprite border");
            checkedSprites++;
        }
        Assert.Greater(checkedSprites, 0);
    }

    [Test]
    public void SmallSprites_ArePackedInTheUiAtlas_AndBigPicturesAreNot()
    {
        var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
        Assert.IsNotNull(atlas, AtlasPath);
        Assert.AreNotEqual(SpritePackerMode.Disabled, EditorSettings.spritePackerMode, "sprite atlases are enabled");
        foreach (Entry e in Entries())
        {
            if (e.kind == "cursors") continue;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PathOf(e));
            bool packed = Array.IndexOf(AtlasKinds, e.kind) >= 0;
            Assert.AreEqual(packed, atlas.CanBindTo(sprite), $"{PathOf(e)} {(packed ? "in" : "not in")} the atlas");
        }
    }
}
