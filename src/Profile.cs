using System;
using System.Collections.Generic;

namespace ClickForge
{
    public enum ClickAction
    {
        Single = 0,
        Double = 1,
        Triple = 2,
        MultiClick = 3,
        ScrollUp = 4,
        ScrollDown = 5,
        MouseDown = 6,
        MouseUp = 7
    }

    public enum RepeatMode
    {
        Infinite = 0,
        Count = 1,
        Duration = 2
    }

    public enum PositionMode
    {
        CurrentCursor = 0,
        FixedPoint = 1,
        RandomInRegion = 2,
        PointSequence = 3
    }

    public enum MovementMode
    {
        Teleport = 0,
        Linear = 1,
        Humanized = 2
    }

    // A savable (x,y) target. Plain properties so JavaScriptSerializer can
    // round-trip it with no custom converters.
    public class ClickPoint
    {
        public int X { get; set; }
        public int Y { get; set; }

        public ClickPoint() { }

        public ClickPoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    // The complete configuration of a clicking session. Everything the engine
    // reads and everything the AI generator writes lives here.
    public class Profile
    {
        // What to click.
        public MouseButton Button { get; set; }
        public ClickAction Action { get; set; }
        public int ClicksPerEvent { get; set; }      // used by MultiClick / Scroll
        public int HoldMinMs { get; set; }           // press -> release delay
        public int HoldMaxMs { get; set; }

        // When to click.
        public int IntervalMinMs { get; set; }        // gap between click events
        public int IntervalMaxMs { get; set; }
        public RepeatMode RepeatMode { get; set; }
        public int RepeatCount { get; set; }
        public int DurationSeconds { get; set; }
        public int StartDelayMs { get; set; }

        // Where to click.
        public PositionMode PositionMode { get; set; }
        public int FixedX { get; set; }
        public int FixedY { get; set; }
        public int RegionLeft { get; set; }
        public int RegionTop { get; set; }
        public int RegionRight { get; set; }
        public int RegionBottom { get; set; }
        public List<ClickPoint> Points { get; set; }
        public bool SequenceLoop { get; set; }

        // How to get there.
        public MovementMode MovementMode { get; set; }
        public int MovementDurationMs { get; set; }   // glide time for Linear/Humanized
        public int JitterRadius { get; set; }         // random offset per target
        public bool ReturnToOrigin { get; set; }

        // Hotkeys (virtual-key codes; F6 / F8 by default).
        public int ToggleHotkeyVk { get; set; }
        public int StopHotkeyVk { get; set; }

        // Window behavior: minimize into the system tray (keep running in the
        // background) instead of the taskbar.
        public bool MinimizeToTray { get; set; }

        // AI settings. ApiKey/Model hold the *current* provider's values (what
        // AiClient uses); the dictionaries remember each provider's key/model so
        // switching providers doesn't lose them.
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public string Provider { get; set; }
        public Dictionary<string, string> ProviderKeys { get; set; }
        public Dictionary<string, string> ProviderModels { get; set; }

        public Profile()
        {
            Button = MouseButton.Left;
            Action = ClickAction.Single;
            ClicksPerEvent = 1;
            HoldMinMs = 15;
            HoldMaxMs = 40;

            IntervalMinMs = 100;
            IntervalMaxMs = 100;
            RepeatMode = RepeatMode.Infinite;
            RepeatCount = 100;
            DurationSeconds = 60;
            StartDelayMs = 1000;

            PositionMode = PositionMode.CurrentCursor;
            FixedX = 0;
            FixedY = 0;
            RegionLeft = 0;
            RegionTop = 0;
            RegionRight = 0;
            RegionBottom = 0;
            Points = new List<ClickPoint>();
            SequenceLoop = true;

            MovementMode = MovementMode.Teleport;
            MovementDurationMs = 300;
            JitterRadius = 0;
            ReturnToOrigin = false;

            ToggleHotkeyVk = 0x75; // VK_F6
            StopHotkeyVk = 0x77;   // VK_F8

            MinimizeToTray = true;

            ApiKey = "";
            Model = "claude-opus-4-8";
            Provider = AiProviders.Anthropic;
            ProviderKeys = new Dictionary<string, string>();
            ProviderModels = new Dictionary<string, string>();
        }

        // Guard against nonsense values (e.g. min > max) so the engine never
        // has to defend itself against a malformed profile.
        public string GetKey(string provider)
        {
            string v;
            if (ProviderKeys != null && ProviderKeys.TryGetValue(provider, out v) && v != null)
                return v;
            return "";
        }

        public string GetModel(string provider)
        {
            string v;
            if (ProviderModels != null && ProviderModels.TryGetValue(provider, out v) && !string.IsNullOrEmpty(v))
                return v;
            return AiProviders.DefaultModel(provider);
        }

        public void SetKey(string provider, string key)
        {
            if (ProviderKeys == null) ProviderKeys = new Dictionary<string, string>();
            ProviderKeys[provider] = key ?? "";
        }

        public void SetModel(string provider, string model)
        {
            if (ProviderModels == null) ProviderModels = new Dictionary<string, string>();
            ProviderModels[provider] = model ?? "";
        }

        public void Normalize()
        {
            if (Points == null)
                Points = new List<ClickPoint>();
            if (ProviderKeys == null) ProviderKeys = new Dictionary<string, string>();
            if (ProviderModels == null) ProviderModels = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(Provider)) Provider = AiProviders.Anthropic;

            // Migrate a legacy single key/model into the Anthropic slot.
            if (!ProviderKeys.ContainsKey(AiProviders.Anthropic) && !string.IsNullOrEmpty(ApiKey))
                ProviderKeys[AiProviders.Anthropic] = ApiKey;
            if (!ProviderModels.ContainsKey(AiProviders.Anthropic) && !string.IsNullOrEmpty(Model)
                && Model.StartsWith("claude"))
                ProviderModels[AiProviders.Anthropic] = Model;

            // Sync the current ApiKey/Model from the selected provider.
            ApiKey = GetKey(Provider);
            Model = GetModel(Provider);

            ClicksPerEvent = Clamp(ClicksPerEvent, 1, 10000);
            HoldMinMs = Clamp(HoldMinMs, 0, 60000);
            HoldMaxMs = Clamp(HoldMaxMs, 0, 60000);
            if (HoldMaxMs < HoldMinMs) HoldMaxMs = HoldMinMs;

            IntervalMinMs = Clamp(IntervalMinMs, 0, 3600000);
            IntervalMaxMs = Clamp(IntervalMaxMs, 0, 3600000);
            if (IntervalMaxMs < IntervalMinMs) IntervalMaxMs = IntervalMinMs;

            RepeatCount = Clamp(RepeatCount, 1, 100000000);
            DurationSeconds = Clamp(DurationSeconds, 1, 864000);
            StartDelayMs = Clamp(StartDelayMs, 0, 600000);

            MovementDurationMs = Clamp(MovementDurationMs, 0, 60000);
            JitterRadius = Clamp(JitterRadius, 0, 5000);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
