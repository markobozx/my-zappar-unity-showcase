using System.Collections.Generic;
using System.Linq;
using Showcase.Experiences.Room;
using UnityEngine;

namespace Showcase.EditorTools.Experiences
{
    // Room Makeover: an open cut-away living room (floor + back and left wall) with five furniture
    // slots whose variants are all preloaded as hidden children, a "My room" board on the back wall
    // and a "Finish room" plinth on the front edge.
    [ShowcaseExperienceBuilder(2, "room", "Room Makeover", "Swap furniture and colours.\nSee prices and details.", "Armchair_01")]
    public static class RoomBuilder
    {
        static readonly Color FloorWood = new Color(0.7f, 0.48f, 0.3f);
        static readonly Color BackWall = new Color(0.47f, 0.74f, 0.6f);
        static readonly Color SideWall = new Color(0.97f, 0.74f, 0.56f);
        static readonly Color Trim = new Color(0.98f, 0.97f, 0.94f);
        static readonly Color BoardWood = new Color(0.55f, 0.37f, 0.26f);
        static readonly Color FinishBase = new Color(0.24f, 0.52f, 0.33f);

        const float WallHeight = 0.8f;
        const float BackWallZ = 0.97f;     // centre of the back wall
        const float LeftWallX = -1.47f;
        const float WallThickness = 0.06f;

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            ctx.Floor(root, new Vector2(3.0f, 2.0f), FloorWood);
            BuildShell(ctx, root);

            var slots = new List<RoomSlot>
            {
                Slot(ctx, root, "Sofa", new Vector3(-0.3f, 0f, 0.58f), 0.95f, true, 0f, new[]
                {
                    ("Couch_09", "Sage Cloud Sofa", 649, "Recycled velvet · walnut legs"),
                    ("Couch_11", "Blush Bloom Sofa", 729, "Bouclé wool blend · curved arms"),
                    ("Couch_12", "Tangerine Loft Sofa", 589, "Washable cotton · deep seat"),
                    ("Couch_08", "Sky Lounge Sofa", 549, "Linen weave · feather cushions"),
                }),
                Slot(ctx, root, "Armchair", new Vector3(0.88f, 0f, 0.02f), 0.54f, true, 55f, new[]
                {
                    ("Armchair_03", "Sage Cloud Chair", 299, "Recycled velvet · matches the sofa"),
                    ("Armchair_04", "Blush Bloom Chair", 329, "Bouclé wool blend · swivel base"),
                    ("Armchair_12", "Tangerine Nook Chair", 279, "Washable cotton · high back"),
                    ("Armchair_02", "Sky Lounge Chair", 259, "Linen weave · solid beech"),
                }),
                Slot(ctx, root, "Table", new Vector3(-0.28f, 0f, 0.0f), 0.2f, false, 0f, new[]
                {
                    ("Coffee_Table_02", "Pebble Coffee Table", 189, "Oiled oak · organic shape"),
                    ("Coffee_Table_03", "Lilac Round Table", 149, "Lacquered top · oak legs"),
                    ("Coffee_Table_04", "Duo Tier Table", 169, "Powder-coated steel · 2 levels"),
                    ("Coffee_Table_01", "Aqua Hoop Table", 129, "Glossy steel hoops"),
                }),
                Slot(ctx, root, "Lamp", new Vector3(-1.18f, 0f, 0.7f), 0.66f, false, 20f, new[]
                {
                    ("Light_07", "Arc Floor Lamp", 159, "Brushed steel · dimmable LED"),
                    ("Light_05", "Tripod Glow Lamp", 119, "Teak legs · linen shade"),
                    ("Light_08", "Lantern Floor Lamp", 139, "Rice paper · warm light"),
                    ("Light_06", "Lilac Reading Lamp", 99, "Matte metal · USB port"),
                }),
                Slot(ctx, root, "Plant", new Vector3(-1.1f, 0f, -0.45f), 0.38f, false, 0f, new[]
                {
                    ("Plants_11", "Monstera Deliciosa", 49, "Terracotta pot · easy care"),
                    ("Plants_14", "Golden Pothos", 39, "Glazed teal pot · trailing"),
                    ("Plants_13", "Snake Plant", 35, "Ceramic pot · air purifying"),
                    ("Plants_20", "Kentia Palm", 59, "Woven pot · loves shade"),
                }),
            };

            var board = ctx.Primitive(PrimitiveType.Cube, "My Room Board", root, new Vector3(0.88f, 0.44f, BackWallZ - WallThickness * 0.5f - 0.015f), new Vector3(1.04f, 0.68f, 0.03f), BoardWood, false);

            var finish = new GameObject("Finish Room").transform;
            finish.SetParent(root, false);
            finish.localPosition = new Vector3(0f, 0f, -0.8f);
            ctx.Primitive(PrimitiveType.Cube, "Plinth", finish, new Vector3(0f, 0.03f, 0f), new Vector3(0.68f, 0.06f, 0.24f), FinishBase, false);
            var finishCol = finish.gameObject.AddComponent<BoxCollider>();
            finishCol.center = new Vector3(0f, 0.08f, 0f);
            finishCol.size = new Vector3(0.78f, 0.2f, 0.32f);
            finish.gameObject.AddComponent<RoomFinishButton>();

