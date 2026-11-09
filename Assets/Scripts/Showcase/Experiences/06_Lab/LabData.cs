using UnityEngine;

namespace Showcase.Experiences.Lab
{
    public struct LabIngredient
    {
        public string Name;
        public Color Color;
        public string Info;
    }

    public class LabFormula
    {
        public int Mask;          // bit i set = ingredient i is in the mix (order-independent)
        public string Name;
        public FxKind Fx;
        public Color Color;
        public string Copy;
        public bool Hero;
    }

    // All lab content: the six ingredients (in vial order) and one formula per 3-ingredient combination.
    public static class LabData
    {
        public const int Aloe = 0, Citrus = 1, Mint = 2, Charcoal = 3, Berry = 4, Spark = 5;
        public const int HeroMask = (1 << Aloe) | (1 << Citrus) | (1 << Spark);

        public static readonly LabIngredient[] Ingredients =
        {
            new LabIngredient { Name = "Aloe", Color = Hex("5CCB4A"), Info = "Cool green gel from the Lumo greenhouse.\nCalms, soothes and hydrates." },
            new LabIngredient { Name = "Citrus", Color = Hex("FF8A1F"), Info = "Sun-ripened Nova zest.\nBrightens and wakes everything up." },
            new LabIngredient { Name = "Mint", Color = Hex("4FE3E0"), Info = "Frosty leaf extract.\nInstant chill, fresh finish." },
            new LabIngredient { Name = "Charcoal", Color = Hex("3B3644"), Info = "Deep-clean ember powder.\nAbsorbs everything. Even light." },
            new LabIngredient { Name = "Berry", Color = Hex("D6337F"), Info = "Wild glowberry juice.\nRich, bold, a little dramatic." },
            new LabIngredient { Name = "Spark", Color = Hex("FFE53B"), Info = "Bottled lightning (tiny!).\nAdds energy. Handle with care." },
        };

        public const string Clues =
            "Begin with something calm and green,\n" +
            "add a splash of morning sun,\n" +
            "then wake it with a jolt of light.\n" +
            "<color=#FF9C8F>Keep the dark one out of it!</color>";

        public static readonly LabFormula[] Formulas =
        {
            F(Aloe, Citrus, Mint, "Morning Breeze Tonic", FxKind.Sparkle, "A2E86B", "Fresh and zingy, but it won't glow.\nSomething needs more energy."),
            F(Aloe, Citrus, Charcoal, "Swamp Lemonade", FxKind.Smoke, "6B7A3A", "Murky green fizz.\nSmells like a pond party."),
            F(Aloe, Citrus, Berry, "Fruit Salad Gel", FxKind.Poof, "E8775A", "Tasty! Not exactly a breakthrough.\nThe berry is hogging the spotlight."),
            F(Aloe, Citrus, Spark, "Nova Glow Serum", FxKind.Magic, "FFC21F", "Calm, bright and glowing.\nThe hero formula!", true),
            F(Aloe, Mint, Charcoal, "Bog Fog", FxKind.Smoke, "5E7A72", "A cold grey cloud rolls out.\nThe dark one ruins everything."),
            F(Aloe, Mint, Berry, "Minty Berry Balm", FxKind.Sparkle, "9E8FD0", "Lovely lip balm.\nWrong lab, though."),
            F(Aloe, Mint, Spark, "Frost Bolt", FxKind.Explosion, "B8F5A0", "Too cold for a spark. Crack!\nSwap the chill for some sunshine."),
            F(Aloe, Charcoal, Berry, "Gloom Jam", FxKind.Smoke, "5A3448", "Thick, dark and sticky.\nStraight into the bin."),
            F(Aloe, Charcoal, Spark, "Soot Sprout", FxKind.Explosion, "6E6B3A", "The dark one smothers the spark.\nBoom. Sooty eyebrows."),
            F(Aloe, Berry, Spark, "Berry Blast", FxKind.Explosion, "F06AA0", "Pink sparks everywhere!\nClose... try something sunnier."),
            F(Citrus, Mint, Charcoal, "Grey Lemon Smog", FxKind.Smoke, "8A8A70", "Sour and smoky.\nOpen a window."),
            F(Citrus, Mint, Berry, "Tutti Frutti Fizz", FxKind.Poof, "F59C9C", "A happy little soda.\nNice, but no glow."),
            F(Citrus, Mint, Spark, "Electric Lemonade", FxKind.Sparkle, "D8F25A", "Bright and buzzing!\nMissing something calm and green."),
            F(Citrus, Charcoal, Berry, "Midnight Marmalade", FxKind.Smoke, "7A3A30", "Dark, bitter and very sticky.\nToast not included."),
            F(Citrus, Charcoal, Spark, "Charred Sunbeam", FxKind.Explosion, "C8742A", "Sunshine plus soot plus spark.\nA very small volcano."),
            F(Citrus, Berry, Spark, "Pop Rock Punch", FxKind.Explosion, "FF5A4A", "It crackles, it pops, it fizzes.\nToo wild for skin care."),
            F(Mint, Charcoal, Berry, "Shadow Sorbet", FxKind.Smoke, "4A3A6A", "Icy, inky and mysterious.\nNobody wants a grey smile."),
            F(Mint, Charcoal, Spark, "Thunder Mist", FxKind.Poof, "7A8AA0", "A tiny storm cloud in a beaker.\nIt rumbles politely."),
            F(Mint, Berry, Spark, "Glitter Slushie", FxKind.Sparkle, "C77CF0", "Sparkly, frosty, purple.\nParty drink, not a serum."),
            F(Charcoal, Berry, Spark, "Black Hole Jelly", FxKind.Explosion, "2E1E3A", "It ate the light. And the spoon.\nStep away slowly."),
        };

        public static LabFormula Find(int mask)
        {
            foreach (var f in Formulas)
                if (f.Mask == mask)
                    return f;
            return null;
        }

        static LabFormula F(int a, int b, int c, string name, FxKind fx, string hex, string copy, bool hero = false)
        {
            return new LabFormula { Mask = (1 << a) | (1 << b) | (1 << c), Name = name, Fx = fx, Color = Hex(hex), Copy = copy, Hero = hero };
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }
    }
}
