using System.Linq;
using Showcase.Experiences.Station;
using UnityEditor;
using UnityEngine;

namespace Showcase.EditorTools.Experiences
{
    // Space Station Escape: cut-away station room (Cosmic Retro props) with the airlock at the back.
    // Left: radio table, red emergency locker + wall keypad. Right: power console (monitor + switch
    // bank) and the broken air unit. Front: tool tray and a crate with decoy tools.
    [ShowcaseExperienceBuilder(4, "station", "Space Station Escape", "Solve 4 puzzles\nto open the airlock.", "CR_Radio")]
    public static class StationBuilder
    {
        const float WallZ = 0.86f;
        const float WallHeight = 0.95f;
        const string WallPiece = "Cosmic_retro_station_wall_plain";

        static readonly Color FloorColor = new Color(0.33f, 0.36f, 0.43f);
        static readonly Color Slate = new Color(0.2f, 0.22f, 0.28f);
        static readonly Color Steel = new Color(0.62f, 0.66f, 0.72f);
        static readonly Color Hazard = new Color(1f, 0.78f, 0.2f);
        static readonly Color Space = new Color(0.06f, 0.07f, 0.2f);

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            var exp = root.gameObject.AddComponent<StationExperience>();
            exp.Title = "Space Station Escape";
            exp.Hint = "Escape the station! Tap things to investigate.";
            // Portrait phones are narrow: back off far enough that the whole 3-unit room fits in width.
            exp.FocusPoint = new Vector3(0f, 0.25f, 0.1f);
            exp.FocusDistance = 6.0f;
            exp.FocusPitch = 46f;

            ctx.Floor(root, new Vector2(3.3f, 2.2f), FloorColor);
            ctx.Primitive(PrimitiveType.Cube, "Hazard Stripe", root, new Vector3(0f, 0.004f, WallZ - 0.16f), new Vector3(0.95f, 0.008f, 0.1f), Hazard, false);

            // ---- back wall with the airlock ----
            var door = ctx.Place("CR_Door", root, new Vector3(0f, 0f, WallZ), WallHeight);
            Bounds db = LocalBounds(root, door);
            float wallFrontZ = WallZ - 0.03f;
            var leftWall = FitWall(ctx, root, -1.62f, db.min.x + 0.01f, out float lf);
            var rightWall = FitWall(ctx, root, db.max.x - 0.01f, 1.62f, out float rf);
            wallFrontZ = Mathf.Min(lf, rf);
            SideWall(ctx, root, -1.62f);
            SideWall(ctx, root, 1.62f);

            // Starfield behind the airlock, visible once it opens.
            var space = ctx.Primitive(PrimitiveType.Cube, "Space", root, new Vector3(0f, 0.38f, WallZ + 0.16f), new Vector3(db.size.x * 0.85f, 0.76f, 0.04f), Space, false);
            var rnd = new System.Random(4);
            for (int i = 0; i < 9; i++)
            {
                float s = 0.012f + (float)rnd.NextDouble() * 0.014f;
                var p = new Vector3(((float)rnd.NextDouble() - 0.5f) * db.size.x * 0.8f, 0.1f + (float)rnd.NextDouble() * 0.6f, WallZ + 0.13f);
                ctx.Primitive(PrimitiveType.Sphere, "Star", space.transform.parent, p, Vector3.one * s, Color.white, false);
            }

