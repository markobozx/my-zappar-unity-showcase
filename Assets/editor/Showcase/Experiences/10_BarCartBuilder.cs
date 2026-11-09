using System.Collections.Generic;
using Showcase.Experiences.BarCart;
using UnityEngine;

namespace Showcase.EditorTools.Experiences
{
    // Build Your Bar Cart: a Lumo Sodas mocktail bar. Back counter with six soda bottles, a bar cart
    // in the middle (three preloaded styles), a glass tray on the left, a garnish tray on the right,
    // a pedestal for the finished drink and a tilted step tracker along the front edge.
    [ShowcaseExperienceBuilder(10, "barcart", "Build Your Bar Cart", "Mix a drink and get\nthe recipe card.", "SM_Prop_Bar_Bottle_02")]
    public static class BarCartBuilder
    {
        static readonly Color FloorColor = new Color(0.36f, 0.3f, 0.42f);
        static readonly Color WallColor = new Color(0.98f, 0.66f, 0.56f);
        static readonly Color CounterWood = new Color(0.55f, 0.36f, 0.25f);
        static readonly Color CounterTop = new Color(0.98f, 0.95f, 0.9f);
        static readonly Color Trim = new Color(0.2f, 0.75f, 0.68f);
        static readonly Color TrayColor = new Color(0.62f, 0.42f, 0.3f);
        static readonly Color DishColor = new Color(0.9f, 0.93f, 0.97f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.3f);
        static readonly Color SlabColor = new Color(0.27f, 0.22f, 0.33f);

        const float CounterZ = 0.74f, CounterDepth = 0.3f, CounterHeight = 0.24f;
        const float CartZ = 0.12f;
        const float TrayZ = -0.22f, TrayX = 0.9f;

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            ctx.Floor(root, new Vector2(3.0f, 2.0f), FloorColor);
            BuildBackBar(ctx, root);

            // Back counter with six sodas.
            var shelf = Anchor(root, "Shelf", new Vector3(0f, CounterHeight, CounterZ));
            string[] bottlePrefabs = { "SM_Prop_Bar_Bottle_05", "SM_Prop_Bar_Bottle_08", "SM_Prop_Bar_Bottle_06", "SM_Prop_Bar_Bottle_03", "SM_Prop_Bar_Bottle_07", "SM_Prop_Bar_Bottle_01" };
            var bottles = new List<BarCartPart>();
            for (int i = 0; i < bottlePrefabs.Length; i++)
            {
                var go = ctx.Place(bottlePrefabs[i], shelf, new Vector3(-1.0f + i * 0.4f, 0f, 0f), 0.24f);
                bottles.Add(Part(go, root, BarCartKind.Bottle, i));
            }
            var shelfTags = Anchor(root, "Shelf Tags", new Vector3(0f, CounterHeight * 0.5f, CounterZ - CounterDepth * 0.5f - 0.006f));

            // Cart styles, all in the same spot; only the first is visible.
            var cartSlot = Anchor(root, "Cart Slot", new Vector3(0f, 0f, CartZ));
            string[] cartPrefabs = { "Kitchen_Table_09", "Work_Table_03", "Kitchen_Table_05" };
            var carts = new List<GameObject>();
            var tops = new List<float>();
            for (int i = 0; i < cartPrefabs.Length; i++)
            {
                var go = ctx.Place(cartPrefabs[i], cartSlot, Vector3.zero, 0.34f);
                if (go == null) continue;
                Part(go, root, BarCartKind.Cart, i, 1.05f);
                tops.Add(root.InverseTransformPoint(ShowcaseBuildContext.RendererBounds(go).max).y);
                go.SetActive(i == 0);
                carts.Add(go);
            }
            var cartTop = Anchor(root, "Cart Top", new Vector3(0f, tops.Count > 0 ? tops[0] : 0.34f, CartZ));

