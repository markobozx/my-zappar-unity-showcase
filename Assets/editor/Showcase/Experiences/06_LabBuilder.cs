using Showcase.Experiences.Lab;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Showcase.EditorTools.Experiences
{
    // Little Lab: a long bench with six ingredient vials on two racks, a big glass beaker in the
    // middle, a RESET button in front of it, a MIX read-out on the bench front, a clue board at the
    // back and Cosmic Retro lab machines around the edges.
    [ShowcaseExperienceBuilder(6, "lab", "Little Lab", "Mix 3 ingredients to find\nthe hero formula.", "Retro_laboratory_DNA_Reactor_Tube")]
    public static class LabBuilder
    {
        const float BenchTop = 0.2625f;
        const float RackTop = BenchTop + 0.03f;
        const float VialHeight = 0.24f;
        const float VialZ = 0.1f;
        static readonly float[] VialX = { -1.12f, -0.8f, -0.48f, 0.48f, 0.8f, 1.12f };
        static readonly Vector3 BeakerPos = new Vector3(0f, BenchTop, 0.38f);

        static readonly Color FloorColor = new Color(0.25f, 0.34f, 0.42f);
        static readonly Color TileColor = new Color(0.3f, 0.41f, 0.5f);
        static readonly Color BenchBody = new Color(0.27f, 0.55f, 0.62f);
        static readonly Color BenchTopColor = new Color(0.94f, 0.93f, 0.9f);
        static readonly Color Trim = new Color(1f, 0.8f, 0.3f);
        static readonly Color Metal = new Color(0.62f, 0.66f, 0.72f);
        static readonly Color Cork = new Color(0.66f, 0.45f, 0.28f);
        static readonly Color Chalk = new Color(0.17f, 0.3f, 0.27f);
        static readonly Color Frame = new Color(0.86f, 0.72f, 0.5f);
        static readonly Color PanelInk = new Color(0.13f, 0.12f, 0.2f, 0.94f);

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            ctx.Floor(root, new Vector2(3.3f, 2.3f), FloorColor);
            ctx.Primitive(PrimitiveType.Cube, "Floor Mat", root, new Vector3(0f, 0.004f, -0.05f), new Vector3(2.9f, 0.008f, 1.9f), TileColor, false);
            ctx.Primitive(PrimitiveType.Cube, "Hazard Line", root, new Vector3(0f, 0.006f, -0.72f), new Vector3(2.9f, 0.008f, 0.04f), Trim, false);

            var glass = GlassMaterial();

            // Bench
            ctx.Primitive(PrimitiveType.Cube, "Bench Body", root, new Vector3(0f, 0.12f, 0.12f), new Vector3(2.6f, 0.24f, 0.95f), BenchBody);
            ctx.Primitive(PrimitiveType.Cube, "Bench Top", root, new Vector3(0f, BenchTop - 0.0125f, 0.12f), new Vector3(2.7f, 0.025f, 1.02f), BenchTopColor, false);
            ctx.Primitive(PrimitiveType.Cube, "Bench Kick", root, new Vector3(0f, 0.02f, -0.36f), new Vector3(2.6f, 0.04f, 0.02f), new Color(0.18f, 0.36f, 0.42f), false);
            ctx.Primitive(PrimitiveType.Cube, "Rack Left", root, new Vector3(-0.8f, BenchTop + 0.015f, VialZ), new Vector3(0.94f, 0.03f, 0.2f), Metal, false);
            ctx.Primitive(PrimitiveType.Cube, "Rack Right", root, new Vector3(0.8f, BenchTop + 0.015f, VialZ), new Vector3(0.94f, 0.03f, 0.2f), Metal, false);

            var exp = root.gameObject.AddComponent<LabExperience>();

            var beaker = BuildBeaker(ctx, root, glass, out var mouth, out var liquid);
            var stream = ctx.Primitive(PrimitiveType.Cylinder, "Pour Stream", root, Vector3.zero, new Vector3(0.022f, 0.1f, 0.022f), Color.white, false);
            stream.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            stream.SetActive(false);

            var vials = new LabVial[VialX.Length];
            for (int i = 0; i < VialX.Length; i++)
                vials[i] = BuildVial(ctx, root, exp, glass, i);

            var reset = BuildResetButton(ctx, root, exp);
            var slots = BuildMixPanel(root, out var status);
            BuildClueBoard(ctx, root);
            BuildProps(ctx, root);

            exp.Id = "lab";
            exp.Title = "Little Lab";
            exp.Hint = "Tap 3 vials to mix a formula.\nRead the clue board to find the hero formula!";
            exp.FocusPoint = new Vector3(0f, 0.3f, 0.12f);
            exp.FocusDistance = 4.3f;
            exp.FocusPitch = 48f;
            ShowcaseBuildContext.Set(exp, "m_vials", vials);
            ShowcaseBuildContext.Set(exp, "m_beaker", beaker);
            ShowcaseBuildContext.Set(exp, "m_beakerMouth", mouth);
            ShowcaseBuildContext.Set(exp, "m_liquid", liquid);
            ShowcaseBuildContext.Set(exp, "m_liquidBottom", 0.026f);
            ShowcaseBuildContext.Set(exp, "m_liquidMax", 0.36f);
            ShowcaseBuildContext.Set(exp, "m_stream", stream.transform);
            ShowcaseBuildContext.Set(exp, "m_resetButton", reset);
            ShowcaseBuildContext.Set(exp, "m_mixSlots", slots);
            ShowcaseBuildContext.Set(exp, "m_mixStatus", status);
            ShowcaseBuildContext.Log("Little Lab: " + vials.Length + " vials, " + LabData.Formulas.Length + " formulas");
        }

        // Transparent URP Lit glass (Flat Kit conversion leaves transparent materials alone).
        static Material GlassMaterial()
        {
            string path = ShowcaseBuildContext.MaterialsDir + "/Lab Glass.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", new Color(0.78f, 0.95f, 1f, 0.28f));
            mat.SetFloat("_Smoothness", 0.85f);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject Glass(GameObject go, Material glass)
        {
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = glass;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static Transform BuildBeaker(ShowcaseBuildContext ctx, Transform root, Material glass, out Transform mouth, out Transform liquid)
        {
            var beaker = new GameObject("Beaker").transform;
            beaker.SetParent(root, false);
            beaker.localPosition = BeakerPos;
            ctx.Primitive(PrimitiveType.Cylinder, "Hot Plate", beaker, new Vector3(0f, 0.012f, 0f), new Vector3(0.46f, 0.012f, 0.46f), new Color(0.3f, 0.3f, 0.36f), false);
            ctx.Primitive(PrimitiveType.Cylinder, "Hot Plate Ring", beaker, new Vector3(0f, 0.025f, 0f), new Vector3(0.4f, 0.002f, 0.4f), new Color(0.45f, 0.47f, 0.55f), false);
            var liquidGo = ctx.Primitive(PrimitiveType.Cylinder, "Liquid", beaker, new Vector3(0f, 0.12f, 0f), new Vector3(0.34f, 0.1f, 0.34f), Color.white, false);
            Glass(ctx.Primitive(PrimitiveType.Cylinder, "Glass", beaker, new Vector3(0f, 0.25f, 0f), new Vector3(0.38f, 0.225f, 0.38f), Color.white, false), glass);
            // Fill marks for 1/3, 2/3 and full.
            for (int k = 1; k <= 3; k++)
            {
                float y = 0.026f + 0.12f * k;
                ctx.Primitive(PrimitiveType.Cube, "Mark " + k, beaker, new Vector3(0f, y, -0.19f), new Vector3(k == 3 ? 0.1f : 0.06f, 0.008f, 0.006f), Color.white, false);
            }
            var box = beaker.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.24f, 0f);
            box.size = new Vector3(0.4f, 0.48f, 0.4f);

            mouth = new GameObject("Mouth").transform;
            mouth.SetParent(beaker, false);
            mouth.localPosition = new Vector3(0f, 0.475f, 0f);
            liquid = liquidGo.transform;
            liquidGo.SetActive(false);
            return beaker;
        }

        static LabVial BuildVial(ShowcaseBuildContext ctx, Transform root, LabExperience owner, Material glass, int index)
        {
            LabIngredient ing = LabData.Ingredients[index];
            float x = VialX[index];
            var vial = new GameObject("Vial " + ing.Name);
            vial.transform.SetParent(root, false);
            vial.transform.localPosition = new Vector3(x, RackTop + VialHeight * 0.5f, VialZ);

            ctx.Primitive(PrimitiveType.Sphere, "Bottom", vial.transform, new Vector3(0f, -0.075f, 0f), new Vector3(0.088f, 0.07f, 0.088f), ing.Color, false);
            ctx.Primitive(PrimitiveType.Cylinder, "Liquid", vial.transform, new Vector3(0f, -0.03f, 0f), new Vector3(0.084f, 0.05f, 0.084f), ing.Color, false);
            Glass(ctx.Primitive(PrimitiveType.Cylinder, "Glass", vial.transform, new Vector3(0f, 0.005f, 0f), new Vector3(0.1f, 0.112f, 0.1f), Color.white, false), glass);
            ctx.Primitive(PrimitiveType.Cylinder, "Lip", vial.transform, new Vector3(0f, 0.112f, 0f), new Vector3(0.11f, 0.006f, 0.11f), Metal, false);
            var cork = ctx.Primitive(PrimitiveType.Cylinder, "Cork", vial.transform, new Vector3(0f, 0.13f, 0f), new Vector3(0.074f, 0.022f, 0.074f), Cork, false);
            var box = vial.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.01f, 0f);
            box.size = new Vector3(0.2f, 0.32f, 0.2f);

            // Floating name tag (stays put while the vial pours).
            var tag = new GameObject("Tag " + ing.Name).transform;
            tag.SetParent(root, false);
            tag.localPosition = new Vector3(x, RackTop + VialHeight + 0.085f, VialZ);
            tag.gameObject.AddComponent<Billboard>();
            var canvas = Ui.WorldCanvas("Canvas", tag, new Vector2(320, 96), 0.28f);
            var bg = Ui.Panel(canvas.transform, PanelInk, "Background");
            Ui.Stretch(bg.rectTransform);
            bg.raycastTarget = false;
            var text = Ui.Label(bg.transform, ing.Name, 60, index == LabData.Charcoal ? new Color(0.75f, 0.72f, 0.82f) : ing.Color, TextAnchor.MiddleCenter, FontStyle.Bold, "Name");
            Ui.Stretch(text.rectTransform, 6, 6, 0, 0);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var comp = vial.AddComponent<LabVial>();
            comp.Index = index;
            comp.MouthY = 0.118f;
            ShowcaseBuildContext.Set(comp, "m_owner", owner);
            ShowcaseBuildContext.Set(comp, "m_cork", cork.transform);
            ShowcaseBuildContext.Set(comp, "m_tagBackground", bg);
            ShowcaseBuildContext.Set(comp, "m_tagText", text);

            BuildInfoButton(root, owner, index, new Vector3(x, BenchTop + 0.055f, VialZ - 0.19f));
            return comp;
        }

        static void BuildInfoButton(Transform root, LabExperience owner, int index, Vector3 pos)
        {
            var go = new GameObject("Info " + LabData.Ingredients[index].Name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.AddComponent<Billboard>().YawOnly = false;
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.065f;
            var canvas = Ui.WorldCanvas("Canvas", go.transform, new Vector2(120, 120), 0.095f);
            var knob = Ui.Panel(canvas.transform, Ui.Accent2, "Circle");
            knob.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            Ui.Stretch(knob.rectTransform);
            knob.raycastTarget = false;
            var ring = Ui.Label(knob.transform, "i", 88, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "I");
            Ui.Stretch(ring.rectTransform);
            var comp = go.AddComponent<LabInfoButton>();
            comp.Index = index;
            ShowcaseBuildContext.Set(comp, "m_owner", owner);
        }

        static LabResetButton BuildResetButton(ShowcaseBuildContext ctx, Transform root, LabExperience owner)
        {
            var btn = new GameObject("Reset Button").transform;
            btn.SetParent(root, false);
            btn.localPosition = new Vector3(0f, BenchTop, -0.17f);
            ctx.Primitive(PrimitiveType.Cylinder, "Base", btn, new Vector3(0f, 0.014f, 0f), new Vector3(0.3f, 0.014f, 0.3f), new Color(0.22f, 0.21f, 0.28f), false);
            var cap = ctx.Primitive(PrimitiveType.Cylinder, "Cap", btn, new Vector3(0f, 0.04f, 0f), new Vector3(0.25f, 0.02f, 0.25f), new Color(1f, 0.36f, 0.3f), false);

            var top = Ui.WorldCanvas("Cap Label", btn, new Vector2(400, 220), 0.23f);
            top.transform.localPosition = new Vector3(0f, 0.062f, 0f);
            top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var text = Ui.Label(top.transform, "RESET\n<size=66>BEAKER</size>", 92, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.lineSpacing = 0.85f;
            Ui.Stretch(text.rectTransform);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.gameObject.AddComponent<Outline>().effectColor = new Color(0.45f, 0.08f, 0.06f, 0.8f);

            var box = btn.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.05f, 0f);
            box.size = new Vector3(0.34f, 0.12f, 0.3f);
            var comp = btn.gameObject.AddComponent<LabResetButton>();
            ShowcaseBuildContext.Set(comp, "m_owner", owner);
            ShowcaseBuildContext.Set(comp, "m_cap", cap.transform);
            return comp;
        }

        // Read-out on the bench front: MIX  (o) (o) (o)  status text.
        static Image[] BuildMixPanel(Transform root, out Text status)
        {
            var canvas = Ui.WorldCanvas("Mix Panel", root, new Vector2(1800, 190), 1.8f);
            canvas.transform.localPosition = new Vector3(0f, 0.135f, -0.362f);
            var bg = Ui.Panel(canvas.transform, PanelInk, "Background");
            Ui.Stretch(bg.rectTransform);
            bg.raycastTarget = false;
            var stripe = Ui.Panel(bg.transform, Trim, "Stripe");
            Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(1800, 10), Vector2.zero);
            stripe.raycastTarget = false;
            var mix = Ui.Label(bg.transform, "MIX", 82, Trim, TextAnchor.MiddleLeft, FontStyle.Bold, "Mix");
            Ui.Place(mix.rectTransform, new Vector2(0f, 0.5f), new Vector2(200, 150), new Vector2(40, -4));

            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var slots = new Image[LabExperience.MixSize];
            for (int k = 0; k < slots.Length; k++)
            {
                var slot = Ui.Panel(bg.transform, new Color(1f, 1f, 1f, 0.18f), "Slot " + (k + 1));
                slot.sprite = knob;
                slot.raycastTarget = false;
                Ui.Place(slot.rectTransform, new Vector2(0f, 0.5f), new Vector2(120, 120), new Vector2(250 + k * 145, -4));
                slots[k] = slot;
            }
            status = Ui.Label(bg.transform, "Pick 3 ingredients", 70, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold, "Status");
            Ui.Place(status.rectTransform, new Vector2(0f, 0.5f), new Vector2(1060, 150), new Vector2(720, -4));
            status.resizeTextForBestFit = true;
            status.resizeTextMinSize = 40;
            status.resizeTextMaxSize = 70;
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Truncate;
            return slots;
        }

        static void BuildClueBoard(ShowcaseBuildContext ctx, Transform root)
        {
            var stand = new GameObject("Clue Board").transform;
            stand.SetParent(root, false);
            stand.localPosition = new Vector3(0f, 0f, 0.98f);
            ctx.Primitive(PrimitiveType.Cylinder, "Post L", stand, new Vector3(-0.66f, 0.38f, 0.03f), new Vector3(0.04f, 0.38f, 0.04f), Metal, false);
            ctx.Primitive(PrimitiveType.Cylinder, "Post R", stand, new Vector3(0.66f, 0.38f, 0.03f), new Vector3(0.04f, 0.38f, 0.04f), Metal, false);

            var board = new GameObject("Board").transform;
            board.SetParent(stand, false);
            board.localPosition = new Vector3(0f, 0.8f, 0f);
            board.localRotation = Quaternion.Euler(10f, 0f, 0f);
            ctx.Primitive(PrimitiveType.Cube, "Frame", board, new Vector3(0f, 0f, 0.01f), new Vector3(1.5f, 0.66f, 0.04f), Frame);
            ctx.Primitive(PrimitiveType.Cube, "Slate", board, new Vector3(0f, 0f, -0.006f), new Vector3(1.42f, 0.58f, 0.012f), Chalk, false);

            var canvas = Ui.WorldCanvas("Clues", board, new Vector2(1420, 580), 1.42f);
            canvas.transform.localPosition = new Vector3(0f, 0f, -0.014f);
            var title = Ui.Label(canvas.transform, "CLUES  ·  the hero formula", 76, Trim, TextAnchor.UpperCenter, FontStyle.Bold, "Title");
            Ui.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(1360, 100), new Vector2(0, -28));
            var riddle = Ui.Label(canvas.transform, LabData.Clues, 58, new Color(0.96f, 0.96f, 0.92f), TextAnchor.UpperCenter, FontStyle.Normal, "Riddle");
            riddle.lineSpacing = 1.1f;
            Ui.Place(riddle.rectTransform, new Vector2(0.5f, 1f), new Vector2(1360, 420), new Vector2(0, -132));
        }

        static void BuildProps(ShowcaseBuildContext ctx, Transform root)
        {
            ctx.Place("Retro_laboratory_Analyzer_DoubleScreen", root, new Vector3(-1.12f, 0f, 0.86f), 0.6f, 12f);
            ctx.Place("Retro_laboratory_DNA_Reactor_Tube", root, new Vector3(1.15f, 0f, 0.86f), 0.72f, -12f);
            ctx.Place("Retro_laboratory_Biohazard_Barrel_1", root, new Vector3(-1.38f, 0f, -0.82f), 0.24f, 20f);
            ctx.Place("Retro_laboratory_Biohazard_Barrel_2", root, new Vector3(1.38f, 0f, -0.82f), 0.24f, -20f);
            ctx.Place("Retro_laboratory_Microscope", root, new Vector3(-0.72f, BenchTop, 0.5f), 0.27f, 25f);
            ctx.Place("Retro_laboratory_Synth_Brewer", root, new Vector3(0.7f, BenchTop, 0.54f), 0.18f, -20f);
            ctx.Place("Retro_laboratory_TestTube_2", root, new Vector3(-1.1f, BenchTop, 0.5f), 0.18f, 10f);
            ctx.Place("Retro_laboratory_notebook", root, new Vector3(1.12f, BenchTop, 0.5f), 0.2f, -15f, true);
        }
    }
}
