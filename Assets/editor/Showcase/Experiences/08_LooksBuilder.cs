using System.Collections.Generic;
using Showcase.Experiences.Looks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Showcase.EditorTools.Experiences
{
    // Gallery diorama: plinths with props from four packs along the back, a couch and a reactor tube on
    // the sides, a progress board + neon sign on the back wall and the four-button style dial in front.
    // The four secrets (one per look) start inactive; the experience shows the one for the current look.
    [ShowcaseExperienceBuilder(8, "looks", "Look Switcher Gallery", "Restyle the world and\nfind hidden objects.", "Plants_01")]
    public static class LooksBuilder
    {
        const string RendererPath = "Assets/Settings/URP-Showcase_Renderer.asset";

        static readonly Color FloorColor = new Color(0.74f, 0.69f, 0.66f);
        static readonly Color RunnerColor = new Color(0.36f, 0.33f, 0.5f);
        static readonly Color WallColor = new Color(0.86f, 0.8f, 0.76f);
        static readonly Color WallTrim = new Color(0.26f, 0.23f, 0.38f);
        static readonly Color PlinthColor = new Color(0.93f, 0.91f, 0.88f);
        static readonly Color PlinthTop = new Color(0.26f, 0.23f, 0.38f);
        static readonly Color DialPlate = new Color(0.2f, 0.18f, 0.29f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.22f);

        static readonly Color[] LookColors =
        {
            new Color(1f, 0.45f, 0.38f),     // Toon
            new Color(0.17f, 0.17f, 0.24f),  // Ink
            new Color(0.42f, 0.66f, 0.18f),  // Game Boy
            new Color(0.6f, 0.38f, 0.95f),   // Pixel
        };

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            var exp = root.gameObject.AddComponent<LooksExperience>();

            ctx.Floor(root, new Vector2(3.3f, 2.3f), FloorColor);
            ctx.Primitive(PrimitiveType.Cube, "Runner", root, new Vector3(0f, 0.003f, 0.02f), new Vector3(2.2f, 0.006f, 0.56f), RunnerColor, false);
            ctx.Primitive(PrimitiveType.Cube, "Back Wall", root, new Vector3(0f, 0.475f, 1.1f), new Vector3(3.3f, 0.95f, 0.08f), WallColor);
            ctx.Primitive(PrimitiveType.Cube, "Back Wall Skirting", root, new Vector3(0f, 0.03f, 1.05f), new Vector3(3.3f, 0.06f, 0.04f), WallTrim, false);
            ctx.Primitive(PrimitiveType.Cube, "Back Wall Cap", root, new Vector3(0f, 0.96f, 1.1f), new Vector3(3.32f, 0.03f, 0.11f), WallTrim, false);

            var exhibits = new List<LookExhibit>();
            exhibits.Add(PlinthExhibit(ctx, root, exp, "Plants_01", new Vector3(-1.15f, 0f, 0.55f), 0.2f, 0.42f, 0f,
                "Potted Cactus", "ithappy Cute Furniture\nFlat Kit toon shading · 3 cel steps"));
            exhibits.Add(PlinthExhibit(ctx, root, exp, "food_burger", new Vector3(0.4f, 0f, 0.6f), 0.28f, 0.15f, -20f,
                "Deluxe Burger", "KayKit Restaurant\nLow-poly · one gradient atlas"));
            exhibits.Add(PlinthExhibit(ctx, root, exp, "CR_Radio", new Vector3(1.15f, 0f, 0.55f), 0.24f, 0.2f, -15f,
                "Space Radio", "Cosmic Retro props\n60s space-age, toon converted"));
            exhibits.Add(FloorExhibit(ctx, root, exp, "Couch_05", new Vector3(-1.1f, 0f, -0.18f), 0.56f, 35f, true, 0.6f,
                "Lounge Daybed", "ithappy Cute Furniture\nSoft pastel toon ramp"));
            exhibits.Add(FloorExhibit(ctx, root, exp, "Retro_laboratory_DNA_Reactor_Tube", new Vector3(1.2f, 0f, -0.2f), 0.5f, -30f, false, 0.48f,
                "Reactor Tube", "Cosmic Retro laboratory\nHard-edged cel highlights"));
            exhibits.Add(WallExhibit(ctx, root, exp, "SM_Prop_Sign_Flamingo_01", new Vector3(0.9f, 0.52f, 1.055f), 0.85f,
                "Neon Flamingo", "Synty Polygon Nightclubs\nEmissive neon on a 4k atlas"));
            exhibits.Add(DaisExhibit(ctx, root, exp, "icecream_bowl_decorated_A", new Vector3(-0.05f, 0f, 0.05f), 0.2f,
                "Sundae Deluxe", "KayKit Restaurant\nRound toon highlights, no textures"));

            // Empty plinth: the golden statue stands here, but only in the Ink look.
            var empty = new GameObject("Empty Plinth").transform;
            empty.SetParent(root, false);
            empty.localPosition = new Vector3(-0.38f, 0f, 0.6f);
            Plinth(ctx, empty, 0.3f, "? ? ?");

            var secrets = new LookSecret[4];
            secrets[0] = RainbowStar(ctx, root, exp, new Vector3(-1.1f, 0.62f, -0.18f));
            secrets[1] = GoldenStatue(ctx, root, exp, new Vector3(-0.38f, 0.3f, 0.6f));
            secrets[2] = Cartridge(ctx, root, exp, new Vector3(0.68f, 0.2f, -0.12f));
            secrets[3] = PixelHeart(ctx, root, exp, new Vector3(0.02f, 0.56f, 0.98f));

            var board = BuildBoard(root, out Text count, out Text[] rows, out Text now);
            BuildHeader(root);
            var buttons = BuildDial(ctx, root, exp);

            var fxPoint = new GameObject("Fx Point").transform;
            fxPoint.SetParent(root, false);
            fxPoint.localPosition = new Vector3(0f, 0.55f, 0.2f);

            exp.Id = "looks";
            exp.Title = "Look Switcher Gallery";
            exp.Hint = "Tap a style button: Toon, Ink, Game Boy or Pixel.\nEach look hides one secret object - tap it!";
            exp.FocusPoint = new Vector3(0f, 0.25f, 0.05f);
            exp.FocusDistance = 4.6f;
            exp.FocusPitch = 48f;
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if (rendererData == null) ShowcaseBuildContext.Log("MISSING renderer data " + RendererPath);
            else ShowcaseBuildContext.Set(exp, "m_renderer", rendererData);
            ShowcaseBuildContext.Set(exp, "m_buttons", buttons);
            ShowcaseBuildContext.Set(exp, "m_secrets", secrets);
            ShowcaseBuildContext.Set(exp, "m_exhibits", exhibits.ToArray());
            ShowcaseBuildContext.Set(exp, "m_boardCount", count);
            ShowcaseBuildContext.Set(exp, "m_boardRows", rows);
            ShowcaseBuildContext.Set(exp, "m_boardNow", now);
            ShowcaseBuildContext.Set(exp, "m_fxPoint", fxPoint);
            ShowcaseBuildContext.Log("Look Switcher Gallery: " + exhibits.Count + " exhibits, " + secrets.Length + " secrets");
        }

        // Plinth centred on the parent's origin; returns its top height.
        static float Plinth(ShowcaseBuildContext ctx, Transform parent, float height, string plaque)
        {
            ctx.Primitive(PrimitiveType.Cube, "Plinth", parent, new Vector3(0f, height * 0.5f - 0.012f, 0f), new Vector3(0.3f, height - 0.024f, 0.3f), PlinthColor, false);
            ctx.Primitive(PrimitiveType.Cube, "Plinth Top", parent, new Vector3(0f, height - 0.012f, 0f), new Vector3(0.33f, 0.024f, 0.33f), PlinthTop, false);
            var label = Ui.WorldCanvas("Plaque", parent, new Vector2(400, 90), 0.24f);
            label.transform.localPosition = new Vector3(0f, height * 0.55f, -0.152f);
            var bg = Ui.Panel(label.transform, PlinthTop, "Background");
            Ui.Stretch(bg.rectTransform);
            bg.raycastTarget = false;
            var t = Ui.Label(bg.transform, plaque, 52, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(t.rectTransform, 6, 6, 0, 0);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return height;
        }

        static LookExhibit PlinthExhibit(ShowcaseBuildContext ctx, Transform root, LooksExperience exp, string prefab, Vector3 pos,
            float plinthHeight, float propHeight, float yaw, string title, string credit)
        {
            var group = new GameObject(title).transform;
            group.SetParent(root, false);
            group.localPosition = pos;
            float top = Plinth(ctx, group, plinthHeight, title);
            ctx.Place(prefab, group, new Vector3(0f, top, 0f), propHeight, yaw, false);
            return Exhibit(group, exp, title, credit, top + propHeight + 0.18f);
        }

        // Low round dais in the middle of the room.
        static LookExhibit DaisExhibit(ShowcaseBuildContext ctx, Transform root, LooksExperience exp, string prefab, Vector3 pos, float propHeight, string title, string credit)
        {
            var group = new GameObject(title).transform;
            group.SetParent(root, false);
            group.localPosition = pos;
            ctx.Primitive(PrimitiveType.Cylinder, "Dais", group, new Vector3(0f, 0.05f, 0f), new Vector3(0.42f, 0.05f, 0.42f), PlinthColor, false);
            ctx.Primitive(PrimitiveType.Cylinder, "Dais Top", group, new Vector3(0f, 0.1f, 0f), new Vector3(0.44f, 0.008f, 0.44f), PlinthTop, false);
            ctx.Place(prefab, group, new Vector3(0f, 0.108f, 0f), propHeight, 0f, false);
            return Exhibit(group, exp, title, credit, 0.5f);
        }

        static LookExhibit FloorExhibit(ShowcaseBuildContext ctx, Transform root, LooksExperience exp, string prefab, Vector3 pos,
            float size, float yaw, bool fitLargestSide, float cardHeight, string title, string credit)
        {
            var group = new GameObject(title).transform;
            group.SetParent(root, false);
            group.localPosition = pos;
            ctx.Place(prefab, group, Vector3.zero, size, yaw, fitLargestSide);
            return Exhibit(group, exp, title, credit, cardHeight);
        }

        static LookExhibit WallExhibit(ShowcaseBuildContext ctx, Transform root, LooksExperience exp, string prefab, Vector3 pos,
            float size, string title, string credit)
        {
            var group = new GameObject(title).transform;
            group.SetParent(root, false);
            group.localPosition = pos;
            var go = ctx.Place(prefab, group, Vector3.zero, size, 0f, true);
            if (go != null)
            {
                // Centre it vertically on the anchor and push its back flat against the wall.
                var b = ShowcaseBuildContext.RendererBounds(go);
                Vector3 c = group.InverseTransformPoint(b.center);
                Vector3 max = group.InverseTransformPoint(b.max);
                go.transform.localPosition -= new Vector3(0f, c.y, max.z);
            }
            return Exhibit(group, exp, title, credit, 0.34f);
        }

        static LookExhibit Exhibit(Transform group, LooksExperience exp, string title, string credit, float cardHeight)
        {
            var box = ShowcaseBuildContext.FitBoxCollider(group.gameObject);
            box.size = Vector3.Max(box.size * 1.1f, Vector3.one * 0.14f);
            var ex = group.gameObject.AddComponent<LookExhibit>();
            ex.Title = title;
            ex.Credit = credit;
            ex.CardHeight = cardHeight;
            ShowcaseBuildContext.Set(ex, "m_owner", exp);
            return ex;
        }

        // Secret root (scale 1, collider, LookSecret) with a "Visual" child that spins and bobs.
        static LookSecret SecretRoot(Transform root, LooksExperience exp, int look, string title, Vector3 pos, out Transform visual)
        {
            var go = new GameObject("Secret " + title);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            var s = go.AddComponent<LookSecret>();
            s.Look = look;
            s.Title = title;
            ShowcaseBuildContext.Set(s, "m_owner", exp);
            ShowcaseBuildContext.Set(s, "m_visual", visual);
            return s;
        }

        static void FinishSecret(LookSecret s, float minSize)
        {
            var box = ShowcaseBuildContext.FitBoxCollider(s.gameObject);
            box.size = Vector3.Max(box.size * 1.3f, Vector3.one * minSize);
            s.gameObject.SetActive(false);
        }

        static LookSecret RainbowStar(ShowcaseBuildContext ctx, Transform root, LooksExperience exp, Vector3 pos)
        {
            var s = SecretRoot(root, exp, 0, "Rainbow star", pos, out Transform v);
            Color[] rainbow =
            {
                new Color(1f, 0.3f, 0.3f), new Color(1f, 0.6f, 0.2f), new Color(1f, 0.88f, 0.25f),
                new Color(0.35f, 0.85f, 0.4f), new Color(0.3f, 0.6f, 1f), new Color(0.7f, 0.4f, 1f)
            };
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f;
                var ray = ctx.Primitive(PrimitiveType.Cube, "Point " + i, v, Quaternion.Euler(0f, 0f, a) * new Vector3(0f, 0.07f, 0f),
                    new Vector3(0.07f, 0.12f, 0.05f), rainbow[i], false);
                ray.transform.localRotation = Quaternion.Euler(0f, 0f, a);
                var tip = ctx.Primitive(PrimitiveType.Cube, "Tip " + i, v, Quaternion.Euler(0f, 0f, a) * new Vector3(0f, 0.135f, 0f),
                    new Vector3(0.035f, 0.035f, 0.04f), rainbow[i], false);
                tip.transform.localRotation = Quaternion.Euler(0f, 0f, a + 45f);
            }
            ctx.Primitive(PrimitiveType.Cylinder, "Core", v, Vector3.zero, new Vector3(0.1f, 0.03f, 0.1f), rainbow[5], false)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            FinishSecret(s, 0.22f);
            return s;
        }

        static LookSecret GoldenStatue(ShowcaseBuildContext ctx, Transform root, LooksExperience exp, Vector3 pos)
        {
            var s = SecretRoot(root, exp, 1, "Golden statue", pos, out Transform v);
            s.Spin = 25f;
            s.Bob = 0f;
            var statue = ctx.Place("SM_Gen_Prop_Statue_01", v, Vector3.zero, 0.34f, 0f);
            if (statue != null)
            {
                var gold = ctx.ToonMaterial("Toon " + ColorUtility.ToHtmlStringRGB(Gold), Gold);
                foreach (var r in statue.GetComponentsInChildren<Renderer>())
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = gold;
                    r.sharedMaterials = mats;
                }
                foreach (var c in statue.GetComponentsInChildren<Collider>())
                    Object.DestroyImmediate(c);
            }
            FinishSecret(s, 0.18f);
            return s;
        }

        static LookSecret Cartridge(ShowcaseBuildContext ctx, Transform root, LooksExperience exp, Vector3 pos)
        {
            var s = SecretRoot(root, exp, 2, "Green cartridge", pos, out Transform v);
            Color body = new Color(0.36f, 0.72f, 0.3f);
            ctx.Primitive(PrimitiveType.Cube, "Body", v, Vector3.zero, new Vector3(0.2f, 0.24f, 0.045f), body, false);
            ctx.Primitive(PrimitiveType.Cube, "Grip", v, new Vector3(0f, 0.108f, 0f), new Vector3(0.2f, 0.024f, 0.055f), new Color(0.27f, 0.55f, 0.22f), false);
            ctx.Primitive(PrimitiveType.Cube, "Label", v, new Vector3(0f, 0.01f, -0.024f), new Vector3(0.15f, 0.13f, 0.006f), new Color(0.88f, 0.95f, 0.7f), false);
            ctx.Primitive(PrimitiveType.Cube, "Label Art", v, new Vector3(0f, 0.02f, -0.028f), new Vector3(0.1f, 0.07f, 0.004f), new Color(0.2f, 0.4f, 0.18f), false);
            ctx.Primitive(PrimitiveType.Cube, "Contacts", v, new Vector3(0f, -0.11f, -0.018f), new Vector3(0.14f, 0.022f, 0.012f), Gold, false);
            v.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            FinishSecret(s, 0.24f);
            return s;
        }

        static LookSecret PixelHeart(ShowcaseBuildContext ctx, Transform root, LooksExperience exp, Vector3 pos)
        {
            var s = SecretRoot(root, exp, 3, "Pixel heart", pos, out Transform v);
            s.Spin = 0f;
            string[] rows =
            {
                ".XX.XX.",
                "XWXXXXX",
                "XXXXXXX",
                ".XXXXX.",
                "..XXX..",
                "...X...",
            };
            const float px = 0.042f;
            Color red = new Color(0.95f, 0.22f, 0.3f), white = new Color(1f, 0.85f, 0.88f);
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    char ch = rows[r][c];
                    if (ch == '.') continue;
                    ctx.Primitive(PrimitiveType.Cube, "Px", v, new Vector3((c - 3) * px, (2.5f - r) * px, 0f), new Vector3(px, px, px), ch == 'W' ? white : red, false);
                }
            FinishSecret(s, 0.3f);
            return s;
        }

        static Canvas WallCanvas(Transform root, string name, Vector2 size, float width, Vector3 pos)
        {
            var c = Ui.WorldCanvas(name, root, size, width);
            c.transform.localPosition = pos;
            c.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            return c;
        }

        static Canvas BuildBoard(Transform root, out Text count, out Text[] rows, out Text now)
        {
            var board = WallCanvas(root, "Progress Board", new Vector2(900, 560), 0.95f, new Vector3(-0.86f, 0.6f, 1.0f));
            var bg = Ui.Panel(board.transform, new Color(0.13f, 0.12f, 0.2f, 0.95f), "Background");
            Ui.Stretch(bg.rectTransform);
            bg.raycastTarget = false;
            var stripe = Ui.Panel(bg.transform, Gold, "Stripe");
            Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(900, 14), Vector2.zero);
            stripe.raycastTarget = false;
            var title = Ui.Label(bg.transform, "SECRETS", 76, Gold, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
            Ui.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(500, 100), new Vector2(40, -26));
            count = Ui.Label(bg.transform, "0/4", 84, Color.white, TextAnchor.MiddleRight, FontStyle.Bold, "Count");
            Ui.Place(count.rectTransform, new Vector2(1f, 1f), new Vector2(300, 100), new Vector2(-40, -26));

            var rowBg = Ui.Panel(bg.transform, new Color(1f, 0.98f, 0.95f, 0.96f), "Rows");
            Ui.Place(rowBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(840, 320), new Vector2(0, -136));
            rowBg.raycastTarget = false;
            rows = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                var dot = Ui.Panel(rowBg.transform, LookColors[i], "Swatch " + i);
                Ui.Place(dot.rectTransform, new Vector2(0f, 1f), new Vector2(40, 40), new Vector2(24, -20 - i * 76));
                dot.raycastTarget = false;
                rows[i] = Ui.Label(rowBg.transform, LooksExperience.LookNames[i].ToUpper() + "   ? ? ?", 50, Ui.Ink, TextAnchor.MiddleLeft, FontStyle.Normal, "Row " + i);
                Ui.Place(rows[i].rectTransform, new Vector2(0f, 1f), new Vector2(740, 76), new Vector2(84, -2 - i * 76));
            }
            now = Ui.Label(bg.transform, "Now showing: <b>Toon</b>", 46, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Normal, "Now");
            Ui.Place(now.rectTransform, new Vector2(0.5f, 0f), new Vector2(840, 80), new Vector2(0, 14));
            return board;
        }

        static void BuildHeader(Transform root)
        {
            var header = WallCanvas(root, "Gallery Header", new Vector2(800, 120), 0.62f, new Vector3(0.02f, 0.86f, 1.04f));
            var t = Ui.Label(header.transform, "LUMO GALLERY", 92, WallTrim, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(t.rectTransform);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        static LookButton[] BuildDial(ShowcaseBuildContext ctx, Transform root, LooksExperience exp)
        {
            var dial = new GameObject("Style Dial").transform;
            dial.SetParent(root, false);
            dial.localPosition = new Vector3(0f, 0f, -0.68f);
            ctx.Primitive(PrimitiveType.Cube, "Plate Trim", dial, new Vector3(0f, 0.012f, -0.03f), new Vector3(2.28f, 0.024f, 0.62f), Gold, false);
            ctx.Primitive(PrimitiveType.Cube, "Plate", dial, new Vector3(0f, 0.02f, -0.03f), new Vector3(2.22f, 0.024f, 0.56f), DialPlate, false);

            var tag = Ui.WorldCanvas("Dial Label", dial, new Vector2(900, 80), 0.9f);
            tag.transform.localPosition = new Vector3(0f, 0.034f, -0.28f);
            tag.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tagText = Ui.Label(tag.transform, "STYLE DIAL  ·  TAP A LOOK", 56, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(tagText.rectTransform);
            tagText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var buttons = new LookButton[4];
            for (int i = 0; i < 4; i++)
            {
                var b = new GameObject("Look " + LooksExperience.LookNames[i]).transform;
                b.SetParent(dial, false);
                b.localPosition = new Vector3(-0.81f + i * 0.54f, 0.032f, 0.02f);
                var ring = ctx.Primitive(PrimitiveType.Cylinder, "Selected Ring", b, new Vector3(0f, 0.006f, 0f), new Vector3(0.48f, 0.006f, 0.48f), Gold, false);
                ctx.Primitive(PrimitiveType.Cylinder, "Base", b, new Vector3(0f, 0.025f, 0f), new Vector3(0.42f, 0.025f, 0.42f), new Color(0.32f, 0.3f, 0.42f), false);
                var cap = ctx.Primitive(PrimitiveType.Cylinder, "Cap", b, new Vector3(0f, 0.065f, 0f), new Vector3(0.36f, 0.02f, 0.36f), LookColors[i], false);

                var top = Ui.WorldCanvas("Cap Label", cap.transform, new Vector2(400, 160), 1f);
                top.transform.localPosition = new Vector3(0f, 1.05f, 0f);
                top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var label = Ui.Label(top.transform, LooksExperience.LookNames[i].ToUpper(), i == 2 ? 62 : 80, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                Ui.Stretch(label.rectTransform);
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.gameObject.AddComponent<Outline>().effectColor = new Color(0.1f, 0.08f, 0.15f, 0.85f);

                var box = b.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.08f, 0f);
                box.size = new Vector3(0.5f, 0.18f, 0.48f);
                var lb = b.gameObject.AddComponent<LookButton>();
                lb.Look = i;
                ShowcaseBuildContext.Set(lb, "m_owner", exp);
                ShowcaseBuildContext.Set(lb, "m_cap", cap.transform);
                ShowcaseBuildContext.Set(lb, "m_selectedRing", ring);
                ring.SetActive(i == 0);
                buttons[i] = lb;
            }
            return buttons;
        }
    }
}
