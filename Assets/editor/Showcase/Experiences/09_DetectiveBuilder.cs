using System.Collections.Generic;
using System.Linq;
using Showcase.Experiences.Detective;
using UnityEngine;

namespace Showcase.EditorTools.Experiences
{
    // Kitchen Detective: a cosy cut-away kitchen (floor, back and left wall) with a counter run and a
    // freezer along the back, a table in the middle, Bolt's charging dock on the right, four hidden
    // clues, a "Case notes" notebook on the back wall and a three-suspect line-up at the front edge.
    [ShowcaseExperienceBuilder(9, "detective", "Kitchen Detective", "Who ate the last ice cream?\nFind 4 clues.", "food_icecream_cone_vanilla")]
    public static class DetectiveBuilder
    {
        static readonly Color FloorTile = new Color(0.8f, 0.6f, 0.5f);
        static readonly Color BackWall = new Color(0.55f, 0.78f, 0.78f);
        static readonly Color SideWall = new Color(0.98f, 0.86f, 0.55f);
        static readonly Color Trim = new Color(0.98f, 0.97f, 0.94f);
        static readonly Color BoardWood = new Color(0.45f, 0.3f, 0.22f);
        static readonly Color LineUp = new Color(0.36f, 0.3f, 0.46f);
        static readonly Color Glow = new Color(1f, 0.88f, 0.35f);
        static readonly Color Pink = new Color(1f, 0.55f, 0.72f);
        static readonly Color Cream = new Color(1f, 0.95f, 0.82f);
        static readonly Color Oil = new Color(0.1f, 0.1f, 0.15f);
        static readonly Color Dark = new Color(0.12f, 0.1f, 0.14f);

        const float WallHeight = 1.0f;
        const float BackWallZ = 0.97f;
        const float LeftWallX = -1.47f;
        const float WallThickness = 0.06f;
        const float BackFace = BackWallZ - WallThickness * 0.5f;
        const float CounterHeight = 0.36f;
        const float CounterDepth = 0.42f;
        const float SuspectScale = 1.25f;

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            ctx.Floor(root, new Vector2(3.0f, 2.0f), FloorTile);
            BuildShell(ctx, root);

            var props = new List<DetectiveProp>();
            var clues = new DetectiveClue[4];

            // ---- Back row: freezer, then a counter run up to the right wall edge ----
            clues[1] = BuildFreezer(ctx, root);

            float x = -0.95f;
            var counterA = RowPiece(ctx, root, "kitchencounter_straight_A", ref x);
            var sink = RowPiece(ctx, root, "kitchencounter_sink", ref x);
            var stove = RowPiece(ctx, root, "stove_single", ref x);
            var counterB = RowPiece(ctx, root, "kitchencounter_straight_B", ref x);
            ShowcaseBuildContext.Log("detective counter run ends at x " + x.ToString("0.00"));
            props.Add(Prop(counterA, "A spotless counter. Well, almost..."));
            props.Add(Prop(sink, "The sink. Only a lonely spoon."));
            props.Add(Prop(stove, "The stove is cold. No cooking tonight."));
            props.Add(Prop(counterB, "Just a counter. Very flat."));

            float counterTop = Top(root, counterA);
            var pan = ctx.Place("pan_A", root, new Vector3(Centre(root, stove).x, counterTop, Centre(root, stove).z), 0.07f, 30f);
            props.Add(Prop(pan, "An empty pan. It saw nothing."));
            var jar = ctx.Place("jar_B_medium", root, new Vector3(Centre(root, counterB).x, Top(root, counterB), Centre(root, counterB).z + 0.05f), 0.12f);
            props.Add(Prop(jar, "A jar of sprinkles. All present."));

            clues[0] = BuildCrumbs(ctx, root, new Vector3(Centre(root, counterA).x, counterTop, Centre(root, counterA).z - 0.03f));

