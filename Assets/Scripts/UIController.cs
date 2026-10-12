using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

public class UIController : MonoBehaviour{
    private int score = 0;
    public float scoreMultiplier = 10f;

    public UIDocument uiDocument;
    private Label scoreLabel;
    private Button restartButton;
    private float elapsedTime;

    /*
        public int score = 0;
        public float scoreMultiplier = 10f;

        public UIDocument uiDocument;
        private Label scoreLabel;
        private Button restartButton;
    */
    
    public void InitUI(){
        elapsedTime = 0f;
        scoreLabel = uiDocument.rootVisualElement.Q<Label>("ScoreLabel");
        scoreLabel.text = $"Score: {score}";
        restartButton = uiDocument.rootVisualElement.Q<Button>("RestartButton");
        restartButton.clicked += ReloadScene;
        ToggleOffRestartButton();
    }

    public void UpdateScore(){
        elapsedTime += Time.deltaTime;
        score = Mathf.FloorToInt(elapsedTime * scoreMultiplier);
        scoreLabel.text = "Score: " + score;
    }

    public void ReloadScene() {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ToggleOnRestartButton(){
        Debug.Log("Toggling on Restart Button");
        restartButton.style.display = DisplayStyle.Flex;
    }

    public void ToggleOffRestartButton(){
        Debug.Log("Toggling off Restart Button");
        restartButton.style.display = DisplayStyle.None;
    }
    
}