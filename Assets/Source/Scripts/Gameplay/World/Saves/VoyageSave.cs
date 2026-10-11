using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RumOverboard.Gameplay.World.Saves
{
    /// <summary>
    /// A saved voyage: everything needed to regenerate the same world and resume progress.
    /// The world is fully deterministic from (Seed, WindDegrees), so only those plus the
    /// reached checkpoint are stored. Host-only: clients never write saves.
    /// </summary>
    [Serializable]
    public sealed class VoyageSave
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public string Id;
        public string Name;
        /// <summary>World seed; 0 = not generated yet (picked by the world on first launch).</summary>
        public int Seed;
        /// <summary>Wind direction used for generation; negative = not generated yet (use the scene wind).</summary>
        public float WindDegrees = -1f;
        /// <summary>Last checkpoint reached (0 = start harbor).</summary>
        public int Reached;
        /// <summary>Total checkpoints in the generated world (for display; 0 = unknown).</summary>
        public int Checkpoints;
        public bool Completed;
        public long CreatedTicks;
        public long UpdatedTicks;

        public bool IsGenerated => Seed != 0 && WindDegrees >= 0f;
        public DateTime Updated => new DateTime(UpdatedTicks, DateTimeKind.Utc).ToLocalTime();

        public static VoyageSave CreateNew(string name, int seed = 0)
        {
            long now = DateTime.UtcNow.Ticks;
            return new VoyageSave
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 12),
                Name = string.IsNullOrWhiteSpace(name) ? "Рейс" : name.Trim(),
                Seed = seed,
                CreatedTicks = now,
                UpdatedTicks = now,
            };
        }

        public string ProgressText
        {
            get
            {
                if (Completed)
                    return "Рейс завершён";
                if (Checkpoints <= 1)
                    return Reached <= 0 ? "Стартовая гавань" : $"Чекпоинт {Reached}";
                return $"Чекпоинт {Reached}/{Checkpoints - 1}";
            }
        }
    }

    /// <summary>JSON files in <c>persistentDataPath/Voyages</c>, one per voyage.</summary>
    public static class VoyageSaveStore
    {
        /// <summary>Overrides the save folder (tests).</summary>
        public static string DirectoryOverride;

        public static string Folder => !string.IsNullOrEmpty(DirectoryOverride)
            ? DirectoryOverride
            : Path.Combine(Application.persistentDataPath, "Voyages");

        public static List<VoyageSave> List()
        {
            var list = new List<VoyageSave>();
            string dir = Folder;
            if (!Directory.Exists(dir))
                return list;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                VoyageSave save = ReadFile(file);
                if (save != null)
                    list.Add(save);
            }
            list.Sort((a, b) => b.UpdatedTicks.CompareTo(a.UpdatedTicks));
            return list;
        }

        public static VoyageSave Load(string id)
        {
            string path = PathFor(id);
            return path != null && File.Exists(path) ? ReadFile(path) : null;
        }

        public static bool Write(VoyageSave save)
        {
            string path = save != null ? PathFor(save.Id) : null;
            if (path == null)
                return false;
            try
            {
                Directory.CreateDirectory(Folder);
                save.Version = VoyageSave.CurrentVersion;
                save.UpdatedTicks = DateTime.UtcNow.Ticks;
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(save, true));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Saves] Could not write voyage '{save.Id}': {e.Message}");
                return false;
            }
        }

        public static void Delete(string id)
        {
            string path = PathFor(id);
            if (path != null && File.Exists(path))
                File.Delete(path);
        }

        private static string PathFor(string id)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return null;
            return Path.Combine(Folder, id + ".json");
        }

        private static VoyageSave ReadFile(string path)
        {
            try
            {
                var save = JsonUtility.FromJson<VoyageSave>(File.ReadAllText(path));
                if (save == null || string.IsNullOrEmpty(save.Id) || save.Version > VoyageSave.CurrentVersion)
                    return null;
                return save;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Saves] Skipping unreadable voyage file '{path}': {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Host-side hand-off from the menu to the world scene: the voyage the next world spawn
    /// loads (and keeps autosaving). Null = sandbox/debug launch, nothing is saved.
    /// </summary>
    public static class VoyageLaunch
    {
        public static VoyageSave Active { get; set; }

        /// <summary>Raised on the host after the active voyage was written to disk.</summary>
        public static event Action<VoyageSave> Saved;

        public static void Save()
        {
            if (Active != null && VoyageSaveStore.Write(Active))
                Saved?.Invoke(Active);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
            Saved = null;
        }
    }
}
