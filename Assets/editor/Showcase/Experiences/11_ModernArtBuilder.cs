using System.Linq;
using Showcase.Experiences.ModernArt;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.EditorTools.Experiences
{
    // Modern Art, opened by the "Arte moderno" book cover (target arte-moderno). Not on a hub pedestal.
    // Target Space is lined up with the image tracker, so 1 unit = half the cover's height: the cover is
    // 2 units tall and 2 x aspect wide, lying in X/Z with its top edge at +Z (Zappar's target convention).
    [ShowcaseExperienceBuilder(11, "modernart", "Modern Art", "Theo van Doesburg on the\nArte moderno cover.", "")]
    public static class ModernArtBuilder
    {
        const string Folder = "Assets/Showcase/ModernArt";
        const float CoverHeight = 2f;
        const float TheoHeight = 0.6f;

        public static void Build(ShowcaseBuildContext ctx, Transform root)
        {
            var exp = root.gameObject.AddComponent<ModernArtExperience>();
            exp.Title = "Modern Art";
            exp.Hint = "Pick a view below · tap Theo";
            exp.CoverHeight = CoverHeight;

            var targetSpace = new GameObject("Target Space").transform;
            targetSpace.SetParent(root, false);
            var tracker = root.GetComponentInParent<Zappar.ZapparImageTrackingTarget>(true);
            if (tracker != null)
            {
                Matrix4x4 m = root.worldToLocalMatrix * tracker.transform.localToWorldMatrix;
                targetSpace.localPosition = m.GetColumn(3);
                targetSpace.localRotation = m.rotation;
                targetSpace.localScale = m.lossyScale;
            }
            else ShowcaseBuildContext.Log("ERROR: modernart: World is not under an image tracker; Target Space left at the root");
            exp.TargetSpace = targetSpace;

            exp.CoverTexture = Texture("layer-cover.png");
            exp.PortraitTexture = Texture("layer-portrait.png");
            exp.BlankTexture = Texture("layer-white.png");
            float aspect = exp.CoverTexture != null ? exp.CoverTexture.width / (float)exp.CoverTexture.height : 707f / 962f;
            exp.Blank = Layer(targetSpace, "Blank Page", exp.BlankTexture, aspect, 1);
            exp.Portrait = Layer(targetSpace, "Portrait", exp.PortraitTexture, aspect, 2);
            exp.Cover = Layer(targetSpace, "Cover", exp.CoverTexture, aspect, 3);

            exp.Theo = Theo(targetSpace, exp);

            // Free-camera framing (World-local): the middle of the cover, far enough back to see all of it.
            Transform world = root;
            while (world != null && world.name != "World") world = world.parent;
            float worldPerTarget = world != null ? targetSpace.lossyScale.x / world.lossyScale.x : 1f;
            exp.FocusPoint = world != null ? world.InverseTransformPoint(targetSpace.TransformPoint(0f, 0.3f, 0f)) : Vector3.zero;
            exp.FocusDistance = Mathf.Min(7.5f, CoverHeight * worldPerTarget * 1.25f);
            exp.FocusPitch = 52f;
            ShowcaseBuildContext.Log("modernart: cover " + (CoverHeight * aspect).ToString("0.###") + " x " + CoverHeight + " target units (" + worldPerTarget.ToString("0.##") + " World units each), Theo " + TheoHeight);
        }

        static Texture2D Texture(string file)
        {
            string path = Folder + "/" + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { ShowcaseBuildContext.Log("MISSING " + path); return null; }
            if (importer.maxTextureSize != 1024 || importer.npotScale != TextureImporterNPOTScale.None || !importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Default;
                importer.maxTextureSize = 1024;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // A flat image the size of the cover. The pivot is the near (bottom) edge, so the layer can stand up.
        static ModernArtExperience.Layer Layer(Transform space, string name, Texture tex, float aspect, int order)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(space, false);
            pivot.localPosition = new Vector3(0f, 0.002f * order, -CoverHeight * 0.5f);
            var holder = new GameObject("Face Up").transform;
            holder.SetParent(pivot, false);
            holder.localPosition = new Vector3(0f, 0f, CoverHeight * 0.5f);
            holder.localRotation = Quaternion.Euler(90f, 0f, 0f);   // canvas faces up, its top toward +Z
            var canvas = Ui.WorldCanvas("Canvas", holder, new Vector2(1000f * aspect, 1000f), CoverHeight * aspect);
            canvas.sortingOrder = order;
            var img = Ui.Rect("Image", canvas.transform).gameObject.AddComponent<RawImage>();
            Ui.Stretch((RectTransform)img.transform);
            img.texture = tex;
            img.raycastTarget = false;
            var group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            return new ModernArtExperience.Layer { Pivot = pivot, Group = group };
        }

        static TheoCharacter Theo(Transform space, ModernArtExperience exp)
        {
            var rootGo = new GameObject("Theo van Doesburg");
            rootGo.transform.SetParent(space, false);
            rootGo.transform.localPosition = new Vector3(0f, 0f, -0.45f);
            var theo = rootGo.AddComponent<TheoCharacter>();
            theo.Experience = exp;
            theo.Height = TheoHeight;

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/TheoVanDoesburg.glb");
            if (asset == null) { ShowcaseBuildContext.Log("MISSING " + Folder + "/TheoVanDoesburg.glb"); return theo; }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, rootGo.transform);
            model.name = "Model";
            // glTF characters face +Z; Theo faces the viewer, who stands at the cover's bottom edge (-Z).
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var b = ShowcaseBuildContext.RendererBounds(model);
            float h = b.size.y / Mathf.Max(1e-5f, rootGo.transform.lossyScale.y);
            if (h > 1e-4f) model.transform.localScale = Vector3.one * (TheoHeight / h);
            b = ShowcaseBuildContext.RendererBounds(model);
            float footY = rootGo.transform.InverseTransformPoint(b.min).y;
            model.transform.localPosition -= new Vector3(0f, footY, 0f);
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;

            theo.Anim = model.GetComponentInChildren<Animation>();
            if (theo.Anim == null) ShowcaseBuildContext.Log("ERROR: modernart: the Theo model has no legacy Animation (set animationMethod: 1 in the .glb import settings)");
            else
            {
                theo.Anim.playAutomatically = false;
                ShowcaseBuildContext.Log("modernart: Theo clips: " + string.Join(", ", theo.Anim.Cast<AnimationState>().Select(s => s.name)));
            }

            var col = rootGo.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, TheoHeight * 0.5f, 0f);
            col.height = TheoHeight * 1.05f;
            col.radius = TheoHeight * 0.22f;
            return theo;
        }
    }
}