            // ---- Middle: kitchen table with the empty tub ----
            var table = ctx.Place("kitchentable_A", root, new Vector3(-0.2f, 0f, 0.12f), 0.3f);
            props.Add(Prop(table, "The kitchen table. Wobbly, but honest."));
            float tableTop = Top(root, table);
            var tub = ctx.Place("icecream_container", root, new Vector3(-0.3f, tableTop, 0.1f), 0.11f, 15f);
            props.Add(Prop(tub, "The empty Nova tub. We knew that."));
            var bowl = ctx.Place("bowl", root, new Vector3(-0.08f, tableTop, 0.18f), 0.05f);
            props.Add(Prop(bowl, "A clean bowl. Not even licked."));
            var knife = ctx.Place("knife", root, new Vector3(-0.1f, tableTop, 0.02f), 0.16f, 70f, true);
            props.Add(Prop(knife, "Just a knife. Sharp, but innocent."));

            // ---- Sides: Mochi's basket, Bolt's dock, crates ----
            var basket = ctx.Place("Food_Basket_A_Empty", root, new Vector3(-1.15f, 0f, -0.12f), 0.14f, 20f);
            props.Add(Prop(basket, "Mochi's basket. Only fluff."));
            var crate = ctx.Place("crate_carrots", root, new Vector3(1.28f, 0f, -0.18f), 0.16f, -15f);
            props.Add(Prop(crate, "Carrots. Nobody steals carrots."));
            var dock = BuildDock(ctx, root, new Vector3(1.22f, 0f, 0.33f));
            props.Add(Prop(dock, "Bolt's dock: 100% charged. Hmm."));

            clues[2] = BuildDrip(ctx, root, new Vector3(0.9f, 0f, 0.24f));
            clues[3] = BuildOil(ctx, root, new Vector3(-0.78f, 0f, 0.4f));

            // ---- Notebook on the back wall, above the right-hand counters ----
            var board = ctx.Primitive(PrimitiveType.Cube, "Notebook Board", root, new Vector3(0.75f, 0.68f, BackFace - 0.015f), new Vector3(0.98f, 0.54f, 0.03f), BoardWood, false);
            // Tapping the notebook opens the case file; a thick box so it's easy to hit.
            var boardBox = board.AddComponent<BoxCollider>();
            boardBox.size = new Vector3(1f, 1f, 3f);
            board.AddComponent<DetectiveNotebook>();

            // ---- Front line-up ----
            ctx.Primitive(PrimitiveType.Cube, "Line-up Mat", root, new Vector3(0f, 0.004f, -0.62f), new Vector3(2.3f, 0.008f, 0.4f), LineUp, false);
            var suspects = new[]
            {
                BuildChef(ctx, root, new Vector3(-0.72f, 0f, -0.62f)),
                BuildCat(ctx, root, new Vector3(0f, 0f, -0.62f)),
                BuildRobot(ctx, root, new Vector3(0.72f, 0f, -0.62f)),
            };

            var marker = BuildMagnifier(ctx, root);

            var exp = root.gameObject.AddComponent<DetectiveExperience>();
            exp.Title = "Kitchen Detective";
            exp.Hint = "Tap around the kitchen to find 4 clues · then accuse a suspect";
            exp.FocusPoint = new Vector3(0f, 0.25f, 0.02f);
            exp.FocusDistance = 3.8f;
            exp.FocusPitch = 46f;
            ShowcaseBuildContext.Set(exp, "m_clues", clues);
            ShowcaseBuildContext.Set(exp, "m_suspects", suspects);
            ShowcaseBuildContext.Set(exp, "m_props", props.Where(p => p != null).ToArray());
            ShowcaseBuildContext.Set(exp, "m_board", board.transform);
            ShowcaseBuildContext.Set(exp, "m_markerTemplate", marker);
            if (tub != null) ShowcaseBuildContext.Set(exp, "m_caseModel", tub);
            ShowcaseBuildContext.Log("detective: " + clues.Count(c => c != null) + " clues, " + props.Count(p => p != null) + " props, " + suspects.Length + " suspects");
        }

