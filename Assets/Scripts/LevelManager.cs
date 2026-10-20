using System;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using TMPro;

namespace Hobby.Erez.Asteroids2D{

public class LevelManager : MonoBehaviour{
    
    [SerializeField] AudioController audioController;
       

    [Header("Asteroids")]
    [SerializeField] AsteroidsFactory asteroidsFactory;
    [SerializeField] List<Asteroid> asteroids = new List<Asteroid>();
    //private Dictionary<AsteroidSize, List<GameObject>> asteroidPrefabs;

    [Header("Ship")]
    [SerializeField] private PlayerController ship;  
    
    [SerializeField] private GameObject shipExplosionEffectPrefab; 
    [SerializeField] private float explosionEffectLifetime = 2f;
    

    [Header("Borders")]
    [SerializeField] private Borders borders;
    [Header("Score")]
    [SerializeField] ScoreManager scoreManager;

    [Header("UI")]
    [SerializeField] private Canvas levelWideCanvas;
    [SerializeField] private GameObject gameOverPane;
    [SerializeField] private GameObject gamePausedPane;
    [SerializeField] private UnityEngine.UI.Button settingsBtn;
    [SerializeField] private UnityEngine.UI.Button quitGameBtn;
    [SerializeField] private UnityEngine.UI.Button pausePlayBtn;
    [SerializeField] private Material pauseTintMaterial;
    private UnityEngine.UI.Image pauseTintOverlay;
    [SerializeField] private Sprite pauseSelectedSprite;
    [SerializeField] private Sprite playSelectedSprite;
    [SerializeField] private Sprite pausePressedSprite;
    [SerializeField] private Sprite playPressedSprite;
    [SerializeField] private Sprite pauseDisabledSprite;
    [SerializeField] private Sprite playDisabledSprite;
    [SerializeField] private Sprite playHighlightedSprite;
    [SerializeField] private Sprite pauseHighlightedSprite;
    
    private UnityEngine.UI.Image pauseImage;
    private bool isPaused = false;

    //public event Action<Asteroid> OnAsteroidHit;
    
    public event Action OnLevelWon;

    private void Awake() {
        Debug.Log($"[Level Awake] Level contains {asteroids.Count} asteroids:");
        //gameOverPane.SetActive(false);
        InitAsteroids();
        ship.OnPlayerHit += OnLevelFailed;
        
    }

    private void OnEnable() {
        InitUI();
        ResumeGame();
    }

    private void OnDisable() {
        ship.OnPlayerHit -= OnLevelFailed;
        quitGameBtn.onClick.RemoveAllListeners();
        settingsBtn.onClick.RemoveAllListeners();
    }

    private void InitUI(){
        gameOverPane.SetActive(false);
        gamePausedPane.SetActive(false);
        InitPauseTintOverlay();
        settingsBtn.onClick.AddListener(()=>{SceneManager.LoadScene("Settings", LoadSceneMode.Additive);});
    }

