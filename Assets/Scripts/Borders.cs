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

        /*private void Reset(){
            AutoAssignBorders();
        }

        [ContextMenu("Auto Assign Borders")]
        /*public void AutoAssignBorders(){
            // Search children or scene for colliders
            BoxCollider2D[] colliders = GetComponentsInChildren<BoxCollider2D>();
            if (colliders.Length == 0){
                GameObject borderRoot = GameObject.Find("Borders");
                if (borderRoot != null){
                    colliders = borderRoot.GetComponentsInChildren<BoxCollider2D>();
                }
            }

            foreach (var col in colliders)
            {
                Vector3 center = col.bounds.center;
                Vector3 extents = col.bounds.extents;

                // Horizontal borders have larger extents.x than extents.y
                if (extents.x > extents.y)
                {
                    if (center.y > 0)
                        topBorder = col;
                    else
                        bottomBorder = col;
                }
                else // Vertical borders have larger extents.y than extents.x
                {
                    if (center.x > 0)
                        rightBorder = col;
                    else
                        leftBorder = col;
                }
            }

            CacheBorders();
        }

        public void CacheBorders(){
            if (topBorder == null || bottomBorder == null || leftBorder == null || rightBorder == null){
                AutoAssignBorders();
            }

            if (topBorder != null && bottomBorder != null && leftBorder != null && rightBorder != null)
            {
                // Inner edge of each border bounding box represents the playable board limit
                minX = leftBorder.bounds.max.x;
                maxX = rightBorder.bounds.min.x;
                minY = bottomBorder.bounds.max.y;
                maxY = topBorder.bounds.min.y;
                hasCachedBounds = true;
            }
        }

        /// <summary>
        /// Tests whether the input asteroid is outside of the rectangular game board defined by the borders.
        /// Uses the asteroid's collider bounds.
        /// </summary>
        /// <param name="asteroid">The asteroid to test.</param>
        /// <returns>True if the asteroid is outside the board borders, false otherwise.</returns>
        public static void AssertExistingBounds(){
        
            Collider2D asteroidCollider = asteroid.GetComponent<Collider2D>();
            if (asteroidCollider == null)
                throw new System.Exception("Asteroid collider not found.");

            // Ensure borders are initialized / cached
            if (!hasCachedBounds)
            {
                if (instance != null)
                {
                    instance.CacheBorders();
                }
                else
                {
                    Borders found = FindFirstObjectByType<Borders>();
                    if (found != null)
                    {
                        instance = found;
                        found.CacheBorders();
                    }
                    else
                    {
                        // Fallback: try locating border colliders directly by tag or root name
                        GameObject borderRoot = GameObject.Find("Borders");
                        if (borderRoot != null)
                        {
                            var b = borderRoot.GetComponent<Borders>() ?? borderRoot.AddComponent<Borders>();
                            instance = b;
                            b.CacheBorders();
                        }
                    }
                }
            }

            if (!hasCachedBounds)
            {
                Debug.LogWarning("[Borders] Border bounds not initialized.");
                throw new System.Exception("Border bounds not initialized. Ensure Borders component is present and configured.");                return false;
            }

        }
    */

    public bool IsOutSideBorders(Asteroid asteroid){
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