        // ---- Shell ----

        static void BuildShell(ShowcaseBuildContext ctx, Transform root)
        {
            const float sideLength = 1.66f;
            float sideZ = BackWallZ + WallThickness * 0.5f - sideLength * 0.5f;
            ctx.Primitive(PrimitiveType.Cube, "Back Wall", root, new Vector3(0f, WallHeight * 0.5f, BackWallZ), new Vector3(3.0f, WallHeight, WallThickness), BackWall, false);
            ctx.Primitive(PrimitiveType.Cube, "Left Wall", root, new Vector3(LeftWallX, WallHeight * 0.5f, sideZ), new Vector3(WallThickness, WallHeight, sideLength), SideWall, false);
            ctx.Primitive(PrimitiveType.Cube, "Back Cap", root, new Vector3(0f, WallHeight + 0.01f, BackWallZ), new Vector3(3.02f, 0.02f, WallThickness + 0.02f), Trim, false);
            ctx.Primitive(PrimitiveType.Cube, "Left Cap", root, new Vector3(LeftWallX, WallHeight + 0.01f, sideZ), new Vector3(WallThickness + 0.02f, 0.02f, sideLength + 0.02f), Trim, false);
            ctx.Primitive(PrimitiveType.Cube, "Left Skirting", root, new Vector3(LeftWallX + WallThickness * 0.5f + 0.01f, 0.035f, sideZ - 0.01f), new Vector3(0.02f, 0.07f, sideLength - 0.02f), Trim, false);

        }

        // ---- Clues ----

