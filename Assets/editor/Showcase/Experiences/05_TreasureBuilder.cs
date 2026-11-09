using Showcase.Experiences.Treasure;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.EditorTools.Experiences
{
    // Treasure vault diorama: chest centre-back, crates and loot piles along the walls, a gold rim,
    // a START button in front and the (inactive) templates of the five falling item kinds.
    [ShowcaseExperienceBuilder(5, "treasure", "Treasure Rain", "Catch gold and gems\nin 45 seconds.", "Gold_Bar")]
    public static class TreasureBuilder
    {
        static readonly Color FloorColor = new Color(0.3f, 0.26f, 0.42f);
        static readonly Color RugColor = new Color(0.4f, 0.34f, 0.58f);
        static readonly Color WallColor = new Color(0.22f, 0.19f, 0.33f);
        static readonly Color Trim = new Color(0.98f, 0.74f, 0.27f);
        static readonly Color GoldBox = new Color(1f, 0.77f, 0.2f);
        static readonly Color Ribbon = new Color(0.9f, 0.22f, 0.28f);
        static readonly Color Cream = new Color(1f, 0.97f, 0.82f);
        static readonly Color BombBody = new Color(0.2f, 0.18f, 0.24f);
        static readonly Color BombStripe = new Color(0.92f, 0.16f, 0.14f);

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            ctx.Floor(root, new Vector2(3.3f, 2.3f), FloorColor);
            ctx.Primitive(PrimitiveType.Cube, "Rug", root, new Vector3(0f, 0.004f, -0.12f), new Vector3(2.7f, 0.008f, 1.55f), RugColor, false);
            ctx.Primitive(PrimitiveType.Cube, "Rug Trim", root, new Vector3(0f, 0.003f, -0.12f), new Vector3(2.78f, 0.006f, 1.63f), Trim, false);

            ctx.Primitive(PrimitiveType.Cube, "Rim Front", root, new Vector3(0f, 0.05f, -1.12f), new Vector3(3.3f, 0.1f, 0.06f), Trim);
            ctx.Primitive(PrimitiveType.Cube, "Rim Left", root, new Vector3(-1.62f, 0.09f, 0f), new Vector3(0.06f, 0.18f, 2.3f), Trim);
            ctx.Primitive(PrimitiveType.Cube, "Rim Right", root, new Vector3(1.62f, 0.09f, 0f), new Vector3(0.06f, 0.18f, 2.3f), Trim);
            ctx.Primitive(PrimitiveType.Cube, "Back Wall", root, new Vector3(0f, 0.175f, 1.12f), new Vector3(3.3f, 0.35f, 0.08f), WallColor);
            ctx.Primitive(PrimitiveType.Cube, "Back Wall Trim", root, new Vector3(0f, 0.365f, 1.12f), new Vector3(3.32f, 0.03f, 0.11f), Trim, false);

            // Vault props; colliders so the rain bounces off them.
            var chest = Solid(ctx.Place("Gems_Chest", root, new Vector3(0f, 0f, 0.78f), 0.4f));
            var bigCrate = Solid(ctx.Place("Containers_Crate_Large", root, new Vector3(-1.2f, 0f, 0.82f), 0.42f, 8f, true));
            Solid(ctx.Place("Containers_Crate_Medium_Wood", root, new Vector3(-0.82f, 0f, 0.9f), 0.28f, -8f, true));
            float crateTop = bigCrate != null ? root.InverseTransformPoint(ShowcaseBuildContext.RendererBounds(bigCrate).max).y : 0.2f;
            Solid(ctx.Place("Containers_Box_Small", root, new Vector3(-1.22f, crateTop, 0.84f), 0.15f, 25f, true));
            Solid(ctx.Place("Containers_Crate_Medium_Tan", root, new Vector3(1.2f, 0f, 0.82f), 0.36f, -10f, true));
            Solid(ctx.Place("Gold_Bars_Stack_Large", root, new Vector3(0.82f, 0f, 0.9f), 0.22f, 10f, true));
            Solid(ctx.Place("Money_Pile_Large", root, new Vector3(-0.52f, 0f, 0.88f), 0.14f, 0f));
            Solid(ctx.Place("Gems_Sack", root, new Vector3(0.52f, 0f, 0.9f), 0.2f, -20f));
            Solid(ctx.Place("Food_Basket_A_Empty", root, new Vector3(-1.3f, 0f, -0.82f), 0.14f, 15f));
            Solid(ctx.Place("Food_Basket_B_Empty", root, new Vector3(1.3f, 0f, -0.82f), 0.14f, -15f));
            Solid(ctx.Place("Silver_Bars_Stack_Medium", root, new Vector3(-1.38f, 0f, 0.1f), 0.22f, 70f, true));
            Solid(ctx.Place("Money_Coins_Stack_Large", root, new Vector3(1.4f, 0f, 0.1f), 0.17f, 0f));

            var mouth = new GameObject("Chest Mouth").transform;
            mouth.SetParent(root, false);
            float chestTop = chest != null ? root.InverseTransformPoint(ShowcaseBuildContext.RendererBounds(chest).max).y : 0.4f;
            mouth.localPosition = new Vector3(0f, chestTop + 0.02f, 0.78f);

            BuildSign(root);
            var start = BuildStartButton(ctx, root);
            var templates = BuildTemplates(ctx, root);

            var exp = root.gameObject.AddComponent<TreasureRainExperience>();
            exp.Id = "treasure";
            exp.Title = "Treasure Rain";
            exp.Hint = "Tap START, then tap the falling treasure.\nAvoid the bomb crates!";
            exp.FocusPoint = new Vector3(0f, 0.25f, 0.05f);
            exp.FocusDistance = 4.6f;
            exp.FocusPitch = 48f;
            ShowcaseBuildContext.Set(exp, "m_templates", templates);
            ShowcaseBuildContext.Set(exp, "m_startButton", start);
            ShowcaseBuildContext.Set(exp, "m_chestMouth", mouth);
            if (chest != null) ShowcaseBuildContext.Set(exp, "m_chest", chest.transform);
            ShowcaseBuildContext.Set(start, "m_owner", exp);
            ShowcaseBuildContext.Log("Treasure Rain: " + templates.Length + " item templates");
        }

        static GameObject Solid(GameObject go)
        {
            if (go == null) return null;
            // FitBoxCollider's `GetComponent ?? AddComponent` misses Unity's fake-null, so add the box first.
            if (go.GetComponent<BoxCollider>() == null) go.AddComponent<BoxCollider>();
            ShowcaseBuildContext.FitBoxCollider(go);
            return go;
        }

        static void BuildSign(Transform root)
        {
            var sign = Ui.WorldCanvas("Vault Sign", root, new Vector2(1200, 330), 1.8f);
            sign.transform.localPosition = new Vector3(0f, 0.62f, 1.14f);
            sign.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            var bg = Ui.Panel(sign.transform, new Color(0.13f, 0.12f, 0.2f, 0.94f), "Background");
            Ui.Stretch(bg.rectTransform);
            bg.raycastTarget = false;
            var stripe = Ui.Panel(bg.transform, Trim, "Stripe");
            Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(1200, 14), Vector2.zero);
            stripe.raycastTarget = false;
            var title = Ui.Label(bg.transform, "NOVA VAULT", 100, Trim, TextAnchor.MiddleCenter, FontStyle.Bold, "Title");
            Ui.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(1160, 120), new Vector2(0, -22));
            var legend = Ui.Label(bg.transform,
                "Gold <color=#FFD23F>+1</color>     Coins <color=#FFD23F>+2</color>     Gems <color=#8FE3FF>+5</color>\nGolden box <color=#FFD23F>+20</color>     Bomb crate <color=#FF6B5E>-10</color>",
                54, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Legend");
            legend.lineSpacing = 1.1f;
            Ui.Place(legend.rectTransform, new Vector2(0.5f, 0f), new Vector2(1160, 160), new Vector2(0, 18));
        }

        static TreasureStartButton BuildStartButton(ShowcaseBuildContext ctx, Transform root)
        {
            var btn = new GameObject("Start Button").transform;
            btn.SetParent(root, false);
            btn.localPosition = new Vector3(0f, 0f, -0.3f);
            ctx.Primitive(PrimitiveType.Cylinder, "Base", btn, new Vector3(0f, 0.03f, 0f), new Vector3(0.62f, 0.03f, 0.62f), new Color(0.55f, 0.38f, 0.14f), false);
            ctx.Primitive(PrimitiveType.Cylinder, "Base Trim", btn, new Vector3(0f, 0.062f, 0f), new Vector3(0.56f, 0.004f, 0.56f), Trim, false);
            var cap = ctx.Primitive(PrimitiveType.Cylinder, "Cap", btn, new Vector3(0f, 0.1f, 0f), new Vector3(0.5f, 0.036f, 0.5f), new Color(1f, 0.36f, 0.3f), false);

            var top = Ui.WorldCanvas("Cap Label", btn, new Vector2(500, 220), 0.46f);
            top.transform.localPosition = new Vector3(0f, 0.138f, 0f);
            top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var topText = Ui.Label(top.transform, "START", 150, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(topText.rectTransform);
            topText.horizontalOverflow = HorizontalWrapMode.Overflow;
            topText.gameObject.AddComponent<Outline>().effectColor = new Color(0.45f, 0.08f, 0.06f, 0.8f);

            var tag = Ui.WorldCanvas("Tag", btn, new Vector2(620, 110), 0.62f);
            tag.transform.localPosition = new Vector3(0f, 0.3f, 0.1f);
            tag.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            var tagBg = Ui.Panel(tag.transform, new Color(1f, 0.98f, 0.95f, 0.95f), "Background");
            Ui.Stretch(tagBg.rectTransform);
            tagBg.raycastTarget = false;
            var tagText = Ui.Label(tagBg.transform, "Tap to play · 45 s", 56, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(tagText.rectTransform, 10, 10, 0, 0);

            var box = btn.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.12f, 0f);
            box.size = new Vector3(0.66f, 0.26f, 0.66f);
            var comp = btn.gameObject.AddComponent<TreasureStartButton>();
            ShowcaseBuildContext.Set(comp, "m_cap", cap.transform);
            return comp;
        }

        // One inactive template per TreasureKind, in enum order, each centred on its own origin.
        static TreasureItem[] BuildTemplates(ShowcaseBuildContext ctx, Transform root)
        {
            var holder = new GameObject("Item Templates").transform;
            holder.SetParent(root, false);
            holder.localPosition = new Vector3(0f, 2f, 0f);

            var items = new TreasureItem[5];
            items[(int)TreasureKind.Gold] = PackTemplate(ctx, holder, TreasureKind.Gold, "Gold_Bar", 0.22f);
            items[(int)TreasureKind.Coins] = PackTemplate(ctx, holder, TreasureKind.Coins, "Money_Coins_Stack_Medium", 0.18f);
            items[(int)TreasureKind.Gems] = PackTemplate(ctx, holder, TreasureKind.Gems, "Gems_Pile_Small", 0.2f);

            var golden = Wrapper(holder, TreasureKind.Golden);
            ctx.Primitive(PrimitiveType.Cube, "Box", golden.transform, Vector3.zero, Vector3.one * 0.17f, GoldBox, false);
            ctx.Primitive(PrimitiveType.Cube, "Ribbon", golden.transform, Vector3.zero, new Vector3(0.176f, 0.176f, 0.036f), Ribbon, false);
            ctx.Primitive(PrimitiveType.Cube, "Bow L", golden.transform, new Vector3(-0.025f, 0.095f, 0f), new Vector3(0.05f, 0.025f, 0.03f), Ribbon, false).transform.localRotation = Quaternion.Euler(0f, 0f, 25f);
            ctx.Primitive(PrimitiveType.Cube, "Bow R", golden.transform, new Vector3(0.025f, 0.095f, 0f), new Vector3(0.05f, 0.025f, 0.03f), Ribbon, false).transform.localRotation = Quaternion.Euler(0f, 0f, -25f);
            foreach (float side in new[] { -1f, 1f })
                for (int k = 0; k < 3; k++)
                {
                    var bar = ctx.Primitive(PrimitiveType.Cube, "Star", golden.transform, new Vector3(0f, 0f, side * 0.087f), new Vector3(0.11f, 0.024f, 0.006f), Cream, false);
                    bar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f + k * 60f);
                }
            items[(int)TreasureKind.Golden] = golden;

            var bomb = Wrapper(holder, TreasureKind.Bomb);
            ctx.Primitive(PrimitiveType.Cube, "Crate", bomb.transform, Vector3.zero, Vector3.one * 0.19f, BombBody, false);
            ctx.Primitive(PrimitiveType.Cube, "Stripe", bomb.transform, Vector3.zero, new Vector3(0.196f, 0.05f, 0.196f), BombStripe, false);
            ctx.Primitive(PrimitiveType.Cube, "Stripe V", bomb.transform, Vector3.zero, new Vector3(0.05f, 0.196f, 0.196f), BombStripe, false);
            ctx.Primitive(PrimitiveType.Cylinder, "Fuse", bomb.transform, new Vector3(0f, 0.118f, 0f), new Vector3(0.024f, 0.022f, 0.024f), new Color(0.35f, 0.33f, 0.36f), false);
            ctx.Primitive(PrimitiveType.Sphere, "Spark", bomb.transform, new Vector3(0f, 0.152f, 0f), Vector3.one * 0.042f, new Color(1f, 0.62f, 0.12f), false);
            items[(int)TreasureKind.Bomb] = bomb;

            holder.gameObject.SetActive(false);
            return items;
        }

        static TreasureItem Wrapper(Transform holder, TreasureKind kind)
        {
            var go = new GameObject("Template " + kind);
            go.transform.SetParent(holder, false);
            go.transform.localPosition = new Vector3(((int)kind - 2) * 0.4f, 0f, 0f);
            var item = go.AddComponent<TreasureItem>();
            item.Kind = kind;
            return item;
        }

        static TreasureItem PackTemplate(ShowcaseBuildContext ctx, Transform holder, TreasureKind kind, string prefab, float size)
        {
            var item = Wrapper(holder, kind);
            var visual = ctx.Place(prefab, item.transform, Vector3.zero, size, 0f, true);
            if (visual != null)
            {
                var b = ShowcaseBuildContext.RendererBounds(visual);
                visual.transform.position += item.transform.position - b.center;
            }
            return item;
        }
    }
}
