using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M18e: the audio catalog must be complete (a missing clip is silently silent in the game, so it fails here).
public sealed class AudioCatalogTests
{
    private const string CatalogPath = "Assets/_Game/Scriptables/Audio/AudioCatalog.asset";
    private const int SfxCount = 21;       // SfxId has 21 values (Click .. Load); the enum is append-only

    private static SerializedObject Catalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ScriptableObject>(CatalogPath);
        Assert.IsNotNull(catalog, CatalogPath);
        return new SerializedObject(catalog);
    }

    [Test]
    public void EverySoundEffect_HasAtLeastOneClip()
    {
        SerializedProperty sfx = Catalog().FindProperty("m_Sfx");
        var seen = new HashSet<int>();
        for (int i = 0; i < sfx.arraySize; i++)
        {
            SerializedProperty entry = sfx.GetArrayElementAtIndex(i);
            int id = entry.FindPropertyRelative("Id").enumValueIndex;
            Assert.IsTrue(seen.Add(id), $"two entries for sfx {id}");
            SerializedProperty clips = entry.FindPropertyRelative("Clips");
            Assert.Greater(clips.arraySize, 0, $"sfx {id} has no clips");
            for (int c = 0; c < clips.arraySize; c++) Assert.IsNotNull(clips.GetArrayElementAtIndex(c).objectReferenceValue, $"sfx {id} clip {c}");
            float volume = entry.FindPropertyRelative("Volume").floatValue;
            Assert.That(volume, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f), $"sfx {id} volume");
        }
        Assert.AreEqual(SfxCount, seen.Count, "one entry per SfxId");
        for (int id = 0; id < SfxCount; id++) Assert.IsTrue(seen.Contains(id), $"missing sfx {id}");
    }

    [Test]
    public void EveryAge_HasThreeAmbienceLayers_AndTheSharedLoopsExist()
    {
        SerializedObject catalog = Catalog();
        SerializedProperty ambience = catalog.FindProperty("m_Ambience");
        Assert.AreEqual(4, ambience.arraySize, "one ambience set per age");
        for (int age = 0; age < 4; age++)
        {
            SerializedProperty set = ambience.GetArrayElementAtIndex(age);
            foreach (string layer in new[] { "Base", "City", "Industry" })
            {
                var clip = set.FindPropertyRelative(layer).objectReferenceValue as AudioClip;
                Assert.IsNotNull(clip, $"age {age} {layer}");
                Assert.Greater(clip.length, 5f, $"age {age} {layer} is a loop, not a blip");
            }
        }
        Assert.IsNotNull(catalog.FindProperty("m_Night").objectReferenceValue, "night loop");
        Assert.IsNotNull(catalog.FindProperty("m_Fire").objectReferenceValue, "fire loop");
        Assert.AreEqual(4, catalog.FindProperty("m_Music").arraySize, "one (possibly empty) music playlist per age");
    }

    [Test]
    public void GameManager_ScenePointsAtTheAudioCatalog()
    {
        string scene = System.IO.File.ReadAllText("Assets/_Game/Scenes/Main.unity");
        string guid = AssetDatabase.AssetPathToGUID(CatalogPath);
        StringAssert.Contains("m_AudioCatalog: {fileID: 11400000, guid: " + guid, scene);
    }
}
