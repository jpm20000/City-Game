using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M18c: the generated (or hand-made) road and ground sprites in the TileArtSet. A missing or wrongly sized
// sprite would silently fall back to the runtime-drawn road, so it fails here instead.
public sealed class TileArtTests
{
    private const string SetPath = "Assets/_Game/Scriptables/Art/TileArtSet.asset";

    private static void CheckSprites(SerializedProperty list, int expected, string what)
    {
        Assert.AreEqual(expected, list.arraySize, what + " count");
        for (int i = 0; i < list.arraySize; i++)
        {
            var sprite = list.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            Assert.IsNotNull(sprite, $"{what}[{i}] is missing");
            Assert.AreEqual(64f, sprite.rect.width, $"{what}[{i}] width");
            Assert.AreEqual(64f, sprite.rect.height, $"{what}[{i}] height");
            Assert.AreEqual(64f, sprite.pixelsPerUnit, $"{what}[{i}] pixels per unit (one cell = one unit)");
        }
    }

    [Test]
    public void TileArtSet_HoldsEverySprite_AtCellSize()
    {
        var set = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SetPath);
        Assert.IsNotNull(set, SetPath);
        var so = new SerializedObject(set);
        CheckSprites(so.FindProperty("m_Roads"), 5 * 16, "roads (5 tiers x 16 masks)");
        CheckSprites(so.FindProperty("m_Ground"), 4 * 4, "ground (4 ages x 4 variants)");
    }

    [Test]
    public void GameManager_ScenePointsAtTheTileArtSet()
    {
        string scene = System.IO.File.ReadAllText("Assets/_Game/Scenes/Main.unity");
        string guid = AssetDatabase.AssetPathToGUID(SetPath);
        StringAssert.Contains("m_TileArt: {fileID: 11400000, guid: " + guid, scene);
    }
}