        static DetectiveClue Clue(Transform root, string name, Vector3 pos, string title, string body, string note)
        {
            var go = new GameObject("Clue " + name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            var clue = go.AddComponent<DetectiveClue>();
            clue.Title = title;
            clue.Body = body;
            clue.NoteLine = note;
            return clue;
        }

        static void FinishClue(DetectiveClue clue, float padding = 1.3f, float minSize = 0.18f)
        {
            var box = ShowcaseBuildContext.FitBoxCollider(clue.gameObject, padding);
            var size = box.size;
            box.size = new Vector3(Mathf.Max(size.x, minSize), Mathf.Max(size.y, minSize * 0.6f), Mathf.Max(size.z, minSize));
            box.center = new Vector3(box.center.x, Mathf.Max(box.center.y, box.size.y * 0.5f - 0.01f), box.center.z);
            float top = 0f;
            foreach (var r in clue.GetComponentsInChildren<Renderer>())
                top = Mathf.Max(top, clue.transform.InverseTransformPoint(r.bounds.max).y);
            clue.CardHeight = top + 0.02f;
        }

        static DetectiveClue BuildCrumbs(ShowcaseBuildContext ctx, Transform root, Vector3 pos)
        {
            var clue = Clue(root, "Crumbs", pos, "Pink Crumbs",
                "A trail of pink waffle crumbs\nruns off the counter edge...\n<color=#FF7361><b>Nova Strawberry Crunch:</b>\nreal waffle in every bite.</color>",
                "Pink crumbs on the counter");
            var t = clue.transform;
            ctx.Place("icecream_waffle", t, new Vector3(0.05f, 0f, 0.02f), 0.1f, 25f, true);
            var rng = new System.Random(9);
            // Crumbs trail from the waffle toward the front edge of the counter.
            for (int i = 0; i < 11; i++)
            {
                float k = i / 10f;
                var p = new Vector3(0.0f - k * 0.14f + ((float)rng.NextDouble() - 0.5f) * 0.05f, 0.01f, -0.02f - k * 0.13f + ((float)rng.NextDouble() - 0.5f) * 0.03f);
                float s = 0.024f + (float)rng.NextDouble() * 0.014f;
                ctx.Primitive(PrimitiveType.Sphere, "Crumb", t, p, new Vector3(s, s * 0.7f, s), Pink, false);
            }
            FinishClue(clue, 1.4f, 0.2f);
            return clue;
        }

        static DetectiveClue BuildFreezer(ShowcaseBuildContext ctx, Transform root)
        {
            var clue = Clue(root, "Freezer", new Vector3(-1.18f, 0f, 0.72f), "Open Freezer",
                "Someone left the freezer\nwide open... and the handle\nhas a round magnet mark.\n<color=#FF7361><b>Nova</b> stays creamy at -18°C\nfor 72 hours.</color>",
                "Freezer left open (magnet mark)");
            var fridge = ctx.Place("Fridge_03", clue.transform, Vector3.zero, 0.8f);
            if (fridge != null)
            {
                foreach (var c in fridge.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);
                var b = LocalBounds(root, fridge);
                fridge.transform.position += root.TransformVector(new Vector3(0f, 0f, BackFace - 0.01f - b.max.z));
                var door = fridge.GetComponentsInChildren<Transform>(true).FirstOrDefault(tr => tr != fridge.transform && tr.name.Contains("Door"));
                if (door != null)
                {
                    clue.Animated = door;
                    clue.ClosedRotation = door.localRotation;
                    clue.OpenRotation = door.localRotation * Quaternion.Euler(0f, -105f, 0f);
                }
                else
                {
                    ShowcaseBuildContext.Log("ERROR detective: Fridge_03 has no door child");
                }
                // A frosty ice cream tub left inside: visible once the door swings open.
                var inside = ctx.Place("icecream_container_icecream_vanilla", clue.transform, new Vector3(0f, 0.47f, 0.0f), 0.08f);
                if (inside != null)
                {
                    var ib = LocalBounds(root, inside);
                    var fb = LocalBounds(root, fridge);
                    inside.transform.position += root.TransformVector(new Vector3(0f, 0f, (fb.min.z + 0.12f) - ib.center.z));
                }
            }
            FinishClue(clue, 1.05f);
            return clue;
        }

        static DetectiveClue BuildDrip(ShowcaseBuildContext ctx, Transform root, Vector3 pos)
        {
            var clue = Clue(root, "Drip", pos, "Melted Drip",
                "A puddle of melted vanilla\nand an empty cone, right next\nto the robot charging dock!\n<color=#FF7361><b>Nova Vanilla Cloud:</b>\nmade with real Madagascar\nvanilla.</color>",
                "Melted drip by the robot dock");
            var t = clue.transform;
            ctx.Primitive(PrimitiveType.Cylinder, "Puddle", t, new Vector3(0f, 0.004f, 0f), new Vector3(0.17f, 0.004f, 0.12f), Cream, false);
            ctx.Primitive(PrimitiveType.Cylinder, "Puddle Blob", t, new Vector3(0.08f, 0.005f, 0.05f), new Vector3(0.07f, 0.004f, 0.06f), Cream, false);
            ctx.Primitive(PrimitiveType.Cylinder, "Drop", t, new Vector3(0.15f, 0.004f, 0.08f), new Vector3(0.035f, 0.004f, 0.035f), Cream, false);
            var cone = ctx.Place("icecream_cone", t, new Vector3(-0.03f, 0f, -0.01f), 0.15f);
            if (cone != null)
            {
                cone.transform.localRotation = Quaternion.Euler(0f, 0f, 75f) * cone.transform.localRotation;
                var b = LocalBounds(t, cone);
                cone.transform.position += t.TransformVector(new Vector3(-0.03f - b.center.x, 0.006f - b.min.y, -0.01f - b.center.z));
            }
            FinishClue(clue, 1.3f, 0.22f);
            return clue;
        }

        static DetectiveClue BuildOil(ShowcaseBuildContext ctx, Transform root, Vector3 pos)
        {
            var clue = Clue(root, "Oil", pos, "Oil Drops",
                "Little drops of machine oil\nlead away from the freezer.\nChefs and cats don't leak oil!\n<color=#FF7361>Clean hands, happy scoops:\n<b>Nova</b> cones come wrapped.</color>",
                "Oil drops by the freezer");
            var t = clue.transform;
            ctx.Primitive(PrimitiveType.Cylinder, "Oil Puddle", t, new Vector3(0f, 0.004f, 0f), new Vector3(0.12f, 0.004f, 0.09f), Oil, false);
            ctx.Primitive(PrimitiveType.Sphere, "Sheen", t, new Vector3(-0.02f, 0.008f, 0.015f), new Vector3(0.035f, 0.004f, 0.02f), new Color(0.5f, 0.55f, 0.8f), false);
            for (int i = 0; i < 4; i++)
            {
                float s = 0.04f - i * 0.006f;
                ctx.Primitive(PrimitiveType.Cylinder, "Oil Drop", t, new Vector3(0.1f + i * 0.08f, 0.004f, -0.03f - i * 0.04f), new Vector3(s, 0.004f, s), Oil, false);
            }
            FinishClue(clue, 1.3f, 0.22f);
            return clue;
        }

        // ---- Props ----

        static DetectiveProp Prop(GameObject go, string quip)
        {
            if (go == null) return null;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
            ShowcaseBuildContext.FitBoxCollider(go, 1.15f);
            var p = go.AddComponent<DetectiveProp>();
            p.Quip = quip;
            return p;
        }

        // Places a back-row piece against the wall, its left edge at x; advances x to its right edge.
        static GameObject RowPiece(ShowcaseBuildContext ctx, Transform root, string prefab, ref float x)
        {
            var go = ctx.Place(prefab, root, new Vector3(x, 0f, 0.7f), CounterHeight);
            if (go == null) return null;
            // KayKit counters are deep restaurant units; slim them so the kitchen floor stays open.
            var b = LocalBounds(root, go);
            float sx = b.size.x > 0.62f ? 0.9f : 1f;
            float sz = Mathf.Min(1f, CounterDepth / b.size.z);
            var ls = go.transform.localScale;
            go.transform.localScale = new Vector3(ls.x * sx, ls.y, ls.z * sz);
            b = LocalBounds(root, go);
            go.transform.position += root.TransformVector(new Vector3(x - b.min.x, 0f, BackFace - 0.005f - b.max.z));
            x += b.size.x;
            ShowcaseBuildContext.Log("detective row " + prefab + " size " + b.size.ToString("0.00"));
            return go;
        }

        static GameObject BuildDock(ShowcaseBuildContext ctx, Transform root, Vector3 pos)
        {
            var dock = new GameObject("Charging Dock");
            dock.transform.SetParent(root, false);
            dock.transform.localPosition = pos;
            var t = dock.transform;
            ctx.Primitive(PrimitiveType.Cube, "Base", t, new Vector3(0f, 0.015f, 0f), new Vector3(0.24f, 0.03f, 0.2f), new Color(0.3f, 0.32f, 0.4f), false);
            ctx.Primitive(PrimitiveType.Cylinder, "Pad", t, new Vector3(0f, 0.032f, -0.01f), new Vector3(0.16f, 0.004f, 0.16f), new Color(0.35f, 0.9f, 0.6f), false);
            ctx.Primitive(PrimitiveType.Cube, "Post", t, new Vector3(0f, 0.15f, 0.08f), new Vector3(0.06f, 0.26f, 0.04f), new Color(0.3f, 0.32f, 0.4f), false);
            ctx.Primitive(PrimitiveType.Cube, "Screen", t, new Vector3(0f, 0.24f, 0.058f), new Vector3(0.045f, 0.06f, 0.005f), new Color(0.35f, 0.9f, 0.6f), false);
            ctx.Primitive(PrimitiveType.Sphere, "Light", t, new Vector3(0f, 0.3f, 0.08f), new Vector3(0.04f, 0.04f, 0.04f), new Color(1f, 0.35f, 0.3f), false);
            return dock;
        }

        // ---- Suspects (toon figures from primitives) ----

        static DetectiveSuspect Suspect(ShowcaseBuildContext ctx, Transform root, string name, Vector3 pos, float headTop, string alibi, string denial, string hint, bool culprit)
        {
            var go = new GameObject("Suspect " + name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            var glow = ctx.Primitive(PrimitiveType.Cylinder, "Glow", go.transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.4f, 0.003f, 0.4f), Glow, false);
            glow.SetActive(false);
            var body = new GameObject("Body").transform;
            body.SetParent(go.transform, false);
            // Figures are modelled at toy size; scale the body so they read at tabletop distance.
            body.localScale = Vector3.one * SuspectScale;
            headTop *= SuspectScale;
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, (headTop + 0.06f) * 0.5f, 0f);
            col.size = new Vector3(0.38f, headTop + 0.06f, 0.34f);
            var s = go.AddComponent<DetectiveSuspect>();
            s.Name = name;
            s.Alibi = alibi;
            s.Denial = denial;
            s.Hint = hint;
            s.IsCulprit = culprit;
            s.CardHeight = headTop;
            s.Body = body;
            s.Glow = glow;
            return s;
        }

