using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Showcase.Experiences.BarCart
{
    public enum BarCartKind { Cart, Bottle, Glass, Garnish }

    // Menu of the fictional, alcohol-free "Lumo Sodas" range. Indices match the props the builder places.
    public static class BarCartData
    {
        public const string Brand = "Lumo Sodas";

        public struct Flavor
        {
            public string Name, Note;
            public Color Color;
            public Flavor(string name, string note, Color color) { Name = name; Note = note; Color = color; }
        }

        public static readonly string[] Carts = { "Walnut Classic", "Aqua Pop", "Cherry Red" };

        public static readonly Flavor[] Flavors =
        {
            new Flavor("Berry Burst", "blackcurrant + raspberry", new Color(0.82f, 0.18f, 0.45f)),
            new Flavor("Yuzu Zing", "yuzu + lemon peel", new Color(1f, 0.84f, 0.2f)),
            new Flavor("Mint Cooler", "garden mint + lime", new Color(0.36f, 0.85f, 0.56f)),
            new Flavor("Mango Glow", "alphonso mango", new Color(1f, 0.55f, 0.16f)),
            new Flavor("Blue Lagoon", "blue spirulina + citrus", new Color(0.22f, 0.56f, 1f)),
            new Flavor("Ginger Spark", "fresh ginger + honey", new Color(0.92f, 0.72f, 0.42f)),
        };

        // Glass name, and the word it gives the drink's name.
        public static readonly (string name, string word)[] Glasses = { ("Highball", "Fizz"), ("Coupe", "Spritz"), ("Tumbler", "Cooler") };

        public static readonly string[] Garnishes = { "Lemon wheel", "Cherry", "Mint sprig" };

        public static Color Blend(IEnumerable<int> flavors)
        {
            var list = flavors.ToList();
            if (list.Count == 0)
                return new Color(0.85f, 0.92f, 1f);
            Color sum = Color.black;
            foreach (int i in list)
                sum += Flavors[i].Color;
            Color avg = sum / list.Count;
            // Averages turn muddy; push saturation and brightness back up so the drink stays vivid.
            Color.RGBToHSV(avg, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 1.25f + 0.05f), Mathf.Clamp01(v * 1.1f + 0.05f));
        }

        public static string HueWord(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out _);
            if (s < 0.22f) return "Cloud";
            float deg = h * 360f;
            if (deg < 15f || deg >= 335f) return "Ruby";
            if (deg < 40f) return "Sunset";
            if (deg < 65f) return "Golden";
            if (deg < 150f) return "Garden";
            if (deg < 200f) return "Lagoon";
            if (deg < 255f) return "Midnight";
            if (deg < 300f) return "Velvet";
            return "Blush";
        }

        public static string DrinkName(IEnumerable<int> flavors, int glass)
        {
            string word = glass >= 0 && glass < Glasses.Length ? Glasses[glass].word : "Fizz";
            return HueWord(Blend(flavors)) + " " + word;
        }
    }
}
