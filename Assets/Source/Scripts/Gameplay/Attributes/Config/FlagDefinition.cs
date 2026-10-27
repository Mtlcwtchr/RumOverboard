using Alchemy.Inspector;
using UnityEngine;

namespace RumOverboard.Gameplay.Attributes
{
    /// <summary>
    /// Boolean trait (Flammable, Floating, CriticalItem, Burning, Wet…). Up to 64 flags in the game;
    /// an entity's flags are one ulong, always replicated as a whole.
    /// Code refers to it through the generated <c>AttrFlag.&lt;AssetName&gt;</c> constant.
    /// </summary>
    [CreateAssetMenu(fileName = "Flag", menuName = "RumOverboard/Attributes/Flag")]
    public sealed class FlagDefinition : ScriptableObject
    {
        [SerializeField, ReadOnly] private byte id;

        [TextArea] public string Description;

        public FlagId Id => new FlagId(id);

#if UNITY_EDITOR
        /// <summary>Tooling only (registry rebuild / self-tests).</summary>
        public void EditorSetId(byte value) => id = value;
#endif
    }
}
