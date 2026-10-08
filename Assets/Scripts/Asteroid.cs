using UnityEngine;
using System;



namespace Hobby.Erez.Asteroids2D{

public enum AsteroidSize { Huge, Big, Med, Small };

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]


public class Asteroid : MonoBehaviour{
    
    [SerializeField] AsteroidSize size = AsteroidSize.Huge; // set per-instance in Inspector
    [SerializeField] float oscillationDegrees = 5f;

    [SerializeField] Vector2 direction = Vector2.zero; // set per-instance in Inspector

    public AsteroidSize Size => size;
    public Vector2 Direction {get => direction; set => direction = value;}
    public float Oscillation => oscillationDegrees;
    
    public int Score => Asteroid.GetScore(size);
    public event Action<Asteroid> OnHit;
    public event Action OnGameOver;
    public event Action<Asteroid> OnOutOfBorders;
    private float speed;

    private Rigidbody2D rb;
    

    void Awake(){
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        if (direction == Vector2.zero){
            direction = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)).normalized;
        }
        ApplyScaleForSize();
        ApplyColor();
        speed = SetSpeed(size) * UnityEngine.Random.Range(0.8f, 1.2f);
        oscillationDegrees = SetOscillationDegrees(size);
        //ApplyScaleForSize();
    }

    void FixedUpdate(){
        /*if(Borders.Instance.IsOutSideBorders(this)){
            Debug.Log($"[Asteroid] {gameObject.name} is outside borders, destroying.");
            OnOutOfBorders?.Invoke(this);
        }
        transform.Translate(direction * speed * Time.fixedDeltaTime);
        */
    }

    void OnCollisionEnter2D(Collision2D collision){
        Debug.Log($"[Collision] Asteroid collides with {collision}");
        if (collision.gameObject.CompareTag(Tags.Border) ||
            collision.gameObject.CompareTag(Tags.Asteroid)){
            //Asteroid will change direction due to screen border collision.
            //New direction is mirror of the original direction plus a random angle oscillation.
            try{
                AudioController.Instance.PlayAsteroidBumpFX();
            }
            catch (Exception e){
                Debug.LogError($"[Asteroid] Error playing asteroid bump sound: {e.Message}");
            }
            Vector2 normal = collision.contacts[0].normal;
            Vector2 reflectedDirection = Vector2.Reflect(direction, normal).normalized;
            float oscillation = UnityEngine.Random.Range(-oscillationDegrees, oscillationDegrees);
            direction = Quaternion.Euler(0, 0, oscillation) * reflectedDirection;
            //rb.AddForce(1.75f*baseSpeed*direction);    
        }
        
    }

    void OnParticleCollision(GameObject other){
        Debug.Log($"[Particle Hit] on {other.ToString()}");
        OnHit?.Invoke(this);
    }

    
    public bool CanSplit => size != AsteroidSize.Small;

    public AsteroidSize GetSmallerSize(){
        return size switch
        {
            AsteroidSize.Huge => AsteroidSize.Big,
            AsteroidSize.Big => AsteroidSize.Med,
            AsteroidSize.Med => AsteroidSize.Small,
            _ => throw new ArgumentException("Invalid asteroid size.")
        };
    }

    private static int GetScore(AsteroidSize size) {
        return size switch{
            AsteroidSize.Huge => 10,
            AsteroidSize.Big => 20,
            AsteroidSize.Med => 30,
            AsteroidSize.Small => 40,
            _ => throw new ArgumentException("Invalid asteroid size.")
        };
    }

    private static float SetSpeed(AsteroidSize size) {
        return size switch{
            AsteroidSize.Huge => 8f,
            AsteroidSize.Big => 10f,
            AsteroidSize.Med => 12.5f,
            AsteroidSize.Small => 18f,
            _ => throw new ArgumentException("Invalid asteroid size.")
        };
    }

    private static float SetOscillationDegrees(AsteroidSize size) {
        return size switch{
            AsteroidSize.Huge => 5f,
            AsteroidSize.Big => 8f,
            AsteroidSize.Med => 10f,
            AsteroidSize.Small => 10f,
            _ => throw new ArgumentException("Invalid asteroid size.")
        };
    }
    private void ApplyScaleForSize(){
        float scale = size switch
        {
            AsteroidSize.Huge => 1.25f,
            AsteroidSize.Big => 1.50f,
            AsteroidSize.Med => 1.25f,
            AsteroidSize.Small => 2.5f,
            _ => throw new ArgumentException("Invalid asteroid size.")
        };
        transform.localScale = Vector3.one * scale;
    }

    private void ApplyColor(){
        
        GetComponent<SpriteRenderer>().color = size switch
        {
            AsteroidSize.Huge => Color.gray,
            AsteroidSize.Big => Color.lightGray,
            AsteroidSize.Med => Color.white,
            AsteroidSize.Small => Color.yellow,
            _ => Color.red
        };
        
    }

    public override string ToString(){            
        string nameAndSize = $"Asteroid Name: {gameObject.name}, Size: {size}";
        string onHitEnabled = $"OnHit is {(OnHit == null ? "not " : "")}enabled.";
        return $"{nameAndSize};\n{onHitEnabled}\n";
            
    } 

}

}