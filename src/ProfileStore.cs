using System;
using System.IO;
using System.Web.Script.Serialization;

namespace ClickForge
{
    // Persists the working profile and named presets under
    // %APPDATA%\MouseClicker. All JSON; portable and human-readable.
    internal static class ProfileStore
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static string Folder()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(root, "MouseClicker");

            // One-time migration: earlier builds stored settings under
            // %APPDATA%\ClickForge. If the new folder doesn't exist yet but the
            // legacy one does, move it across so saved profiles aren't lost.
            if (!Directory.Exists(dir))
            {
                string legacy = Path.Combine(root, "ClickForge");
                if (Directory.Exists(legacy))
                {
                    try { Directory.Move(legacy, dir); }
                    catch { }
                }
            }

            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string ConfigPath()
        {
            return Path.Combine(Folder(), "config.json");
        }

        public static string ProfilesFolder()
        {
            string dir = Path.Combine(Folder(), "profiles");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ---- Working config (auto-saved on exit) --------------------------

        public static Profile LoadConfig()
        {
            try
            {
                string path = ConfigPath();
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    Profile p = Json.Deserialize<Profile>(text);
                    if (p != null)
                    {
                        p.Normalize();
                        return p;
                    }
                }
            }
            catch { }
            return new Profile();
        }

        public static void SaveConfig(Profile p)
        {
            try
            {
                File.WriteAllText(ConfigPath(), Json.Serialize(p));
            }
            catch { }
        }

        // ---- Named presets ------------------------------------------------

        public static void SaveNamed(string name, Profile p)
        {
            string path = Path.Combine(ProfilesFolder(), Sanitize(name) + ".json");
            File.WriteAllText(path, Json.Serialize(p));
        }

        public static Profile LoadNamed(string name)
        {
            string path = Path.Combine(ProfilesFolder(), Sanitize(name) + ".json");
            string text = File.ReadAllText(path);
            Profile p = Json.Deserialize<Profile>(text);
            if (p != null) p.Normalize();
            return p;
        }

        public static void DeleteNamed(string name)
        {
            string path = Path.Combine(ProfilesFolder(), Sanitize(name) + ".json");
            if (File.Exists(path))
                File.Delete(path);
        }

        public static string[] ListNames()
        {
            try
            {
                string[] files = Directory.GetFiles(ProfilesFolder(), "*.json");
                string[] names = new string[files.Length];
                for (int i = 0; i < files.Length; i++)
                    names[i] = Path.GetFileNameWithoutExtension(files[i]);
                Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                return names;
            }
            catch
            {
                return new string[0];
            }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "profile";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Trim();
            if (name.Length == 0)
                return "profile";

            // Windows reserves device names (CON, PRN, AUX, NUL, COM1-9,
            // LPT1-9) regardless of extension — writing "con.json" fails.
            string upper = name.ToUpperInvariant();
            string[] reserved =
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };
            foreach (string r in reserved)
                if (upper == r) return "_" + name;
            return name;
        }
    }
}
