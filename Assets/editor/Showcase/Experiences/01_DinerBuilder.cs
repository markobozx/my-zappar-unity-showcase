using System.Linq;
using Showcase.Experiences.Diner;
using UnityEngine;

namespace Showcase.EditorTools.Experiences
{
    // Pop-Up Diner Rush: a prep table with the plate in the middle, two rows of ingredient crates
    // in front (burger row, pizza row), the order-ticket rail and pick-up counter behind.
    [ShowcaseExperienceBuilder(1, "diner", "Pop-Up Diner Rush", "Build burgers and pizzas\nbefore the timer runs out.", "food_burger")]
    public static class DinerBuilder
    {
        const float CounterHeight = 0.34f;
        const float CounterZ = 0.84f;
        const float CrateSize = 0.29f;

        static readonly (Ingredient ingredient, string crate, string display)[] Bins =
        {
            (Ingredient.Bun, "crate_buns", "food_ingredient_bun"),
            (Ingredient.Patty, "crate_steak", "food_ingredient_burger_cooked"),
            (Ingredient.Cheese, "crate_cheese", "food_ingredient_cheese"),
            (Ingredient.Lettuce, "crate_lettuce", "food_ingredient_lettuce"),
            (Ingredient.Tomato, "crate_tomatoes", "food_ingredient_tomato"),
            (Ingredient.Onion, "crate_onions", "food_ingredient_onion"),
            (Ingredient.Dough, "crate_dough", "food_ingredient_dough"),
            (Ingredient.Sauce, "crate", "food_ingredient_tomato_sauce"),
            (Ingredient.Pepperoni, "crate_pepperoni", "food_ingredient_pepperoni"),
            (Ingredient.Mushroom, "crate_mushrooms", "food_ingredient_mushroom"),
        };

        // Indexed by Piece.
        static readonly string[] Pieces =
        {
            "food_ingredient_bun_bottom", "food_ingredient_bun_top", "food_ingredient_burger_cooked",
            "food_ingredient_cheese_slice", "food_ingredient_lettuce_slice", "food_ingredient_tomato_slices",
            "food_ingredient_onion_rings", "food_ingredient_dough_base", "food_ingredient_tomato_sauce",
            "food_ingredient_cheese_grated", "food_ingredient_pepperoni_slices", "food_ingredient_mushroom_chopped",
        };

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            ctx.Floor(root, new Vector2(3.0f, 2.2f), new Color(0.55f, 0.78f, 0.76f));

            // Back: counter run, left part holds the ticket rail, right part is the pick-up pass.
            var back = new GameObject("Back Counter").transform;
            back.SetParent(root, false);
            var first = ctx.Place("kitchencounter_straight_A", back, new Vector3(0f, 0f, CounterZ), CounterHeight);
            float w = ShowcaseBuildContext.RendererBounds(first).size.x / root.lossyScale.x;
            Object.DestroyImmediate(first);
            int count = Mathf.Max(1, Mathf.FloorToInt(2.2f / w));
            float x0 = -(count - 1) * w * 0.5f;
            for (int i = 0; i < count; i++)
                ctx.Place("kitchencounter_straight_A", back, new Vector3(x0 + i * w, 0f, CounterZ), CounterHeight);
            ShowcaseBuildContext.Log("diner: counter piece width " + w.ToString("0.00") + " x " + count);

            ctx.Primitive(PrimitiveType.Cube, "Ticket Rail", back, new Vector3(-0.47f, CounterHeight + 0.015f, CounterZ - 0.1f),
                new Vector3(1.0f, 0.03f, 0.05f), new Color(0.62f, 0.64f, 0.7f), false);
            var anchors = new[] { new Vector3(-0.24f, CounterHeight + 0.03f, CounterZ - 0.1f), new Vector3(-0.74f, CounterHeight + 0.03f, CounterZ - 0.1f) }
                .Select((p, i) => { var t = new GameObject("Ticket " + i).transform; t.SetParent(back, false); t.localPosition = p; return t; }).ToArray();

            var pickup = new GameObject("Pickup").transform;
            pickup.SetParent(root, false);
            pickup.localPosition = new Vector3(0.32f, CounterHeight, CounterZ - 0.05f);
            var signPos = new Vector3(0.62f, CounterHeight + 0.4f, CounterZ + 0.14f);
            foreach (float dx in new[] { -0.27f, 0.27f })
                ctx.Primitive(PrimitiveType.Cylinder, "Sign Post", back, new Vector3(signPos.x + dx, CounterHeight + 0.2f, signPos.z + 0.02f),
                    new Vector3(0.025f, 0.2f, 0.025f), new Color(0.3f, 0.3f, 0.36f), false);
            BuildSign(back, signPos);

