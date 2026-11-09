using System.Linq;

namespace Showcase.Experiences.Diner
{
    // What the bins hold. Order matters: the builder places one bin per value in this order.
    public enum Ingredient { Bun, Patty, Cheese, Lettuce, Tomato, Onion, Dough, Sauce, Pepperoni, Mushroom }

    // The visual that lands on the plate; one ingredient can look different per dish
    // (bottom/top bun, sliced vs grated cheese). Index of the builder's piece prefab array.
    public enum Piece { BunBottom, BunTop, Patty, CheeseSlice, Lettuce, Tomato, Onion, Dough, Sauce, CheeseGrated, Pepperoni, Mushroom }

    public class Recipe
    {
        public string Name;
        public bool Pizza;
        public string Price;
        public Ingredient[] Steps;

        public int Kcal => Steps.Sum(s => DinerMenu.Info(s).kcal);
        public int Points => 10 * Steps.Length + 20;
    }

    // The fictional "Nova Diner" menu: recipes, ingredient facts and which piece model to stack.
    public static class DinerMenu
    {
        public const string Brand = "Nova Diner";

        public static readonly Recipe[] Recipes =
        {
            new Recipe { Name = "Classic Burger", Price = "€ 6.50", Steps = new[] { Ingredient.Bun, Ingredient.Patty, Ingredient.Bun } },
            new Recipe { Name = "Cheese Burger", Price = "€ 7.20", Steps = new[] { Ingredient.Bun, Ingredient.Patty, Ingredient.Cheese, Ingredient.Lettuce, Ingredient.Bun } },
            new Recipe { Name = "Nova Deluxe", Price = "€ 8.90", Steps = new[] { Ingredient.Bun, Ingredient.Patty, Ingredient.Cheese, Ingredient.Lettuce, Ingredient.Tomato, Ingredient.Bun } },
            new Recipe { Name = "Garden Crunch", Price = "€ 6.20", Steps = new[] { Ingredient.Bun, Ingredient.Lettuce, Ingredient.Tomato, Ingredient.Onion, Ingredient.Bun } },
            new Recipe { Name = "Onion Smash", Price = "€ 7.60", Steps = new[] { Ingredient.Bun, Ingredient.Patty, Ingredient.Onion, Ingredient.Cheese, Ingredient.Bun } },
            new Recipe { Name = "Margherita", Pizza = true, Price = "€ 8.00", Steps = new[] { Ingredient.Dough, Ingredient.Sauce, Ingredient.Cheese } },
            new Recipe { Name = "Pepperoni Pizza", Pizza = true, Price = "€ 9.50", Steps = new[] { Ingredient.Dough, Ingredient.Sauce, Ingredient.Cheese, Ingredient.Pepperoni } },
            new Recipe { Name = "Funghi Pizza", Pizza = true, Price = "€ 9.00", Steps = new[] { Ingredient.Dough, Ingredient.Sauce, Ingredient.Cheese, Ingredient.Mushroom } },
            new Recipe { Name = "Nova Supreme", Pizza = true, Price = "€ 11.40", Steps = new[] { Ingredient.Dough, Ingredient.Sauce, Ingredient.Cheese, Ingredient.Pepperoni, Ingredient.Mushroom, Ingredient.Onion } },
        };

        // The ingredient tray shows one row per dish type; rarely used ones go last (they may need a scroll).
        public static readonly Ingredient[] BurgerTray = { Ingredient.Bun, Ingredient.Patty, Ingredient.Cheese, Ingredient.Lettuce, Ingredient.Tomato, Ingredient.Onion };
        public static readonly Ingredient[] PizzaTray = { Ingredient.Dough, Ingredient.Sauce, Ingredient.Cheese, Ingredient.Pepperoni, Ingredient.Mushroom, Ingredient.Onion };

