#define HANDLE_PLAYER_COLLIDE_ASTEROID
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;

namespace Hobby.Erez.Asteroids2D{
    public class PlayerController : MonoBehaviour{
        [Header("Movement")]
        [SerializeField] private float thrustForce = 50.0f;
        [SerializeField] private float maxSpeed = 0.200f;
        [SerializeField] private AudioClip thrustAudio;
        
        
        public event Action OnPlayerHit;
        private Rigidbody2D playerRigidbody;
        private AudioSource audioSource;
        private ParticleSystem weaponSystem;
        private bool isFiring = false;

            
        private void Awake(){
            InitPlayer();
            //InitLazerParams();
        }

        private void Start(){
            isFiring = false;
        }

        private void InitPlayer(){
            playerRigidbody = GetComponent<Rigidbody2D>();
            audioSource = GetComponent<AudioSource>();
            weaponSystem = GetComponent<ParticleSystem>();
            if(weaponSystem == null){
                Debug.LogError("[PlayerController] No ParticleSystem component found on player for laser weapon.");
                return;
            }
            
        }

       void InitLazerParams(){
            /*ParticleSystem.EmitParams lazerParams = new ParticleSystem.EmitParams{
                position = spawnOrigin,
                velocity = fireDirection * laserSpeed,
                startLifetime = laserLifetime,
                startSize3D = new Vector3(laserSize.x, laserSize.y, 1f),
                startColor = laserColor,
                applyShapeToPosition = false
            };*/
        }
        private void FixedUpdate(){
            if (Mouse.current != null && Mouse.current.leftButton.isPressed){
                MovePlayer();
            }
        }

        private void MovePlayer(){
            if (Camera.main == null || Mouse.current == null) return;
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.value);
            Vector2 direction = (mousePos - transform.position).normalized;
            transform.up = direction;
            playerRigidbody.AddForce(direction * thrustForce);
        }

        public void OnFire(InputValue value){
            
            bool isPressed = value.isPressed;
            if (isPressed && !isFiring){ 
                Debug.Log($"[Player] Fire button pressed.");
                isFiring = true;
                weaponSystem.Play();
            }
            else if (!isPressed){
                isFiring = false;
                Debug.Log($"[Player] Fire button released.");
                weaponSystem.Stop();
            }
        }

        /*public void FireLaserBurst(){
            if (weaponsArray == null) return;

            Vector3 spawnOrigin = transform.position + transform.up * noseOffset;

            Vector3 fireDirection = transform.up;
            //float angle = (i - (kLaserCount - 1) / 2.0f) * spreadAngle;
            fireDirection =  transform.up;
    
            var emitParams = new ParticleSystem.EmitParams{
                position = spawnOrigin,
                velocity = fireDirection * laserSpeed,
                startLifetime = laserLifetime,
                startSize3D = new Vector3(laserSize.x, laserSize.y, 1f),
                startColor = laserColor,
                applyShapeToPosition = false
            };

            weaponSystem.Emit(emitParams, 1);
        }*/
        
        #if HANDLE_PLAYER_COLLIDE_ASTEROID
        private void OnCollisionEnter2D(Collision2D collision){
            if (collision.gameObject.CompareTag(Tags.Asteroid)){
                Debug.Log($"[Collision] Player collides with {collision.gameObject.name}");
                OnPlayerHit?.Invoke();
            }
        }
        #endif

        private void HandleGameOver(){
            /*if (audioSource != null && explosionAudio != null)
            {
                audioSource.PlayOneShot(explosionAudio);
            }
            if (TryGetComponent<SpriteRenderer>(out var sr)) sr.enabled = false;
            if (TryGetComponent<Collider2D>(out var col)) col.enabled = false;
            Destroy(gameObject, explosionAudio != null ? explosionAudio.length : 0f);
            */
            Destroy(gameObject);
        }
    }
}