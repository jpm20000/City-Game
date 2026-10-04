using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// M18f: the cosmetic vehicles. Each is one mesh on the shared Kit material with no collider (not selectable, cheap).
public sealed class VehicleSetTests
{
    private const string SetPath = "Assets/_Game/Scriptables/Art/VehicleSet.asset";

    [Test]
    public void EveryAge_HasVehicles_OneRendererOnTheKitMaterial_NoCollider()
    {
        var set = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SetPath);
        Assert.IsNotNull(set, SetPath);
        var kit = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Kit/Kit.mat");
        SerializedProperty ages = new SerializedObject(set).FindProperty("m_Ages");
        Assert.AreEqual(4, ages.arraySize, "one list per age");
        for (int age = 0; age < 4; age++)
        {
            SerializedProperty prefabs = ages.GetArrayElementAtIndex(age).FindPropertyRelative("Prefabs");
            Assert.GreaterOrEqual(prefabs.arraySize, 2, $"age {age} needs at least two vehicles");
            for (int i = 0; i < prefabs.arraySize; i++)
            {
                var prefab = prefabs.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                Assert.IsNotNull(prefab, $"age {age} vehicle {i}");
                Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
                Assert.AreEqual(1, renderers.Length, prefab.name + " renderers");
                Assert.AreSame(kit, renderers[0].sharedMaterial, prefab.name + " material");
                Assert.IsNull(prefab.GetComponentInChildren<Collider>(true), prefab.name + " must have no collider");
                Bounds bounds = prefab.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.Less(bounds.size.z, 0.5f, prefab.name + " is longer than a cell can hold");
                Assert.AreEqual(0f, bounds.min.y, 0.01f, prefab.name + " stands on the ground");
            }
        }
    }

    [Test]
    public void GameManager_ScenePointsAtTheVehicleSet()
    {
        string scene = System.IO.File.ReadAllText("Assets/_Game/Scenes/Main.unity");
        string guid = AssetDatabase.AssetPathToGUID(SetPath);
        StringAssert.Contains("m_VehicleSet: {fileID: 11400000, guid: " + guid, scene);
    }
}
