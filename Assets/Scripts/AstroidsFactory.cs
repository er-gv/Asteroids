using UnityEngine;

using System;
using System.Linq;
using System.Collections.Generic;

namespace Hobby.Erez.Asteroids2D{

public class AsteroidsFactory : MonoBehaviour{
    
    private Dictionary<AsteroidSize, List<GameObject>> asteroidPrefabs = new Dictionary<AsteroidSize, List<GameObject>>();
    

    private void Awake(){
    
       
       List<GameObject> collection = Resources.LoadAll<GameObject>("Asteroids/Prefabs").ToList();
       foreach (AsteroidSize size in Enum.GetValues(typeof(AsteroidSize))){
            var filtered = collection.Where(prefab => 
                prefab.name.Contains(size.ToString()));
            
            asteroidPrefabs[size] = filtered.ToList();
        }
    }
    

    private GameObject PickPrefab(AsteroidSize size){        
        List<GameObject> prefabsCollection = asteroidPrefabs[size];
        Debug.Log($"Num prefabs for size {size}: {prefabsCollection.Count()}");
        foreach(var pref in prefabsCollection){
            Debug.Log(pref);
        }
        int index = UnityEngine.Random.Range(0, prefabsCollection.Count());
        Debug.Log($"PickPrefab: {prefabsCollection[index].gameObject.name}.");
        return prefabsCollection[index];
    }

    
    public void SpawnSmallerAsteroids(Vector2 center, float parentRadius, AsteroidSize childrenSize, ref List<Asteroid> spawnedAsteroids){
        Debug.Log($"[Hit Split] Splitting asteroid of size {childrenSize} into two smaller asteroids.");
        int numChildren = UnityEngine.Random.Range(2, 3); // always split into 2 smaller asteroids
        float angleIncrement = 360f / numChildren;
        for (int i = 0; i < numChildren; i++){
            Vector2 offset = Quaternion.Euler(0, 0, angleIncrement * i) * Vector2.right * parentRadius; // offset based on asteroid size
            Asteroid newAsteroid = SpawnAsteroid(childrenSize, center + offset);
            float oscillation = UnityEngine.Random.Range(-newAsteroid.Oscillation, newAsteroid.Oscillation);
            newAsteroid.Direction = Quaternion.Euler(0, 0, oscillation) * offset; // spread out the smaller asteroids
            spawnedAsteroids.Add(newAsteroid);
        }    
    }

    public Asteroid SpawnAsteroid(AsteroidSize size, Vector2 position){
        Debug.Log($"SpawnAsteroid called, Size: {size}, Position {position}");
        GameObject prefab = PickPrefab(size);
        GameObject obj = Instantiate(prefab, position, Quaternion.identity);
        Asteroid asteroid = obj.GetComponent<Asteroid>();
        return asteroid;
    }

    

    
}
}