            PrefabUtility.UnpackPrefabInstance(door, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var leaves = door.GetComponentsInChildren<Transform>(true).Where(t => t.name == "CR_Door1" || t.name == "CR_Door2").ToList();
            if (leaves.Count == 2)
            {
                var c0 = ShowcaseBuildContext.RendererBounds(leaves[0].gameObject);
                var c1 = ShowcaseBuildContext.RendererBounds(leaves[1].gameObject);
                if (c0.center.x > c1.center.x) { leaves.Reverse(); (c0, c1) = (c1, c0); }
                Vector3 axis = root.right;
                float slide0 = c0.size.x * 0.92f, slide1 = c1.size.x * 0.92f;
                if (Mathf.Abs(c0.center.x - c1.center.x) < Mathf.Abs(c0.center.y - c1.center.y))
                {
                    // Leaves stacked vertically: slide both sideways out of the frame instead.
                    slide0 = slide1 = Mathf.Max(c0.size.x, c1.size.x) * 0.95f;
                }
                exp.AirlockLeft = leaves[0];
                exp.AirlockRight = leaves[1];
                exp.AirlockLeftOpen = leaves[0].parent.InverseTransformPoint(leaves[0].position - axis * slide0);
                exp.AirlockRightOpen = leaves[1].parent.InverseTransformPoint(leaves[1].position + axis * slide1);
                // The frame comes plain white; a cool steel tone sits better against the dark walls.
            foreach (var r in door.GetComponentsInChildren<Renderer>().Where(r => r.name == "CR_Door_Wall"))
                r.sharedMaterial = ctx.ToonMaterial("Station Airlock Frame", new Color(0.5f, 0.55f, 0.64f));
            ShowcaseBuildContext.Log("station: airlock leaves " + c0.size + " / " + c1.size);
            }
            else
                ShowcaseBuildContext.Log("ERROR station: CR_Door leaves not found (" + string.Join(",", door.GetComponentsInChildren<Transform>().Select(t => t.name)) + ")");
            Fit(door, 1.05f);
            exp.Airlock = Prop(door, StationPropKind.Airlock);

            Decor(ctx, "CR_WallLamp_1", root, new Vector3(db.min.x - 0.2f, 0.74f, wallFrontZ - 0.03f), 0.13f);
            Decor(ctx, "CR_WallLamp_1", root, new Vector3(db.max.x + 0.2f, 0.74f, wallFrontZ - 0.03f), 0.13f);

            // ---- left: radio, locker, keypad ----
            var table = ctx.Place("CR_Table_Small_1", root, new Vector3(-0.86f, 0f, -0.3f), 0.3f);
            float tableTop = LocalBounds(root, table).max.y;
            var radio = ctx.Place("CR_Radio", root, new Vector3(-0.86f, tableTop, -0.3f), 0.15f, 15f);
            Fit(radio, 1.3f);
            exp.Radio = Prop(radio, StationPropKind.Radio);
            ctx.Place("CR_Chair_Small", root, new Vector3(-0.46f, 0f, -0.32f), 0.32f, -70f);

            var locker = ctx.Place("CR_Locker_Emergency_Red", root, new Vector3(-1.12f, 0f, 0.56f), 0.82f);
            Bounds lb = LocalBounds(root, locker);
            PrefabUtility.UnpackPrefabInstance(locker, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var lockerDoor = locker.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t != locker.transform && t.name.Contains("Door"));
            if (lockerDoor != null)
            {
                Bounds dbw = ShowcaseBuildContext.RendererBounds(lockerDoor.gameObject);
                var hinge = new GameObject("Hinge").transform;
                hinge.SetParent(locker.transform, false);
                hinge.position = new Vector3(dbw.min.x, dbw.center.y, dbw.center.z);
                hinge.rotation = root.rotation;
                lockerDoor.SetParent(hinge, true);
                exp.LockerHinge = hinge;
                ShowcaseBuildContext.Log("station: locker door z " + root.InverseTransformPoint(dbw.center).z.ToString("0.000") + " body centre z " + lb.center.z.ToString("0.000"));
            }
            else
                ShowcaseBuildContext.Log("ERROR station: locker door child not found");
            Fit(locker, 1.1f);
            exp.Locker = Prop(locker, StationPropKind.Locker);

            var spanner = ctx.Place("CR_Tool_Spanner", root, new Vector3(lb.center.x + 0.05f, 0f, lb.min.z - 0.16f), 0.24f, 25f, true);
            Fit(spanner, 1.6f);
            exp.Spanner = Prop(spanner, StationPropKind.Spanner);

            float keypadX = (lb.max.x + db.min.x) * 0.5f;
            var keypad = new GameObject("Keypad").transform;
            keypad.SetParent(root, false);
            keypad.localPosition = new Vector3(keypadX, 0.46f, wallFrontZ - 0.03f);
            exp.KeypadAnchor = keypad;
            exp.KeypadWidth = Mathf.Min(0.36f, (db.min.x - lb.max.x) - 0.06f);
            float kh = exp.KeypadWidth * 1.5f;
            ctx.Primitive(PrimitiveType.Cube, "Keypad Plate", keypad, new Vector3(0f, 0f, 0.018f), new Vector3(exp.KeypadWidth + 0.03f, kh + 0.03f, 0.025f), Slate, false);

            Decor(ctx, "CR_FirstAidKit_Large", root, new Vector3(-1.3f, 0f, 0.2f), 0.17f, 80f);
            Decor(ctx, "CR_StorageCrate_Small_1", root, new Vector3(-0.98f, 0f, -0.8f), 0.2f, 10f);

            // ---- right: power console and broken air unit ----
            var computer = ctx.Place("CR_Computer_WithMonitor", root, new Vector3(0.78f, 0f, 0.26f), 0.56f);
            Bounds cb = LocalBounds(root, computer);
            Fit(computer, 1.1f);
            exp.Monitor = Prop(computer, StationPropKind.Monitor);
            var screen = new GameObject("Monitor Screen").transform;
            screen.SetParent(root, false);
            // The CRT screen is recessed in the upper half of the unit and leans back slightly.
            screen.localPosition = new Vector3(cb.center.x - cb.size.x * 0.04f, cb.min.y + cb.size.y * 0.79f, cb.min.z + cb.size.z * 0.3f);
            screen.localRotation = Quaternion.Euler(10f, 0f, 0f);
            exp.MonitorAnchor = screen;
            exp.MonitorWidth = cb.size.x * 0.56f;
            ShowcaseBuildContext.Log("station: computer bounds " + cb.center + " size " + cb.size);

            var console = ctx.Place("CR_Table_Small_1", root, new Vector3(0.78f, 0f, -0.27f), 0.28f);
            float consoleTop = LocalBounds(root, console).max.y;
            BuildSwitches(ctx, root, exp, new Vector3(0.78f, consoleTop, -0.27f));

            // Wall-mounted air unit on the back wall, right of the airlock.
            float acX = (db.max.x + 1.48f) * 0.5f;
            var ac = ctx.Place("CR_AirConditioner", root, new Vector3(acX, 0.34f, WallZ - 0.3f), Mathf.Min(0.62f, 1.4f - db.max.x), 0f, true);
            Bounds ab = LocalBounds(root, ac);
            ac.transform.position += root.TransformVector(new Vector3(0f, 0f, wallFrontZ - 0.005f - ab.max.z));
            ab = LocalBounds(root, ac);
            Fit(ac, 1.2f);
            exp.AirConditioner = Prop(ac, StationPropKind.AirConditioner);
            var smoke = new GameObject("Smoke Point").transform;
            smoke.SetParent(root, false);
            smoke.localPosition = new Vector3(ab.center.x, ab.max.y + 0.02f, ab.center.z);
            exp.SmokePoint = smoke;

            // ---- front: tool tray and decoy tools ----
            var tray = new GameObject("Tool Tray").transform;
            tray.SetParent(root, false);
            tray.localPosition = new Vector3(0f, 0f, -0.72f);
            ctx.Primitive(PrimitiveType.Cube, "Tray Base", tray, new Vector3(0f, 0.025f, 0f), new Vector3(0.48f, 0.05f, 0.22f), Slate, false);
            ctx.Primitive(PrimitiveType.Cube, "Tray Mat", tray, new Vector3(0f, 0.051f, 0f), new Vector3(0.42f, 0.004f, 0.16f), Steel, false);
            var sign = new GameObject("Tray Sign").transform;
            sign.SetParent(tray, false);
            sign.localPosition = new Vector3(0f, 0.125f, 0.13f);
            sign.localRotation = Quaternion.Euler(25f, 0f, 0f);
            exp.TraySign = sign;
            // TraySlot is where the spanner's pivot goes so that the tool lies centred on the mat.
            Bounds sb = LocalBounds(root, spanner);
            Vector3 pivotFromBottom = spanner.transform.localPosition - new Vector3(sb.center.x, sb.min.y, sb.center.z);
            var slot = new GameObject("Tray Slot").transform;
            slot.SetParent(root, false);
            slot.localPosition = tray.localPosition + new Vector3(0f, 0.054f, 0f) + pivotFromBottom;
            exp.TraySlot = slot;

            var crate = ctx.Place("CR_StorageCrate_Large_1", root, new Vector3(0.98f, 0f, -0.74f), 0.28f, -8f);
            float crateTop = LocalBounds(root, crate).max.y;
            var wrench = ctx.Place("CR_Tool_Wrench", root, new Vector3(0.88f, crateTop, -0.74f), 0.2f, 60f, true);
            Fit(wrench, 1.5f);
            Prop(wrench, StationPropKind.Decoy).Message = "A wrench? Wrong size for those bolts.";
            var driver = ctx.Place("CR_Tool_Screwdriver", root, new Vector3(1.08f, crateTop, -0.72f), 0.16f, -30f, true);
            Fit(driver, 1.6f);
            Prop(driver, StationPropKind.Decoy).Message = "A screwdriver won't budge those bolts.";
            Decor(ctx, "CR_StorageCrate_Small_2", root, new Vector3(1.24f, 0f, 0.22f), 0.2f, -12f);
            spanner.SetActive(false);   // revealed when the locker opens
        }

