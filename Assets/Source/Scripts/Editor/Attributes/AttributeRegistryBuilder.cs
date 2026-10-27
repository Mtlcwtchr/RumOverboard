using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RumOverboard.Gameplay.Attributes;
using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools.Attributes
{
    /// <summary>
    /// Assigns stable ids to every Attribute / Flag / Effect definition asset, fills the
    /// <see cref="AttributeRegistry"/> (Resources) and generates <c>Attr.g.cs</c> with typed constants
    /// (<c>Attr.Stamina</c>, <c>AttrFlag.Flammable</c>, <c>AttrEffect.Wet</c>).
    ///
    /// Ids already assigned are kept (adding/removing assets never renumbers the rest), so ids stay
    /// the same between builds of the same content. Runs automatically when definition assets are
    /// created / renamed / deleted; also available from the menu.
    /// </summary>
    public static class AttributeRegistryBuilder
    {
        public const string ConfigFolder = "Assets/Source/Configs/Attributes";
        public const string RegistryPath = ConfigFolder + "/Resources/" + AttributeRegistry.ResourcesPath + ".asset";
        public const string GeneratedPath = "Assets/Source/Scripts/Gameplay/Attributes/Generated/Attr.g.cs";

        [MenuItem("RumOverboard/Attributes/Rebuild Registry + Codegen", priority = 0)]
        public static void RebuildMenu() => Rebuild(log: true);

        public static void Rebuild(bool log)
        {
            List<AttributeDefinition> attributes = FindAll<AttributeDefinition>();
            List<FlagDefinition> flags = FindAll<FlagDefinition>();
            List<EffectDefinition> effects = FindAll<EffectDefinition>();

            int changed = 0;
            changed += AssignIds(attributes, a => a.Id.Value, (a, v) => a.EditorSetId((ushort)v), ushort.MaxValue);
            changed += AssignIds(flags, f => f.Id.Value, (f, v) => f.EditorSetId((byte)v), FlagId.MaxFlags);
            changed += AssignIds(effects, e => e.Id.Value, (e, v) => e.EditorSetId((ushort)v), ushort.MaxValue);

            if (flags.Count > FlagId.MaxFlags)
                Debug.LogError($"[Attributes] {flags.Count} flags defined, max is {FlagId.MaxFlags}. Extra flags got no id.");

            attributes.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
            flags.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
            effects.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));

            AttributeRegistry registry = LoadOrCreateRegistry();
            registry.EditorSet(attributes, flags, effects, ComputeHash(attributes, flags, effects));
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();

            bool codeChanged = WriteGenerated(attributes, flags, effects);

            if (log)
                Debug.Log($"[Attributes] Registry rebuilt: {attributes.Count} attributes, {flags.Count} flags, " +
                          $"{effects.Count} effects; {changed} id(s) assigned; codegen {(codeChanged ? "updated" : "unchanged")}.", registry);
        }

        // ───────────────────────── ids ─────────────────────────

        private static int AssignIds<T>(List<T> assets, System.Func<T, int> getId, System.Action<T, int> setId, int maxId)
            where T : ScriptableObject
        {
            // Stable order for newcomers: by asset path.
            assets.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));

            var used = new HashSet<int>();
            var needId = new List<T>();
            foreach (T asset in assets)
            {
                int id = getId(asset);
                if (id > 0 && id <= maxId && used.Add(id)) continue;
                needId.Add(asset); // unassigned or duplicate (e.g. asset duplicated with Ctrl+D)
            }

            int next = 1;
            int changed = 0;
            foreach (T asset in needId)
            {
                while (used.Contains(next)) next++;
                if (next > maxId)
                {
                    setId(asset, 0);
                    EditorUtility.SetDirty(asset);
                    continue;
                }
                used.Add(next);
                setId(asset, next);
                EditorUtility.SetDirty(asset);
                changed++;
            }
            return changed;
        }

        private static int ComputeHash(List<AttributeDefinition> attributes, List<FlagDefinition> flags, List<EffectDefinition> effects)
        {
            unchecked
            {
                int hash = (int)2166136261;
                void Mix(int id, Object asset)
                {
                    hash = (hash ^ id) * 16777619;
                    foreach (char c in AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)))
                        hash = (hash ^ c) * 16777619;
                }
                foreach (var a in attributes) Mix(a.Id.Value, a);
                foreach (var f in flags) Mix(f.Id.Value + 0x10000, f);
                foreach (var e in effects) Mix(e.Id.Value + 0x20000, e);
                return hash;
            }
        }

        // ───────────────────────── assets ─────────────────────────

        private static List<T> FindAll<T>() where T : Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(guid => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(a => a != null)
                .Distinct()
                .ToList();

        private static AttributeRegistry LoadOrCreateRegistry()
        {
            var registry = AssetDatabase.LoadAssetAtPath<AttributeRegistry>(RegistryPath);
            if (registry != null) return registry;

            EnsureFolder(Path.GetDirectoryName(RegistryPath)?.Replace('\\', '/'));
            registry = ScriptableObject.CreateInstance<AttributeRegistry>();
            AssetDatabase.CreateAsset(registry, RegistryPath);
            return registry;
        }

        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ───────────────────────── codegen ─────────────────────────

        private static bool WriteGenerated(List<AttributeDefinition> attributes, List<FlagDefinition> flags, List<EffectDefinition> effects)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Generated by RumOverboard ▸ Attributes ▸ Rebuild Registry. Do not edit by hand.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
            sb.AppendLine("namespace RumOverboard.Gameplay.Attributes");
            sb.AppendLine("{");

            AppendClass(sb, "Attr", "AttributeId", attributes.Where(a => a.Id.IsValid)
                .Select(a => (a.name, (int)a.Id.Value, a.Kind.ToString())));
            sb.AppendLine();
            AppendClass(sb, "AttrFlag", "FlagId", flags.Where(f => f.Id.IsValid)
                .Select(f => (f.name, (int)f.Id.Value, "flag")));
            sb.AppendLine();
            AppendClass(sb, "AttrEffect", "EffectId", effects.Where(e => e.Id.IsValid)
                .Select(e => (e.name, (int)e.Id.Value, "effect")));

            sb.AppendLine("}");

            string text = sb.ToString().Replace("\r\n", "\n");
            string fullPath = Path.GetFullPath(GeneratedPath);
            if (File.Exists(fullPath) && File.ReadAllText(fullPath).Replace("\r\n", "\n") == text)
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
            File.WriteAllText(fullPath, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(GeneratedPath);
            return true;
        }

        private static void AppendClass(StringBuilder sb, string className, string idType, IEnumerable<(string name, int id, string comment)> items)
        {
            sb.AppendLine($"    public static class {className}");
            sb.AppendLine("    {");
            var usedNames = new HashSet<string>();
            foreach (var (assetName, id, comment) in items)
            {
                string field = ToIdentifier(assetName);
                if (!usedNames.Add(field)) field = $"{field}_{id}";
                string cast = idType == "FlagId" ? "byte" : "ushort";
                sb.AppendLine($"        /// <summary>{comment}: {assetName}</summary>");
                sb.AppendLine($"        public static readonly {idType} {field} = new {idType}(({cast}){id});");
            }
            sb.AppendLine("    }");
        }

        private static string ToIdentifier(string name)
        {
            var sb = new StringBuilder();
            bool upper = true;
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(upper ? char.ToUpperInvariant(c) : c);
                    upper = false;
                }
                else upper = true;
            }
            if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }
    }

    /// <summary>Re-runs the registry rebuild when definition assets are added / renamed / deleted.</summary>
    internal sealed class AttributeDefinitionPostprocessor : AssetPostprocessor
    {
        private static bool _scheduled;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // MPPM clone editors share Assets read-only: only the main editor owns ids and codegen.
            if (_scheduled || !Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor) return;
            if (!Touches(imported) && !Touches(moved) && !TouchesDeleted(deleted)) return;

            _scheduled = true;
            EditorApplication.delayCall += () =>
            {
                _scheduled = false;
                AttributeRegistryBuilder.Rebuild(log: false);
            };
        }

        private static bool Touches(string[] paths)
        {
            foreach (string p in paths)
            {
                if (!p.EndsWith(".asset")) continue;
                System.Type t = AssetDatabase.GetMainAssetTypeAtPath(p);
                if (t == typeof(AttributeDefinition) || t == typeof(FlagDefinition) || t == typeof(EffectDefinition))
                    return true;
            }
            return false;
        }

        // Type of a deleted asset is unknown; only react inside the attributes config folder.
        private static bool TouchesDeleted(string[] paths)
        {
            foreach (string p in paths)
                if (p.EndsWith(".asset") && p.StartsWith(AttributeRegistryBuilder.ConfigFolder) && !p.Contains("/Resources/"))
                    return true;
            return false;
        }
    }
}
