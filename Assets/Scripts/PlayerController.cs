using UnityEngine;
using UnityEngine.InputSystem;
//using UnityEngine.SceneManagement;

//using UIController;
namespace Hobby.Erez.Asteroids2D{
public class PlayerController : MonoBehaviour{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField] float thrustForce = 50.0f;
    [SerializeField] float maxSpeed = 0.200f;
    [SerializeField] private AudioClip thrustAudio;
    [SerializeField] private AudioClip explosionAudio;
    //[SerializeField] GameObject exhustFlame;
    
    private Rigidbody2D playerRigidbody;
    private AudioSource audioSource;
    private ParticleSystem wepondSystem;
    private ParticleSystem explosionParticles;
    private float elapsedTime = 0f;
    private int firePressCount = 0;
    private bool isFiring;

   

    
    
    //public GameObject thrustSound;
    //public GameObject bounceSound;
    
    //private UIController uiController;
    
    void Awake(){
        InitPlayer();
    }

    void Start(){
        isFiring = false;
        //uiController = new UIController();
        //uiController.InitUI();
    }

    
    private void InitPlayer(){
        playerRigidbody = GetComponent<Rigidbody2D>();
        audioSource = GetComponent<AudioSource>();
        wepondSystem = GetComponent<ParticleSystem>();
        //explosionParticles = GetComponent<ParticleSystem>();
        //exhustFlame.SetActive(false);
        //thrustSound.SetActive(false);
        //bounceSound.SetActive(false);
    }
    // Update is called once per frame
    
    void Update(){
        
        if (Mouse.current.leftButton.wasPressedThisFrame){
            //Debug.Log("[Player] exhustFlame.SetActive(true)");
            //thrustSound.SetActive(true);
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame){
            //Debug.Log("[Player] exhustFlame.SetActive(false)");
            //thrustSound.SetActive(false);
        }

        
                
        //uiController.UpdateScore();
    }
    void FixedUpdate(){
        if (Mouse.current.leftButton.isPressed){  
            //audioSource.PlayOneShot(thrustAudio);
            MovePlayer();
        }
    }
    /*private void UpdateScore(){
        elapsedTime += Time.deltaTime;
        score = Mathf.FloorToInt(elapsedTime * scoreMultiplier);
        scoreLabel.text = "Score: " + score;
    }*/

    private void MovePlayer(){
        //Debug.Log("[Player]  called.");
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.value);
        Vector2 direction = (mousePos - transform.position).normalized;
        transform.up = direction;           
        playerRigidbody.AddForce(direction * thrustForce);
       
        //if (playerRigidbody.linearVelocity.magnitude > maxSpeed){
        //    playerRigidbody.linearVelocity = playerRigidbody.linearVelocity.normalized * maxSpeed;
        //}
    }

    public void OnFire(InputValue value){
        isFiring = value.isPressed;

        if (isFiring){
            firePressCount++;
            Debug.Log($"[Player] Fire button pressed and held down ({firePressCount}).");
            // particleSystem.ShootOneProjectile();
        }
        else{
            Debug.Log($"[Player] Fire button released after being held down ({firePressCount}).");
        }   
        // Ignore release events.
    }   

    void OnCollisionEnter2D(Collision2D collision){
        //Debug.Log($"[bounce] Player collided with an object tagged {collision.gameObject.tag}");        
        if (collision.gameObject.CompareTag("t_asteroid")){
            
        }
        
        if (collision.gameObject.CompareTag("t_border")){     
           // Debug.Log("[bounce] PlayBounceSound()");
        }
        
    }
    
    void ReloadScene() {
        //SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void HandleGameOver(){
       //Debug.Log("[bounce] GameOver");
       
        audioSource.PlayOneShot(explosionAudio);
        GetComponent<SpriteRenderer>().enabled = false;
        GetComponent<Collider2D>().enabled = false;
        Destroy(gameObject, explosionAudio.length);
        //mainAudio.Play(explosionEffect);
        //Instantiate(explosionEffect, transform.position, transform.rotation);
        /*thrustSound.SetActive(false);
        
        
        Instantiate(explosionSound, transform.position, transform.rotation);
        //uiController.ToggleOnRestartButton();
        */
    }

    void PlayBounceSound(){
        //bounceSound.SetActive(true);
        
        // Implement bounce sound effect here
    }

    
}
}