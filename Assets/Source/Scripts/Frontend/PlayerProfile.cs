using System;
using System.Text;
using UnityEngine;

namespace RumOverboard.Frontend
{
    /// <summary>Local player identity (PlayerPrefs). Steam will replace the name source later.</summary>
    public static class PlayerProfile
    {
        public const int MaxNameLength = 24;
        private const string NameKey = "ro.profile.name";
        private static string _sessionName;

        /// <summary>Name for this process only (tests, later the Steam persona); not written to PlayerPrefs.</summary>
        public static void SetSessionName(string name) => _sessionName = Sanitize(name);

        public static string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_sessionName))
                    return _sessionName;
                string name = Sanitize(PlayerPrefs.GetString(NameKey, string.Empty));
                if (string.IsNullOrEmpty(name))
                {
                    name = "Матрос " + UnityEngine.Random.Range(100, 1000);
                    PlayerPrefs.SetString(NameKey, name);
                }
                return name;
            }
            set
            {
                string name = Sanitize(value);
                if (string.IsNullOrEmpty(name))
                    return;
                _sessionName = null;
                PlayerPrefs.SetString(NameKey, name);
                PlayerPrefs.Save();
            }
        }

        public static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
                if (!char.IsControl(c))
                    sb.Append(c);
            string s = sb.ToString();
            return s.Length > MaxNameLength ? s.Substring(0, MaxNameLength) : s;
        }

        /// <summary>Connection token payload (UTF-8 name) the host reads via GetPlayerConnectionToken.</summary>
        public static byte[] Token() => Encoding.UTF8.GetBytes(Name);

        public static string FromToken(byte[] token)
        {
            if (token == null || token.Length == 0)
                return string.Empty;
            try { return Sanitize(Encoding.UTF8.GetString(token)); }
            catch (ArgumentException) { return string.Empty; }
        }
    }

    /// <summary>Short human-friendly lobby codes (no 0/O/1/I ambiguity).</summary>
    public static class LobbyCode
    {
        public const int Length = 6;
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        public static string Create(System.Random rng = null)
        {
            rng ??= new System.Random(Guid.NewGuid().GetHashCode());
            var chars = new char[Length];
            for (int i = 0; i < Length; i++)
                chars[i] = Alphabet[rng.Next(Alphabet.Length)];
            return new string(chars);
        }

        /// <summary>Upper-cases and strips separators; returns null if the result isn't a valid code.</summary>
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;
            var sb = new StringBuilder(Length);
            foreach (char c in input.Trim().ToUpperInvariant())
            {
                if (c == ' ' || c == '-' || c == '_')
                    continue;
                if (Alphabet.IndexOf(c) < 0)
                    return null;
                sb.Append(c);
            }
            return sb.Length == Length ? sb.ToString() : null;
        }
    }
}
