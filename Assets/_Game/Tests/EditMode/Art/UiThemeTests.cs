using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// M32a: the UI theme. UiTheme lives in Assembly-CSharp, which this assembly can't reference, so it is reached by name
// (like ArtContractTests). The asset must equal the code defaults (a missing asset is then the same look), and Paint
// must leave a widget flat without a sprite and slice a bordered one.
public sealed class UiThemeTests
{
    private const string ThemePath = "Assets/_Game/Resources/UiTheme.asset";

    private static Type ThemeType()
    {
        Type type = Type.GetType("UiTheme, Assembly-CSharp");
        Assert.IsNotNull(type, "UiTheme not found in Assembly-CSharp");
        return type;
    }

    private static void Paint(Image image, Sprite sprite, Color color)
    {
        MethodInfo paint = ThemeType().GetMethod("Paint", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(paint, "UiTheme.Paint");
        paint.Invoke(null, new object[] { image, sprite, color });
    }

    [Test]
    public void Asset_EqualsTheCodeDefaults()
    {
        var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ThemePath);
        Assert.IsNotNull(asset, ThemePath);
        Assert.AreEqual(ThemeType(), asset.GetType());
        var defaults = ScriptableObject.CreateInstance(ThemeType());
        try
        {
            var expected = new SerializedObject(defaults);
            var actual = new SerializedObject(asset);
            SerializedProperty property = expected.GetIterator();
            int compared = 0;
            for (bool enter = true; property.NextVisible(enter); enter = false)
            {
                if (property.name == "m_Script") continue;
                SerializedProperty other = actual.FindProperty(property.propertyPath);
                Assert.IsNotNull(other, property.propertyPath);
                if (property.propertyType == SerializedPropertyType.Color)
                {
                    Color e = property.colorValue, a = other.colorValue;
                    Assert.That(Vector4.Distance(e, a), Is.LessThan(1e-4f), $"{property.name}: asset {a}, code {e}");
                }
                else if (property.propertyType == SerializedPropertyType.ObjectReference)
                    Assert.AreEqual(property.objectReferenceValue, other.objectReferenceValue, property.name);
                compared++;
            }
            Assert.Greater(compared, 10, "palette and sprite slots compared");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(defaults);
        }
    }

    [Test]
    public void Paint_WithoutSprite_OnlySetsTheColour()
    {
        var go = new GameObject("Image", typeof(RectTransform), typeof(Image));
        try
        {
            var image = go.GetComponent<Image>();
            Paint(image, null, Color.red);
            Assert.AreEqual(Color.red, image.color);
            Assert.IsNull(image.sprite);
            Assert.AreEqual(Image.Type.Simple, image.type);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Paint_SlicesABorderedSprite_AndKeepsAPlainOneSimple()
    {
        var texture = new Texture2D(12, 12);
        var bordered = Sprite.Create(texture, new Rect(0, 0, 12, 12), new Vector2(0.5f, 0.5f), 300f, 0,
            SpriteMeshType.FullRect, new Vector4(4, 4, 4, 4));
        var plain = Sprite.Create(texture, new Rect(0, 0, 12, 12), new Vector2(0.5f, 0.5f), 300f);
        var go = new GameObject("Image", typeof(RectTransform), typeof(Image));
        try
        {
            var image = go.GetComponent<Image>();
            Paint(image, bordered, Color.white);
            Assert.AreSame(bordered, image.sprite);
            Assert.AreEqual(Image.Type.Sliced, image.type);
            Paint(image, plain, Color.gray);
            Assert.AreSame(plain, image.sprite);
            Assert.AreEqual(Image.Type.Simple, image.type);
            Assert.AreEqual(Color.gray, image.color);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(bordered);
            UnityEngine.Object.DestroyImmediate(plain);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