        static void Eyes(ShowcaseBuildContext ctx, Transform body, float y, float z, float spread, float size)
        {
            ctx.Primitive(PrimitiveType.Sphere, "Eye L", body, new Vector3(-spread, y, z), Vector3.one * size, Dark, false);
            ctx.Primitive(PrimitiveType.Sphere, "Eye R", body, new Vector3(spread, y, z), Vector3.one * size, Dark, false);
        }

        static DetectiveSuspect BuildChef(ShowcaseBuildContext ctx, Transform root, Vector3 pos)
        {
            var s = Suspect(ctx, root, "Chef Pip", pos, 0.46f,
                "\"I was baking all night!\nMy mitts are full of flour,\nnot pink crumbs.\"",
                "Pip: \"Me? I only eat sorbet!\"",
                "who could open the freezer with a magnet?", false);
            var b = s.Body;
            var white = Color.white;
            ctx.Primitive(PrimitiveType.Capsule, "Coat", b, new Vector3(0f, 0.11f, 0f), new Vector3(0.17f, 0.11f, 0.17f), white, false);
            ctx.Primitive(PrimitiveType.Cylinder, "Scarf", b, new Vector3(0f, 0.2f, 0f), new Vector3(0.12f, 0.012f, 0.12f), new Color(0.95f, 0.3f, 0.3f), false);
            ctx.Primitive(PrimitiveType.Sphere, "Button 1", b, new Vector3(0f, 0.15f, -0.083f), Vector3.one * 0.018f, new Color(0.95f, 0.3f, 0.3f), false);
            ctx.Primitive(PrimitiveType.Sphere, "Button 2", b, new Vector3(0f, 0.1f, -0.085f), Vector3.one * 0.018f, new Color(0.95f, 0.3f, 0.3f), false);
            ctx.Primitive(PrimitiveType.Sphere, "Head", b, new Vector3(0f, 0.27f, 0f), Vector3.one * 0.14f, new Color(1f, 0.72f, 0.55f), false);
            Eyes(ctx, b, 0.285f, -0.062f, 0.028f, 0.02f);
            ctx.Primitive(PrimitiveType.Sphere, "Cheek L", b, new Vector3(-0.045f, 0.262f, -0.052f), Vector3.one * 0.024f, Pink, false);
            ctx.Primitive(PrimitiveType.Sphere, "Cheek R", b, new Vector3(0.045f, 0.262f, -0.052f), Vector3.one * 0.024f, Pink, false);
            ctx.Primitive(PrimitiveType.Cube, "Moustache", b, new Vector3(0f, 0.255f, -0.067f), new Vector3(0.06f, 0.014f, 0.012f), new Color(0.4f, 0.25f, 0.15f), false);
            ctx.Primitive(PrimitiveType.Cylinder, "Hat Band", b, new Vector3(0f, 0.345f, 0f), new Vector3(0.1f, 0.03f, 0.1f), white, false);
            ctx.Primitive(PrimitiveType.Sphere, "Hat Puff", b, new Vector3(0f, 0.405f, 0f), new Vector3(0.16f, 0.1f, 0.16f), white, false);
            return s;
        }

