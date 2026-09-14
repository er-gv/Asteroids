using UnityEngine;
using UnityEngine.InputSystem;

namespace Hobby.Erez.Asteroids2D
{
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float thrustForce = 50.0f;
        [SerializeField] private float maxSpeed = 0.200f;
        [SerializeField] private AudioClip thrustAudio;
        
        [Header("Laser Weapon Settings")]
        
        [SerializeField] private GameObject[] weaponsArray;
        //[SerializeField] private float fireInterval = 1.0f;
        //[SerializeField] private float laserSpeed = 30f;
        //[SerializeField] private float laserLifetime = 2.5f;
        //[SerializeField] private Vector2 laserSize = new Vector2(0.15f, 1.2f);
        [ColorUsage(true, true)]
        //[SerializeField] private Color laserColor = new Color(0.5f, 2.0f, 2.5f, 1.0f);
        //[SerializeField] private float noseOffset = 0.5f;
        

        private Rigidbody2D playerRigidbody;
        private AudioSource audioSource;
        private ParticleSystem weaponSystem;
        private float fireTimer = 0f;
        private int firePressCount = 0;
        private bool isFiring = false;

    
        /*public float FireInterval { get => fireInterval; set => fireInterval = value; }
        public float LaserSpeed { get => laserSpeed; set => laserSpeed = value; }
        public float LaserLifetime { get => laserLifetime; set => laserLifetime = value; }
        public Vector2 LaserSize { get => laserSize; set => laserSize = value; }
        public Color LaserColor { get => laserColor; set => laserColor = value; }
*/
        private void Awake()
        {
            InitPlayer();
        }

        private void Start()
        {
            isFiring = false;
            fireTimer = 0f;
        }

        private void InitPlayer()
        {
            playerRigidbody = GetComponent<Rigidbody2D>();
            audioSource = GetComponent<AudioSource>();
            weaponSystem = GetComponent<ParticleSystem>();

            /*if (laserSize == Vector2.zero)
            {
                laserSize = new Vector2(0.15f, 1.2f);
            }
            if (laserColor.a <= 0.001f)
            {
                laserColor = new Color(0.5f, 2.0f, 2.5f, 1.0f);
            }
            if (noseOffset <= 0.001f)
            {
                noseOffset = 0.5f;
            }*/
        }

       
        private void FixedUpdate()
        {
            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                MovePlayer();
            }
        }

        private void MovePlayer()
        {
            if (Camera.main == null || Mouse.current == null) return;
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.value);
            Vector2 direction = (mousePos - transform.position).normalized;
            transform.up = direction;
            playerRigidbody.AddForce(direction * thrustForce);
        }

        public void OnFire(InputValue value){
            
            if (value.isPressed && !isFiring){ 
                isFiring = true;
                foreach (GameObject weapon in weaponsArray){
                    var cannon = weapon.GetComponent<Cannon>();
                    if (cannon != null){
                        cannon.StartFiring();
                    }
                }
            }
            else if (!value.isPressed){
                isFiring = false;
                Debug.Log($"[Player] Fire button released after being held down ({firePressCount}).");
                foreach (GameObject weapon in weaponsArray){
                    var cannon = weapon.GetComponent<Cannon>();
                    if (cannon != null){
                        cannon.StopFiring();
                    }
                }
            }
        }

        /*public void FireLaserBurst()
        {
            if (weaponsArray == null) return;

            Vector3 spawnOrigin = transform.position + transform.up * noseOffset;

            Vector3 fireDirection = transform.up;
            //float angle = (i - (kLaserCount - 1) / 2.0f) * spreadAngle;
            fireDirection =  transform.up;
    
            var emitParams = new ParticleSystem.EmitParams
            {
                position = spawnOrigin,
                velocity = fireDirection * laserSpeed,
                startLifetime = laserLifetime,
                startSize3D = new Vector3(laserSize.x, laserSize.y, 1f),
                startColor = laserColor,
                applyShapeToPosition = false
            };

            weaponSystem.Emit(emitParams, 1);
        }*/

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision.gameObject.CompareTag(Tags.Asteroid))
            {
                Debug.Log($"[Collision] Player collides with {collision.gameObject.name}");
                //HandleGameOver();
            }

            if (collision.gameObject.CompareTag(Tags.Border))
            {
                Debug.Log($"[Collision] Player collides with {collision.gameObject.name}");
                AudioController.Instance.PlayVictorySound();            
            }
        }

        private void HandleGameOver()
        {
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