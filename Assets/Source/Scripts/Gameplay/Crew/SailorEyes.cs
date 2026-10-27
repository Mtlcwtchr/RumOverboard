using UnityEngine;

namespace RumOverboard.Gameplay.Crew
{
    /// <summary>Eye drawing on the sailor's face decal. Order = cells of the eyes atlas (see Character.md).</summary>
    public enum EyeStyle
    {
        Dots = 0,
        Sleepy = 1,
        Angry = 2,
        Bulging = 3,
        Patch = 4,
        Crosses = 5, // knocked out
    }

    /// <summary>
    /// Picks the eye drawing on the sailor model's face decal (material slot "M_SailorFace"): the decal
    /// samples one cell of a 3x2 atlas, chosen by the material's base-map tiling/offset through a
    /// per-renderer MaterialPropertyBlock (the shared material stays untouched). Cosmetic, per peer.
    /// </summary>
    [ExecuteAlways]
    public sealed class SailorEyes : MonoBehaviour
    {
        public const int Columns = 3;
        public const int Rows = 2;
        public const string FaceMaterialName = "M_SailorFace";
        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        [SerializeField] private SkinnedMeshRenderer target;
        [SerializeField] private EyeStyle style = EyeStyle.Dots;

        private MaterialPropertyBlock _block;

        public EyeStyle Style
        {
            get => style;
            set
            {
                style = value;
                Apply();
            }
        }

        /// <summary>Base-map tiling (xy) and offset (zw) of the atlas cell for <paramref name="s"/>.</summary>
        public static Vector4 CellScaleOffset(EyeStyle s)
        {
            int i = (int)s;
            int col = i % Columns;
            int row = Rows - 1 - i / Columns; // first three styles are the top row
            return new Vector4(1f / Columns, 1f / Rows, (float)col / Columns, (float)row / Rows);
        }

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        public void Apply()
        {
            if (target == null)
                target = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (target == null)
                return;

            int index = FaceMaterialIndex(target);
            if (index < 0)
                return;

            _block ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(_block, index);
            _block.SetVector(BaseMapST, CellScaleOffset(style));
            target.SetPropertyBlock(_block, index);
        }

        public static int FaceMaterialIndex(Renderer renderer)
        {
            Material[] mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].name.StartsWith(FaceMaterialName))
                    return i;
            return -1;
        }
    }
}
