using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ClickForge
{
    // Result of a pattern generation attempt.
    internal class AiResult
    {
        public bool Success;
        public string Explanation;                        // human summary
        public IDictionary<string, object> Pattern;       // fields to apply
        public string Error;                              // set when Success == false
        public bool UsedOffline;                          // heuristic, not the API
    }

    // Turns a natural-language description into a click/movement pattern.
    // Primary path: the Anthropic Messages API with the user's own key.
    // Fallback: a local keyword heuristic so the feature is useful key-free.
    internal class AiClient
    {
        private const string Endpoint = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";

        private readonly JavaScriptSerializer _json;

        public AiClient()
        {
            _json = new JavaScriptSerializer();
            _json.MaxJsonLength = 8 * 1024 * 1024;
        }

        public async Task<AiResult> GeneratePatternAsync(string prompt, Profile current)
        {
            if (string.IsNullOrEmpty(current.ApiKey))
                return OfflineFallback(prompt, "No API key set — used the built-in offline generator.");

            try
            {
                string body = BuildRequestBody(prompt, current);

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(60);
                    using (var req = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                    {
                        req.Headers.TryAddWithoutValidation("x-api-key", current.ApiKey);
                        req.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
                        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

                        HttpResponseMessage resp = await http.SendAsync(req).ConfigureAwait(false);
                        string respText = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!resp.IsSuccessStatusCode)
                            return Fail(DescribeApiError(respText, (int)resp.StatusCode));

                        return ParseApiResponse(respText);
                    }
                }
            }
            catch (Exception ex)
            {
                // Network trouble shouldn't leave the user stuck: degrade to
                // the offline generator and say so.
                AiResult off = OfflineFallback(prompt,
                    "API request failed (" + ex.Message + ") — used the offline generator instead.");
                return off;
            }
        }

        // ---- Request building --------------------------------------------

        private string BuildRequestBody(string prompt, Profile current)
        {
            var vs = ScreenInfo.Virtual();
            var pr = ScreenInfo.Primary();

            var msg = new Dictionary<string, object>();
            msg["role"] = "user";
            msg["content"] = prompt;

            var req = new Dictionary<string, object>();
            req["model"] = string.IsNullOrEmpty(current.Model) ? "claude-opus-4-8" : current.Model;
            req["max_tokens"] = 2048;
            req["system"] = SystemPrompt(vs, pr);
            req["messages"] = new object[] { msg };

            return _json.Serialize(req);
        }

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
            sb.Append("- clicksPerEvent: integer (clicks per event for MultiClick, or scroll notches)\n");
            sb.Append("- intervalMinMs, intervalMaxMs: integers, gap between click events (equal = fixed rate)\n");
            sb.Append("- holdMinMs, holdMaxMs: integers, press-to-release hold time (small random range looks human)\n");
            sb.Append("- repeatMode: \"Infinite\" | \"Count\" | \"Duration\"\n");
            sb.Append("- repeatCount: integer (for Count)\n");
            sb.Append("- durationSeconds: integer (for Duration)\n");
            sb.Append("- startDelayMs: integer countdown before clicking begins\n");
            sb.Append("- positionMode: \"CurrentCursor\" | \"FixedPoint\" | \"RandomInRegion\" | \"PointSequence\"\n");
            sb.Append("- fixedX, fixedY: integers (for FixedPoint)\n");
            sb.Append("- regionLeft, regionTop, regionRight, regionBottom: integers (for RandomInRegion)\n");
            sb.Append("- points: array of {\"x\":int,\"y\":int} (for PointSequence)\n");
            sb.Append("- sequenceLoop: boolean\n");
            sb.Append("- movementMode: \"Teleport\" | \"Linear\" | \"Humanized\" (Humanized = curved, natural cursor travel)\n");
            sb.Append("- movementDurationMs: integer glide time for Linear/Humanized\n");
            sb.Append("- jitterRadius: integer random pixel offset applied to each target\n");
            sb.Append("- returnToOrigin: boolean, restore cursor after the run\n");
            sb.Append("- explanation: one short sentence describing the pattern in plain English\n\n");
            sb.Append("Choose values that literally satisfy the user's request. ");
            sb.Append("If they ask for human-like behavior, use Humanized movement, a small jitterRadius, ");
            sb.Append("and randomized hold and interval ranges.");
            return sb.ToString();
        }

        // ---- Response parsing --------------------------------------------

        private AiResult ParseApiResponse(string respText)
        {
            object parsed = _json.DeserializeObject(respText);
            var root = parsed as IDictionary<string, object>;
            if (root == null)
                return Fail("Unexpected API response shape.");

            object contentObj;
            if (!root.TryGetValue("content", out contentObj))
                return Fail("API response had no content.");

            string text = ExtractText(contentObj);
            if (string.IsNullOrEmpty(text))
                return Fail("API returned an empty message.");

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

        private static string ExtractText(object contentObj)
        {
            object[] blocks = contentObj as object[];
            if (blocks == null) return null;
            var sb = new StringBuilder();
            foreach (object block in blocks)
            {
                var map = block as IDictionary<string, object>;
                if (map == null) continue;
                object type;
                if (map.TryGetValue("type", out type) && type != null && type.ToString() == "text")
                {
                    object t;
                    if (map.TryGetValue("text", out t) && t != null)
                        sb.Append(t.ToString());
                }
            }
            return sb.ToString();
        }

        // Pull the first balanced {...} object out of arbitrary text (handles
        // stray prose or code fences the model might add).
        private static string ExtractJsonObject(string text)
        {
            int start = text.IndexOf('{');
            if (start < 0) return null;

            int depth = 0;
            bool inString = false;
            bool escaped = false;
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
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                            return text.Substring(start, i - start + 1);
                    }
                }
            }
            return null;
        }

        private string DescribeApiError(string respText, int status)
        {
            try
            {
                var root = _json.DeserializeObject(respText) as IDictionary<string, object>;
                if (root != null)
                {
                    object err;
                    if (root.TryGetValue("error", out err))
                    {
                        var em = err as IDictionary<string, object>;
                        if (em != null)
                        {
                            object m;
                            if (em.TryGetValue("message", out m) && m != null)
                                return "API error " + status + ": " + m;
                        }
                    }
                }
            }
            catch { }
            return "API error " + status + ".";
        }

        private static AiResult Fail(string message)
        {
            var r = new AiResult();
            r.Success = false;
            r.Error = message;
            return r;
        }

        // ---- Offline heuristic -------------------------------------------

        // A small, deterministic parser for common phrasings so the AI panel
        // does something useful even without an API key.
        private AiResult OfflineFallback(string prompt, string note)
        {
            var pat = new Dictionary<string, object>();
            string p = (prompt ?? "").ToLowerInvariant();
            var pr = ScreenInfo.Primary();

            if (Regex.IsMatch(p, @"\bright[ -]?click"))
                pat["button"] = "Right";
            else if (Regex.IsMatch(p, @"\bmiddle[ -]?click"))
                pat["button"] = "Middle";

            if (p.Contains("triple")) pat["clickType"] = "Triple";
            else if (p.Contains("double")) pat["clickType"] = "Double";
            else if (p.Contains("scroll up")) pat["clickType"] = "ScrollUp";
            else if (p.Contains("scroll down")) pat["clickType"] = "ScrollDown";

            // Rate: "N cps" / "N clicks per second".
            Match cps = Regex.Match(p, @"(\d+(?:\.\d+)?)\s*(?:cps|clicks?\s*(?:per|/|a)\s*(?:second|sec|s))");
            if (cps.Success)
            {
                double rate = ParseDouble(cps.Groups[1].Value, 1);
                if (rate > 0)
                {
                    int ms = (int)Math.Round(1000.0 / rate);
                    pat["intervalMinMs"] = ms;
                    pat["intervalMaxMs"] = ms;
                }
            }
            else
            {
                // "every N seconds" / "every N ms".
                Match sec = Regex.Match(p, @"every\s*(\d+(?:\.\d+)?)\s*(seconds?|secs?|s|ms|milliseconds?|minutes?|mins?)");
                if (sec.Success)
                {
                    int ms = ToMs(ParseDouble(sec.Groups[1].Value, 1), sec.Groups[2].Value);
                    pat["intervalMinMs"] = ms;
                    pat["intervalMaxMs"] = ms;
                }
            }

            // Human-like behavior.
            if (p.Contains("human") || p.Contains("random") || p.Contains("natural") || p.Contains("organic"))
            {
                pat["movementMode"] = "Humanized";
                pat["jitterRadius"] = 4;
                pat["holdMinMs"] = 20;
                pat["holdMaxMs"] = 70;
                if (pat.ContainsKey("intervalMinMs"))
                {
                    int mn = PatternMapper.GetInt(pat, "intervalMinMs", 100);
                    pat["intervalMinMs"] = (int)(mn * 0.7);
                    pat["intervalMaxMs"] = (int)(mn * 1.3) + 1;
                }
            }

            // Run limits.
            Match times = Regex.Match(p, @"(\d+)\s*(?:times|clicks)\b");
            if (times.Success)
            {
                pat["repeatMode"] = "Count";
                pat["repeatCount"] = (int)ParseDouble(times.Groups[1].Value, 100);
            }
            Match forDur = Regex.Match(p, @"for\s*(\d+(?:\.\d+)?)\s*(seconds?|secs?|s|minutes?|mins?|m|hours?|h)");
            if (forDur.Success)
            {
                pat["repeatMode"] = "Duration";
                pat["durationSeconds"] = ToSeconds(ParseDouble(forDur.Groups[1].Value, 60), forDur.Groups[2].Value);
            }

            // Location.
            if (p.Contains("center") || p.Contains("centre") || p.Contains("middle of the screen"))
            {
                pat["positionMode"] = "FixedPoint";
                pat["fixedX"] = pr.Left + pr.Width / 2;
                pat["fixedY"] = pr.Top + pr.Height / 2;
            }

            var r = new AiResult();
            r.Success = true;
            r.UsedOffline = true;
            r.Pattern = pat;
            r.Explanation = note;
            return r;
        }

        private static double ParseDouble(string s, double fallback)
        {
            double d;
            if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out d))
                return d;
            return fallback;
        }

        private static int ToMs(double value, string unit)
        {
            unit = unit.ToLowerInvariant();
            if (unit.StartsWith("ms") || unit.StartsWith("milli")) return (int)Math.Round(value);
            if (unit.StartsWith("min")) return (int)Math.Round(value * 60000);
            return (int)Math.Round(value * 1000); // seconds
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
