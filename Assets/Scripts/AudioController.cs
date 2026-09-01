using System;
using UnityEngine;

namespace Hobby.Erez.Asteroids2D{

public class AudioController: MonoBehaviour{
    
     public static AudioController Instance { get; private set; }
    
    [Header("Audio")]
    [SerializeField] AudioSource musicSource;
    [SerializeField] AudioSource fxSource;
    [SerializeField] AudioClip victorySound;
    [SerializeField] AudioClip asteroidSplitFX;

    public void Awake(){
        
        if (Instance != null && Instance != this){
            Destroy(gameObject);
            return;
        }
        Instance = this;
        musicSource = GetComponent<AudioSource>();
        fxSource = GetComponent<AudioSource>();
        DontDestroyOnLoad(gameObject); // survives scene loads, per your "persist between levels" goal
    }

    public void MuteMusic() => musicSource.mute = true;
    public void UnmuteMusic() => musicSource.mute = false;
    public void MuteFx() => fxSource.mute = true;
    public void UnmuteFx() => fxSource.mute = false;
    public void SetMusicVolume(float volume) => musicSource.volume = Mathf.Clamp01(volume);
    public void PlayAsteroidSplitFX() => fxSource.PlayOneShot(asteroidSplitFX);
    public void PlayVictorySound() => musicSource.PlayOneShot(victorySound);
    


}
}