            var exp = root.gameObject.AddComponent<BarCartExperience>();
            var shakerGo = ctx.Place("SM_Prop_Cocktail_Shaker_01", cartTop, exp.ShakerSlot, 0.2f);
            Clean(shakerGo);
            ShowcaseBuildContext.FitBoxCollider(shakerGo, 1.3f);
            var shaker = shakerGo.AddComponent<BarCartShaker>();

            // Glass tray (left).
            ctx.Primitive(PrimitiveType.Cube, "Glass Tray", root, new Vector3(-TrayX, 0.025f, TrayZ), new Vector3(0.7f, 0.05f, 0.3f), TrayColor, false);
            ctx.Primitive(PrimitiveType.Cube, "Glass Tray Trim", root, new Vector3(-TrayX, 0.01f, TrayZ), new Vector3(0.72f, 0.02f, 0.32f), Trim, false);
            var glassTray = Anchor(root, "Glass Tray Top", new Vector3(-TrayX, 0.05f, TrayZ));
            var glassSpecs = new[]
            {
                // prefab, height, liquid bottom/top (fraction of height), liquid diameter (fraction of width), bottom radius fraction
                ("SM_Prop_Bar_Glass_Empty_08", 0.17f, 0.1f, 0.95f, 0.86f, 0.9f),
                ("SM_Prop_Bar_Glass_Empty_09", 0.15f, 0.52f, 0.96f, 0.88f, 0.0f),
                ("SM_Prop_Bar_Glass_Empty_07", 0.12f, 0.12f, 0.94f, 0.88f, 0.85f),
            };
            var glasses = new List<BarCartPart>();
            var rims = new List<Transform>();
            var liquids = new List<Transform>();
            var full = new List<Vector3>();
            var bottomFrac = new List<float>();
            for (int i = 0; i < glassSpecs.Length; i++)
            {
                var (prefab, height, lb, lt, ld, bf) = glassSpecs[i];
                var go = ctx.Place(prefab, glassTray, new Vector3(-0.22f + i * 0.22f, 0f, 0f), height);
                glasses.Add(Part(go, root, BarCartKind.Glass, i));
                var b = LocalBounds(go, go.transform);
                float width = Mathf.Min(b.size.x, b.size.z);
                var liquid = ctx.Primitive(PrimitiveType.Cylinder, "Liquid", go.transform, new Vector3(b.center.x, b.min.y + lb * b.size.y, b.center.z),
                    new Vector3(width * ld, 0.0001f, width * ld), Color.white, false);
                liquid.GetComponent<Renderer>().enabled = false;
                liquids.Add(liquid.transform);
                full.Add(new Vector3(width * ld, (lt - lb) * b.size.y, width * ld));
                bottomFrac.Add(bf);
                var rim = new GameObject("Rim").transform;
                rim.SetParent(go.transform, false);
                rim.localPosition = new Vector3(b.center.x + width * 0.3f, b.max.y - b.size.y * 0.04f, b.center.z);
                rims.Add(rim);
            }
            var glassTags = Anchor(root, "Glass Tags", new Vector3(-TrayX, 0.06f, TrayZ - 0.25f));

            // Garnish tray (right): each garnish on a little dish.
            ctx.Primitive(PrimitiveType.Cube, "Garnish Tray", root, new Vector3(TrayX, 0.025f, TrayZ), new Vector3(0.7f, 0.05f, 0.3f), TrayColor, false);
            ctx.Primitive(PrimitiveType.Cube, "Garnish Tray Trim", root, new Vector3(TrayX, 0.01f, TrayZ), new Vector3(0.72f, 0.02f, 0.32f), Trim, false);
            var garnishTray = Anchor(root, "Garnish Tray Top", new Vector3(TrayX, 0.065f, TrayZ));
            var garnishSpecs = new[] { ("SM_Prop_Lemon_Slice_01", 0.085f), ("icecream_cherry", 0.06f), ("SM_Prop_Cocktail_Leaf_01", 0.085f) };
            var garnishes = new List<BarCartPart>();
            for (int i = 0; i < garnishSpecs.Length; i++)
            {
                var (prefab, size) = garnishSpecs[i];
                float x = -0.22f + i * 0.22f;
                ctx.Primitive(PrimitiveType.Cylinder, "Dish", root, new Vector3(TrayX + x, 0.0575f, TrayZ), new Vector3(0.16f, 0.0075f, 0.16f), DishColor, false);
                var go = ctx.Place(prefab, garnishTray, new Vector3(x, 0f, 0f), size, 0f, true);
                garnishes.Add(Part(go, root, BarCartKind.Garnish, i));
            }
            var garnishTags = Anchor(root, "Garnish Tags", new Vector3(TrayX, 0.06f, TrayZ - 0.25f));

