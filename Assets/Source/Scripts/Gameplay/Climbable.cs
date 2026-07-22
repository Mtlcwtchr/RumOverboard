using UnityEngine;

namespace RumOverboard.Gameplay
{
    /// <summary>
    /// Marks a surface as climbable (the ship's mast, rigging, ladders...).
    /// Detection is layer-based: put the object on a "Climbable" layer and point
    /// NetworkPlayer's climb LayerMask at it. This marker just carries the climb
    /// axis and gives the surface a self-documenting component.
    ///
    /// Requires a Collider (trigger is fine — CheckSphere reads triggers).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Climbable : MonoBehaviour
    {
        [Tooltip("Local axis the crew climbs along (usually up the mast).")]
        public Vector3 ClimbAxisLocal = Vector3.up;

        /// <summary>World-space climb axis at runtime.</summary>
        public Vector3 ClimbAxisWorld => transform.TransformDirection(ClimbAxisLocal.normalized);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + ClimbAxisWorld * 2f);
        }
    }
}
