namespace Hobby.Erez.Asteroids2D
{
public interface IWeapon
{
    public float FireInterval { get; set; }
    void StartFiring();
    void StopFiring();
}}