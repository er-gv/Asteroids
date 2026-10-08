using System;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
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
    [SerializeField] private GameObject gameOverPane;

    private bool isPaused;
    

    //public event Action<Asteroid> OnAsteroidHit;
    
    public event Action OnLevelWon;

    private void Awake() {
        Debug.Log($"[Level Awake] Level contains {asteroids.Count} asteroids:");
        gameOverPane.SetActive(false);
        InitAsteroids();
        ship.OnPlayerHit += OnLevelFailed;
    }

    private void InitAsteroids() {
        foreach (Asteroid asteroid in asteroids) {
            asteroid.OnHit += HandleAsteroidHit;
            asteroid.OnGameOver += OnLevelWon;
            asteroid.OnOutOfBorders += RemoveAsteroid;
            Debug.Log($"[Level Awake] Asteroid: {asteroid.ToString()}");
        }
    }


        // ----- Pause / Resume / Quit / Restart -----
        public void PauseGame()
    {
        Debug.Log("[LevelManager] PauseGame called.");
        if (isPaused) return;
        isPaused = true;
        Time.timeScale = 0f;
        AudioController.Instance.MuteMusic();
    }

    public void ResumeGame()
    {
        Debug.Log("[LevelManager] ResumeGame called.");
        if (!isPaused) return;
        isPaused = false;
        Time.timeScale = 1f;
        AudioController.Instance.UnmuteMusic();
    }

    public void QuitGame()
    {
        Time.timeScale = 1f; // don't leave the app in a paused state on exit
        // No score save call here — quitting intentionally discards progress.
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
        //AudioController.Instance.PlayGameOverSound();
        Debug.Log("[LevelManager] Level Failed. Game over.");
        ExplodeShip();
                
        gameOverPane.SetActive(true);
        Transform caption = gameOverPane.transform.Find("Caption");
        TMP_Text gameOverText = caption != null ? caption.GetComponent<TMP_Text>() : null;
        if (gameOverText != null){
            gameOverText.text = "Game Over!\nFinal score: " + scoreManager.Score.ToString();
        }
        else{
            Debug.LogError("[LevelManager] No TextMeshPro component found on gameOverPane/Caption.");
        }
    }

    private void HandleLevelWon(){
        Debug.Log("[LevelManager] Level Won! All asteroids destroyed.");
        ship.enabled = false; // disables ship movement/input handling
        AudioController.Instance.PlayVictorySound();
        gameOverPane.SetActive(true);
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