        static DetectiveSuspect BuildCat(ShowcaseBuildContext ctx, Transform root, Vector3 pos)
        {
            var s = Suspect(ctx, root, "Cat Mochi", pos, 0.34f,
                "\"Mrrr. I napped in my basket\nall night. Paws can't open\nfreezers... can they?\"",
                "Mochi: \"Mrow?! I'm a cat, not a thief!\"",
                "check what's next to the charging dock.", false);
            var b = s.Body;
            var fur = new Color(0.96f, 0.66f, 0.38f);
            ctx.Primitive(PrimitiveType.Capsule, "Body", b, new Vector3(0f, 0.09f, 0f), new Vector3(0.17f, 0.09f, 0.17f), fur, false);
            ctx.Primitive(PrimitiveType.Sphere, "Belly", b, new Vector3(0f, 0.09f, -0.06f), new Vector3(0.11f, 0.11f, 0.06f), Cream, false);
            ctx.Primitive(PrimitiveType.Sphere, "Head", b, new Vector3(0f, 0.225f, 0f), new Vector3(0.16f, 0.14f, 0.15f), fur, false);
            ctx.Primitive(PrimitiveType.Sphere, "Muzzle", b, new Vector3(0f, 0.205f, -0.06f), new Vector3(0.07f, 0.045f, 0.04f), Cream, false);
            ctx.Primitive(PrimitiveType.Sphere, "Nose", b, new Vector3(0f, 0.218f, -0.08f), Vector3.one * 0.018f, Pink, false);
            Eyes(ctx, b, 0.24f, -0.065f, 0.035f, 0.022f);
            foreach (float side in new[] { -1f, 1f })
            {
                var ear = ctx.Primitive(PrimitiveType.Cube, "Ear", b, new Vector3(side * 0.048f, 0.29f, 0.005f), new Vector3(0.05f, 0.05f, 0.02f), fur, false);
                ear.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var inner = ctx.Primitive(PrimitiveType.Cube, "Ear Inner", b, new Vector3(side * 0.048f, 0.285f, -0.007f), new Vector3(0.028f, 0.028f, 0.01f), Pink, false);
                inner.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }
            var tail = ctx.Primitive(PrimitiveType.Capsule, "Tail", b, new Vector3(0.1f, 0.09f, 0.06f), new Vector3(0.035f, 0.08f, 0.035f), fur, false);
            tail.transform.localRotation = Quaternion.Euler(20f, 0f, -35f);
            ctx.Primitive(PrimitiveType.Cylinder, "Collar", b, new Vector3(0f, 0.165f, 0f), new Vector3(0.13f, 0.01f, 0.13f), new Color(0.36f, 0.62f, 1f), false);
            return s;
        }

