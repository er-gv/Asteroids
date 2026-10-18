using System.Collections.Generic;
using UnityEngine;

namespace Hobby.Erez.Asteroids2D{
    public class Borders : MonoBehaviour{
        public static Borders Instance { get; private set; }

        [Header("Border Colliders / Objects")]
        [SerializeField] private BoxCollider2D topBorder;
        [SerializeField] private BoxCollider2D bottomBorder;
        [SerializeField] private BoxCollider2D leftBorder;
        [SerializeField] private BoxCollider2D rightBorder;

        [SerializeField] private float epsilon = 5f;

        // Cached play area bounds
        private static bool hasCachedBounds = false;
        private static float minX;
        private static float maxX;
        private static float minY;
        private static float maxY;

        public BoxCollider2D TopBorder => topBorder;
        public BoxCollider2D BottomBorder => bottomBorder;
        public BoxCollider2D LeftBorder => leftBorder;
        public BoxCollider2D RightBorder => rightBorder;

        private void Awake(){
            if (Instance != null && Instance != this){
                Destroy(gameObject);
                return;
            }
            Instance = this;
            hasCachedBounds = false;
            DontDestroyOnLoad(gameObject); // survives scene loads, per your "persist between levels" goal       
            
        }

        private void OnEnable(){
            if (!hasCachedBounds){
                CacheBorders();
                hasCachedBounds = true;
            }
        }

        public void CacheBorders(){
            if (topBorder == null || bottomBorder == null || leftBorder == null || rightBorder == null){
                throw new System.InvalidOperationException("One or more border colliders are not assigned.");
            }

            if (topBorder != null && bottomBorder != null && leftBorder != null && rightBorder != null)
            {
                // Inner edge of each border bounding box represents the playable board limit
                minX = leftBorder.bounds.min.x -epsilon;
                maxX = rightBorder.bounds.max.x + epsilon;
                minY = bottomBorder.bounds.min.y - epsilon;
                maxY = topBorder.bounds.max.y + epsilon;
                hasCachedBounds = true;
            }
        }

    public bool IsOutsideBorders(Vector2 point){
        if(!hasCachedBounds){
            throw new System.Exception("Border bounds not initialized. Ensure Borders component is present and configured.");
        } 
        return point.x < minX || point.x > maxX || point.y < minY || point.y > maxY;
    }   

    public bool IsOutsideBorders(Asteroid asteroid){
        if(!hasCachedBounds){
            throw new System.Exception("Border bounds not initialized. Ensure Borders component is present and configured.");
        } 
        if (asteroid == null){
            throw new System.ArgumentNullException("Asteroid is null.");
        }

        Collider2D asteroidCollider = asteroid.GetComponent<Collider2D>();
        if (asteroidCollider == null)
            throw new System.Exception("Asteroid collider not found.");

        Bounds asteroidBounds = asteroidCollider.bounds;
        // Outside if the asteroid bounds extend beyond the playable boundaries
        return 
            asteroidBounds.min.x < minX ||
            asteroidBounds.max.x > maxX ||
            asteroidBounds.min.y < minY ||
            asteroidBounds.max.y > maxY ;
        
    }
}}
