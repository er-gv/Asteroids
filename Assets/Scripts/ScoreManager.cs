using UnityEngine;

   
namespace Hobby.Erez.Asteroids2D
{
/// <summary>
/// Persistent score tracker for a Unity game.
/// Attach this to a persistent GameObject (e.g. a GameManager that survives
/// scene loads via DontDestroyOnLoad), or use it as a static/singleton utility.
///
/// Persistence uses Unity's PlayerPrefs, which stores data locally on disk
/// (registry on Windows, plist on macOS/iOS, XML on Android/Linux).
/// This is the standard "simple disk DB" for small game data like scores.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    private const string ScoreKey = "player_score";

    public static ScoreManager Instance { get; private set; }

    [SerializeField] private int score;

    /// <summary>Fired whenever the score changes, passing the new score.</summary>
    public event System.Action<int> OnScoreChanged;

    private void Awake(){
        // Simple singleton so ScoreManager.Instance is reachable from anywhere.
        if (Instance != null && Instance != this){
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadScore();
    }

    /// <summary>Current score value.</summary>
    public int GetScore(){
        return score;
    }

    /// <summary>Adds (or subtracts, if negative) to the score and persists it.</summary>
    public void AddScore(int amount)
    {
        score += amount;
        if (score < 0) score = 0;

        OnScoreChanged?.Invoke(score);
        //SaveScore();
    }

    /// <summary>Resets score to zero and persists it.</summary>
    public void ResetScore()
    {
        score = 0;
        OnScoreChanged?.Invoke(score);
        //SaveScore();
    }

    /// <summary>Writes the current score to disk.</summary>
    public void SaveScore()
    {
        PlayerPrefs.SetInt(ScoreKey, score);
        PlayerPrefs.Save(); // forces an immediate flush to disk
    }

    /// <summary>Loads the score from disk into memory.</summary>
    public void LoadScore()
    {
        score = PlayerPrefs.GetInt(ScoreKey, 0);
        OnScoreChanged?.Invoke(score);
    }
}
}