        static DetectiveSuspect BuildRobot(ShowcaseBuildContext ctx, Transform root, Vector3 pos)
        {
            var s = Suspect(ctx, root, "Robot Bolt", pos, 0.42f,
                "\"BEEP. I was on my charging\ndock all night. Battery 100%.\nI did not compute ice cream.\"",
                "", "", true);
            var b = s.Body;
            var steel = new Color(0.5f, 0.66f, 0.82f);
            var light = new Color(0.86f, 0.9f, 0.95f);
            ctx.Primitive(PrimitiveType.Cube, "Treads", b, new Vector3(0f, 0.025f, 0f), new Vector3(0.19f, 0.05f, 0.14f), new Color(0.25f, 0.25f, 0.3f), false);
            ctx.Primitive(PrimitiveType.Cube, "Torso", b, new Vector3(0f, 0.125f, 0f), new Vector3(0.16f, 0.15f, 0.13f), steel, false);
            ctx.Primitive(PrimitiveType.Cube, "Chest Panel", b, new Vector3(0f, 0.13f, -0.066f), new Vector3(0.085f, 0.05f, 0.01f), new Color(0.4f, 0.9f, 1f), false);
            // The giveaway: a vanilla smudge on his chest.
            ctx.Primitive(PrimitiveType.Sphere, "Smudge", b, new Vector3(0.045f, 0.175f, -0.066f), new Vector3(0.03f, 0.022f, 0.01f), Cream, false);
            foreach (float side in new[] { -1f, 1f })
                ctx.Primitive(PrimitiveType.Capsule, "Arm", b, new Vector3(side * 0.1f, 0.13f, 0f), new Vector3(0.035f, 0.06f, 0.035f), light, false);
            ctx.Primitive(PrimitiveType.Cube, "Head", b, new Vector3(0f, 0.255f, 0f), new Vector3(0.14f, 0.1f, 0.11f), light, false);
            ctx.Primitive(PrimitiveType.Sphere, "Eye L", b, new Vector3(-0.034f, 0.26f, -0.055f), Vector3.one * 0.032f, new Color(0.25f, 0.85f, 1f), false);
            ctx.Primitive(PrimitiveType.Sphere, "Eye R", b, new Vector3(0.034f, 0.26f, -0.055f), Vector3.one * 0.032f, new Color(0.25f, 0.85f, 1f), false);
            ctx.Primitive(PrimitiveType.Cube, "Mouth", b, new Vector3(0f, 0.226f, -0.056f), new Vector3(0.05f, 0.008f, 0.004f), Dark, false);
            ctx.Primitive(PrimitiveType.Cylinder, "Antenna", b, new Vector3(0f, 0.33f, 0f), new Vector3(0.012f, 0.025f, 0.012f), Dark, false);
            ctx.Primitive(PrimitiveType.Sphere, "Antenna Tip", b, new Vector3(0f, 0.37f, 0f), Vector3.one * 0.035f, new Color(1f, 0.35f, 0.3f), false);
            return s;
        }

