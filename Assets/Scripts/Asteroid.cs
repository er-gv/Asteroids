using UnityEngine;
using System;



namespace Hobby.Erez.Asteroids2D{

public enum AsteroidSize { Huge, Big, Med, Small };

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]


public class Asteroid : MonoBehaviour{
    [SerializeField] float baseSpeed = 2f;
    [SerializeField] AsteroidSize size = AsteroidSize.Huge; // set per-instance in Inspector
    [SerializeField] float oscillationDegrees = 5f;

    
    public AsteroidSize Size => size;
    public int Score => Asteroid.GetScore(size);
    public event Action<Asteroid> OnHit;
    public event Action OnGameOver;

    private Rigidbody2D rb;
    private Vector2 direction;

    void Awake(){
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        direction = UnityEngine.Random.insideUnitCircle.normalized;
        ApplyScaleForSize();
        ApplyColor();

        //ApplyScaleForSize();
    }

    void FixedUpdate()
    {
        rb.MovePosition(rb.position + direction * baseSpeed * Time.fixedDeltaTime);
    }

    void OnCollisionEnter2D(Collision2D collision){
        Debug.Log($"[Collision] Asteroid collides with {collision}");
        if (collision.gameObject.CompareTag(Tags.Border)){
            //Asteroid will change direction due to screen border collision.
            //New direction is mirror of the original direction plus a random angle oscillation.
            AudioController.Instance.PlayAsteroidBumpFX();
            Vector2 normal = collision.contacts[0].normal;
            Vector2 reflectedDirection = Vector2.Reflect(direction, normal).normalized;
            float oscillation = UnityEngine.Random.Range(-oscillationDegrees, oscillationDegrees);
            direction = Quaternion.Euler(0, 0, oscillation) * reflectedDirection;
        }
        else if(collision.gameObject.CompareTag(Tags.Ship))
        //|| collision.gameObject.CompareTag(Tags.Asteroid)
        {
            AudioController.Instance.PlayAsteroidSplitFX();
            //OnGameOver.Invoke();
        }
    }

    void OnParticleCollision(GameObject other){
        Debug.Log($"[Hit] on {other.ToString()}");
        Hit();
    }

    public void Hit(){
        OnHit?.Invoke(this);
    }

    public bool CanSplit => size != AsteroidSize.Small;

    public AsteroidSize GetSmallerSize(){
        return size switch
        {
            AsteroidSize.Huge => AsteroidSize.Big,
            AsteroidSize.Big => AsteroidSize.Med,
            AsteroidSize.Med => AsteroidSize.Small,
            _ => AsteroidSize.Small
        };
    }

    private static int GetScore(AsteroidSize size) {
        return size switch{
            AsteroidSize.Huge => 10,
            AsteroidSize.Big => 20,
            AsteroidSize.Med => 30,
            AsteroidSize.Small => 40,
            _ => 0
        };
    }

    private void ApplyScaleForSize(){
        float scale = size switch
        {
            AsteroidSize.Huge => 1.25f,
            AsteroidSize.Big => 1.50f,
            AsteroidSize.Med => 1.25f,
            AsteroidSize.Small => 2.5f,
            _ => 1f
        };
        transform.localScale = Vector3.one * scale;
    }

    private void ApplyColor(){
        
        GetComponent<SpriteRenderer>().color = size switch
        {
            AsteroidSize.Huge => Color.red,
            AsteroidSize.Big => Color.green,
            AsteroidSize.Med => Color.orange,
            AsteroidSize.Small => Color.yellow,
            _ => Color.clear
        };
        
    }

    public override string ToString(){            
        string nameAndSize = $"Asteroid Name: {gameObject.name}, Size: {size}";
        string onHitEnabled = $"OnHit is {(OnHit == null ? "not " : "")}enabled.";
        return $"{nameAndSize};\n{onHitEnabled}\n";
            
    } 

}

}