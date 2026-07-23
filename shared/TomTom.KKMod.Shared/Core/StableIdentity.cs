using System;

namespace TomTom.KKMod.Shared
{
    public static class StableIdentity
    {
        public static uint ComputeFnv1a(string value)
        {
            uint hash = 2166136261;
            if (value == null)
                return hash;

            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return hash;
        }

        public static string Sanitize(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value))
                return "item";

            var characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                if (!char.IsLetterOrDigit(characters[i]) && characters[i] != '_')
                    characters[i] = '_';
            }

            string result = new string(characters);
            return maximumLength > 0 && result.Length > maximumLength
                ? result.Substring(0, maximumLength)
                : result;
        }
    }
}
