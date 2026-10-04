using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The random-event popup (M17): while an event is pending it dims the screen, shows its title, text and two or three
// choices (price and effect on each; unaffordable ones are disabled, the last is always free) and keeps the game
// paused. Choosing resolves it and brings back the speed that was running. Built at runtime like the Budget panel, so
// there is no scene or prefab edit.
public sealed class EventPopup : MonoBehaviour
{
    private static readonly Color PanelColor = new Color(0.10f, 0.12f, 0.15f, 0.97f);
    private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.33f);
    private static readonly Color DisabledColor = new Color(0.17f, 0.18f, 0.21f);
    private static readonly Color TitleColor = new Color(0.95f, 0.80f, 0.35f);
    private const float PanelWidth = 560f;

    private GameManager m_Game;
    private GameObject m_Root;
    private TMP_Text m_Title;
    private TMP_Text m_Body;
    private TMP_Text m_Footer;
    private RectTransform m_Choices;
    private readonly List<Button> m_Buttons = new();
    private string m_ShownId = "";
    private GameSpeed m_PreviousSpeed = GameSpeed.x1;

    public bool IsOpen => m_Root != null && m_Root.activeSelf;

    public void Init(GameManager game, Transform canvas)
    {
        m_Game = game;
        transform.SetParent(canvas, false);
        var rect = (RectTransform)transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        // Dimmed full-screen blocker, so nothing underneath is clicked while the event waits.
        m_Root = new GameObject("Blocker", typeof(RectTransform), typeof(Image));
        m_Root.transform.SetParent(transform, false);
        var blocker = (RectTransform)m_Root.transform;
        blocker.anchorMin = Vector2.zero;
        blocker.anchorMax = Vector2.one;
        blocker.offsetMin = blocker.offsetMax = Vector2.zero;
        m_Root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(m_Root.transform, false);
        var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(PanelWidth, 0f);
        panel.GetComponent<Image>().color = PanelColor;
        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 16, 16);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        m_Title = Text(panel.transform, "", 26, TitleColor);
        m_Body = Text(panel.transform, "", 17, new Color(0.85f, 0.88f, 0.93f));

        var choices = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup));
        choices.transform.SetParent(panel.transform, false);
        m_Choices = (RectTransform)choices.transform;
        var choiceLayout = choices.GetComponent<VerticalLayoutGroup>();
        choiceLayout.spacing = 8f;
        choiceLayout.childControlWidth = true;
        choiceLayout.childControlHeight = true;
        choiceLayout.childForceExpandWidth = true;
        choiceLayout.childForceExpandHeight = false;

        m_Footer = Text(panel.transform, "", 13, new Color(0.60f, 0.64f, 0.70f));
        m_Root.SetActive(false);
        EscapeRouter.Register(this, EscapeRouter.Event, () => IsOpen);
    }

    private void OnEnable()
    {
        GameEvents.MoneyChanged += OnMoneyChanged;
        GameEvents.CityLoaded += OnCityLoaded;
    }

    private void OnDisable()
    {
        GameEvents.MoneyChanged -= OnMoneyChanged;
        GameEvents.CityLoaded -= OnCityLoaded;
    }

    private void OnMoneyChanged(float money)
    {
        if (IsOpen) RefreshAffordability();
    }

    private void OnCityLoaded()
    {
        if (IsOpen) Hide(false);
        m_ShownId = "";
    }

    private void Update()
    {
        SimulationSystem sim = m_Game != null ? m_Game.Simulation : null;
        if (sim == null) return;

        EventDefinition pending = sim.Disasters.Enabled ? sim.Disasters.Events.Pending : null;
        if (pending == null)
        {
            if (IsOpen) Hide(true);
            return;
        }

        if (!IsOpen || m_ShownId != pending.Id) Show(pending);
        // Keys 1-4 can unpause; the event waits until it is answered.
        m_Game.Clock.SetSpeed(GameSpeed.Paused);
    }

    private void Show(EventDefinition definition)
    {
        if (!IsOpen)
        {
            m_PreviousSpeed = m_Game.Clock.Speed;
            AudioController.Play(SfxId.EventOpen);
        }
        m_ShownId = definition.Id;
        transform.SetAsLastSibling();

        m_Title.text = definition.Title;
        m_Body.text = definition.Text;
        for (int i = m_Choices.childCount - 1; i >= 0; i--)
        {
            GameObject old = m_Choices.GetChild(i).gameObject;
            old.SetActive(false);       // out of the layout now; Destroy only lands at the end of the frame
            Destroy(old);
        }
        m_Buttons.Clear();

        int population = m_Game.Population.Population;
        for (int i = 0; i < definition.Choices.Length; i++)
        {
            EventChoice choice = definition.Choices[i];
            int index = i;
            Button button = MakeChoice(choice, population, () => Choose(index));
            m_Buttons.Add(button);
        }
        m_Footer.text = $"Time is paused until you choose. Unanswered for {m_Game.Balance.EventAutoDays} days it takes the last option.";
        m_Root.SetActive(true);
        RefreshAffordability();
    }

    private void Choose(int index)
    {
        if (!m_Game.Simulation.Disasters.Events.Choose(index)) return;
        AudioController.Play(SfxId.EventChoice);
        Hide(true);
    }

    private void Hide(bool restoreSpeed)
    {
        m_Root.SetActive(false);
        m_ShownId = "";
        if (restoreSpeed && m_Game.Clock.Speed == GameSpeed.Paused) m_Game.Clock.SetSpeed(m_PreviousSpeed);
    }

    private void RefreshAffordability()
    {
        RandomEventSystem events = m_Game.Simulation.Disasters.Events;
        for (int i = 0; i < m_Buttons.Count; i++)
        {
            bool can = events.CanChoose(i);
            m_Buttons[i].interactable = can;
            m_Buttons[i].GetComponent<Image>().color = can ? ButtonColor : DisabledColor;
        }
    }

    private Button MakeChoice(EventChoice choice, int population, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(choice.Label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(m_Choices, false);
        go.GetComponent<Image>().color = ButtonColor;
        go.GetComponent<LayoutElement>().minHeight = 56f;
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);

        float price = choice.PriceAt(population);
        string cost = price > 0f ? $"  <color=#F2665A>−${price:N0}</color>" : choice.Reward > 0f ? $"  <color=#73D973>+${choice.Reward:N0}</color>" : "";
        var text = Text(go.transform, $"<b>{choice.Label}</b>{cost}\n<size=88%><color=#B8C0CC>{choice.Description}</color></size>", 17, Color.white);
        text.alignment = TextAlignmentOptions.Left;
        var rect = (RectTransform)text.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(12f, 4f);
        rect.offsetMax = new Vector2(-12f, -4f);
        Destroy(text.GetComponent<LayoutElement>());
        return button;
    }

    private static TMP_Text Text(Transform parent, string text, float size, Color color) => UiKit.Text(parent, text, size, color);
}
