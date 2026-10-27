using UnityEngine;

namespace RumOverboard.Gameplay.Items
{
    /// <summary>
    /// Local cosmetic debris (Tier C — computed per peer, never replicated): a few chunks flying out
    /// and falling, moved by hand (no physics, so it works on clients that don't simulate).
    /// </summary>
    public sealed class DebrisChunk : MonoBehaviour
    {
        private Vector3 _velocity;
        private Vector3 _spin;
        private float _life;

        public static void Burst(Bounds bounds, Material material, int count = 6)
        {
            Vector3 size = Vector3.Max(bounds.size * 0.3f, Vector3.one * 0.05f);
            for (int i = 0; i < count; i++)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(go.GetComponent<Collider>());
                go.name = "Debris";
                go.transform.position = bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents * 0.6f);
                go.transform.rotation = Random.rotation;
                go.transform.localScale = size * Random.Range(0.6f, 1.1f);
                if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
                var chunk = go.AddComponent<DebrisChunk>();
                chunk._velocity = (go.transform.position - bounds.center).normalized * Random.Range(1.5f, 3.5f) + Vector3.up * 2.5f;
                chunk._spin = Random.insideUnitSphere * 360f;
                chunk._life = Random.Range(2.5f, 4f);
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _velocity += Physics.gravity * dt;
            transform.position += _velocity * dt;
            transform.Rotate(_spin * dt, Space.World);
            _life -= dt;
            if (_life <= 0f) Destroy(gameObject);
        }
    }
}
