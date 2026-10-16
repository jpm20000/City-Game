using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// M32a: the UI theme. UiTheme lives in Assembly-CSharp, which this assembly can't reference, so it is reached by name
// (like ArtContractTests). The asset's palette must equal the code defaults (a missing asset is then the same colours;
// since M32d the asset also carries the art, which the code defaults leave out: the flat look), and Paint must leave a
// widget flat without a sprite and slice a bordered one. M32d: the button and panel painters with the shipped art.
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
    public void AssetPalette_EqualsTheCodeDefaults_AndTheDefaultsHaveNoArt()
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
                    Assert.IsNull(property.objectReferenceValue, $"{property.name}: the code default has no art");
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

    private static object Call(string method, params object[] args)
    {
        foreach (MethodInfo m in ThemeType().GetMethods(BindingFlags.Public | BindingFlags.Static))
            if (m.Name == method && m.GetParameters().Length == args.Length) return m.Invoke(null, args);
        Assert.Fail($"UiTheme.{method} with {args.Length} parameters");
        return null;
    }

    private static Sprite ThemeSprite(string slot)
    {
        var theme = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ThemePath);
        return (Sprite)ThemeType().GetField(slot).GetValue(theme);
    }

    // M32d: with the shipped button art a button is drawn from it (sliced, untinted) and shows hover / press / disabled
    // by sprite swap; the on look is the active art; a choice button's disabled (selected) state is the active art.
    [Test]
    public void PaintButton_UsesTheButtonArt_WithSpriteSwapStates()
    {
        Sprite normal = ThemeSprite("ButtonSprite"), active = ThemeSprite("ButtonActiveSprite");
        Sprite pressed = ThemeSprite("ButtonPressedSprite"), disabled = ThemeSprite("ButtonDisabledSprite");
        Assert.IsNotNull(normal, "the theme has button art (CityBuilder > Sync UI Art)");
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        try
        {
            var image = go.GetComponent<Image>();
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            Type look = ThemeType().GetNestedType("ButtonLook");
            Call("PaintButton", image, Enum.Parse(look, "Normal"), Color.red, null);
            Assert.AreSame(normal, image.sprite);
            Assert.AreEqual(Image.Type.Sliced, image.type);
            Assert.AreEqual(Color.white, image.color, "full-colour art is not tinted");
            Assert.AreEqual(Selectable.Transition.SpriteSwap, button.transition);
            Assert.AreSame(active, button.spriteState.highlightedSprite);
            Assert.AreSame(pressed, button.spriteState.pressedSprite);
            Assert.AreSame(disabled, button.spriteState.disabledSprite);
            Call("PaintButton", image, Enum.Parse(look, "Active"), Color.red, null);
            Assert.AreSame(active, image.sprite);
            Call("SelectedWhenDisabled", button);
            Assert.AreSame(active, button.spriteState.disabledSprite, "a choice button's selected (disabled) state is the active art");
            Call("PaintButton", image, Enum.Parse(look, "Normal"), Color.red, null);
            Assert.AreSame(active, button.spriteState.disabledSprite, "repainting keeps it");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    // M32d: a framed panel is drawn from the panel art and its layout padding clears the frame.
    [Test]
    public void PaintPanel_UsesTheFrame_AndRaisesTheLayoutPadding()
    {
        Sprite frame = ThemeSprite("PanelSprite");
        Assert.IsNotNull(frame, "the theme has panel art");
        var go = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        try
        {
            var image = go.GetComponent<Image>();
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 30, 2, 2);
            Call("PaintPanel", image, Color.blue, false);
            Assert.AreSame(frame, image.sprite);
            Assert.AreEqual(Color.white, image.color);
            int min = (int)ThemeType().GetField("FramePadding").GetValue(null);
            Assert.GreaterOrEqual(layout.padding.left, min);
            Assert.GreaterOrEqual(layout.padding.top, min);
            Assert.AreEqual(30, layout.padding.right, "a bigger padding is kept");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
