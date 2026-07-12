namespace ClickForge
{
    // The AI providers ClickForge can generate patterns with. Model lists are
    // suggestions — the model box is editable, so any current model id works.
    internal static class AiProviders
    {
        public const string Anthropic = "Anthropic";
        public const string OpenAI = "OpenAI";
        public const string Google = "Google";

        public static readonly string[] All = { Anthropic, OpenAI, Google };

        public static string Display(string provider)
        {
            switch (provider)
            {
                case Anthropic: return "Claude  (Anthropic)";
                case OpenAI: return "GPT  (OpenAI)";
                case Google: return "Gemini  (Google)";
            }
            return provider;
        }

        public static string FromDisplayIndex(int i)
        {
            if (i >= 0 && i < All.Length) return All[i];
            return Anthropic;
        }

        public static int DisplayIndex(string provider)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i] == provider) return i;
            return 0;
        }

        public static string[] Models(string provider)
        {
            switch (provider)
            {
                case Anthropic:
                    return new string[]
                    {
                        "claude-opus-4-8", "claude-sonnet-5", "claude-haiku-4-5", "claude-opus-4-7"
                    };
                case OpenAI:
                    return new string[]
                    {
                        "gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini", "o4-mini"
                    };
                case Google:
                    return new string[]
                    {
                        "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash", "gemini-1.5-pro"
                    };
            }
            return new string[] { "" };
        }

        public static string DefaultModel(string provider)
        {
            string[] m = Models(provider);
            return m.Length > 0 ? m[0] : "";
        }

        // Where to get an API key, shown as a hint in the UI.
        public static string KeyHint(string provider)
        {
            switch (provider)
            {
                case Anthropic: return "console.anthropic.com  ·  sent only to api.anthropic.com";
                case OpenAI: return "platform.openai.com  ·  sent only to api.openai.com";
                case Google: return "aistudio.google.com  ·  sent only to generativelanguage.googleapis.com";
            }
            return "";
        }
    }
}
