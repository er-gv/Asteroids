using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

/// <summary>
/// Attach this to a GameObject holding a UIDocument component
/// referencing MainMenu.uxml. Handles clicks on the front-screen labels.
/// </summary>
namespace Hobby.Erez.Asteroids2D{
[RequireComponent(typeof(UIDocument))]
public class GameSettingsController : MonoBehaviour{

    
    private UIDocument uiDocument;
    private Slider musicVolumeSlider;
    private Slider sfxVolumeSlider;
    private Toggle sfxToggle;
    private Toggle musicToggle;
    private Label backBtn;

    private void OnEnable(){
        uiDocument = GetComponent<UIDocument>();
        Debug.Log($"[GameSettingsController] OnEnable: uiDocument={uiDocument}");
        var root = uiDocument.rootVisualElement;
        Debug.Log($"[GameSettingsController] OnEnable: root={root}");
        
        musicVolumeSlider = root.Q<Slider>("music-slider");
        sfxVolumeSlider = root.Q<Slider>("sfx-slider");
        
        sfxToggle = root.Q<Toggle>("sfx-toggle");
        musicToggle = root.Q<Toggle>("music-toggle");
        backBtn = root.Q<Label>("back-button");
               

        backBtn?.RegisterCallback<ClickEvent>(OnBackButtonClicked);
        musicToggle?.RegisterValueChangedCallback(OnMusicToggleChanged);
        sfxToggle?.RegisterValueChangedCallback(OnSfxToggleChanged);
        musicVolumeSlider?.RegisterValueChangedCallback(OnSetMusicVolume);
        sfxVolumeSlider?.RegisterValueChangedCallback(OnSetSFXVolume);

    }

    private void Start(){
        Debug.Log($"[GameSettingsController] Start: uiDocument={uiDocument}");
        Debug.Log($"[Settings] OnEnable: Initializing sliders and toggles with current audio settings.");
        musicVolumeSlider.SetValueWithoutNotify(AudioController.Instance.MusicVolume);
        musicVolumeSlider.SetValueWithoutNotify(AudioController.Instance.SFXVolume);
        musicToggle.SetValueWithoutNotify(AudioController.Instance.IsMusicMuted);
        sfxToggle.SetValueWithoutNotify(AudioController.Instance.IsSFXMuted);
        Debug.Log($"[Settings] After setting sliders and toggles to AudioController values:");
        Debug.Log($"[Settings] music vol: {musicVolumeSlider.value}");
        Debug.Log($"[Settings] music enabled:{ musicToggle.value}");
    }

    private void OnDisable(){
        backBtn?.UnregisterCallback<ClickEvent>(OnBackButtonClicked);
        musicVolumeSlider?.UnregisterValueChangedCallback(OnSetMusicVolume);
        sfxVolumeSlider?.UnregisterValueChangedCallback(OnSetSFXVolume);
        sfxToggle?.UnregisterValueChangedCallback(OnSfxToggleChanged);
        musicToggle?.UnregisterValueChangedCallback(OnMusicToggleChanged);
    }
    
    

   private void OnSetMusicVolume(ChangeEvent<float> evt){
        Debug.Log($"[Settings] OnSetMusicVolume: New value={evt.newValue}");
        AudioController.Instance.SetMusicVolume(Mathf.Clamp01(evt.newValue));
    }

    private void OnSetSFXVolume(ChangeEvent<float> evt){
        Debug.Log($"[Settings] OnSetSFXVolume: New value={evt.newValue}");
        AudioController.Instance.SetSFXVolume(Mathf.Clamp01(evt.newValue));
        AudioController.Instance.PlayAsteroidBumpFX();
    }

    private void OnMusicToggleChanged(ChangeEvent<bool> evt){
        if(false==evt.newValue){AudioController.Instance.MuteMusic();}
        else{AudioController.Instance.UnmuteMusic();}
    }   
    
    private void OnSfxToggleChanged(ChangeEvent<bool> evt){
        if (evt.newValue){ AudioController.Instance.MuteSFX();} 
        else { AudioController.Instance.UnmuteSFX();}
    }

    private void OnBackButtonClicked(ClickEvent evt) {
        SceneManager.UnloadSceneAsync("Settings");
    }
     
}}