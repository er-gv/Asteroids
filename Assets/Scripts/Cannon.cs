using UnityEngine;
using System.Collections;

namespace Hobby.Erez.Asteroids2D
{
    public class Cannon : MonoBehaviour, IWeapon
    {
        
        [Header("Weapon Settings")]
        
        [SerializeField] private float fireInterval = 0.5f;
        [SerializeField] private float laserSpeed = 30f;
        [SerializeField] private float laserLifetime = 2.5f;
        [SerializeField] private Vector2 laserSize = new Vector2(0.15f, 0.0005f);
        [ColorUsage(true, true)]
        [SerializeField] private Color laserColor = new Color(0.5f, 2.0f, 2.5f, 1.0f);
        [SerializeField] private float noseOffset = 0f;
        

        [SerializeField] ParticleSystem weaponSystem;
        //private float fireTimer = 0f;
        
        //private bool isFiring = false;
        
        private ParticleSystem.EmitParams emitParams;
        private bool IsRunning {get; set;}
    
        public float FireInterval { get => fireInterval; set => fireInterval = value; }
        public float LaserSpeed { get => laserSpeed; set => laserSpeed = value; }
        public float LaserLifetime { get => laserLifetime; set => laserLifetime = value; }
        public Vector2 LaserSize { get => laserSize; set => laserSize = value; }
        public Color LaserColor { get => laserColor; set => laserColor = value; }

        private void Awake()
        {
            InitCannon();
        }

        private void Start()
        {
            //isFiring = false;
            //fireTimer = 0f;
            emitParams = new ParticleSystem.EmitParams
            {
                startLifetime = laserLifetime,
                startSize3D = new Vector3(laserSize.x, laserSize.y, 1f),
                startColor = laserColor,
                applyShapeToPosition = false
            };
        }

        private void InitCannon(){
            
            if (laserColor.a <= 0.001f)
            {
                laserColor = new Color(0.8f, 2.0f, 0.5f, 0.750f);
            }
           
        }

        public void StartFiring()
        {
            if(IsRunning) return;

            IsRunning = true;
            StartCoroutine(FireLaserBurst());
        }

        public void StopFiring()
        {
            StopAllCoroutines();
            IsRunning = false;
            
        }
        
        

        public IEnumerator FireLaserBurst(){
        
            while (true)
            {
                Vector3 spawnOrigin = transform.position + transform.up * noseOffset;
                Vector3 fireDirection = transform.up;
                //float angle = (i - (kLaserCount - 1) / 2.0f) * spreadAngle;
                emitParams.position = spawnOrigin;
                emitParams.velocity = fireDirection * laserSpeed;
                weaponSystem.Emit(emitParams, 1);
                yield return new WaitForSeconds( FireInterval );    
            }
        }

    }
}