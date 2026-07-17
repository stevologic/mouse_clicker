using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ClickForge
{
    internal class UpdateInfo
    {
        public bool Available;
        public string LatestTag; // e.g. "v3.7"
        public string Url;       // release page to open
    }

    // Checks GitHub for a newer release. Link-only (never downloads), and fails
    // silently when offline or rate-limited — it must never disrupt the app.
    internal static class UpdateChecker
    {
        private const string LatestApi = "https://api.github.com/repos/stevologic/mouse_clicker/releases/latest";
        private const string ReleasesPage = "https://github.com/stevologic/mouse_clicker/releases/latest";

        public static async Task<UpdateInfo> CheckAsync(string currentVersion)
        {
            var info = new UpdateInfo();
            info.Url = ReleasesPage;
            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(8);
                    // GitHub's API requires a User-Agent.
                    http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "mouseclicker.app");
                    http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                    string text = await http.GetStringAsync(LatestApi).ConfigureAwait(false);

                    var json = new JavaScriptSerializer();
                    var root = json.DeserializeObject(text) as IDictionary<string, object>;
                    if (root != null)
                    {
                        object tag, url;
                        if (root.TryGetValue("html_url", out url) && url != null)
                            info.Url = url.ToString();
                        if (root.TryGetValue("tag_name", out tag) && tag != null)
                        {
                            info.LatestTag = tag.ToString();
                            info.Available = IsNewer(info.LatestTag, currentVersion);
                        }
                    }
                }
            }
            catch { /* offline / rate-limited / parse error — stay quiet */ }
            return info;
        }

        // True if latestTag (e.g. "v3.7") is a higher version than current
        // (e.g. "3.6"). Compares dotted numeric components; extra text ignored.
        internal static bool IsNewer(string latestTag, string current)
        {
            int[] a = Parse(latestTag);
            int[] b = Parse(current);
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                int ai = i < a.Length ? a[i] : 0;
                int bi = i < b.Length ? b[i] : 0;
                if (ai != bi) return ai > bi;
            }
            return false;
        }

        private static int[] Parse(string v)
        {
            if (string.IsNullOrEmpty(v)) return new int[0];
            MatchCollection ms = Regex.Matches(v, "\\d+");
            int[] r = new int[ms.Count];
            for (int i = 0; i < ms.Count; i++)
            {
                int val;
                int.TryParse(ms[i].Value, out val);
                r[i] = val;
            }
            return r;
        }
    }
}
