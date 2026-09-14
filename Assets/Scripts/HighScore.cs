using System.Collections.Generic;
using UnityEngine;
using Hobby.Erez.Asteroids2D;

namespace Hobby.Erez.Asteroids2D
{
    /// <summary>
    /// Persistent high-score table. The current score is read from ScoreManager
    /// only when SaveHighScore is called.
    /// </summary>
    public class HighScore : MonoBehaviour{
        private const string HighScoreTableKey = "high_score_table";
        private const int MaxHighScores = 10;

        [SerializeField] private string playerName = "Player1";
        [SerializeField] private List<HighScoreRecord> records = new List<HighScoreRecord>();

        [System.Serializable]
        public class HighScoreRecord
        {
            public string playerName;
            public int score;
        }

        [System.Serializable]
        private class HighScoreTable
        {
            public List<HighScoreRecord> records = new List<HighScoreRecord>();
        }

        private void Awake()
        {
            LoadHighScores();
        }

        public string GetPlayerName()
        {
            return playerName;
        }

        public void SetPlayerName(string name)
        {
            playerName = string.IsNullOrWhiteSpace(name) ? "Player1" : name;
        }

        public IReadOnlyList<HighScoreRecord> GetHighScores()
        {
            return records;
        }

        public void SaveHighScore()
        {
            if (ScoreManager.Instance == null)
            {
                return;
            }

            int currentScore = ScoreManager.Instance.GetScore();
            HighScoreRecord existingRecord = records.Find(record => record.playerName == playerName);

            if (existingRecord == null)
            {
                records.Add(new HighScoreRecord { playerName = playerName, score = currentScore });
            }
            else if (currentScore > existingRecord.score)
            {
                existingRecord.score = currentScore;
            }

            records.Sort((first, second) => second.score.CompareTo(first.score));

            if (records.Count > MaxHighScores)
            {
                records.RemoveRange(MaxHighScores, records.Count - MaxHighScores);
            }

            SaveHighScores();
        }

        public void LoadHighScores()
        {
            records = new List<HighScoreRecord>();
            string json = PlayerPrefs.GetString(HighScoreTableKey, "");

            if (!string.IsNullOrEmpty(json))
            {
                HighScoreTable table = JsonUtility.FromJson<HighScoreTable>(json);
                if (table != null && table.records != null)
                {
                    records = table.records;
                }
            }
        }

        private void SaveHighScores()
        {
            HighScoreTable table = new HighScoreTable { records = records };
            PlayerPrefs.SetString(HighScoreTableKey, JsonUtility.ToJson(table));
            PlayerPrefs.Save();
        }
    }
}
