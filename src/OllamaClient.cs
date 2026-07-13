using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ClickForge
{
    // Talks to a local Ollama runtime (default http://localhost:11434) so the
    // app can list installed models and download new ones from within the UI —
    // the model then runs on-device via Ollama.
    internal class OllamaClient
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        // Names of the models currently installed. Throws if the server is
        // unreachable (so the caller can tell "no models" from "no Ollama").
        public async Task<List<string>> ListModelsAsync(string baseUrl)
        {
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(8);
                HttpResponseMessage resp = await http.GetAsync(baseUrl + "/api/tags").ConfigureAwait(false);
                string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var list = new List<string>();
                if (!resp.IsSuccessStatusCode)
                    return list;

                var root = _json.DeserializeObject(text) as IDictionary<string, object>;
                object[] models = root != null ? Get(root, "models") as object[] : null;
                if (models != null)
                {
                    foreach (object m in models)
                    {
                        var mm = m as IDictionary<string, object>;
                        object name = mm != null ? Get(mm, "name") : null;
                        if (name != null) list.Add(name.ToString());
                    }
                }
                return list;
            }
        }

        // Streams a model download, reporting (statusText, percent) as it goes.
        // percent is -1 for phases without a byte total (manifest, verify, etc.).
        public async Task PullAsync(string baseUrl, string model, Action<string, double> onProgress)
        {
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromMinutes(60);
                var body = new Dictionary<string, object>();
                body["model"] = model;
                body["stream"] = true;

                var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/pull");
                req.Content = new StringContent(_json.Serialize(body), Encoding.UTF8, "application/json");

                HttpResponseMessage resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw new Exception("HTTP " + (int)resp.StatusCode + ": " + Trim(err));
                }

                using (Stream stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        if (line.Length == 0) continue;
                        var obj = _json.DeserializeObject(line) as IDictionary<string, object>;
                        if (obj == null) continue;

                        object err = Get(obj, "error");
                        if (err != null) throw new Exception(err.ToString());

                        object status = Get(obj, "status");
                        string statusText = status != null ? status.ToString() : "";
                        double pct = -1;
                        object comp = Get(obj, "completed"), tot = Get(obj, "total");
                        if (comp != null && tot != null)
                        {
                            double c = ToD(comp), t = ToD(tot);
                            if (t > 0) pct = c / t * 100.0;
                        }
                        if (onProgress != null) onProgress(statusText, pct);
                    }
                }
            }
        }

        private static object Get(IDictionary<string, object> d, string key)
        {
            object v;
            return (d != null && d.TryGetValue(key, out v)) ? v : null;
        }

        private static double ToD(object v)
        {
            if (v is double) return (double)v;
            if (v is int) return (int)v;
            if (v is long) return (long)v;
            if (v is decimal) return (double)(decimal)v;
            double d;
            if (double.TryParse(v.ToString(), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return 0;
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length > 200 ? s.Substring(0, 200) : s;
        }
    }
}