    private void InitPauseTintOverlay(){
        if (pauseTintOverlay != null){
            pauseTintOverlay.gameObject.SetActive(false);
            return;
        }

        
        if (levelWideCanvas == null){
            Debug.LogError("[LevelManager] Pause button must be under a Canvas to show the pause tint.");
            return;
        }

        GameObject overlayObject = new GameObject("PauseTintOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        overlayObject.layer = levelWideCanvas.gameObject.layer;
        RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayRect.SetParent(levelWideCanvas.transform, false);
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlayRect.SetAsFirstSibling();

        pauseTintOverlay = overlayObject.GetComponent<UnityEngine.UI.Image>();
        pauseTintOverlay.material = pauseTintMaterial;
        pauseTintOverlay.color = new Color(0.8805f, 0.7886f, 0.6008f, 0.3f);
        pauseTintOverlay.raycastTarget = false;
        pauseTintOverlay.gameObject.SetActive(false);
    }

    public void OnPauseStateChanged() {
        if (isPaused) {
            ResumeGame();
        } 
        else {
            PauseGame();
        }
    }
    
    private void InitAsteroids() {
        foreach (Asteroid asteroid in asteroids) {
            asteroid.OnHit += HandleAsteroidHit;
            asteroid.OnGameOver += OnLevelWon;
            asteroid.OnOutOfBorders += RemoveAsteroid;
            Debug.Log($"[Level Awake] Asteroid: {asteroid.ToString()}");
        }
    }


    // ----- Pause / Resume / Quit / Restart / Go to High score -----
    
    
    public void PauseGame(){
        Debug.Log("[toggle] PauseGame called.");
        isPaused = true;
        gamePausedPane.SetActive(true);
        if (pauseTintOverlay != null) pauseTintOverlay.gameObject.SetActive(true);
        Time.timeScale = 0f;
        AudioController.Instance.MuteMusic();
        UpdatePauseSprite();
        SetAsteroidAlpha(0.25f);

    }

    public void ResumeGame(){
        Debug.Log("[toggle] ResumeGame called.");
        isPaused = false;
        gamePausedPane.SetActive(false);
        if (pauseTintOverlay != null) pauseTintOverlay.gameObject.SetActive(false);
        Time.timeScale = 1f;
        AudioController.Instance.UnmuteMusic();
        UpdatePauseSprite();
        SetAsteroidAlpha(1f);
    }    

    private void SetAsteroidAlpha(float alpha){
        foreach (Asteroid asteroid in asteroids){
            SpriteRenderer spriteRenderer = asteroid.GetComponent<SpriteRenderer>();
            Color color = spriteRenderer.color;
            color.a = alpha;
            spriteRenderer.color = color;
        }
    }

    private void UpdatePauseSprite() {
        
            UnityEngine.UI.SpriteState spriteState = new();
            
            Debug.Log($"[toggle] state is {isPaused}");
            spriteState.highlightedSprite = isPaused ? playHighlightedSprite : pauseHighlightedSprite; 
            spriteState.pressedSprite = isPaused ? playPressedSprite : pausePressedSprite;
            spriteState.selectedSprite = isPaused ? playSelectedSprite : pauseSelectedSprite;
            spriteState.disabledSprite = isPaused ? playDisabledSprite : pauseDisabledSprite;
            pausePlayBtn.spriteState = spriteState;
    }

    public void RestartGame(){
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void OnQuitToMenu(){
        Debug.Log("[LevelManager] QuitGame called.");
        SceneManager.UnloadSceneAsync(SceneManager.GetActiveScene().name);
        SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
    }

    public void OnLoadHighScore() {
        
        SceneManager.UnloadSceneAsync(SceneManager.GetActiveScene().name);
        SceneManager.LoadSceneAsync("HighScore", LoadSceneMode.Single);
    }
    public void OnSettingsBtnClicked(){
        Debug.Log("[LevelManager] Settings button clicked.");
        SceneManager.LoadScene("Settings", LoadSceneMode.Additive);
    }
    private void RemoveAsteroid(Asteroid a) {
        asteroids.Remove(a);
        Destroy(a.gameObject);
    }
    
    private void HandleAsteroidHit(Asteroid asteroid){
        Debug.Log($"[Hit] Hit called on asteroid:\n{asteroid.ToString()}.");
        asteroid.OnHit -= HandleAsteroidHit;
        asteroid.OnGameOver -= OnLevelWon;
        ScoreManager.Instance.AddScore(asteroid.Score);
        try{
            AudioController.Instance.PlayAsteroidSplitFX();
        }
        catch (Exception e){
            Debug.LogError($"[LevelManager] Error playing asteroid bump sound: {e.Message}");
        }
                
        
        if (asteroid.CanSplit) {
            SpawnSmallerAsteroids(asteroid);
        }
        asteroids.Remove(asteroid);
        Destroy(asteroid.gameObject);
        if (asteroids.Count == 0){
            HandleLevelWon();
        }
    }

    private void SpawnSmallerAsteroids(Asteroid asteroid) {
        Debug.Log($"[Hit Split] Splitting asteroid of size {asteroid.Size} into two smaller asteroids.");
        //take params needed for creating smaller asteroidsOnAsteroidHit?.Invoke(asteroid);
        AsteroidSize childSize = asteroid.GetSmallerSize();
        float parentRadius = asteroid.GetComponent<CircleCollider2D>().radius * asteroid.transform.localScale.x;
        Vector2 position = asteroid.transform.position;
        List<Asteroid> spawnedAsteroids = new List<Asteroid>();
        asteroidsFactory.SpawnSmallerAsteroids(position, parentRadius, childSize, ref spawnedAsteroids);
        foreach (Asteroid child in spawnedAsteroids) {
            child.OnHit += HandleAsteroidHit;
            child.OnGameOver += OnLevelWon;
            child.OnOutOfBorders += RemoveAsteroid;
            asteroids.Add(child);
        }
        spawnedAsteroids.Clear();
    }



        // ----- Game Over / Level Won -----

    public void OnLevelFailed(){
        ship.enabled = false; // disables ship movement/input handling
        pausePlayBtn.interactable = false;
        settingsBtn.interactable = false;
        quitGameBtn.interactable = false;
        //AudioController.Instance.PlayGameOverSound();
        Debug.Log("[LevelManager] Level Failed. Game over.");
        ExplodeShip();
                       
        //gameOverPane.SetActive(true);
        //Transform caption = gameOverPane.transform.Find("Caption");
        /*TMP_Text gameOverText = caption != null ? caption.GetComponent<TMP_Text>() : null;
        if (gameOverText != null){
            gameOverText.text = "Game Over!\nFinal score: " + scoreManager.Score.ToString();
        }
        else{
            Debug.LogError("[LevelManager] No TextMeshPro component found on gameOverPane/Caption.");
        }*/
    }

    private void HandleLevelWon(){
        Debug.Log("[LevelManager] Level Won! All asteroids destroyed.");
        ship.enabled = false; // disables ship movement/input handling
        AudioController.Instance.PlayVictorySound();
        //gameOverPane.SetActive(true);
    }

    
    private void ExplodeShip(){
        if (ship == null){
            Debug.LogError("[LevelManager] Ship reference is null. Cannot explode ship.");
            return;
        }
        Vector3 shipPosition = ship.transform.position;
        Destroy(ship.gameObject);
        GameObject explosionInstance = Instantiate(shipExplosionEffectPrefab, shipPosition, Quaternion.identity);
        if (explosionInstance == null){
            Debug.LogError("[LevelManager] Ship explosion effect is not assigned.");
            return;
        }

        try{
            AudioController.Instance.PlayShipCrushFX();
        }
        catch (Exception e){
            Debug.LogError($"[LevelManager] Error playing ship explosion sound: {e.Message}");
        }
        finally{ 
            Destroy(explosionInstance.gameObject, explosionEffectLifetime);
        }
        
        
    }
    
   

}
}