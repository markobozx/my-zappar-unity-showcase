using System.Collections;
using UnityEngine;

// A static display prop that squashes and hops when tapped.
public class ShowcaseProp : MonoBehaviour
{
    [SerializeField]
    private float m_hopHeight = 0.12f;
    [SerializeField]
    private float m_hopTime = 0.4f;

    private bool m_busy;
    private Vector3 m_restPosition;
    private Vector3 m_restScale;

    void Awake()
    {
        m_restPosition = transform.localPosition;
        m_restScale = transform.localScale;
    }

    void OnDisable()
    {
        // World is hidden whenever the target is lost; never leave a prop mid-hop.
        m_busy = false;
        transform.localPosition = m_restPosition;
        transform.localScale = m_restScale;
    }

    public void Poke()
    {
        if (!m_busy && isActiveAndEnabled)
            StartCoroutine(Hop());
    }

    private IEnumerator Hop()
    {
        m_busy = true;
        for (float t = 0f; t < 1f; t += Time.deltaTime / m_hopTime)
        {
            float arc = 4f * t * (1f - t);
            float squash = 1f + 0.15f * Mathf.Sin(t * Mathf.PI * 2f);
            transform.localPosition = m_restPosition + Vector3.up * (m_hopHeight * arc);
            transform.localScale = new Vector3(m_restScale.x * squash, m_restScale.y / squash, m_restScale.z * squash);
            yield return null;
        }
        transform.localPosition = m_restPosition;
        transform.localScale = m_restScale;
        m_busy = false;
    }
}