        static void BuildSwitches(ShowcaseBuildContext ctx, Transform root, StationExperience exp, Vector3 topCentre)
        {
            var bank = new GameObject("Switch Bank").transform;
            bank.SetParent(root, false);
            bank.localPosition = topCentre;
            ctx.Primitive(PrimitiveType.Cube, "Bank Plate", bank, new Vector3(0f, 0.015f, 0f), new Vector3(0.5f, 0.03f, 0.2f), Slate, false);
            exp.Switches = new StationProp[3];
            exp.Levers = new Transform[3];
            exp.Lamps = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                var sw = new GameObject("Switch " + (i + 1)).transform;
                sw.SetParent(bank, false);
                sw.localPosition = new Vector3((i - 1) * 0.155f, 0.03f, 0f);
                ctx.Primitive(PrimitiveType.Cube, "Housing", sw, new Vector3(0f, 0.025f, 0f), new Vector3(0.1f, 0.05f, 0.12f), Steel, false);
                var pivot = new GameObject("Lever").transform;
                pivot.SetParent(sw, false);
                pivot.localPosition = new Vector3(0f, 0.05f, 0.01f);
                pivot.localRotation = Quaternion.Euler(35f, 0f, 0f);
                ctx.Primitive(PrimitiveType.Cube, "Stick", pivot, new Vector3(0f, 0.05f, 0f), new Vector3(0.022f, 0.1f, 0.022f), Slate, false);
                ctx.Primitive(PrimitiveType.Sphere, "Knob", pivot, new Vector3(0f, 0.1f, 0f), Vector3.one * 0.045f, Ui.Accent, false);
                var lamp = ctx.Primitive(PrimitiveType.Sphere, "Lamp", sw, new Vector3(0f, 0.05f, -0.045f), new Vector3(0.04f, 0.025f, 0.04f), new Color(0.55f, 0.12f, 0.12f), false);
                var box = sw.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.08f, 0f);
                box.size = new Vector3(0.15f, 0.17f, 0.18f);
                var prop = Prop(sw.gameObject, StationPropKind.Switch);
                prop.Index = i;
                exp.Switches[i] = prop;
                exp.Levers[i] = pivot;
                exp.Lamps[i] = lamp.GetComponent<Renderer>();
            }
        }

        // FitBoxCollider's `GetComponent() ?? AddComponent()` misses Unity's fake-null, so add the box first.
        static BoxCollider Fit(GameObject go, float padding)
        {
            if (go.GetComponent<BoxCollider>() == null) go.AddComponent<BoxCollider>();
            return ShowcaseBuildContext.FitBoxCollider(go, padding);
        }

        static StationProp Prop(GameObject go, StationPropKind kind)
        {
            var p = go.AddComponent<StationProp>();
            p.Kind = kind;
            return p;
        }

        static GameObject Decor(ShowcaseBuildContext ctx, string name, Transform root, Vector3 pos, float height, float yaw = 0f)
        {
            var go = ctx.Place(name, root, pos, height, yaw);
            if (go != null)
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            return go;
        }

        // A back-wall piece stretched to span x0..x1; outputs its front face z.
        static GameObject FitWall(ShowcaseBuildContext ctx, Transform root, float x0, float x1, out float frontZ)
        {
            var go = ctx.Place(WallPiece, root, new Vector3((x0 + x1) * 0.5f, 0f, WallZ), WallHeight);
            frontZ = WallZ;
            if (go == null) return null;
            Bounds b = LocalBounds(root, go);
            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(s.x * (x1 - x0) / Mathf.Max(b.size.x, 0.001f), s.y, s.z);
            Recentre(root, go, new Vector3((x0 + x1) * 0.5f, 0f, WallZ));
            frontZ = LocalBounds(root, go).min.z;
            return go;
        }

        // Cut-away side wall along z, only at the back so the viewer can see in.
        static void SideWall(ShowcaseBuildContext ctx, Transform root, float x)
        {
            const float z0 = 0.25f, z1 = WallZ + 0.04f;
            var go = ctx.Place(WallPiece, root, new Vector3(x, 0f, (z0 + z1) * 0.5f), WallHeight, 90f);
            if (go == null) return;
            Bounds b = LocalBounds(root, go);
            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(s.x * (z1 - z0) / Mathf.Max(b.size.z, 0.001f), s.y, s.z);
            Recentre(root, go, new Vector3(x, 0f, (z0 + z1) * 0.5f));
        }

        static void Recentre(Transform root, GameObject go, Vector3 localBottomCentre)
        {
            Bounds b = ShowcaseBuildContext.RendererBounds(go);
            Vector3 target = root.TransformPoint(localBottomCentre);
            go.transform.position += new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);
        }

        // Renderer bounds expressed in root-local coordinates (root is axis-aligned with World).
        static Bounds LocalBounds(Transform root, GameObject go)
        {
            Bounds w = ShowcaseBuildContext.RendererBounds(go);
            Vector3 a = root.InverseTransformPoint(w.min), c = root.InverseTransformPoint(w.max);
            var b = new Bounds((a + c) * 0.5f, Vector3.zero);
            b.Encapsulate(Vector3.Min(a, c));
            b.Encapsulate(Vector3.Max(a, c));
            return b;
        }
    }
}