            // Pedestal for the finished drink.
            ctx.Primitive(PrimitiveType.Cylinder, "Pedestal", root, new Vector3(0f, 0.03f, -0.42f), new Vector3(0.26f, 0.03f, 0.26f), Gold, false);
            ctx.Primitive(PrimitiveType.Cylinder, "Pedestal Top", root, new Vector3(0f, 0.065f, -0.42f), new Vector3(0.2f, 0.005f, 0.2f), CounterTop, false);
            var pedestalTop = Anchor(root, "Pedestal Anchor", new Vector3(0f, 0.07f, -0.42f));

            // Step tracker: a slanted slab along the front edge; the canvas is built at runtime on its face.
            var tracker = Anchor(root, "Tracker", new Vector3(0f, 0.16f, -0.82f));
            var slab = ctx.Primitive(PrimitiveType.Cube, "Tracker Slab", tracker, new Vector3(0f, 0f, 0f), new Vector3(1.96f, 0.4f, 0.03f), SlabColor, false);
            slab.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
            slab.transform.localPosition = slab.transform.localRotation * new Vector3(0f, 0f, 0.02f);

            var recipe = Anchor(root, "Recipe Anchor", new Vector3(0f, 0.86f, 0.38f));

            exp.Title = "Build Your Bar Cart";
            exp.Hint = "Tap the cart to restyle it, then press Next";
            exp.FocusPoint = new Vector3(0f, 0.22f, 0f);
            exp.FocusDistance = 3.7f;
            exp.FocusPitch = 46f;
            exp.Carts = carts.ToArray();
            exp.CartTops = tops.ToArray();
            exp.CartTop = cartTop;
            exp.Bottles = bottles.ToArray();
            exp.Glasses = glasses.ToArray();
            exp.Garnishes = garnishes.ToArray();
            exp.GlassRims = rims.ToArray();
            exp.Liquids = liquids.ToArray();
            exp.LiquidFull = full.ToArray();
            exp.LiquidBottomFrac = bottomFrac.ToArray();
            exp.Shaker = shaker;
            exp.PedestalTop = pedestalTop;
            exp.TrackerAnchor = tracker;
            exp.RecipeAnchor = recipe;
            exp.ShelfTagAnchor = shelfTags;
            exp.GlassTagAnchor = glassTags;
            exp.GarnishTagAnchor = garnishTags;
            ShowcaseBuildContext.Log("barcart: " + carts.Count + " carts (tops " + string.Join(", ", tops.ConvertAll(t => t.ToString("0.00"))) + "), "
                                     + bottles.Count + " bottles, " + glasses.Count + " glasses, " + garnishes.Count + " garnishes");
        }

