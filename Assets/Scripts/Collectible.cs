using UnityEngine;

// A falling item the player can tap to collect. Cleans itself up if missed.
[RequireComponent(typeof(Rigidbody))]
public class Collectible : MonoBehaviour
{
    public float Lifetime = 12f;

    private float m_age;

    void Update()
    {
        m_age += Time.deltaTime;
        if (m_age > Lifetime || transform.localPosition.y < -3f)
            Destroy(gameObject);
    }
}