            var exp = root.gameObject.AddComponent<RoomExperience>();
            exp.Title = "Room Makeover";
            exp.Hint = "Tap furniture to restyle it · ♥ Save favourites · then Finish room";
            exp.FocusPoint = new Vector3(0f, 0.22f, 0.05f);
            exp.FocusDistance = 3.7f;
            exp.FocusPitch = 46f;
            ShowcaseBuildContext.Set(exp, "m_slots", slots.ToArray());
            ShowcaseBuildContext.Set(exp, "m_board", board.transform);
            ShowcaseBuildContext.Set(exp, "m_finish", finish);
        }

        static void BuildShell(ShowcaseBuildContext ctx, Transform root)
        {
            const float sideLength = 1.66f;
            float sideZ = BackWallZ + WallThickness * 0.5f - sideLength * 0.5f;
            ctx.Primitive(PrimitiveType.Cube, "Back Wall", root, new Vector3(0f, WallHeight * 0.5f, BackWallZ), new Vector3(3.0f, WallHeight, WallThickness), BackWall, false);
            ctx.Primitive(PrimitiveType.Cube, "Left Wall", root, new Vector3(LeftWallX, WallHeight * 0.5f, sideZ), new Vector3(WallThickness, WallHeight, sideLength), SideWall, false);
            // Skirting boards and a top trim so the cut-away reads as a finished model.
            ctx.Primitive(PrimitiveType.Cube, "Back Skirting", root, new Vector3(0.015f, 0.035f, BackWallZ - WallThickness * 0.5f - 0.01f), new Vector3(2.97f, 0.07f, 0.02f), Trim, false);
            ctx.Primitive(PrimitiveType.Cube, "Left Skirting", root, new Vector3(LeftWallX + WallThickness * 0.5f + 0.01f, 0.035f, sideZ - 0.01f), new Vector3(0.02f, 0.07f, sideLength - 0.02f), Trim, false);
            ctx.Primitive(PrimitiveType.Cube, "Back Cap", root, new Vector3(0f, WallHeight + 0.01f, BackWallZ), new Vector3(3.02f, 0.02f, WallThickness + 0.02f), Trim, false);
            ctx.Primitive(PrimitiveType.Cube, "Left Cap", root, new Vector3(LeftWallX, WallHeight + 0.01f, sideZ), new Vector3(WallThickness + 0.02f, 0.02f, sideLength + 0.02f), Trim, false);

            Decor(ctx.Place("Carpet_11", root, new Vector3(-0.28f, 0f, 0.08f), 1.15f, 0f, true));
            var picture = ctx.Place("Picture_05", root, new Vector3(-0.3f, 0.47f, 0.9f), 0.26f);
            Decor(picture);
            AgainstBackWall(picture);
            var window = ctx.Place("Window_White_03", root, new Vector3(LeftWallX + 0.05f, 0.2f, 0.12f), 0.45f, -90f);
            Decor(window);
            if (window != null)
            {
                var b = ShowcaseBuildContext.RendererBounds(window);
                float face = root.TransformPoint(new Vector3(LeftWallX + WallThickness * 0.5f, 0f, 0f)).x;
                window.transform.position += new Vector3(face - b.min.x - 0.02f * root.lossyScale.x, 0f, 0f);
            }
        }

        static void AgainstBackWall(GameObject go)
        {
            if (go == null) return;
            var b = ShowcaseBuildContext.RendererBounds(go);
            float face = go.transform.parent.TransformPoint(new Vector3(0f, 0f, BackWallZ - WallThickness * 0.5f)).z;
            go.transform.position += new Vector3(0f, 0f, face - b.max.z);
        }

        static void Decor(GameObject go)
        {
            if (go == null) return;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
        }

        static RoomSlot Slot(ShowcaseBuildContext ctx, Transform root, string category, Vector3 pos, float size, bool fitLargestSide, float yaw,
            (string prefab, string name, int price, string material)[] variants)
        {
            var go = new GameObject("Slot " + category);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            var slot = go.AddComponent<RoomSlot>();
            slot.Category = category;
            var list = new List<RoomVariant>();
            float maxHeight = 0f;
            foreach (var (prefab, name, price, material) in variants)
            {
                var model = ctx.Place(prefab, go.transform, Vector3.zero, size, yaw, fitLargestSide);
                if (model == null) continue;
                foreach (var c in model.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);
                // FitBoxCollider's `GetComponent ?? AddComponent` misses Unity's editor fake-null, so add the box first.
                model.AddComponent<BoxCollider>();
                // Generous tap target: phones are coarse.
                ShowcaseBuildContext.FitBoxCollider(model, 1.2f);
                var b = ShowcaseBuildContext.RendererBounds(model);
                maxHeight = Mathf.Max(maxHeight, go.transform.InverseTransformVector(Vector3.up * b.size.y).y);
                list.Add(new RoomVariant { Model = model, Name = name, Price = price, Material = material, BaseScale = model.transform.localScale });
                model.SetActive(list.Count == 1);
            }
            slot.Variants = list.ToArray();
            slot.CardOffset = new Vector3(0f, maxHeight + 0.3f, 0f);
            ShowcaseBuildContext.Log("room slot " + category + ": " + list.Count + " variants, height " + maxHeight.ToString("0.00"));
            return slot;
        }
    }
}
