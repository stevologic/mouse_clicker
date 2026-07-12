using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ClickForge
{
    internal class AiResult
    {
        public bool Success;
        public string Explanation;
        public IDictionary<string, object> Pattern;
        public string Error;
        public bool UsedOffline;
    }

    // Turns a natural-language description into a click/movement pattern using
    // the user's chosen provider (Claude / OpenAI / Gemini) with their own key.
    // Falls back to a local keyword heuristic when no key is set.
    internal class AiClient
    {
        private const string AnthropicVersion = "2023-06-01";

        private readonly JavaScriptSerializer _json;

        public AiClient()
        {
            _json = new JavaScriptSerializer();
            _json.MaxJsonLength = 8 * 1024 * 1024;
        }

        public async Task<AiResult> GeneratePatternAsync(string prompt, Profile current)
        {
            string provider = string.IsNullOrEmpty(current.Provider) ? AiProviders.Anthropic : current.Provider;
            string key = current.ApiKey;
            string model = string.IsNullOrEmpty(current.Model) ? AiProviders.DefaultModel(provider) : current.Model;

            if (string.IsNullOrEmpty(key))
                return OfflineFallback(prompt, "No API key set — used the built-in offline generator.");

            try
            {
                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(60);
                    using (HttpRequestMessage req = BuildRequest(provider, model, key, prompt, current))
                    {
                        HttpResponseMessage resp = await http.SendAsync(req).ConfigureAwait(false);
                        string respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!resp.IsSuccessStatusCode)
                            return Fail(DescribeApiError(provider, respText, (int)resp.StatusCode));

                        string text = ExtractProviderText(provider, respText);
                        if (string.IsNullOrEmpty(text))
                            return Fail("The model returned an empty response.");

                        string jsonText = ExtractJsonObject(text);
                        if (jsonText == null)
                            return Fail("Could not find a JSON object in the model's reply.");

                        var pattern = _json.DeserializeObject(jsonText) as IDictionary<string, object>;
                        if (pattern == null)
                            return Fail("The model's JSON could not be parsed.");

                        string explanation = PatternMapper.GetString(pattern, "explanation");
                        var result = new AiResult();
                        result.Success = true;
                        result.Pattern = pattern;
                        result.Explanation = string.IsNullOrEmpty(explanation) ? "Pattern generated." : explanation;
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return OfflineFallback(prompt,
                    "API request failed (" + ex.Message + ") — used the offline generator instead.");
            }
        }

        // ---- Per-provider request building -------------------------------

        private HttpRequestMessage BuildRequest(string provider, string model, string key, string prompt, Profile current)
        {
            var vs = ScreenInfo.Virtual();
            var pr = ScreenInfo.Primary();
            string system = SystemPrompt(vs, pr);

            if (provider == AiProviders.OpenAI)
            {
                var body = new Dictionary<string, object>();
                body["model"] = model;
                body["messages"] = new object[]
                {
                    Msg("system", system),
                    Msg("user", prompt)
                };
                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                req.Content = JsonContent(body);
                return req;
            }

            if (provider == AiProviders.Google)
            {
                var sys = new Dictionary<string, object>();
                sys["parts"] = new object[] { Part(system) };
                var userContent = new Dictionary<string, object>();
                userContent["role"] = "user";
                userContent["parts"] = new object[] { Part(prompt) };
                var genCfg = new Dictionary<string, object>();
                genCfg["maxOutputTokens"] = 2048;
                genCfg["responseMimeType"] = "application/json";

                var body = new Dictionary<string, object>();
                body["systemInstruction"] = sys;
                body["contents"] = new object[] { userContent };
                body["generationConfig"] = genCfg;

                string url = "https://generativelanguage.googleapis.com/v1beta/models/"
                           + Uri.EscapeDataString(model) + ":generateContent?key=" + Uri.EscapeDataString(key);
                var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Content = JsonContent(body);
                return req;
            }

            // Default: Anthropic.
            {
                var msg = new Dictionary<string, object>();
                msg["role"] = "user";
                msg["content"] = prompt;
                var body = new Dictionary<string, object>();
                body["model"] = model;
                body["max_tokens"] = 2048;
                body["system"] = system;
                body["messages"] = new object[] { msg };

                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                req.Headers.TryAddWithoutValidation("x-api-key", key);
                req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
                req.Content = JsonContent(body);
                return req;
            }
        }

        private static Dictionary<string, object> Msg(string role, string content)
        {
            var d = new Dictionary<string, object>();
            d["role"] = role; d["content"] = content;
            return d;
        }

        private static Dictionary<string, object> Part(string text)
        {
            var d = new Dictionary<string, object>();
            d["text"] = text;
            return d;
        }

        private StringContent JsonContent(object body)
        {
            return new StringContent(_json.Serialize(body), Encoding.UTF8, "application/json");
        }

        // ---- Per-provider response text extraction -----------------------

        private string ExtractProviderText(string provider, string respText)
        {
            var root = _json.DeserializeObject(respText) as IDictionary<string, object>;
            if (root == null) return null;

            if (provider == AiProviders.OpenAI)
            {
                object[] choices = Get(root, "choices") as object[];
                if (choices != null && choices.Length > 0)
                {
                    var c0 = choices[0] as IDictionary<string, object>;
                    var message = c0 != null ? Get(c0, "message") as IDictionary<string, object> : null;
                    if (message != null)
                    {
                        object content = Get(message, "content");
                        if (content != null) return content.ToString();
                    }
                }
                return null;
            }

            if (provider == AiProviders.Google)
            {
                object[] candidates = Get(root, "candidates") as object[];
                if (candidates != null && candidates.Length > 0)
                {
                    var c0 = candidates[0] as IDictionary<string, object>;
                    var content = c0 != null ? Get(c0, "content") as IDictionary<string, object> : null;
                    object[] parts = content != null ? Get(content, "parts") as object[] : null;
                    if (parts != null)
                    {
                        var sb = new StringBuilder();
                        foreach (object p in parts)
                        {
                            var pm = p as IDictionary<string, object>;
                            if (pm != null)
                            {
                                object t = Get(pm, "text");
                                if (t != null) sb.Append(t.ToString());
                            }
                        }
                        return sb.ToString();
                    }
                }
                return null;
            }

            // Anthropic: content is a list of blocks; concat the text ones.
            object[] blocks = Get(root, "content") as object[];
            if (blocks == null) return null;
            var b = new StringBuilder();
            foreach (object block in blocks)
            {
                var map = block as IDictionary<string, object>;
                if (map == null) continue;
                object type = Get(map, "type");
                if (type != null && type.ToString() == "text")
                {
                    object t = Get(map, "text");
                    if (t != null) b.Append(t.ToString());
                }
            }
            return b.ToString();
        }

        private static object Get(IDictionary<string, object> d, string key)
        {
            object v;
            return (d != null && d.TryGetValue(key, out v)) ? v : null;
        }

        // Pull the first balanced {...} object out of arbitrary text.
        private static string ExtractJsonObject(string text)
        {
            int start = text.IndexOf('{');
            if (start < 0) return null;
            int depth = 0; bool inString = false, escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                }
                else
                {
                    if (c == '"') inString = true;
                    else if (c == '{') depth++;
                    else if (c == '}') { depth--; if (depth == 0) return text.Substring(start, i - start + 1); }
                }
            }
            return null;
        }

        private string DescribeApiError(string provider, string respText, int status)
        {
            try
            {
                var root = _json.DeserializeObject(respText) as IDictionary<string, object>;
                if (root != null)
                {
                    var err = Get(root, "error");
                    var em = err as IDictionary<string, object>;
                    if (em != null)
                    {
                        object m = Get(em, "message");
                        if (m != null) return AiProviders.Display(provider) + " error " + status + ": " + m;
                    }
                    // Some errors are a bare string.
                    if (err != null) return AiProviders.Display(provider) + " error " + status + ": " + err;
                }
            }
            catch { }
            return AiProviders.Display(provider) + " error " + status + ".";
        }

        private static AiResult Fail(string message)
        {
            var r = new AiResult();
            r.Success = false;
            r.Error = message;
            return r;
        }

        // ---- System prompt -----------------------------------------------

        private static string SystemPrompt(ScreenRect vs, ScreenRect pr)
        {
            var sb = new StringBuilder();
            sb.Append("You configure a Windows auto-clicker by returning a single JSON object. ");
            sb.Append("Return ONLY the JSON object, no prose, no markdown fences.\n\n");
            sb.Append("Display geometry (physical pixels):\n");
            sb.Append("- Primary screen: width ").Append(pr.Width).Append(", height ").Append(pr.Height).Append(".\n");
            sb.Append("- Primary center: x=").Append(pr.Left + pr.Width / 2).Append(", y=").Append(pr.Top + pr.Height / 2).Append(".\n");
            sb.Append("- Virtual desktop bounds: left=").Append(vs.Left).Append(", top=").Append(vs.Top)
              .Append(", width=").Append(vs.Width).Append(", height=").Append(vs.Height).Append(".\n\n");
            sb.Append("JSON fields (all optional; omit any you don't need):\n");
            sb.Append("- button: \"Left\" | \"Right\" | \"Middle\"\n");
            sb.Append("- clickType: \"Single\" | \"Double\" | \"Triple\" | \"MultiClick\" | \"ScrollUp\" | \"ScrollDown\"\n");
            sb.Append("- clicksPerEvent: integer\n");
            sb.Append("- intervalMinMs, intervalMaxMs: integers, gap between click events (equal = fixed rate)\n");
            sb.Append("- holdMinMs, holdMaxMs: integers, press-to-release hold time\n");
            sb.Append("- repeatMode: \"Infinite\" | \"Count\" | \"Duration\"\n");
            sb.Append("- repeatCount: integer; durationSeconds: integer; startDelayMs: integer\n");
            sb.Append("- positionMode: \"CurrentCursor\" | \"FixedPoint\" | \"RandomInRegion\" | \"PointSequence\"\n");
            sb.Append("- fixedX, fixedY: integers\n");
            sb.Append("- regionLeft, regionTop, regionRight, regionBottom: integers\n");
            sb.Append("- points: array of {\"x\":int,\"y\":int}; sequenceLoop: boolean\n");
            sb.Append("- movementMode: \"Teleport\" | \"Linear\" | \"Humanized\"\n");
            sb.Append("- movementDurationMs: integer; jitterRadius: integer; returnToOrigin: boolean\n");
            sb.Append("- explanation: one short sentence describing the pattern in plain English\n\n");
            sb.Append("Choose values that literally satisfy the user's request. For human-like behavior use ");
            sb.Append("Humanized movement, a small jitterRadius, and randomized hold and interval ranges.");
            return sb.ToString();
        }

        // ---- Offline heuristic -------------------------------------------

        private AiResult OfflineFallback(string prompt, string note)
        {
            var pat = new Dictionary<string, object>();
            string p = (prompt ?? "").ToLowerInvariant();
            var pr = ScreenInfo.Primary();

            if (Regex.IsMatch(p, @"\bright[ -]?click")) pat["button"] = "Right";
            else if (Regex.IsMatch(p, @"\bmiddle[ -]?click")) pat["button"] = "Middle";

            if (p.Contains("triple")) pat["clickType"] = "Triple";
            else if (p.Contains("double")) pat["clickType"] = "Double";
            else if (p.Contains("scroll up")) pat["clickType"] = "ScrollUp";
            else if (p.Contains("scroll down")) pat["clickType"] = "ScrollDown";

            Match cps = Regex.Match(p, @"(\d+(?:\.\d+)?)\s*(?:cps|clicks?\s*(?:per|/|a)\s*(?:second|sec|s))");
            if (cps.Success)
            {
                double rate = ParseDouble(cps.Groups[1].Value, 1);
                if (rate > 0) { int ms = (int)Math.Round(1000.0 / rate); pat["intervalMinMs"] = ms; pat["intervalMaxMs"] = ms; }
            }
            else
            {
                Match sec = Regex.Match(p, @"every\s*(\d+(?:\.\d+)?)\s*(seconds?|secs?|s|ms|milliseconds?|minutes?|mins?)");
                if (sec.Success) { int ms = ToMs(ParseDouble(sec.Groups[1].Value, 1), sec.Groups[2].Value); pat["intervalMinMs"] = ms; pat["intervalMaxMs"] = ms; }
            }

            if (p.Contains("human") || p.Contains("random") || p.Contains("natural") || p.Contains("organic"))
            {
                pat["movementMode"] = "Humanized"; pat["jitterRadius"] = 4; pat["holdMinMs"] = 20; pat["holdMaxMs"] = 70;
                if (pat.ContainsKey("intervalMinMs"))
                {
                    int mn = PatternMapper.GetInt(pat, "intervalMinMs", 100);
                    pat["intervalMinMs"] = (int)(mn * 0.7); pat["intervalMaxMs"] = (int)(mn * 1.3) + 1;
                }
            }

            Match times = Regex.Match(p, @"(\d+)\s*(?:times|clicks)\b");
            if (times.Success) { pat["repeatMode"] = "Count"; pat["repeatCount"] = (int)ParseDouble(times.Groups[1].Value, 100); }
            Match forDur = Regex.Match(p, @"for\s*(\d+(?:\.\d+)?)\s*(seconds?|secs?|s|minutes?|mins?|m|hours?|h)");
            if (forDur.Success) { pat["repeatMode"] = "Duration"; pat["durationSeconds"] = ToSeconds(ParseDouble(forDur.Groups[1].Value, 60), forDur.Groups[2].Value); }

            if (p.Contains("center") || p.Contains("centre") || p.Contains("middle of the screen"))
            {
                pat["positionMode"] = "FixedPoint"; pat["fixedX"] = pr.Left + pr.Width / 2; pat["fixedY"] = pr.Top + pr.Height / 2;
            }

            var r = new AiResult();
            r.Success = true; r.UsedOffline = true; r.Pattern = pat; r.Explanation = note;
            return r;
        }

        private static double ParseDouble(string s, double fallback)
        {
            double d;
            if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }

        private static int ToMs(double value, string unit)
        {
            unit = unit.ToLowerInvariant();
            if (unit.StartsWith("ms") || unit.StartsWith("milli")) return (int)Math.Round(value);
            if (unit.StartsWith("min")) return (int)Math.Round(value * 60000);
            return (int)Math.Round(value * 1000);
        }

        private static int ToSeconds(double value, string unit)
        {
            unit = unit.ToLowerInvariant();
            if (unit.StartsWith("min") || unit == "m") return (int)Math.Round(value * 60);
            if (unit.StartsWith("hour") || unit == "h") return (int)Math.Round(value * 3600);
            return (int)Math.Round(value);
        }
    }
}
