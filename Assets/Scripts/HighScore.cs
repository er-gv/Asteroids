using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

namespace Hobby.Erez.Asteroids2D
{
    /// <summary>
    /// Persistent high-score table. The current score is read from ScoreManager
    /// only when SaveHighScore is called.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HighScore : MonoBehaviour{
        private const string HighScoreTableKey = "high_score_table";
        private const int MaxHighScores = 10;

        [SerializeField] private string playerName = "Player1";
        [SerializeField] private List<HighScoreRecord> records = new List<HighScoreRecord>();

        private UIDocument uiDocument;
        [System.Serializable]
        public class HighScoreRecord{
            public string playerName;
            public int score;
        }

        [System.Serializable]
        private class HighScoreTable{
            public List<HighScoreRecord> records = new List<HighScoreRecord>();
        }

        private void Awake(){
            
            LoadHighScores();
            
        }

        void Start(){
            RenderHighScores();
        }

        public void OnEnable(){
            uiDocument = GetComponent<UIDocument>();
            var root = uiDocument.rootVisualElement;
            var backButton = root.Q<Label>("back-button");
            backButton.RegisterCallback<ClickEvent>(OnBackClicked);
            
        }

        public void OnDisable(){
            var root = uiDocument.rootVisualElement;
            var backButton = root.Q<Label>("back-button");
            backButton.UnregisterCallback<ClickEvent>(OnBackClicked);
        }
    
        public string GetPlayerName(){
            return playerName;
        }

        public void SetPlayerName(string name)
        {
            playerName = string.IsNullOrWhiteSpace(name) ? "Player1" : name;
        }

        public IReadOnlyList<HighScoreRecord> GetHighScores(){
            return records;
        }

        public void SaveHighScore(){
            if (ScoreManager.Instance == null){
                return;
            }

            int currentScore = ScoreManager.Instance.Score;
            HighScoreRecord existingRecord = records.Find(record => record.playerName == playerName);

            if (existingRecord == null){
                records.Add(new HighScoreRecord { playerName = playerName, score = currentScore });
            }
            else if (currentScore > existingRecord.score){
                existingRecord.score = currentScore;
            }

            records.Sort((first, second) => second.score.CompareTo(first.score));

            if (records.Count > MaxHighScores){
                records.RemoveRange(MaxHighScores, records.Count - MaxHighScores);
            }

            SaveHighScores();
        }

        private void RenderHighScores(){
            
            var root = uiDocument.rootVisualElement;
            var highScoreList = root.Q<VisualElement>("high-score-list");
            if (records.Count == 0){
                highScoreList.Add(new Label("No scores recorded yet.") { name = "empty-score-entry" });
                return;
            }

            for (int index = 0; index < records.Count; index++){
                var record = records[index];
                var entry = new Label($"{index + 1,2}.  {record.playerName,-16} {record.score,6}"){
                    name = $"score-entry-{index + 1}"
                };
                highScoreList.Add(entry);
            }
        }

        public void LoadHighScores(){
            records = new List<HighScoreRecord>();
            string json = PlayerPrefs.GetString(HighScoreTableKey, "");

            if (!string.IsNullOrEmpty(json)){
                HighScoreTable table = JsonUtility.FromJson<HighScoreTable>(json);
                if (table != null && table.records != null){
                    records = table.records;
                }
            }
        }

        private void SaveHighScores(){
            HighScoreTable table = new HighScoreTable { records = records };
            PlayerPrefs.SetString(HighScoreTableKey, JsonUtility.ToJson(table));
            PlayerPrefs.Save();
        }

        private void OnBackClicked(ClickEvent evt){
            SceneManager.UnloadSceneAsync(SceneManager.GetActiveScene().buildIndex);
            SceneManager.LoadScene("MainMenu");
        }
    }
}
