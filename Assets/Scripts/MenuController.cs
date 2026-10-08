using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach this to a GameObject holding a UIDocument component
/// referencing MainMenu.uxml. Handles clicks on the front-screen labels.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class MenuController : MonoBehaviour{

    [Header("Scene to load for New Game")]
    [SerializeField] private string gameSceneName = "Asteroids";
    [Header("Scene to load for Settings (optional)")]
    [SerializeField] private string settingsSceneName = "Settings";

    [Header("Scene to load for High Score (optional)")]
    [SerializeField] private string highScoreSceneName = "HighScore";

    private UIDocument uiDocument;

    private void OnEnable(){
        uiDocument = GetComponent<UIDocument>();
        var root = uiDocument.rootVisualElement;

        var newGameLabel = root.Q<Label>("new-game-label");
        var highScoreLabel = root.Q<Label>("high-score-label");
        var settingsLabel = root.Q<Label>("settings-label");
        var quitLabel = root.Q<Label>("quit-label");

        newGameLabel.RegisterCallback<ClickEvent>(OnNewGameClicked);
        highScoreLabel.RegisterCallback<ClickEvent>(OnHighScoreClicked);
        settingsLabel.RegisterCallback<ClickEvent>(OnSettingsClicked);
        quitLabel.RegisterCallback<ClickEvent>(OnQuitClicked);
    }

    private void OnDisable()
    {
        var root = uiDocument.rootVisualElement;
        root.Q<Label>("new-game-label")?.UnregisterCallback<ClickEvent>(OnNewGameClicked);
        root.Q<Label>("high-score-label")?.UnregisterCallback<ClickEvent>(OnHighScoreClicked);
        root.Q<Label>("settings-label")?.UnregisterCallback<ClickEvent>(OnSettingsClicked);
        root.Q<Label>("quit-label")?.UnregisterCallback<ClickEvent>(OnQuitClicked);
    }

    private void OnNewGameClicked(ClickEvent evt)
    {
        SceneManager.LoadScene(gameSceneName);
    }

    private void OnHighScoreClicked(ClickEvent evt)
    {
        SceneManager.LoadScene(highScoreSceneName);
    }

    private void OnSettingsClicked(ClickEvent evt)
    {
        SceneManager.LoadScene(settingsSceneName);
    }

    private void OnQuitClicked(ClickEvent evt)
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}