using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hobby.Erez.Asteroids2D{

public class AudioController: MonoBehaviour{
    
     public static AudioController Instance { get; private set; }
    
    [Header("Audio")]
    [SerializeField] AudioClip backgroundMusic;
    [SerializeField] AudioClip victorySound;
    [SerializeField] AudioClip asteroidSplitFX;
    
    [SerializeField] AudioClip asteroidBumpFX;
    [SerializeField] AudioClip shipCrushFX;

    private AudioSource musicSource;
    private AudioSource sfxSource;
    
    public void Awake(){

        if (Instance != null && Instance != this){
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitMusic();
        InitSfxAudio();

        DontDestroyOnLoad(gameObject); // survives scene loads, per your "persist between levels" goal       
    }

    private void InitSfxAudio(){
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.volume = 1f;
    }
    private void InitMusic(){
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.clip = backgroundMusic;
        musicSource.loop = true;
        musicSource.playOnAwake = true;
        musicSource.volume = 0.5f;
        musicSource.Play();
    }

    public void ToggleMusicPause(bool mute){
        Debug.Log($"[AudioController] ToggleMusicPause: {mute}");
        if(mute){musicSource.Pause();}
        else{musicSource.UnPause();}
    }

    public void MuteMusic() => musicSource.mute = true;
    public void UnmuteMusic() => musicSource.mute = false;
    public void MuteFx() => sfxSource.mute = true;
    public void UnmuteFx() => sfxSource.mute = false;
    public void SetMusicVolume(float volume) { 
        Debug.Log($"[AudioController] SetMusicVolume: {volume}");
        musicSource.volume = Mathf.Clamp01(volume);
    }
    public void PlayAsteroidSplitFX() => sfxSource.PlayOneShot(asteroidSplitFX);
    public void PlayAsteroidBumpFX() => sfxSource.PlayOneShot(asteroidBumpFX);
    public void PlayShipCrushFX() => sfxSource.PlayOneShot(shipCrushFX);
    public void PlayVictorySound() => musicSource.PlayOneShot(victorySound);
    


}
}