        static void BuildBackBar(ShowcaseBuildContext ctx, Transform root)
        {
            const float wallZ = 0.97f, wallH = 0.86f;
            ctx.Primitive(PrimitiveType.Cube, "Back Wall", root, new Vector3(0f, wallH * 0.5f, wallZ), new Vector3(3.0f, wallH, 0.06f), WallColor, false);
            ctx.Primitive(PrimitiveType.Cube, "Wall Cap", root, new Vector3(0f, wallH + 0.01f, wallZ), new Vector3(3.02f, 0.02f, 0.08f), Trim, false);
            ctx.Primitive(PrimitiveType.Cube, "Counter", root, new Vector3(0f, (CounterHeight - 0.02f) * 0.5f, CounterZ), new Vector3(2.8f, CounterHeight - 0.02f, CounterDepth), CounterWood, false);
            ctx.Primitive(PrimitiveType.Cube, "Counter Top", root, new Vector3(0f, CounterHeight - 0.01f, CounterZ - 0.01f), new Vector3(2.86f, 0.02f, CounterDepth + 0.04f), CounterTop, false);
            ctx.Primitive(PrimitiveType.Cube, "Counter Stripe", root, new Vector3(0f, 0.035f, CounterZ - CounterDepth * 0.5f - 0.004f), new Vector3(2.8f, 0.03f, 0.01f), Trim, false);

            // Upper shelf with a few big decor bottles either side of the sign.
            ctx.Primitive(PrimitiveType.Cube, "Wall Shelf L", root, new Vector3(-1.05f, 0.5f, wallZ - 0.1f), new Vector3(0.62f, 0.025f, 0.14f), CounterWood, false);
            ctx.Primitive(PrimitiveType.Cube, "Wall Shelf R", root, new Vector3(1.05f, 0.5f, wallZ - 0.1f), new Vector3(0.62f, 0.025f, 0.14f), CounterWood, false);
            Decor(ctx.Place("SM_Prop_Bottle_Large_01", root, new Vector3(-1.2f, 0.5125f, wallZ - 0.1f), 0.26f));
            Decor(ctx.Place("SM_Prop_Bottle_Large_03", root, new Vector3(-0.9f, 0.5125f, wallZ - 0.1f), 0.22f));
            Decor(ctx.Place("SM_Prop_Ice_Bucket_01", root, new Vector3(0.95f, 0.5125f, wallZ - 0.1f), 0.14f));
            Decor(ctx.Place("SM_Prop_Bottle_Large_04", root, new Vector3(1.22f, 0.5125f, wallZ - 0.1f), 0.24f));

            var sign = new GameObject("Sign").transform;
            sign.SetParent(root, false);
            sign.localPosition = new Vector3(0f, 0.6f, wallZ - 0.035f);
            var canvas = Ui.WorldCanvas("Canvas", sign, new Vector2(800, 260), 1.0f);
            var bg = Ui.Panel(canvas.transform, new Color(0.13f, 0.12f, 0.2f, 1f), "Background");
            bg.raycastTarget = false;
            Ui.Stretch(bg.rectTransform);
            var title = Ui.Label(bg.transform, "LUMO SODAS", 110, new Color(1f, 0.85f, 0.35f), TextAnchor.MiddleCenter, FontStyle.Bold, "Brand");
            Ui.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(780, 150), new Vector2(0, -20));
            var sub = Ui.Label(bg.transform, "MOCKTAIL BAR  -  0% ALCOHOL", 44, new Color(0.45f, 0.95f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold, "Sub");
            Ui.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(780, 70), new Vector2(0, 26));
        }

        static Transform Anchor(Transform root, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.localPosition = localPos;
            return t;
        }

        static void Clean(GameObject go)
        {
            if (go == null) return;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
        }

        static void Decor(GameObject go) => Clean(go);

        // Tappable prop with a generous box collider (at least ~0.13 stage units on each side).
        static BarCartPart Part(GameObject go, Transform root, BarCartKind kind, int index, float padding = 1.2f)
        {
            if (go == null) return null;
            Clean(go);
            var box = ShowcaseBuildContext.FitBoxCollider(go, padding);
            float min = 0.13f * root.lossyScale.x / Mathf.Max(go.transform.lossyScale.x, 1e-5f);
            box.size = Vector3.Max(box.size, new Vector3(min, min, min));
            var part = go.AddComponent<BarCartPart>();
            part.Kind = kind;
            part.Index = index;
            return part;
        }

        static Bounds LocalBounds(GameObject go, Transform space)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
