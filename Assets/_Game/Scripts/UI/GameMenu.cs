using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD Save / Load / New buttons. Load is disabled until a save exists; New needs a second click
// within m_ConfirmSeconds because it discards the current city.
public sealed class GameMenu : MonoBehaviour
{
    [SerializeField] private SaveGameController m_SaveGame;
    [SerializeField] private Button m_SaveButton;
    [SerializeField] private Button m_LoadButton;
    [SerializeField] private Button m_NewButton;
    [SerializeField] private TMP_Text m_NewLabel;
    [SerializeField] private string m_NewText = "New";
    [SerializeField] private string m_ConfirmText = "Sure?";
    [SerializeField] private float m_ConfirmSeconds = 3f;

    private float m_ConfirmTimer;

    private void Start()
    {
        if (m_SaveGame == null) return;

        if (m_SaveButton != null) m_SaveButton.onClick.AddListener(() =>
        {
            m_SaveGame.Save();
            RefreshLoadButton();
        });
        if (m_LoadButton != null) m_LoadButton.onClick.AddListener(() => m_SaveGame.Load());
        if (m_NewButton != null) m_NewButton.onClick.AddListener(OnNewClicked);

        RefreshLoadButton();
        SetConfirming(false);
    }

    private void Update()
    {
        if (m_ConfirmTimer <= 0f) return;

        // Unscaled so the prompt still times out while paused.
        m_ConfirmTimer -= Time.unscaledDeltaTime;
        if (m_ConfirmTimer <= 0f) SetConfirming(false);
    }

    private void OnEnable()
    {
        GameEvents.Notification += OnNotification;
    }

    private void OnDisable()
    {
        GameEvents.Notification -= OnNotification;
    }

    // F5 saves bypass the button; any notification is a cheap moment to recheck the file.
    private void OnNotification(string message)
    {
        RefreshLoadButton();
    }

    private void OnNewClicked()
    {
        if (m_ConfirmTimer > 0f)
        {
            SetConfirming(false);
            m_SaveGame.NewCity();
        }
        else
        {
            SetConfirming(true);
        }
    }

    private void SetConfirming(bool confirming)
    {
        m_ConfirmTimer = confirming ? m_ConfirmSeconds : 0f;
        if (m_NewLabel != null) m_NewLabel.text = confirming ? m_ConfirmText : m_NewText;
    }

    private void RefreshLoadButton()
    {
        if (m_LoadButton != null && m_SaveGame != null) m_LoadButton.interactable = m_SaveGame.HasSave;
    }
}