            // Centre: prep table with the plate.
            var table = ctx.Place("kitchentable_A", root, new Vector3(0f, 0f, 0.15f), 0.36f);
            float tableTop = ShowcaseBuildContext.RendererBounds(table).max.y;
            float tableTopLocal = root.InverseTransformPoint(new Vector3(0f, tableTop, 0f)).y;
            var plate = ctx.Place("plate", root, new Vector3(0f, tableTopLocal, 0.12f), 0.36f, 0f, true);
            float plateTop = root.InverseTransformPoint(ShowcaseBuildContext.RendererBounds(plate).max).y;
            var stack = new GameObject("Stack").transform;
            stack.SetParent(root, false);
            stack.localPosition = new Vector3(0f, plateTop - 0.004f, 0.12f);
            ctx.Place("ketchup", root, new Vector3(0.32f, tableTopLocal, 0.25f), 0.13f);
            ctx.Place("mustard", root, new Vector3(0.4f, tableTopLocal, 0.12f), 0.13f);
            ctx.Place("menu", root, new Vector3(-0.34f, tableTopLocal, 0.2f), 0.12f, 20f);

            // Front: two rows of five crates, burger things nearest the table.
            var bins = new GameObject("Bins").transform;
            bins.SetParent(root, false);
            for (int i = 0; i < Bins.Length; i++)
            {
                var (ingredient, crateName, displayName) = Bins[i];
                int row = i / 5, col = i % 5;
                var holder = new GameObject("Bin " + ingredient).transform;
                holder.SetParent(bins, false);
                holder.localPosition = new Vector3(-0.74f + col * 0.37f, 0f, -0.3f - row * 0.46f);
                var crate = ctx.Place(crateName, holder, Vector3.zero, CrateSize, 0f, true);
                float crateTop = holder.InverseTransformPoint(ShowcaseBuildContext.RendererBounds(crate).max).y;
                float crateFront = holder.InverseTransformPoint(ShowcaseBuildContext.RendererBounds(crate).min).z;
                var display = ctx.Place(displayName, holder, new Vector3(0f, crateTop - 0.03f, 0f), 0.17f, 15f, true);
                // FitBoxCollider's `GetComponent ?? AddComponent` misses Unity's fake null, so add it first.
                holder.gameObject.AddComponent<BoxCollider>();
                var box = ShowcaseBuildContext.FitBoxCollider(holder.gameObject);
                box.size *= 1.2f;
                var bin = holder.gameObject.AddComponent<DinerBin>();
                bin.Ingredient = ingredient;
                bin.Display = display != null ? display.transform : null;
                bin.FrontWidth = CrateSize * 0.95f;
                bin.FrontZ = crateFront - 0.012f;
                bin.LabelY = crateTop * 0.42f;
            }

            // Decor at the back corners.
            ctx.Place("pizzabox_stacked", root, new Vector3(0.95f, 0f, 0.28f), 0.2f, -20f);
            ctx.Place("jar_A_large", root, new Vector3(-0.95f, 0f, 0.3f), 0.18f);

            var exp = root.gameObject.AddComponent<DinerRushExperience>();
            exp.Title = "Pop-Up Diner Rush";
            exp.Hint = "Tap the crates in the ticket's order · i = menu info";
            exp.FocusPoint = new Vector3(0f, 0.22f, -0.05f);
            exp.FocusDistance = 3.2f;
            exp.FocusPitch = 50f;
            ShowcaseBuildContext.Set(exp, "m_piecePrefabs", Pieces.Select(n => (Object)ctx.Prefab(n)).ToArray());
            ShowcaseBuildContext.Set(exp, "m_platePrefab", ctx.Prefab("plate"));
            ShowcaseBuildContext.Set(exp, "m_plate", plate.transform);
            ShowcaseBuildContext.Set(exp, "m_stack", stack);
            ShowcaseBuildContext.Set(exp, "m_ticketAnchors", anchors.Cast<Object>().ToArray());
            ShowcaseBuildContext.Set(exp, "m_pickup", pickup);
        }

        // "NOVA DINER · PICK-UP" sign over the pass, built as a world canvas so it shows in previews too.
        static void BuildSign(Transform parent, Vector3 localPos)
        {
            var holder = new GameObject("Sign").transform;
            holder.SetParent(parent, false);
            holder.localPosition = localPos;
            holder.localRotation = Quaternion.Euler(20f, 0f, 0f);
            var canvas = Ui.WorldCanvas("Canvas", holder, new Vector2(700, 200), 0.72f);
            var bg = Ui.Panel(canvas.transform, Ui.Ink, "Background");
            Ui.Stretch(bg.rectTransform);
            var stripe = Ui.Panel(bg.transform, Ui.Accent, "Stripe");
            Ui.Place(stripe.rectTransform, new Vector2(0.5f, 0f), new Vector2(700, 16), Vector2.zero);
            bg.raycastTarget = stripe.raycastTarget = false;
            var title = Ui.Label(bg.transform, "NOVA DINER", 96, new Color(1f, 0.82f, 0.3f), TextAnchor.MiddleCenter, FontStyle.Bold, "Brand");
            Ui.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(680, 120), new Vector2(0, -10));
            var sub = Ui.Label(bg.transform, "PICK-UP  ›", 44, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Sub");
            Ui.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(680, 60), new Vector2(0, 22));
        }
    }
}
