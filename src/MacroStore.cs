using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ClickForge
{
    // Persists recorded macros as JSON under %APPDATA%\MouseClicker\macros —
    // same portable, human-readable format as profiles.
    internal static class MacroStore
    {
        private static readonly JavaScriptSerializer Json = MakeSerializer();

        private static JavaScriptSerializer MakeSerializer()
        {
            var j = new JavaScriptSerializer();
            j.MaxJsonLength = 64 * 1024 * 1024; // recordings can be large
            return j;
        }

        private static string Folder()
        {
            string dir = Path.Combine(ProfileStore.Folder(), "macros");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void Save(string name, Macro m)
        {
            File.WriteAllText(PathFor(name), Json.Serialize(m));
        }

        public static Macro Load(string name)
        {
            Macro m = Json.Deserialize<Macro>(File.ReadAllText(PathFor(name)));
            if (m == null) return null;
            if (m.Steps == null) m.Steps = new List<RecordedStep>();
            if (m.WindowTitle == null) m.WindowTitle = "";
            return m;
        }

        public static void Delete(string name)
        {
            string path = PathFor(name);
            if (File.Exists(path)) File.Delete(path);
        }

        public static string[] ListNames()
        {
            try
            {
                string[] files = Directory.GetFiles(Folder(), "*.json");
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

        private static string PathFor(string name)
        {
            return Path.Combine(Folder(), ProfileStore.Sanitize(name) + ".json");
        }
    }
}
