using System;
using System.Collections.Generic;
using System.Linq;
using Showcase.Experiences.Nightclub;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.EditorTools.Experiences
{
    // Hidden-Object Nightclub: a compact, dim club diorama (dance floor, DJ booth, bar, lounge)
    // with 8 lost items tucked around it, an inventory shelf along the front edge, a checklist
    // board on the back wall and a hint pad.
    [ShowcaseExperienceBuilder(3, "nightclub", "Hidden-Object Nightclub", "Find 8 hidden items\nwith your flashlight.", "SM_Prop_DJ_Turntable_02")]
    public static class NightclubBuilder
    {
        static readonly Color FloorColor = new Color(0.075f, 0.065f, 0.11f);
        static readonly Color WallColor = new Color(0.11f, 0.085f, 0.17f);
        static readonly Color RiserColor = new Color(0.16f, 0.14f, 0.22f);
        static readonly Color ShelfColor = new Color(0.2f, 0.16f, 0.3f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.22f);

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            s_ctx = ctx;
            var exp = root.gameObject.AddComponent<NightclubExperience>();
            exp.Title = "Hidden-Object Nightclub";
            exp.Hint = "Tap the 8 lost items hidden around the club";
            exp.FocusPoint = new Vector3(0f, 0.2f, 0f);
            exp.FocusDistance = 3.9f;
            exp.FocusPitch = 46f;

            var decoys = new List<GameObject>();
            GameObject Prop(string name, Vector3 pos, float height, float yaw = 0f, bool largest = false, Transform parent = null)
            {
                var go = ctx.Place(name, parent != null ? parent : root, pos, height, yaw, largest);
                if (go != null) decoys.Add(go);
                return go;
            }
            float Top(GameObject go) => go == null ? 0f : root.InverseTransformPoint(ShowcaseBuildContext.RendererBounds(go).max).y;

            // ---- room shell ----
            ctx.Floor(root, new Vector2(3.1f, 2.1f), FloorColor);
            ctx.Primitive(PrimitiveType.Cube, "Back Wall", root, new Vector3(0f, 0.5f, 1.02f), new Vector3(3.1f, 1.0f, 0.06f), WallColor);
            ctx.Primitive(PrimitiveType.Cube, "Wall Neon Strip", root, new Vector3(0f, 0.985f, 0.985f), new Vector3(3.1f, 0.025f, 0.02f), new Color(1f, 0.25f, 0.75f), false);
            ctx.Primitive(PrimitiveType.Cube, "DJ Riser", root, new Vector3(0f, 0.03f, 0.72f), new Vector3(1.66f, 0.06f, 0.5f), RiserColor, false);

            // ---- dance floor (tiles animate in time with the beat) ----
            var dance = new GameObject("Dance Floor").transform;
            dance.SetParent(root, false);
            var tiles = new List<Renderer>();
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 4; c++)
                {
                    var t = ctx.Primitive(PrimitiveType.Cube, "Tile " + r + c, dance, new Vector3(-0.42f + c * 0.28f, 0.006f, -0.43f + r * 0.28f), new Vector3(0.26f, 0.012f, 0.26f), new Color(0.55f, 0.2f, 0.85f), false);
                    tiles.Add(t.GetComponent<Renderer>());
                }
            exp.DanceTiles = tiles.ToArray();
            exp.DanceColors = new[] { new Color(1f, 0.25f, 0.7f), new Color(0.2f, 0.85f, 1f), new Color(0.6f, 0.3f, 1f), new Color(1f, 0.8f, 0.25f) };

            // ---- DJ booth ----
            var booth = Prop("SM_Prop_DJ_Booth_01", new Vector3(0f, 0.06f, 0.7f), 0.34f);
            float boothTop = Top(booth);
            var speakerL = Prop("SM_Prop_Speaker_01", new Vector3(-0.64f, 0.06f, 0.74f), 0.42f, 15f);
            var speakerR = Prop("SM_Prop_Speaker_01", new Vector3(0.64f, 0.06f, 0.74f), 0.42f, -15f);
            Prop("SM_Prop_DJ_Turntable_02", new Vector3(0.16f, boothTop, 0.66f), 0.16f, 0f, true);

            // ---- bar corner ----
            var bar = Prop("SM_Prop_Bar_01", new Vector3(1.12f, 0f, 0.5f), 0.36f);
            float barTop = Top(bar);
            Prop("SM_Prop_Bar_Bottle_01", new Vector3(0.96f, barTop, 0.5f), 0.12f);
            Prop("SM_Prop_Bar_Bottle_04", new Vector3(1.18f, barTop, 0.46f), 0.13f);
            Prop("SM_Prop_Bar_Glass_05", new Vector3(1.05f, barTop, 0.44f), 0.07f);
            Prop("SM_Prop_Stool_01", new Vector3(0.94f, 0f, 0.18f), 0.25f);
            Prop("SM_Prop_Stool_01", new Vector3(1.3f, 0f, 0.18f), 0.25f, 20f);
            Prop("SM_Prop_Sign_Bar_01", new Vector3(1.12f, 0.55f, 0.98f), 0.42f, -90f, true);

            // ---- lounge ----
            Prop("SM_Prop_Sofa_01", new Vector3(-1.05f, 0f, 0.62f), 0.28f);
            var table = Prop("SM_Prop_Table_05", new Vector3(-1.0f, 0f, 0.14f), 0.14f);
            float tableTop = Top(table);
            Prop("SM_Prop_Plant_Bush_01", new Vector3(-1.4f, 0f, 0.84f), 0.32f);
            Prop("SM_Prop_Beanbag_01", new Vector3(-1.18f, 0f, -0.42f), 0.16f, 25f);
            Prop("SM_Prop_Sign_Club_Moon_01", new Vector3(-1.05f, 0.55f, 0.98f), 0.32f, 0f, true);
            Prop("SM_Prop_Floor_Light_01", new Vector3(0.8f, 0f, -0.45f), 0.14f, 0f, true);
            Prop("SM_Prop_Floor_Light_01", new Vector3(-0.72f, 0f, -0.5f), 0.14f, 0f, true);

            foreach (var d in decoys)
            {
                if (d.GetComponentsInChildren<Collider>().Length == 0) { d.AddComponent<BoxCollider>(); ShowcaseBuildContext.FitBoxCollider(d); }
                d.AddComponent<NightclubDecoy>().Game = exp;
            }
            exp.Decoys = decoys.Select(d => d.GetComponent<NightclubDecoy>()).ToArray();

            // ---- hidden items ----
            float speakerTop = Top(speakerR);
            var items = new List<NightclubItem>
            {
                Item(ctx, root, exp, "VIP Wristband", "Nova Night · VIP wristband", "Front-of-line access,\nall night long.", "check the bar top",
                    new Color(0.7f, 0.45f, 1f), new Vector3(1.28f, barTop, 0.52f), 30f,
                    c => ctx.Place("SM_Prop_Bracelets_01", c, Vector3.zero, 0.11f, 0f, true)),
                Item(ctx, root, exp, "Lost Phone", "Lumo Mobile · Pulse X", "Battery at 4%.\nSomeone is frantic.", "look under the lounge table",
                    new Color(0.3f, 0.85f, 1f), new Vector3(-0.9f, 0f, 0.1f), -35f,
                    c => ctx.Place("SM_Prop_Cellphone_01", c, Vector3.zero, 0.11f, 0f, true)),
                Item(ctx, root, exp, "Sunglasses", "Halo Shades · Midnight", "Sunglasses at night?\nVery on brand.", "something is on the DJ booth",
                    new Color(1f, 0.35f, 0.7f), new Vector3(-0.2f, boothTop, 0.62f), 20f,
                    c => ctx.Place("SM_Prop_Glasses_01", c, Vector3.zero, 0.12f, 0f, true)),
                Item(ctx, root, exp, "Golden Ticket", "Nova Night · Golden ticket", "One free encore,\nredeemable at the booth.", "peek between the bar stools",
                    Gold, new Vector3(1.12f, 0f, 0.22f), -10f, Ticket, Quaternion.Euler(35f, 0f, 0f)),
                Item(ctx, root, exp, "Neon Fizz", "Lumo Lounge · Neon Fizz", "Yuzu, mint and a\nsplash of glow.", "the lounge table looks busy",
                    new Color(0.4f, 1f, 0.5f), new Vector3(-1.1f, tableTop, 0.22f), 0f,
                    c => ctx.Place("SM_Prop_Tiki_Cup_01", c, Vector3.zero, 0.11f)),
                Item(ctx, root, exp, "Vinyl Record", "Orbit Records · Night Drive EP", "Limited pressing,\nonly 500 made.", "try beside the DJ booth",
                    new Color(1f, 0.3f, 0.3f), new Vector3(-0.46f, 0.06f, 0.56f), 20f, Record, Quaternion.Euler(12f, 0f, 0f)),
                Item(ctx, root, exp, "Coat-check Key", "Nova Night · Coat check #27", "Somebody's jacket\nis waiting.", "something glints by the beanbag",
                    new Color(1f, 0.6f, 0.2f), new Vector3(-1.04f, 0f, -0.48f), 40f, Key, Quaternion.identity, Quaternion.Euler(-70f, 0f, 0f)),
                Item(ctx, root, exp, "Party Crown", "Glitz Co. · Birthday crown", "Tonight's birthday star\nlost their crown.", "look up on the right speaker",
                    Gold, new Vector3(0.64f, speakerTop, 0.74f), 15f, Crown),
            };
            exp.Items = items.ToArray();

            // ---- inventory shelf ----
            ctx.Primitive(PrimitiveType.Cube, "Inventory Shelf", root, new Vector3(-0.15f, 0.025f, -0.86f), new Vector3(2.5f, 0.05f, 0.18f), ShelfColor, false);
            ctx.Primitive(PrimitiveType.Cube, "Shelf Neon Edge", root, new Vector3(-0.15f, 0.04f, -0.952f), new Vector3(2.5f, 0.02f, 0.012f), new Color(0.25f, 0.9f, 1f), false);
            var slots = new Transform[8];
            for (int i = 0; i < 8; i++)
            {
                var slot = new GameObject("Shelf Slot " + (i + 1)).transform;
                slot.SetParent(root, false);
                slot.localPosition = new Vector3(-1.2f + i * 0.3f, 0.055f, -0.86f);
                ctx.Primitive(PrimitiveType.Cylinder, "Pad", slot, new Vector3(0f, -0.002f, 0f), new Vector3(0.16f, 0.004f, 0.16f), new Color(0.32f, 0.26f, 0.48f), false);
                slots[i] = slot;
            }
            exp.ShelfSlots = slots;

            // ---- hint pad ----
            var hint = new GameObject("Hint Button").transform;
            hint.SetParent(root, false);
            hint.localPosition = new Vector3(1.3f, 0f, -0.84f);
            ctx.Primitive(PrimitiveType.Cylinder, "Base", hint, new Vector3(0f, 0.02f, 0f), new Vector3(0.24f, 0.02f, 0.24f), new Color(0.2f, 0.16f, 0.3f), false);
            ctx.Primitive(PrimitiveType.Cylinder, "Cap", hint, new Vector3(0f, 0.05f, 0f), new Vector3(0.19f, 0.015f, 0.19f), new Color(1f, 0.85f, 0.25f), false);
            var hintCol = hint.gameObject.AddComponent<BoxCollider>();
            hintCol.center = new Vector3(0f, 0.06f, 0f);
            hintCol.size = new Vector3(0.28f, 0.14f, 0.28f);
            hint.gameObject.AddComponent<NightclubHintButton>().Game = exp;
            var hintCanvas = Ui.WorldCanvas("Label", hint, new Vector2(300, 110), 0.26f);
            hintCanvas.transform.localPosition = new Vector3(0f, 0.085f, 0f);
            hintCanvas.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            var hintText = Ui.Label(hintCanvas.transform, "HINT", 80, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(hintText.rectTransform);

            BuildBoard(root, exp);
            BuildLights(root, exp);
        }

        // Container with its pivot at the bottom centre of the visual and a finger-sized collider.
        static NightclubItem Item(ShowcaseBuildContext ctx, Transform root, NightclubExperience exp, string title, string brand, string blurb, string hint,
            Color color, Vector3 pos, float yaw, Action<Transform> visual, Quaternion? tilt = null, Quaternion? shelfRotation = null)
        {
            var c = new GameObject("Item " + title).transform;
            c.SetParent(root, false);
            c.localPosition = pos;
            c.localRotation = Quaternion.Euler(0f, yaw, 0f) * (tilt ?? Quaternion.identity);
            visual(c);
            foreach (var col in c.GetComponentsInChildren<Collider>()) col.enabled = false;
            c.gameObject.AddComponent<BoxCollider>();
            var box = ShowcaseBuildContext.FitBoxCollider(c.gameObject, 1.25f);
            box.enabled = true;
            box.size = Vector3.Max(box.size, new Vector3(0.16f, 0.12f, 0.16f));
            var item = c.gameObject.AddComponent<NightclubItem>();
            item.Game = exp;
            item.DisplayName = title;
            item.Brand = brand;
            item.Blurb = blurb;
            item.HintText = hint;
            item.Color = color;
            item.HomeParent = root;
            item.HomePosition = c.localPosition;
            item.HomeRotation = c.localRotation;
            item.HomeScale = c.localScale;
            item.ShelfRotation = shelfRotation ?? Quaternion.identity;
            return item;
        }

        static ShowcaseBuildContext s_ctx;

        static void Ticket(Transform c)
        {
            var ctx = Ctx();
            ctx.Primitive(PrimitiveType.Cube, "Card", c, new Vector3(0f, 0.045f, 0f), new Vector3(0.15f, 0.09f, 0.008f), Gold, false);
            ctx.Primitive(PrimitiveType.Cube, "Stripe", c, new Vector3(0f, 0.045f, -0.005f), new Vector3(0.15f, 0.022f, 0.004f), new Color(0.75f, 0.25f, 0.55f), false);
            ctx.Primitive(PrimitiveType.Cube, "Stub", c, new Vector3(0.05f, 0.045f, -0.005f), new Vector3(0.006f, 0.09f, 0.004f), new Color(0.6f, 0.45f, 0.1f), false);
        }

        static void Record(Transform c)
        {
            var ctx = Ctx();
            var disc = ctx.Primitive(PrimitiveType.Cylinder, "Disc", c, new Vector3(0f, 0.08f, 0f), new Vector3(0.16f, 0.004f, 0.16f), new Color(0.05f, 0.05f, 0.07f), false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var label = ctx.Primitive(PrimitiveType.Cylinder, "Label", c, new Vector3(0f, 0.08f, -0.002f), new Vector3(0.06f, 0.004f, 0.06f), new Color(1f, 0.3f, 0.3f), false);
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        static void Key(Transform c)
        {
            var ctx = Ctx();
            var orange = new Color(1f, 0.6f, 0.2f);
            ctx.Primitive(PrimitiveType.Cylinder, "Bow", c, new Vector3(-0.045f, 0.008f, 0f), new Vector3(0.055f, 0.008f, 0.055f), Gold, false);
            ctx.Primitive(PrimitiveType.Cylinder, "Hole", c, new Vector3(-0.045f, 0.012f, 0f), new Vector3(0.022f, 0.008f, 0.022f), FloorColor, false);
            ctx.Primitive(PrimitiveType.Cube, "Shaft", c, new Vector3(0.02f, 0.008f, 0f), new Vector3(0.09f, 0.012f, 0.016f), Gold, false);
            ctx.Primitive(PrimitiveType.Cube, "Tooth 1", c, new Vector3(0.05f, 0.008f, -0.014f), new Vector3(0.012f, 0.012f, 0.02f), Gold, false);
            ctx.Primitive(PrimitiveType.Cube, "Tooth 2", c, new Vector3(0.03f, 0.008f, -0.012f), new Vector3(0.01f, 0.012f, 0.014f), Gold, false);
            ctx.Primitive(PrimitiveType.Cube, "Tag", c, new Vector3(-0.09f, 0.006f, 0f), new Vector3(0.04f, 0.008f, 0.03f), orange, false);
        }

        static void Crown(Transform c)
        {
            var ctx = Ctx();
            ctx.Primitive(PrimitiveType.Cylinder, "Band", c, new Vector3(0f, 0.028f, 0f), new Vector3(0.11f, 0.028f, 0.11f), Gold, false);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                var p = new Vector3(Mathf.Sin(a) * 0.048f, 0.07f, Mathf.Cos(a) * 0.048f);
                var spike = ctx.Primitive(PrimitiveType.Cube, "Point " + i, c, p, new Vector3(0.024f, 0.04f, 0.024f), Gold, false);
                spike.transform.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 45f);
                ctx.Primitive(PrimitiveType.Sphere, "Gem " + i, c, p + Vector3.up * 0.026f, Vector3.one * 0.018f, new Color(1f, 0.3f, 0.6f), false);
            }
        }

        static ShowcaseBuildContext Ctx() => s_ctx;

        // Checklist board on the back wall: header plus 8 silhouettes that light up when found.
        static void BuildBoard(Transform root, NightclubExperience exp)
        {
            var anchor = new GameObject("Checklist Board").transform;
            anchor.SetParent(root, false);
            anchor.localPosition = new Vector3(0f, 0.765f, 0.98f);
            anchor.localRotation = Quaternion.Euler(12f, 0f, 0f);
            var canvas = Ui.WorldCanvas("Canvas", anchor, new Vector2(1100, 440), 1.15f);
            var bg = Ui.Panel(canvas.transform, new Color(0.06f, 0.05f, 0.1f, 0.94f), "Background");
            Ui.Stretch(bg.rectTransform);
            var frame = Ui.Panel(bg.transform, new Color(1f, 0.25f, 0.75f), "Top Neon");
            Ui.Place(frame.rectTransform, new Vector2(0.5f, 1f), new Vector2(1100, 10), Vector2.zero);
            var frame2 = Ui.Panel(bg.transform, new Color(0.25f, 0.9f, 1f), "Bottom Neon");
            Ui.Place(frame2.rectTransform, new Vector2(0.5f, 0f), new Vector2(1100, 10), Vector2.zero);
            // World canvases report "pointer over UI" even when 3D props sit in front of them, so a
            // raycast-target board would swallow taps on every item drawn over it.
            bg.raycastTarget = frame.raycastTarget = frame2.raycastTarget = false;

            exp.BoardHeader = Ui.Label(bg.transform, "LOST & FOUND   0 / 8", 76, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Header");
            Ui.Place(exp.BoardHeader.rectTransform, new Vector2(0.5f, 1f), new Vector2(1060, 90), new Vector2(0, -20));

            var icons = new Image[8];
            var labels = new Text[8];
            for (int i = 0; i < 8; i++)
            {
                int col = i % 4, row = i / 4;
                var cell = Ui.Rect("Slot " + (i + 1), bg.transform);
                Ui.Place(cell, new Vector2(0f, 1f), new Vector2(255, 150), new Vector2(22 + col * 266, -118 - row * 158));
                icons[i] = Ui.Panel(cell, new Color(0.22f, 0.2f, 0.32f), "Icon");
                Ui.Place(icons[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(64, 64), new Vector2(0, -14));
                icons[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                icons[i].raycastTarget = false;
                labels[i] = Ui.Label(cell, "? ? ?", 46, new Color(0.6f, 0.58f, 0.75f), TextAnchor.MiddleCenter, FontStyle.Bold, "Name");
                Ui.Place(labels[i].rectTransform, new Vector2(0.5f, 0f), new Vector2(255, 60), new Vector2(0, 0));
                labels[i].verticalOverflow = VerticalWrapMode.Truncate;
                labels[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                labels[i].resizeTextForBestFit = true;
                labels[i].resizeTextMinSize = 24;
                labels[i].resizeTextMaxSize = 46;
            }
            exp.BoardIcons = icons;
            exp.BoardLabels = labels;
        }

        static void BuildLights(Transform root, NightclubExperience exp)
        {
            float s = root.lossyScale.x;
            Light Make(string name, LightType type, Vector3 pos, Color color, float intensity, float range)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos;
                var l = go.AddComponent<Light>();
                l.type = type;
                l.color = color;
                // Point/spot light falls off with the square of world distance, so scale with World.
                l.intensity = intensity * s * s;
                l.range = range * s;
                l.shadows = LightShadows.None;
                return l;
            }
            var flash = Make("Flashlight", LightType.Spot, new Vector3(0f, 1.7f, -1.6f), new Color(1f, 0.95f, 0.82f), 1.0f, 4.5f);
            flash.spotAngle = 30f;
            flash.innerSpotAngle = 16f;
            flash.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0f, 0.2f) - flash.transform.localPosition);
            exp.Flashlight = flash;
            exp.ClubLights = new[]
            {
                Make("Dance Light", LightType.Point, new Vector3(0f, 0.6f, -0.15f), new Color(1f, 0.3f, 0.8f), 0.22f, 1.1f),
                Make("DJ Light", LightType.Point, new Vector3(0f, 0.65f, 0.45f), new Color(0.3f, 0.8f, 1f), 0.25f, 1.0f),
                Make("Bar Light", LightType.Point, new Vector3(1.0f, 0.6f, 0.2f), new Color(1f, 0.7f, 0.3f), 0.22f, 0.9f),
            };
        }
    }
}
