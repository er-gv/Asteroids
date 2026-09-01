using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace Hobby.Erez.Asteroids2D{

public class LevelManager : MonoBehaviour{
    
    [SerializeField] AudioController audioController;
    [Header("Score")]
    private int score;
    public event Action<int> OnScoreChanged;

    
    [Header("Asteroids")]
    [SerializeField] AsteroidsFactory asteroidsFactory;
    [SerializeField] List<Asteroid> asteroids = new List<Asteroid>();
    //private Dictionary<AsteroidSize, List<GameObject>> asteroidPrefabs;

    [Header("Ship")]
    [SerializeField] private PlayerController ship;  
    [SerializeField] private ParticleSystem explosionParticlesPrefab;


    [Header("UI")]
    [SerializeField] private GameObject playAgainCanvas;

    private bool isPaused;
     

    //public event Action<Asteroid> OnAsteroidHit;
    public event Action OnGameOver;
    public event Action OnLevelWon;

    private void Awake(){
        Debug.Log($"[Level Awake] Level contains {asteroids.Count} asteroids:");
        int count = 1;
        foreach(Asteroid asteroid in asteroids){
            asteroid.OnHit += HandleAsteroidHit;
            Debug.Log($"[Level {count}:]  {asteroid.ToString()}\n");
            ++count;
        }
    }

    // ----- Score -----
    public void UpdateScore(int amount)
    {
        score += amount;
        OnScoreChanged?.Invoke(score);
    }

    // ----- Pause / Resume / Quit / Restart -----
    public void PauseGame()
    {
        if (isPaused) return;
        isPaused = true;
        Time.timeScale = 0f;
        AudioListener.pause = true;
    }

    public void ResumeGame()
    {
        if (!isPaused) return;
        isPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
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

    

    private void HandleAsteroidHit(Asteroid asteroid){
        Debug.Log($"[Level] Hit called on asteroid:\n{asteroid.ToString()}.");
        //audioController.PlayAsteroidSplitFX();
        asteroid.OnHit -= HandleAsteroidHit;
       

        //OnAsteroidHit?.Invoke(asteroid);
        UpdateScore(asteroid.Score);

        if (asteroid.CanSplit){
            AsteroidSize childSize = asteroid.GetSmallerSize();
            for (int i = 0; i < 2; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * 0.5f;
                Asteroid newAsteroid = asteroidsFactory.SpawnAsteroid(childSize, (Vector2)asteroid.transform.position + offset);
                newAsteroid.OnHit += HandleAsteroidHit;
                asteroids.Add(newAsteroid);
            }
        }
        asteroids.Remove(asteroid);
        Destroy(asteroid.gameObject);

        //if (asteroids.Count == 0)
        //{
        //    HandleLevelWon();
        //}
    }

    

    // ----- Game Over / Level Won -----
    public void HandleGameOver()
    {
        Vector3 shipPosition = ship.transform.position;
        Destroy(ship.gameObject);
        Instantiate(explosionParticlesPrefab, shipPosition, Quaternion.identity);

        OnGameOver?.Invoke();
    }

    private void HandleLevelWon()
    {
        ship.enabled = false; // disables ship movement/input handling
        AudioController.Instance.PlayVictorySound();
        playAgainCanvas.SetActive(true);

        OnLevelWon?.Invoke();
    }


}
}