        public static (string label, string title, string body, int kcal) Info(Ingredient i)
        {
            switch (i)
            {
                case Ingredient.Bun: return ("Bun", "Nova Brioche Bun", "Baked fresh every morning\n140 kcal · contains gluten, egg", 140);
                case Ingredient.Patty: return ("Patty", "Nova Smash Patty", "Grass-fed beef, griddled\n250 kcal · gluten-free", 250);
                case Ingredient.Cheese: return ("Cheese", "Golden Cheddar", "Aged 9 months, melts in 30 s\n110 kcal · contains milk", 110);
                case Ingredient.Lettuce: return ("Lettuce", "Crisp Iceberg", "Grown 20 km from the diner\n5 kcal · vegan", 5);
                case Ingredient.Tomato: return ("Tomato", "Vine Tomato", "Sliced to order\n10 kcal · vegan", 10);
                case Ingredient.Onion: return ("Onion", "Red Onion Rings", "Sweet and sharp, shaved thin\n15 kcal · vegan", 15);
                case Ingredient.Dough: return ("Dough", "Stone-Base Dough", "48-hour slow rise\n380 kcal · contains gluten", 380);
                case Ingredient.Sauce: return ("Sauce", "Nova Red Sauce", "Sun-ripe tomatoes and basil\n40 kcal · vegan", 40);
                case Ingredient.Pepperoni: return ("Pepperoni", "Spicy Pepperoni", "Cups and chars in the oven\n140 kcal · contains pork", 140);
                default: return ("Mushroom", "Forest Mushrooms", "Roasted chestnut mushrooms\n20 kcal · vegan", 20);
            }
        }

        // stepIndex is where in the recipe this ingredient lands (a bun on top of something is the lid).
        public static Piece PieceFor(Ingredient i, Recipe recipe, int stepIndex)
        {
            switch (i)
            {
                case Ingredient.Bun: return stepIndex == 0 ? Piece.BunBottom : Piece.BunTop;
                case Ingredient.Patty: return Piece.Patty;
                case Ingredient.Cheese: return recipe != null && recipe.Pizza ? Piece.CheeseGrated : Piece.CheeseSlice;
                case Ingredient.Lettuce: return Piece.Lettuce;
                case Ingredient.Tomato: return Piece.Tomato;
                case Ingredient.Onion: return Piece.Onion;
                case Ingredient.Dough: return Piece.Dough;
                case Ingredient.Sauce: return Piece.Sauce;
                case Ingredient.Pepperoni: return Piece.Pepperoni;
                default: return Piece.Mushroom;
            }
        }

        // Footprint on the plate (stage units) and how much a piece sinks into the one below,
        // as a fraction of its own thickness (pizza toppings lie on the base, burger layers stack).
        public static (float width, float sink) PieceFit(Piece p)
        {
            switch (p)
            {
                case Piece.BunBottom: case Piece.BunTop: return (0.2f, 0.1f);
                case Piece.Patty: return (0.2f, 0.15f);
                case Piece.CheeseSlice: return (0.21f, 0.3f);
                case Piece.Lettuce: return (0.22f, 0.3f);
                case Piece.Tomato: return (0.18f, 0.3f);
                case Piece.Onion: return (0.18f, 0.4f);
                case Piece.Dough: return (0.3f, 0f);
                case Piece.Sauce: return (0.27f, 0.6f);
                case Piece.CheeseGrated: return (0.25f, 0.6f);
                default: return (0.22f, 0.7f);
            }
        }
    }
}

namespace Showcase.Experiences.Diner
{
    public static class DinerUtil
    {
        public static UnityEngine.Bounds Bounds(UnityEngine.Transform t)
        {
            var rs = t.GetComponentsInChildren<UnityEngine.Renderer>();
            if (rs.Length == 0) return new UnityEngine.Bounds(t.position, UnityEngine.Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // Bounds of all meshes under root, expressed in `space`'s local coordinates (robust to a tilted AR world).
        public static UnityEngine.Bounds LocalBounds(UnityEngine.Transform root, UnityEngine.Transform space)
        {
            var result = new UnityEngine.Bounds();
            bool any = false;
            var toLocal = space.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<UnityEngine.MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = toLocal * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + UnityEngine.Vector3.Scale(mb.extents, new UnityEngine.Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { result = new UnityEngine.Bounds(p, UnityEngine.Vector3.zero); any = true; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        // World point at the top centre of everything rendered under t.
        public static UnityEngine.Vector3 TopOf(UnityEngine.Transform t)
        {
            var b = Bounds(t);
            return new UnityEngine.Vector3(b.center.x, b.max.y, b.center.z);
        }
    }
}
