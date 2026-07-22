using System.Collections.Generic;
using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    public interface IOceanCurrentZone
    {
        Vector3 EvaluateCurrent(Vector3 worldPos, float time);
    }

    public class OceanCurrentSystem : MonoBehaviour
    {
        [SerializeField] private Vector3 globalCurrent = new Vector3(0.3f, 0f, 0.1f);
        [SerializeField] private bool autoDiscoverZones = true;

        private readonly List<IOceanCurrentZone> _zones = new List<IOceanCurrentZone>(16);

        public Vector3 GlobalCurrent
        {
            get => globalCurrent;
            set => globalCurrent = value;
        }

        private void Awake()
        {
            RebuildZoneCache();
        }

        private void OnValidate()
        {
            if (autoDiscoverZones)
                RebuildZoneCache();
        }

        public void RebuildZoneCache()
        {
            _zones.Clear();
            if (!autoDiscoverZones)
                return;

            var zones = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] is IOceanCurrentZone zone)
                    _zones.Add(zone);
            }
        }

        public Vector3 EvaluateCurrent(Vector3 worldPos, float time)
        {
            Vector3 current = globalCurrent;
            for (int i = 0; i < _zones.Count; i++)
                current += _zones[i].EvaluateCurrent(worldPos, time);
            return current;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.0f, 0.8f, 1f, 0.8f);
            Vector3 origin = transform.position;
            Gizmos.DrawLine(origin, origin + globalCurrent * 8f);
            Gizmos.DrawSphere(origin + globalCurrent * 8f, 0.2f);
        }
#endif
    }
}

