using System.Collections.Generic;
using Showcase.Experiences.Stack;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.EditorTools.Experiences
{
    // Loading dock: pallet in the centre (inside a "Cargo" group so it can be loaded onto the truck),
    // a height ruler on its left, a primitive Nova flatbed truck parked at the right, dock door and
    // dispatch board on the back wall, a few KayKit crates/barrels as dressing, and the inactive
    // templates of the six droppable item kinds.
    [ShowcaseExperienceBuilder(7, "stack", "Stack the Delivery", "Build the tallest tower\nbefore the truck leaves.", "Containers_Crate_Medium_Wood")]
    public static class StackBuilder
    {
        static readonly Color FloorColor = new Color(0.36f, 0.39f, 0.47f);
        static readonly Color WallColor = new Color(0.22f, 0.24f, 0.32f);
        static readonly Color DoorColor = new Color(0.55f, 0.58f, 0.66f);
        static readonly Color DoorSlat = new Color(0.44f, 0.47f, 0.55f);
        static readonly Color Hazard = new Color(1f, 0.8f, 0.2f);
        static readonly Color HazardDark = new Color(0.16f, 0.15f, 0.2f);
        static readonly Color NovaBlue = new Color(0.25f, 0.47f, 0.98f);
        static readonly Color NovaBlueLight = new Color(0.5f, 0.68f, 1f);
        static readonly Color Coral = new Color(1f, 0.45f, 0.38f);
        static readonly Color Cream = new Color(1f, 0.96f, 0.86f);
        static readonly Color Tyre = new Color(0.14f, 0.13f, 0.17f);
        static readonly Color Steel = new Color(0.7f, 0.72f, 0.78f);
        static readonly Color Chassis = new Color(0.25f, 0.25f, 0.3f);
        static readonly Color BedWood = new Color(0.78f, 0.55f, 0.32f);
        static readonly Color Glass = new Color(0.62f, 0.86f, 1f);
        static readonly Color Cardboard = new Color(0.82f, 0.62f, 0.4f);

        const float RulerX = -0.78f;
        const float RulerZ = -0.3f;

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            var ground = new List<Collider>();
            var floor = ctx.Floor(root, new Vector2(3.3f, 2.3f), FloorColor);
            ground.Add(floor.GetComponent<Collider>());
            BuildMarkings(ctx, root);
            BuildBackWall(ctx, root, ground);

            // Cargo group: pallet + brand label; its origin is the pallet's floor centre.
            var cargo = new GameObject("Cargo").transform;
            cargo.SetParent(root, false);
            var pallet = ctx.Place("Pallet_Wood", cargo, Vector3.zero, 0.56f, 0f, true);
            BoxCollider palletBox = null;
            float palletTop = 0.09f;
            if (pallet != null)
            {
                palletBox = Solid(pallet);
                palletTop = root.InverseTransformPoint(ShowcaseBuildContext.RendererBounds(pallet).max).y;
                float palletFront = root.InverseTransformPoint(ShowcaseBuildContext.RendererBounds(pallet).min).z;
                BuildInfoTag(cargo, palletTop, palletFront);
            }

            var ruler = BuildRuler(ctx, root, palletTop, out Transform marker, out Text markerLabel);
            var truck = BuildTruck(ctx, root, ground, out Transform[] wheels, out Transform exhaust, out Vector3 bedLocal);
            BuildDressing(ctx, root, ground);
            var board = BuildDispatchBoard(root, out Text boardTime, out Text boardDrops);

            var guide = ctx.Primitive(PrimitiveType.Cylinder, "Drop Guide", root, new Vector3(0f, 0.5f, 0f), new Vector3(0.008f, 0.2f, 0.008f), Coral, false);
            var target = ctx.Primitive(PrimitiveType.Cylinder, "Drop Target", root, new Vector3(0f, palletTop + 0.004f, 0f), new Vector3(0.11f, 0.003f, 0.11f), Coral, false);
            guide.SetActive(false);
            target.SetActive(false);

            var templates = BuildTemplates(ctx, root);

            var exp = root.gameObject.AddComponent<StackDeliveryExperience>();
            exp.Id = "stack";
            exp.Title = "Stack the Delivery";
            exp.Hint = "Tap anywhere or DROP to drop the item.\nStack it high before the truck leaves!";
            exp.FocusPoint = new Vector3(0.12f, 0.42f, 0.08f);
            exp.FocusDistance = 4.1f;
            exp.FocusPitch = 40f;
            ShowcaseBuildContext.Set(exp, "m_templates", templates);
            ShowcaseBuildContext.Set(exp, "m_cargo", cargo);
            if (palletBox != null) ShowcaseBuildContext.Set(exp, "m_palletCollider", palletBox);
            ShowcaseBuildContext.Set(exp, "m_truck", truck);
            ShowcaseBuildContext.Set(exp, "m_wheels", wheels);
            ShowcaseBuildContext.Set(exp, "m_exhaust", exhaust);
            ShowcaseBuildContext.Set(exp, "m_bedLocal", bedLocal);
            ShowcaseBuildContext.Set(exp, "m_groundColliders", ground.ToArray());
            ShowcaseBuildContext.Set(exp, "m_heightMarker", marker);
            ShowcaseBuildContext.Set(exp, "m_boardTime", boardTime);
            ShowcaseBuildContext.Set(exp, "m_boardDrops", boardDrops);
            ShowcaseBuildContext.Set(exp, "m_dropGuide", guide.transform);
            ShowcaseBuildContext.Set(exp, "m_dropTarget", target.transform);
            ShowcaseBuildContext.Log("Stack the Delivery: pallet top " + palletTop.ToString("0.000") + ", " + templates.Length + " item templates, " + ground.Count + " ground colliders");
        }

        static BoxCollider Solid(GameObject go)
        {
            if (go == null) return null;
            // FitBoxCollider's `GetComponent ?? AddComponent` misses Unity's fake-null, so add the box first.
            if (go.GetComponent<BoxCollider>() == null) go.AddComponent<BoxCollider>();
            return ShowcaseBuildContext.FitBoxCollider(go);
        }

        static void Strip(ShowcaseBuildContext ctx, Transform root, string name, Vector3 pos, Vector3 size, Color c)
        {
            ctx.Primitive(PrimitiveType.Cube, name, root, pos, size, c, false);
        }

        static void BuildMarkings(ShowcaseBuildContext ctx, Transform root)
        {
            // Yellow box around the stacking zone, and the truck bay lines.
            const float h = 0.004f, w = 0.03f, half = 0.42f;
            Strip(ctx, root, "Zone Front", new Vector3(0f, h * 0.5f, -half), new Vector3(half * 2f + w, h, w), Hazard);
            Strip(ctx, root, "Zone Back", new Vector3(0f, h * 0.5f, half), new Vector3(half * 2f + w, h, w), Hazard);
            Strip(ctx, root, "Zone Left", new Vector3(-half, h * 0.5f, 0f), new Vector3(w, h, half * 2f), Hazard);
            Strip(ctx, root, "Zone Right", new Vector3(half, h * 0.5f, 0f), new Vector3(w, h, half * 2f), Hazard);
            Strip(ctx, root, "Bay Front", new Vector3(1.02f, h * 0.5f, 0.24f), new Vector3(1.1f, h, w), Hazard);
            Strip(ctx, root, "Bay Back", new Vector3(1.02f, h * 0.5f, 0.88f), new Vector3(1.1f, h, w), Hazard);
            // Hazard-striped dock edge along the back wall.
            for (int i = 0; i < 11; i++)
                Strip(ctx, root, "Dock Edge", new Vector3(-1.5f + i * 0.3f, h * 0.5f, 1.0f), new Vector3(0.15f, h, 0.06f), i % 2 == 0 ? Hazard : HazardDark);
        }

        static void BuildBackWall(ShowcaseBuildContext ctx, Transform root, List<Collider> ground)
        {
            var wall = ctx.Primitive(PrimitiveType.Cube, "Back Wall", root, new Vector3(0f, 0.36f, 1.1f), new Vector3(3.3f, 0.72f, 0.08f), WallColor);
            ground.Add(wall.GetComponent<Collider>());
            ctx.Primitive(PrimitiveType.Cube, "Wall Trim", root, new Vector3(0f, 0.735f, 1.1f), new Vector3(3.32f, 0.03f, 0.11f), NovaBlue, false);

            // Roller door with slats, framed in hazard yellow.
            ctx.Primitive(PrimitiveType.Cube, "Dock Door", root, new Vector3(0.05f, 0.24f, 1.055f), new Vector3(1.0f, 0.48f, 0.02f), DoorColor, false);
            for (int i = 0; i < 7; i++)
                ctx.Primitive(PrimitiveType.Cube, "Door Slat", root, new Vector3(0.05f, 0.04f + i * 0.065f, 1.043f), new Vector3(0.98f, 0.012f, 0.008f), DoorSlat, false);
            ctx.Primitive(PrimitiveType.Cube, "Door Frame L", root, new Vector3(-0.47f, 0.25f, 1.05f), new Vector3(0.04f, 0.5f, 0.03f), Hazard, false);
            ctx.Primitive(PrimitiveType.Cube, "Door Frame R", root, new Vector3(0.57f, 0.25f, 1.05f), new Vector3(0.04f, 0.5f, 0.03f), Hazard, false);
            ctx.Primitive(PrimitiveType.Cube, "Door Frame Top", root, new Vector3(0.05f, 0.5f, 1.05f), new Vector3(1.08f, 0.04f, 0.03f), Hazard, false);

            var sign = Ui.WorldCanvas("Dock Sign", root, new Vector2(1100, 150), 0.95f);
            sign.transform.localPosition = new Vector3(0.05f, 0.62f, 1.055f);
            var bg = Ui.Panel(sign.transform, NovaBlue, "Background");
            Ui.Stretch(bg.rectTransform);
            bg.raycastTarget = false;
            var text = Ui.Label(bg.transform, "NOVA LOGISTICS  ·  DOCK 7", 92, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(text.rectTransform);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        // Cardboard label stuck to the pallet front; tapping it opens the brand card.
        static void BuildInfoTag(Transform cargo, float palletTop, float palletFront)
        {
            var tag = new GameObject("Info Tag").transform;
            tag.SetParent(cargo, false);
            tag.localPosition = new Vector3(0f, palletTop * 0.5f, palletFront - 0.004f);
            var canvas = Ui.WorldCanvas("Label", tag, new Vector2(600, 120), 0.38f);
            var bg = Ui.Panel(canvas.transform, Cream, "Background");
            Ui.Stretch(bg.rectTransform);
            bg.raycastTarget = false;
            var stripe = Ui.Panel(bg.transform, NovaBlue, "Stripe");
            Ui.Place(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(26, 120), Vector2.zero);
            stripe.raycastTarget = false;
            var text = Ui.Label(bg.transform, "<color=#3F78FA>NOVA</color> LOGISTICS  <color=#FF7361>(i)</color>", 56, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(text.rectTransform, 40, 6, 0, 0);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var box = tag.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.44f, 0.14f, 0.08f);
            var comp = tag.gameObject.AddComponent<StackInfoTag>();
            ShowcaseBuildContext.Set(comp, "m_title", "Nova Logistics");
            ShowcaseBuildContext.Set(comp, "m_body", "Our boxes survive anything.\nNext-day delivery to 120 cities.\nStack smart: heavy at the bottom!");
            ShowcaseBuildContext.Set(comp, "m_cardOffset", new Vector3(-0.55f, 0.45f, 0f));
        }

        static Transform BuildRuler(ShowcaseBuildContext ctx, Transform root, float palletTop, out Transform marker, out Text markerLabel)
        {
            var ruler = new GameObject("Height Ruler").transform;
            ruler.SetParent(root, false);
            ruler.localPosition = new Vector3(RulerX, 0f, RulerZ);
            const float top = 1.18f;
            ctx.Primitive(PrimitiveType.Cylinder, "Base", ruler, new Vector3(0f, 0.015f, 0f), new Vector3(0.14f, 0.015f, 0.14f), HazardDark, false);
            ctx.Primitive(PrimitiveType.Cube, "Pole", ruler, new Vector3(0f, top * 0.5f, 0f), new Vector3(0.036f, top, 0.036f), Cream, false);
            ctx.Primitive(PrimitiveType.Sphere, "Cap", ruler, new Vector3(0f, top + 0.01f, 0f), Vector3.one * 0.05f, Coral, false);

            // Tick every 0.1 units (25 cm), a label every 0.2 (50 cm) from the pallet top.
            for (int k = 1; palletTop + k * 0.1f < top - 0.02f; k++)
            {
                bool major = k % 2 == 0;
                float y = palletTop + k * 0.1f;
                ctx.Primitive(PrimitiveType.Cube, "Tick", ruler, new Vector3(0.02f + (major ? 0.035f : 0.02f), y, 0f), new Vector3(major ? 0.07f : 0.04f, 0.008f, 0.03f), major ? Coral : HazardDark, false);
                if (!major) continue;
                var c = Ui.WorldCanvas("Label " + k * 25, ruler, new Vector2(300, 100), 0.22f);
                c.transform.localPosition = new Vector3(-0.14f, y, -0.02f);
                var t = Ui.Label(c.transform, (k * 25) + " cm", 70, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
                Ui.Stretch(t.rectTransform);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.gameObject.AddComponent<Outline>().effectColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            }

            // Moving height marker: arrow on the pole + dashed line toward the tower + label.
            marker = new GameObject("Height Marker").transform;
            marker.SetParent(root, false);
            marker.localPosition = new Vector3(RulerX, palletTop, RulerZ);
            var arrow = ctx.Primitive(PrimitiveType.Cube, "Arrow", marker, new Vector3(0.045f, 0f, 0f), new Vector3(0.05f, 0.05f, 0.05f), Coral, false);
            arrow.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            for (int i = 0; i < 3; i++)
                ctx.Primitive(PrimitiveType.Cube, "Dash", marker, new Vector3(0.12f + i * 0.11f, 0f, 0f), new Vector3(0.06f, 0.008f, 0.008f), Coral, false);
            markerLabel = null;   // the experience pins a WorldTag with the height to the marker
            return ruler;
        }

        // Stylised Nova flatbed: parked at the right, cab toward +X (it drives off to the right).
        static Transform BuildTruck(ShowcaseBuildContext ctx, Transform root, List<Collider> ground, out Transform[] wheels, out Transform exhaust, out Vector3 bedLocal)
        {
            var truck = new GameObject("Truck").transform;
            truck.SetParent(root, false);
            truck.localPosition = new Vector3(0.97f, 0f, 0.56f);
            const float wz = 0.52f;
            ctx.Primitive(PrimitiveType.Cube, "Chassis", truck, new Vector3(0.02f, 0.11f, 0f), new Vector3(0.94f, 0.06f, wz - 0.08f), Chassis, false);
            ctx.Primitive(PrimitiveType.Cube, "Bed", truck, new Vector3(-0.12f, 0.155f, 0f), new Vector3(0.64f, 0.03f, wz), BedWood, false);
            ctx.Primitive(PrimitiveType.Cube, "Bed Rail Front", truck, new Vector3(-0.12f, 0.18f, -wz * 0.5f + 0.01f), new Vector3(0.64f, 0.025f, 0.02f), NovaBlue, false);
            ctx.Primitive(PrimitiveType.Cube, "Bed Rail Back", truck, new Vector3(-0.12f, 0.18f, wz * 0.5f - 0.01f), new Vector3(0.64f, 0.025f, 0.02f), NovaBlue, false);
            ctx.Primitive(PrimitiveType.Cube, "Headboard", truck, new Vector3(0.215f, 0.26f, 0f), new Vector3(0.03f, 0.2f, wz), Steel, false);
            ctx.Primitive(PrimitiveType.Cube, "Cab", truck, new Vector3(0.36f, 0.26f, 0f), new Vector3(0.25f, 0.26f, wz - 0.02f), NovaBlue, false);
            ctx.Primitive(PrimitiveType.Cube, "Cab Roof", truck, new Vector3(0.35f, 0.4f, 0f), new Vector3(0.22f, 0.03f, wz - 0.06f), NovaBlueLight, false);
            ctx.Primitive(PrimitiveType.Cube, "Windshield", truck, new Vector3(0.486f, 0.31f, 0f), new Vector3(0.006f, 0.1f, wz - 0.1f), Glass, false);
            ctx.Primitive(PrimitiveType.Cube, "Side Window", truck, new Vector3(0.39f, 0.32f, -(wz - 0.02f) * 0.5f - 0.003f), new Vector3(0.12f, 0.08f, 0.006f), Glass, false);
            ctx.Primitive(PrimitiveType.Cube, "Bumper", truck, new Vector3(0.495f, 0.12f, 0f), new Vector3(0.03f, 0.05f, wz), Steel, false);
            ctx.Primitive(PrimitiveType.Sphere, "Headlight L", truck, new Vector3(0.488f, 0.18f, -0.18f), Vector3.one * 0.04f, Hazard, false);
            ctx.Primitive(PrimitiveType.Sphere, "Headlight R", truck, new Vector3(0.488f, 0.18f, 0.18f), Vector3.one * 0.04f, Hazard, false);
            ctx.Primitive(PrimitiveType.Sphere, "Tail Light", truck, new Vector3(-0.445f, 0.12f, -0.2f), Vector3.one * 0.03f, Coral, false);
            var pipe = ctx.Primitive(PrimitiveType.Cylinder, "Exhaust", truck, new Vector3(0.23f, 0.38f, wz * 0.5f - 0.04f), new Vector3(0.025f, 0.09f, 0.025f), Steel, false);
            exhaust = new GameObject("Exhaust Tip").transform;
            exhaust.SetParent(truck, false);
            exhaust.localPosition = new Vector3(0.23f, 0.48f, wz * 0.5f - 0.04f);

            var logo = Ui.WorldCanvas("Door Logo", truck, new Vector2(300, 120), 0.2f);
            logo.transform.localPosition = new Vector3(0.34f, 0.22f, -(wz - 0.02f) * 0.5f - 0.004f);
            var logoText = Ui.Label(logo.transform, "NOVA\n<size=34>LOGISTICS</size>", 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(logoText.rectTransform);
            logoText.lineSpacing = 0.8f;
            logoText.horizontalOverflow = HorizontalWrapMode.Overflow;

            var ws = new List<Transform>();
            foreach (float x in new[] { -0.32f, -0.17f, 0.36f })
                foreach (float z in new[] { -1f, 1f })
                {
                    var wheel = new GameObject("Wheel").transform;
                    wheel.SetParent(truck, false);
                    wheel.localPosition = new Vector3(x, 0.07f, z * (wz * 0.5f - 0.02f));
                    wheel.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    ctx.Primitive(PrimitiveType.Cylinder, "Tyre", wheel, Vector3.zero, new Vector3(0.14f, 0.03f, 0.14f), Tyre, false);
                    ctx.Primitive(PrimitiveType.Cube, "Hub", wheel, new Vector3(0f, z * 0.032f, 0f), new Vector3(0.06f, 0.006f, 0.02f), Steel, false);
                    ctx.Primitive(PrimitiveType.Cylinder, "Hub Cap", wheel, new Vector3(0f, z * 0.03f, 0f), new Vector3(0.07f, 0.004f, 0.07f), Steel, false);
                    ws.Add(wheel);
                }
            wheels = ws.ToArray();

            // One box for the whole truck: items toppling onto it count as dropped.
            var box = truck.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0.02f, 0.21f, 0f);
            box.size = new Vector3(0.96f, 0.42f, wz);
            ground.Add(box);
            bedLocal = new Vector3(-0.12f, 0.17f, 0f);
            return truck;
        }

        static void BuildDressing(ShowcaseBuildContext ctx, Transform root, List<Collider> ground)
        {
            void Add(string prefab, Vector3 pos, float size, float yaw, bool largest)
            {
                var go = ctx.Place(prefab, root, pos, size, yaw, largest);
                var box = Solid(go);
                if (box != null) ground.Add(box);
            }
            Add("Containers_Pile_Large", new Vector3(-1.18f, 0f, -0.78f), 0.38f, 15f, true);
            Add("Pallet_Wood_Covered_A", new Vector3(-0.82f, 0f, 0.78f), 0.3f, -6f, true);
            Add("Fuel_A_Barrels", new Vector3(-1.32f, 0f, 0.05f), 0.3f, 20f, true);
            Add("Fuel_C_Barrel", new Vector3(-1.36f, 0f, -0.32f), 0.2f, 0f, false);
            Add("Pallet_Plastic_Orange", new Vector3(1.22f, 0f, -0.32f), 0.3f, 8f, true);
            Add("Containers_Box_Large", new Vector3(1.22f, 0.045f, -0.32f), 0.2f, -12f, true);
            Add("Food_Crate_Large_Apples", new Vector3(1.3f, 0f, -0.78f), 0.24f, -20f, true);
            Add("Containers_Crate_Small_Green", new Vector3(-1.32f, 0f, 0.74f), 0.15f, 25f, true);
        }

        static Transform BuildDispatchBoard(Transform root, out Text time, out Text drops)
        {
            var board = Ui.WorldCanvas("Dispatch Board", root, new Vector2(700, 330), 0.6f);
            board.transform.localPosition = new Vector3(-1.0f, 0.48f, 1.055f);
            var bg = Ui.Panel(board.transform, new Color(0.11f, 0.11f, 0.16f, 0.96f), "Background");
            Ui.Stretch(bg.rectTransform);
            bg.raycastTarget = false;
            var stripe = Ui.Panel(bg.transform, Hazard, "Stripe");
            Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(700, 12), Vector2.zero);
            stripe.raycastTarget = false;
            var title = Ui.Label(bg.transform, "TRUCK DEPARTS IN", 54, Hazard, TextAnchor.MiddleCenter, FontStyle.Bold, "Title");
            Ui.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(680, 80), new Vector2(0, -20));
            time = Ui.Label(bg.transform, "1:00", 120, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Time");
            Ui.Place(time.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(680, 130), new Vector2(0, -5));
            time.horizontalOverflow = HorizontalWrapMode.Overflow;
            drops = Ui.Label(bg.transform, "Stacked 0    Drops", 44, new Color(0.8f, 0.82f, 0.9f), TextAnchor.MiddleCenter, FontStyle.Bold, "Drops");
            Ui.Place(drops.rectTransform, new Vector2(0.5f, 0f), new Vector2(680, 70), new Vector2(0, 16));
            drops.horizontalOverflow = HorizontalWrapMode.Overflow;
            return board.transform;
        }

        // One inactive template per StackKind (enum order). Root: StackItem + fitted BoxCollider,
        // centred on the origin; mesh under "Visual" so squash tweens don't touch the collider.
        static StackItem[] BuildTemplates(ShowcaseBuildContext ctx, Transform root)
        {
            var holder = new GameObject("Item Templates").transform;
            holder.SetParent(root, false);
            holder.localPosition = new Vector3(0f, 2.2f, 0f);

            var items = new StackItem[6];
            items[(int)StackKind.Crate] = PackTemplate(ctx, holder, StackKind.Crate, "Containers_Crate_Medium_Wood", 0.24f, 2f, false);
            items[(int)StackKind.Box] = PackTemplate(ctx, holder, StackKind.Box, "Containers_Box_Medium", 0.22f, 1f, false);
            items[(int)StackKind.Barrel] = PackTemplate(ctx, holder, StackKind.Barrel, "Fuel_A_Barrel", 0.27f, 3f, true);
            items[(int)StackKind.Basket] = PackTemplate(ctx, holder, StackKind.Basket, "Food_Basket_A_Berries", 0.22f, 0.8f, false);
            items[(int)StackKind.AppleCrate] = PackTemplate(ctx, holder, StackKind.AppleCrate, "Food_Crate_Large_Apples", 0.28f, 2.5f, false);

            var nova = Wrapper(holder, StackKind.NovaBox, 1.2f);
            var v = new GameObject("Visual").transform;
            v.SetParent(nova.transform, false);
            ctx.Primitive(PrimitiveType.Cube, "Box", v, Vector3.zero, new Vector3(0.24f, 0.15f, 0.2f), Cardboard, false);
            ctx.Primitive(PrimitiveType.Cube, "Band", v, Vector3.zero, new Vector3(0.246f, 0.05f, 0.206f), NovaBlue, false);
            ctx.Primitive(PrimitiveType.Cube, "Tape", v, new Vector3(0f, 0.076f, 0f), new Vector3(0.04f, 0.004f, 0.204f), Cream, false);
            var label = Ui.WorldCanvas("Logo", v, new Vector2(300, 80), 0.15f);
            label.transform.localPosition = new Vector3(0f, 0f, -0.104f);
            var lt = Ui.Label(label.transform, "NOVA", 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(lt.rectTransform);
            nova.Visual = v;
            var nb = nova.gameObject.AddComponent<BoxCollider>();
            nb.size = new Vector3(0.24f, 0.15f, 0.2f);
            items[(int)StackKind.NovaBox] = nova;

            holder.gameObject.SetActive(false);
            return items;
        }

        static StackItem Wrapper(Transform holder, StackKind kind, float mass)
        {
            var go = new GameObject("Template " + kind);
            go.transform.SetParent(holder, false);
            go.transform.localPosition = new Vector3(((int)kind - 2.5f) * 0.45f, 0f, 0f);
            var item = go.AddComponent<StackItem>();
            item.Kind = kind;
            item.Mass = mass;
            return item;
        }

        static StackItem PackTemplate(ShowcaseBuildContext ctx, Transform holder, StackKind kind, string prefab, float size, float mass, bool lyingDown)
        {
            var item = Wrapper(holder, kind, mass);
            var visualRoot = new GameObject("Visual").transform;
            visualRoot.SetParent(item.transform, false);
            item.Visual = visualRoot;
            var visual = ctx.Place(prefab, visualRoot, Vector3.zero, size, 0f, true);
            if (visual != null)
            {
                if (lyingDown)
                    visual.transform.rotation = Quaternion.AngleAxis(90f, item.transform.forward) * visual.transform.rotation;
                var b = ShowcaseBuildContext.RendererBounds(visual);
                visual.transform.position += item.transform.position - b.center;
            }
            if (item.GetComponent<BoxCollider>() == null) item.gameObject.AddComponent<BoxCollider>();
            ShowcaseBuildContext.FitBoxCollider(item.gameObject);
            return item;
        }
    }
}