        static GameObject BuildMagnifier(ShowcaseBuildContext ctx, Transform root)
        {
            var go = new GameObject("Magnifier Template");
            go.transform.SetParent(root, false);
            var t = go.transform;
            var rim = ctx.Primitive(PrimitiveType.Cylinder, "Rim", t, Vector3.zero, new Vector3(0.11f, 0.006f, 0.11f), new Color(0.95f, 0.72f, 0.25f), false);
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lens = ctx.Primitive(PrimitiveType.Cylinder, "Lens", t, Vector3.zero, new Vector3(0.085f, 0.008f, 0.085f), new Color(0.7f, 0.92f, 1f), false);
            lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var handle = ctx.Primitive(PrimitiveType.Capsule, "Handle", t, new Vector3(0.062f, -0.062f, 0f), new Vector3(0.026f, 0.04f, 0.026f), new Color(0.45f, 0.28f, 0.2f), false);
            handle.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            go.SetActive(false);
            return go;
        }

        // ---- Helpers ----

        // Renderer bounds expressed in `space` local coordinates.
        static Bounds LocalBounds(Transform space, GameObject go)
        {
            var wb = ShowcaseBuildContext.RendererBounds(go);
            var b = new Bounds(space.InverseTransformPoint(wb.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                b.Encapsulate(space.InverseTransformPoint(c));
            }
            return b;
        }

        static float Top(Transform root, GameObject go) => go != null ? LocalBounds(root, go).max.y : CounterHeight;
        static Vector3 Centre(Transform root, GameObject go) => go != null ? LocalBounds(root, go).center : Vector3.zero;
    }
}
