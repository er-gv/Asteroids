using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Events;
using UnityEngine.Audio;


using UnityEngine.SceneManagement;
namespace Hobby.Erez.Asteroids2D{
    
public class MainMenu : MonoBehaviour{

    /*bool isMusicOn = true;
    public AudioSource backgroundAudio;
    public float audioVolume = 0.75f;
       
    public Button btnQT;
    public Button btnAudio;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start(){

        Button btnA = new Button();
        Button btnBiBi = new Button();

        GameObject canvasGO = new GameObject("Canvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.offsetMin = Vector2.zero;
        canvasRT.offsetMax = Vector2.zero;

        GameObject btnAGO = new GameObject("ButtonA");
        btnAGO.transform.SetParent(canvasGO.transform);
        btnA = btnAGO.AddComponent<Button>();
        Image btnAImage = btnAGO.AddComponent<Image>();
        btnAImage.color = Color.white;
        RectTransform btnART = btnAGO.GetComponent<RectTransform>();
        btnART.sizeDelta = new Vector2(100, 50);
        btnART.anchoredPosition = new Vector2(-60, 0);

        GameObject btnATextGO = new GameObject("Text");
        btnATextGO.transform.SetParent(btnAGO.transform);
        Text btnAText = btnATextGO.AddComponent<Text>();
        btnAText.text = "A";
        btnAText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        btnAText.alignment = TextAnchor.MiddleCenter;
        RectTransform btnATextRT = btnATextGO.GetComponent<RectTransform>();
        btnATextRT.offsetMin = Vector2.zero;
        btnATextRT.offsetMax = Vector2.zero;

        btnA.targetGraphic = btnAImage;
        btnA.onClick.AddListener(() => {
            btnAText.text = char.IsUpper(btnAText.text[0]) ? "a" : "A";
        });

        GameObject btnBiBiGO = new GameObject("ButtonBiBi");
        btnBiBiGO.transform.SetParent(canvasGO.transform);
        btnBiBi = btnBiBiGO.AddComponent<Button>();
        Image btnBiBiImage = btnBiBiGO.AddComponent<Image>();
        btnBiBiImage.color = Color.white;
        RectTransform btnBiBiRT = btnBiBiGO.GetComponent<RectTransform>();
        btnBiBiRT.sizeDelta = new Vector2(100, 50);
        btnBiBiRT.anchoredPosition = new Vector2(60, 0);

        GameObject btnBiBiTextGO = new GameObject("Text");
        btnBiBiTextGO.transform.SetParent(btnBiBiGO.transform);
        Text btnBiBiText = btnBiBiTextGO.AddComponent<Text>();
        btnBiBiText.text = "BiBi";
        btnBiBiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        btnBiBiText.alignment = TextAnchor.MiddleCenter;
        RectTransform btnBiBiTextRT = btnBiBiTextGO.GetComponent<RectTransform>();
        btnBiBiTextRT.offsetMin = Vector2.zero;
        btnBiBiTextRT.offsetMax = Vector2.zero;

        btnBiBi.targetGraphic = btnBiBiImage;
        btnBiBi.onClick.AddListener(() => {
            string reversed = "";
            foreach (char c in btnBiBiText.text) {
                reversed += char.IsUpper(c) ? char.ToLower(c) : char.ToUpper(c);
            }
            btnBiBiText.text = reversed;
        });
        
        Debug.Log("Starting Main Menu");
        
       

        backgroundAudio = GameObject.Find("backgroundAudio").GetComponent<AudioSource>();
        if(backgroundAudio == null){
            Debug.LogError("backgroundAudio not found");
            QuitGame();
        }
        Debug.Log("Main Menu initialized successfully");
        //btnStartGame.onClick.AddListener(() => StartGame());
        if(btnQT == null){
            Debug.LogError("btnQT not assigned in Inspector");
            QuitGame();
        }
        btnQT.onClick.AddListener(QuitGame);
        //btnAudioToggle.onClick.AddListener(() => OnAudioToggleClick());
        Debug.Log($"Before setting volume: music state: {isMusicOn}, volume is {backgroundAudio.volume}");
        
        SetBackgroundAudioVolume(audioVolume);
        Debug.Log($"Initial music state: {isMusicOn}, volume is {backgroundAudio.volume}");
        //ToggleMusic();
    }

    // Update is called once per frame
    void Update(){
        
    }

    void OnDestroy(){
        Debug.Log("Destroying Main Menu");
        btnQT?.onClick.RemoveListener(QuitGame);
    }

    public void SetBackgroundAudioVolume(float volume){
        Debug.Log($"Setting background audio volume to {volume}");
        audioVolume = volume;
        backgroundAudio.volume = volume;
    }

    void ToggleMusic(){
        Debug.Log("Before Toggling music: " + isMusicOn);
        isMusicOn = !isMusicOn;
        Debug.Log("After Toggling music: " + isMusicOn);
        if(isMusicOn){
            Debug.Log("Starting music");
            backgroundAudio.Play();
        }else{
            Debug.Log("Stopping music");
            backgroundAudio.Stop();
        }
    }

    /// <summary>OnClick handler for the audio toggle button. Assign in Inspector or use from OnGUI.</summary>
    public void OnAudioToggleClick(){
        Debug.Log("Audio toggle button clicked");   
        ToggleMusic();
    }


    public void StartGame(){
        Debug.Log("Starting game");
        SceneManager.LoadScene("Game");
    }

    public void QuitGame(){
        Debug.Log("Exiting game");
        Application.Quit();
    }



    /// <summary>
    /// Displays a credits label in the GUI.
    /// </summary>
    /// <remarks>
    /// This method renders a GUI label showing credits information.
    /// Currently displays a placeholder "Credits: TODO" message.
    /// The label is positioned at coordinates (10, 70) with dimensions of 100x20 pixels.
    /// </remarks>
    void ShowCredits(){
        GUI.Label(new Rect(10, 70, 100, 20), "Credits: TODO");
    }*/
}
}