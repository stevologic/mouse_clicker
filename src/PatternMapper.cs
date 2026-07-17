using System;
using System.Collections.Generic;

namespace ClickForge
{
    // Applies an AI- or preset-generated pattern (a loose string->object map,
    // as produced by JavaScriptSerializer) onto a Profile. Every field is
    // optional: whatever the pattern omits keeps the profile's current value.
    internal static class PatternMapper
    {
        public static void ApplyToProfile(Profile p, IDictionary<string, object> pat)
        {
            if (pat == null) return;

            p.Button = ParseEnum(GetString(pat, "button"), p.Button);
            p.Action = ParseEnum(GetString(pat, "clickType"), p.Action);
            p.Action = ParseEnum(GetString(pat, "action"), p.Action);

            p.ClicksPerEvent = GetInt(pat, "clicksPerEvent", p.ClicksPerEvent);
            p.HoldMinMs = GetInt(pat, "holdMinMs", p.HoldMinMs);
            p.HoldMaxMs = GetInt(pat, "holdMaxMs", p.HoldMaxMs);

            p.IntervalMinMs = GetInt(pat, "intervalMinMs", p.IntervalMinMs);
            p.IntervalMaxMs = GetInt(pat, "intervalMaxMs", p.IntervalMaxMs);
            p.RepeatMode = ParseEnum(GetString(pat, "repeatMode"), p.RepeatMode);
            p.RepeatCount = GetInt(pat, "repeatCount", p.RepeatCount);
            p.DurationSeconds = GetInt(pat, "durationSeconds", p.DurationSeconds);
            p.StartDelayMs = GetInt(pat, "startDelayMs", p.StartDelayMs);

            p.PositionMode = ParseEnum(GetString(pat, "positionMode"), p.PositionMode);
            p.FixedX = GetInt(pat, "fixedX", p.FixedX);
            p.FixedY = GetInt(pat, "fixedY", p.FixedY);
            p.RegionLeft = GetInt(pat, "regionLeft", p.RegionLeft);
            p.RegionTop = GetInt(pat, "regionTop", p.RegionTop);
            p.RegionRight = GetInt(pat, "regionRight", p.RegionRight);
            p.RegionBottom = GetInt(pat, "regionBottom", p.RegionBottom);
            p.SequenceLoop = GetBool(pat, "sequenceLoop", p.SequenceLoop);

            List<ClickPoint> pts = GetPoints(pat, "points");
            if (pts != null)
                p.Points = pts;

            p.MovementMode = ParseEnum(GetString(pat, "movementMode"), p.MovementMode);
            p.MovementDurationMs = GetInt(pat, "movementDurationMs", p.MovementDurationMs);
            p.JitterRadius = GetInt(pat, "jitterRadius", p.JitterRadius);
            p.ReturnToOrigin = GetBool(pat, "returnToOrigin", p.ReturnToOrigin);

            p.Normalize();
        }

        // ---- Typed getters over the loose map ----------------------------

        public static string GetString(IDictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
                return v.ToString();
            return null;
        }

        public static int GetInt(IDictionary<string, object> d, string key, int fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
                return fallback;
            try
            {
                if (v is int) return (int)v;
                if (v is long) return (int)(long)v;
                if (v is double) return (int)Math.Round((double)v);
                if (v is decimal) return (int)(decimal)v;
                double parsed;
                if (double.TryParse(v.ToString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out parsed))
                    return (int)Math.Round(parsed);
            }
            catch { }
            return fallback;
        }

        public static bool GetBool(IDictionary<string, object> d, string key, bool fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
                return fallback;
            if (v is bool) return (bool)v;
            string s = v.ToString().Trim().ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes") return true;
            if (s == "false" || s == "0" || s == "no") return false;
            return fallback;
        }

        private static List<ClickPoint> GetPoints(IDictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
                return null;

            object[] arr = v as object[];
            if (arr == null)
            {
                var list = v as System.Collections.IList;
                if (list == null) return null;
                var res = new List<ClickPoint>();
                foreach (object item in list)
                    AddPoint(res, item);
                return res;
            }

            var result = new List<ClickPoint>();
            foreach (object item in arr)
                AddPoint(result, item);
            return result;
        }

        private static void AddPoint(List<ClickPoint> list, object item)
        {
            var map = item as IDictionary<string, object>;
            if (map == null) return;
            int x = GetInt(map, "x", int.MinValue);
            int y = GetInt(map, "y", int.MinValue);
            if (x == int.MinValue || y == int.MinValue) return;
            list.Add(new ClickPoint(x, y));
        }

        private static TEnum ParseEnum<TEnum>(string s, TEnum fallback) where TEnum : struct
        {
            if (string.IsNullOrEmpty(s))
                return fallback;
            try
            {
                // Enum.Parse also accepts raw numbers ("7"), which can produce a
                // value outside the enum's defined range — reject those so a bad
                // model reply can't poison the profile.
                TEnum parsed = (TEnum)Enum.Parse(typeof(TEnum), s.Trim(), true);
                if (!Enum.IsDefined(typeof(TEnum), parsed))
                    return fallback;
                return parsed;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
