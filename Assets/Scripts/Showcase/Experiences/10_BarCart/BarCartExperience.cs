using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Showcase.Experiences.BarCart
{
    // Build Your Bar Cart: style a cart, load three Lumo sodas, pick a glass and garnish, shake, pour,
    // and get a recipe card for the generated mocktail. Every prop is placed by BarCartBuilder; items
    // only move between their home spot and the cart, so nothing is instantiated from assets at runtime.
    // The current step is derived from the choices, so players can change their mind until they shake.
    public class BarCartExperience : ShowcaseExperience
    {
        [Header("Cart styles (preloaded, one visible)")]
        public GameObject[] Carts;
        public float[] CartTops;                 // root-local y of each cart's top surface
        public Transform CartTop;                // moves to the active cart's top; chosen items ride on it

        [Header("Choices")]
        public BarCartPart[] Bottles;
        public BarCartPart[] Glasses;
        public BarCartPart[] Garnishes;
        public Transform[] GlassRims;            // where a garnish sits, child of each glass
        public Transform[] Liquids;              // pivot at the liquid's bottom, child of each glass
        public Vector3[] LiquidFull;             // local scale of each liquid when full
        public float[] LiquidBottomFrac;         // bottom radius / top radius (0 = cone for the coupe)
        public BarCartShaker Shaker;

        [Header("Stage")]
        public Transform PedestalTop;
        public Transform TrackerAnchor;
        public Transform RecipeAnchor;
        public Transform ShelfTagAnchor;
        public Transform GlassTagAnchor;
        public Transform GarnishTagAnchor;

        [Header("Cart layout (CartTop-local)")]
        public Vector3[] BottleSlots = { new Vector3(-0.15f, 0f, 0.07f), new Vector3(0f, 0f, 0.07f), new Vector3(0.15f, 0f, 0.07f) };
        public Vector3 ShakerSlot = new Vector3(-0.15f, 0f, -0.07f);
        public Vector3 GlassSlot = new Vector3(0.14f, 0f, -0.07f);
        public Vector3 GarnishSlot = new Vector3(0f, 0f, -0.08f);

        private static readonly string[] StepNames = { "Cart", "Sodas", "Glass", "Shake", "Pour" };
        private static readonly Color Done = Ui.Good;
        private static readonly Color Upcoming = new Color(0.8f, 0.79f, 0.84f);
        private static readonly Color CardBg = new Color(1f, 0.98f, 0.95f, 0.97f);

        private class Home
        {
            public Transform Parent;
            public Vector3 Pos, Scale, PivotOffset;   // PivotOffset: pivot minus bottom-centre, parent space
            public Quaternion Rot;
        }

        private readonly Dictionary<Transform, Home> m_home = new Dictionary<Transform, Home>();
        private readonly Dictionary<Transform, Coroutine> m_anims = new Dictionary<Transform, Coroutine>();
        private Material[] m_liquidMats;
        private bool m_cached;

        // State
        private int m_cart;
        private bool m_cartConfirmed;
        private readonly List<int> m_picked = new List<int>();
        private int m_glass = -1, m_garnish = -1;
        private float m_shake;
        private bool m_pouring, m_done;
        private int m_lastStep;

        // World UI
        private Image[] m_stepDots;
        private Text[] m_stepNums, m_stepLabels;
        private Image[] m_stepLinks;
        private Text m_instruction;
        private Button m_next;
        private Transform m_ring;
        private Image m_ringFill;
        private Text m_ringText, m_ringCaption;
        private Transform m_recipe;
        private Text m_recipeName, m_recipeBody, m_recipeGlass;
        private Image m_recipeSwatch;
        private Text[] m_bottleTags;
        private Image[] m_bottleTagBgs;

        private static Sprite s_circle, s_ring;

        // ---- Public state (used by SelfTest and the UI) ----
        public int Step => m_done ? 6 : !m_cartConfirmed ? 1 : m_picked.Count < 3 ? 2 : (m_glass < 0 || m_garnish < 0) ? 3 : m_shake < 1f ? 4 : 5;
        public int CartIndex => m_cart;
        public IReadOnlyList<int> Picked => m_picked;
        public int GlassIndex => m_glass;
        public int GarnishIndex => m_garnish;
        public float ShakeProgress => m_shake;
        public bool IsDone => m_done;
        public string DrinkName => BarCartData.DrinkName(m_picked, m_glass);
        public string RecipeText => m_recipeBody != null ? m_recipeBody.text : "";
        public bool RecipeVisible => m_recipe != null && m_recipe.gameObject.activeSelf;
        private bool Locked => m_shake > 0f || m_pouring || m_done;

        protected override void OnEnter()
        {
            EnsureEventSystem();
            CacheHomes();
            EnsureUi();
            ResetAll();
        }

        protected override void OnExit()
        {
            StopAllCoroutines();
            m_anims.Clear();
            m_pouring = false;
        }

        void OnDestroy()
        {
            if (m_liquidMats != null)
                foreach (var m in m_liquidMats)
                    if (m != null) Destroy(m);
        }

        void Update()
        {
            if (m_done && PedestalTop != null)
                PedestalTop.Rotate(0f, 40f * Time.deltaTime, 0f, Space.Self);
        }

        private static void EnsureEventSystem()
        {
            // The world-space Next/Pour button needs an EventSystem; full scene builds add one, single builds may not.
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
                return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ---------- Reset ----------

        public void ResetAll()
        {
            StopAllCoroutines();
            m_anims.Clear();
            WorldCard.CloseOpen();
            if (Hud != null) Hud.HideResult();

            m_cart = 0;
            m_cartConfirmed = false;
            m_picked.Clear();
            m_glass = m_garnish = -1;
            m_shake = 0f;
            m_pouring = m_done = false;

            // Garnishes first: they may be parented to a glass.
            foreach (var g in Garnishes) GoHome(g.transform);
            foreach (var g in Glasses) GoHome(g.transform);
            foreach (var b in Bottles) GoHome(b.transform);
            GoHome(Shaker.transform);
            for (int i = 0; i < Carts.Length; i++)
            {
                GoHome(Carts[i].transform);
                Carts[i].SetActive(i == 0);
            }
            CartTop.localPosition = new Vector3(CartTop.localPosition.x, CartTops[0], CartTop.localPosition.z);
            PedestalTop.localRotation = Quaternion.identity;
            for (int i = 0; i < Liquids.Length; i++)
                SetLiquid(i, 0f, Color.white);
            if (m_recipe != null) m_recipe.gameObject.SetActive(false);
            m_lastStep = 1;
            Refresh();
        }

        // ---------- Taps ----------

        public void Tap(BarCartPart part)
        {
            if (part == null) return;
            switch (part.Kind)
            {
                case BarCartKind.Cart: CycleCart(); break;
                case BarCartKind.Bottle: TapBottle(part.Index); break;
                case BarCartKind.Glass: TapGlass(part.Index); break;
                case BarCartKind.Garnish: TapGarnish(part.Index); break;
            }
        }

        private bool CheckLocked(Transform t)
        {
            if (!Locked) return false;
            Wiggle(t);
            Toast(m_done ? "Cheers! Tap New drink to mix another" : "Already shaken - finish the drink!");
            return true;
        }

        public void CycleCart()
        {
            if (CheckLocked(Carts[m_cart].transform)) return;
            Carts[m_cart].SetActive(false);
            m_cart = (m_cart + 1) % Carts.Length;
            var cart = Carts[m_cart];
            cart.SetActive(true);
            Run(cart.transform, Pop(cart.transform, m_home[cart.transform].Scale));
            Run(CartTop, MoveY(CartTop, CartTops[m_cart], 0.3f));
            Fx.Play(FxKind.Poof, cart.transform.position + transform.up * 0.15f * transform.lossyScale.y, 0.9f);
            Toast("Cart: " + BarCartData.Carts[m_cart] + "  (" + (m_cart + 1) + "/" + Carts.Length + ")");
            Refresh();
        }

        public void ConfirmCart()
        {
            if (m_cartConfirmed) return;
            m_cartConfirmed = true;
            Fx.Play(FxKind.Sparkle, Carts[m_cart].transform.position + transform.up * 0.3f * transform.lossyScale.y, 0.8f);
            Refresh();
        }

        public void TapBottle(int i)
        {
            if (i < 0 || i >= Bottles.Length) return;
            var t = Bottles[i].transform;
            if (CheckLocked(t)) return;
            m_cartConfirmed = true;
            if (m_picked.Contains(i))
            {
                m_picked.Remove(i);
                Run(t, Arc(t, m_home[t].Parent, m_home[t].Pos, m_home[t].Rot, m_home[t].Scale, 0.18f, 0.45f));
                Fx.Play(FxKind.Poof, t.position, 0.6f);
                Toast(BarCartData.Flavors[i].Name + " back on the shelf");
                LayoutBottles();
            }
            else if (m_picked.Count >= 3)
            {
                Wiggle(t);
                Toast("The cart holds 3 sodas - tap one on the cart to put it back");
                return;
            }
            else
            {
                m_picked.Add(i);
                LayoutBottles();
                Fx.Play(FxKind.Pickup, t.position + transform.up * 0.1f * transform.lossyScale.y, 0.7f);
                Toast("+ " + BarCartData.Flavors[i].Name + "  (" + m_picked.Count + "/3)");
            }
            Refresh();
        }

        private void LayoutBottles()
        {
            for (int k = 0; k < m_picked.Count; k++)
            {
                var t = Bottles[m_picked[k]].transform;
                var h = m_home[t];
                Vector3 target = BottleSlots[k] + h.PivotOffset;
                if (t.parent == CartTop && (t.localPosition - target).sqrMagnitude < 1e-6f) continue;
                Run(t, Arc(t, CartTop, target, h.Rot, h.Scale, 0.3f, 0.5f));
            }
        }

        public void TapGlass(int i)
        {
            if (i < 0 || i >= Glasses.Length) return;
            var t = Glasses[i].transform;
            if (Step == 5 && i == m_glass && !m_pouring) { Pour(); return; }
            if (CheckLocked(t)) return;
            m_cartConfirmed = true;
            if (m_glass == i)
            {
                // Put it back; its garnish waits on the cart.
                m_glass = -1;
                if (m_garnish >= 0) MoveGarnish();
                Run(t, Arc(t, m_home[t].Parent, m_home[t].Pos, m_home[t].Rot, m_home[t].Scale, 0.18f, 0.45f));
                Toast(BarCartData.Glasses[i].name + " back on the tray");
            }
            else
            {
                int old = m_glass;
                m_glass = i;
                if (m_garnish >= 0) MoveGarnish();
                if (old >= 0)
                {
                    var o = Glasses[old].transform;
                    Run(o, Arc(o, m_home[o].Parent, m_home[o].Pos, m_home[o].Rot, m_home[o].Scale, 0.18f, 0.45f));
                }
                var h = m_home[t];
                Run(t, Arc(t, CartTop, GlassSlot + h.PivotOffset, h.Rot, h.Scale, 0.3f, 0.5f));
                Fx.Play(FxKind.Sparkle, t.position, 0.6f);
                Toast("Glass: " + BarCartData.Glasses[i].name);
            }
            Refresh();
        }

        public void TapGarnish(int i)
        {
            if (i < 0 || i >= Garnishes.Length) return;
            var t = Garnishes[i].transform;
            if (CheckLocked(t)) return;
            m_cartConfirmed = true;
            if (m_garnish == i)
            {
                m_garnish = -1;
                Run(t, Arc(t, m_home[t].Parent, m_home[t].Pos, m_home[t].Rot, m_home[t].Scale, 0.18f, 0.45f));
                Toast(BarCartData.Garnishes[i] + " back on the tray");
            }
            else
            {
                if (m_garnish >= 0)
                {
                    var o = Garnishes[m_garnish].transform;
                    Run(o, Arc(o, m_home[o].Parent, m_home[o].Pos, m_home[o].Rot, m_home[o].Scale, 0.18f, 0.45f));
                }
                m_garnish = i;
                MoveGarnish();
                Fx.Play(FxKind.Pickup, t.position, 0.5f);
                Toast("Garnish: " + BarCartData.Garnishes[i]);
            }
            Refresh();
        }

        // Garnish goes onto the chosen glass's rim, or waits on the cart until a glass is chosen.
        private void MoveGarnish(bool instant = false)
        {
            var t = Garnishes[m_garnish].transform;
            var h = m_home[t];
            Transform parent;
            Vector3 pos, scale;
            Quaternion rot;
            if (m_glass < 0)
            {
                parent = CartTop;
                pos = GarnishSlot + h.PivotOffset;
                rot = h.Rot;
                scale = h.Scale;
            }
            else
            {
                parent = GlassRims[m_glass];
                // Home parents are unrotated, unit-scale children of this root, so home values are root-space.
                pos = parent.InverseTransformPoint(parent.position + transform.TransformVector(h.PivotOffset));
                rot = Quaternion.Inverse(parent.rotation) * transform.rotation * h.Rot;
                scale = h.Scale * (transform.lossyScale.x / Mathf.Max(parent.lossyScale.x, 1e-5f));
            }
            if (instant)
            {
                FinishAnim(t);
                Place(t, parent, pos, rot, scale);
            }
            else
                Run(t, Arc(t, parent, pos, rot, scale, 0.3f, 0.5f));
        }

        // ---------- Shake & pour ----------

        public void ShakeTap()
        {
            int step = Step;
            if (step < 4)
            {
                Wiggle(Shaker.transform);
                Toast(step <= 2 ? "Load 3 sodas onto the cart first" : "Pick a glass and a garnish first");
                return;
            }
            if (step >= 5)
            {
                Wiggle(Shaker.transform);
                Toast(m_done ? "Cheers! Tap New drink to mix another" : "Shaken! Now press Pour");
                return;
            }
            AddShake(0.125f, true);
        }

        public void AddShake(float amount, bool wobble)
        {
            if (Step != 4 || amount <= 0f) return;
            m_shake = Mathf.Min(1f, m_shake + amount);
            if (wobble)
            {
                Run(Shaker.transform, Wobble(Shaker.transform, 24f));
                Fx.Play(FxKind.Hit, Shaker.transform.position + transform.up * 0.2f * transform.lossyScale.y, 0.35f);
            }
            if (m_shake >= 1f)
            {
                Fx.Play(FxKind.Sparkle, Shaker.transform.position + transform.up * 0.22f * transform.lossyScale.y, 1f);
                Toast("Perfectly shaken! Press Pour");
            }
            Refresh();
        }

        public void Pour()
        {
            if (Step != 5 || m_pouring) return;
            StartCoroutine(PourRoutine());
        }

        private IEnumerator PourRoutine()
        {
            m_pouring = true;
            Refresh();
            int g = m_glass;
            var shaker = Shaker.transform;
            var sh = m_home[shaker];
            Color target = BarCartData.Blend(m_picked);
            Color start = BarCartData.Flavors[m_picked[0]].Color;

            Vector3 above = GlassSlot + new Vector3(-0.16f, 0.24f, 0f);
            yield return Run(shaker, Arc(shaker, CartTop, above, Quaternion.Euler(0f, 0f, -105f) * sh.Rot, sh.Scale, 0.12f, 0.45f));

            const float pourTime = 1.8f;
            float nextBubble = 0f;
            for (float a = 0f; a < 1f; a += Time.deltaTime / pourTime)
            {
                SetLiquid(g, a, Color.Lerp(start, target, Mathf.SmoothStep(0f, 1f, a)));
                if (a >= nextBubble)
                {
                    nextBubble += 0.18f;
                    Fx.Play(FxKind.Sparkle, LiquidTopWorld(g), 0.35f);
                }
                yield return null;
            }
            SetLiquid(g, 1f, target);

            yield return Run(shaker, Arc(shaker, sh.Parent, sh.Pos, sh.Rot, sh.Scale, 0.12f, 0.4f));
            FinishDrink(false);
            yield return new WaitForSeconds(0.9f);
            ShowResultPanel();
        }

        private void FinishDrink(bool instant)
        {
            int g = m_glass;
            var t = Glasses[g].transform;
            var h = m_home[t];
            m_pouring = false;
            m_done = true;
            SetLiquid(g, 1f, BarCartData.Blend(m_picked));
            if (instant)
            {
                StopAllCoroutines();
                m_anims.Clear();
                var sh = m_home[Shaker.transform];
                Place(Shaker.transform, sh.Parent, sh.Pos, sh.Rot, sh.Scale);
                Place(t, PedestalTop, h.PivotOffset, h.Rot, h.Scale);
                if (m_garnish >= 0)
                {
                    MoveGarnish(true);
                }
            }
            else
            {
                Run(t, Arc(t, PedestalTop, h.PivotOffset, h.Rot, h.Scale, 0.35f, 0.6f));
            }
            Fx.Play(FxKind.Confetti, PedestalTop.position + transform.up * 0.25f * transform.lossyScale.y, 1.2f);
            Fx.Play(FxKind.Magic, PedestalTop.position + transform.up * 0.1f * transform.lossyScale.y, 0.8f);
            FillRecipe();
            m_recipe.gameObject.SetActive(true);
            if (!instant) Run(m_recipe, Pop(m_recipe, Vector3.one));
            else m_recipe.localScale = Vector3.one;
            Refresh();
        }

        private void ShowResultPanel()
        {
            if (Hud == null) return;
            string body = string.Join(" + ", m_picked.Select(i => BarCartData.Flavors[i].Name)) +
                          "\nin a " + BarCartData.Glasses[m_glass].name + " with a " + BarCartData.Garnishes[m_garnish].ToLowerInvariant() +
                          "\n<size=28>0% alcohol - 100% " + BarCartData.Brand + ". Your recipe card is on the cart.</size>";
            Hud.ShowResult(DrinkName, body,
                ("New drink", ResetAll),
                ("Hub", () => { if (ShowcaseHub.Instance != null) ShowcaseHub.Instance.ReturnToHub(); }));
        }

        public void NextPressed()
        {
            int step = Step;
            if (step == 1) ConfirmCart();
            else if (step == 5) Pour();
            else if (step == 6) ResetAll();
        }

        // ---------- Liquid ----------

        private void SetLiquid(int g, float fill, Color color)
        {
            var l = Liquids[g];
            if (l == null) return;
            fill = Mathf.Clamp01(fill);
            float side = Mathf.Lerp(LiquidBottomFrac[g], 1f, fill);
            Vector3 full = LiquidFull[g];
            l.localScale = new Vector3(full.x * side, Mathf.Max(full.y * fill, 1e-4f), full.z * side);
            var r = l.GetComponent<Renderer>();
            r.enabled = fill > 0.01f;
            if (m_liquidMats != null && m_liquidMats[g] != null)
            {
                m_liquidMats[g].SetColor("_BaseColor", color);
                if (m_liquidMats[g].HasProperty("_Color")) m_liquidMats[g].SetColor("_Color", color);
            }
        }

        private Vector3 LiquidTopWorld(int g) => Liquids[g].position + Liquids[g].up * Liquids[g].lossyScale.y;

        // A frustum with its pivot at the bottom centre: height 1, top radius 0.5, bottom radius 0.5 * bottomFrac.
        private static Mesh Frustum(float bottomFrac)
        {
            const int n = 20;
            var v = new List<Vector3>();
            var tri = new List<int>();
            float rb = 0.5f * bottomFrac, rt = 0.5f;
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * rb);
                v.Add(d * rt + Vector3.up);
            }
            for (int i = 0; i < n; i++)
            {
                int b = i * 2;
                tri.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
            }
            int top = v.Count;
            v.Add(Vector3.up);
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v.Add(new Vector3(Mathf.Cos(a) * rt, 1f, Mathf.Sin(a) * rt));
            }
            for (int i = 0; i < n; i++)
                tri.AddRange(new[] { top, top + 2 + i, top + 1 + i });
            var mesh = new Mesh { name = "Liquid Frustum" };
            mesh.SetVertices(v);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- Movement helpers ----------

        private void CacheHomes()
        {
            if (m_cached) return;
            m_cached = true;
            var all = new List<Transform>();
            all.AddRange(Bottles.Select(b => b.transform));
            all.AddRange(Glasses.Select(b => b.transform));
            all.AddRange(Garnishes.Select(b => b.transform));
            all.Add(Shaker.transform);
            all.AddRange(Carts.Select(c => c.transform));
            foreach (var t in all)
            {
                var b = LocalBounds(t.gameObject, t.parent);
                m_home[t] = new Home
                {
                    Parent = t.parent, Pos = t.localPosition, Rot = t.localRotation, Scale = t.localScale,
                    PivotOffset = t.localPosition - new Vector3(b.center.x, b.min.y, b.center.z),
                };
            }
            m_liquidMats = new Material[Liquids.Length];
            for (int i = 0; i < Liquids.Length; i++)
            {
                if (Liquids[i] == null) continue;
                Liquids[i].GetComponent<MeshFilter>().sharedMesh = Frustum(LiquidBottomFrac[i]);
                m_liquidMats[i] = Liquids[i].GetComponent<Renderer>().material;
            }
        }

        // Mesh bounds of go expressed in `space` (independent of how the AR target tilts the world).
        private static Bounds LocalBounds(GameObject go, Transform space)
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
            if (!any) b = new Bounds(space.InverseTransformPoint(go.transform.position), Vector3.zero);
            return b;
        }

        private void GoHome(Transform t)
        {
            if (!m_home.TryGetValue(t, out var h)) return;
            Place(t, h.Parent, h.Pos, h.Rot, h.Scale);
        }

        private static void Place(Transform t, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = rot;
            t.localScale = scale;
        }

        // One animation per transform: a new one replaces the old, and every animation ends on
        // absolute targets, so interrupted tweens never leave drift behind.
        private Coroutine Run(Transform t, IEnumerator routine)
        {
            FinishAnim(t);
            var c = StartCoroutine(routine);
            m_anims[t] = c;
            return c;
        }

        private void FinishAnim(Transform t)
        {
            if (m_anims.TryGetValue(t, out var c) && c != null)
                StopCoroutine(c);
            m_anims.Remove(t);
        }

        private IEnumerator Arc(Transform t, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, float height, float time)
        {
            t.SetParent(parent, true);
            Vector3 p0 = t.localPosition, s0 = t.localScale;
            Quaternion r0 = t.localRotation;
            Vector3 up = parent.InverseTransformVector(transform.up * height * transform.lossyScale.y);
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                float e = Mathf.SmoothStep(0f, 1f, a);
                t.localPosition = Vector3.Lerp(p0, pos, e) + up * (4f * a * (1f - a));
                t.localRotation = Quaternion.Slerp(r0, rot, e);
                t.localScale = Vector3.Lerp(s0, scale, e);
                yield return null;
            }
            Place(t, parent, pos, rot, scale);
            m_anims.Remove(t);
        }

        private IEnumerator Pop(Transform t, Vector3 to)
        {
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.32f)
            {
                t.localScale = Vector3.LerpUnclamped(to * 0.05f, to, Tween.EaseOutBack(a));
                yield return null;
            }
            t.localScale = to;
            m_anims.Remove(t);
        }

        private IEnumerator MoveY(Transform t, float y, float time)
        {
            float y0 = t.localPosition.y;
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                var p = t.localPosition;
                t.localPosition = new Vector3(p.x, Mathf.LerpUnclamped(y0, y, Tween.EaseOutBack(a)), p.z);
                yield return null;
            }
            var q = t.localPosition;
            t.localPosition = new Vector3(q.x, y, q.z);
            m_anims.Remove(t);
        }

        // Side-to-side rock around the item's resting pose (feedback for taps that do nothing).
        private void Wiggle(Transform t)
        {
            if (!m_home.ContainsKey(t) || m_anims.ContainsKey(t)) return;
            Run(t, Wobble(t, 10f));
        }

        private IEnumerator Wobble(Transform t, float degrees)
        {
            var h = m_home[t];
            Quaternion baseRot = t.parent == h.Parent || t.parent == CartTop ? h.Rot : t.localRotation;
            Vector3 basePos = t.parent == h.Parent ? h.Pos : t.localPosition;
            Vector3 up = t.parent.InverseTransformVector(transform.up * 0.03f * transform.lossyScale.y);
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.35f)
            {
                float k = Mathf.Sin(a * Mathf.PI * 4f) * (1f - a);
                t.localRotation = Quaternion.Euler(0f, 0f, degrees * k) * baseRot;
                t.localPosition = basePos + up * Mathf.Sin(a * Mathf.PI);
                yield return null;
            }
            t.localRotation = baseRot;
            t.localPosition = basePos;
            m_anims.Remove(t);
        }

        private void Toast(string text)
        {
            if (Hud != null) Hud.Toast(text);
        }

        // ---------- World UI ----------

        private void Refresh()
        {
            int step = Step;
            if (step != m_lastStep && step == 4 && !m_done)
            {
                // Draw attention to the shaker once everything is in place.
                Run(Shaker.transform, Wobble(Shaker.transform, 16f));
                Fx.Play(FxKind.Stars, Shaker.transform.position + transform.up * 0.25f * transform.lossyScale.y, 0.6f);
            }
            m_lastStep = step;

            if (m_stepDots != null)
            {
                for (int i = 0; i < 5; i++)
                {
                    int n = i + 1;
                    bool done = n < step, current = n == step;
                    m_stepDots[i].color = done ? Done : current ? Ui.Accent : Upcoming;
                    m_stepNums[i].text = done ? "✓" : n.ToString();
                    m_stepNums[i].color = done || current ? Color.white : Ui.Muted;
                    m_stepLabels[i].color = current ? Ui.Ink : Ui.Muted;
                    m_stepLabels[i].fontStyle = current ? FontStyle.Bold : FontStyle.Normal;
                    if (i < 4) m_stepLinks[i].color = n < step ? Done : Upcoming;
                }
                m_instruction.text = Instruction(step);
                bool showNext = step == 1 || (step == 5 && !m_pouring) || step == 6;
                m_next.gameObject.SetActive(showNext);
                Ui.SetButtonLabel(m_next, step == 1 ? "Next ►" : step == 5 ? "Pour ►" : "New drink");
                m_next.GetComponent<Image>().color = step == 6 ? Ui.Accent2 : step == 5 ? Ui.Good : Ui.Accent;
            }

            if (m_ring != null)
            {
                bool show = (step == 4 || step == 5) && !m_pouring && !m_done;
                m_ring.gameObject.SetActive(show);
                m_ringFill.fillAmount = m_shake;
                m_ringFill.color = m_shake >= 1f ? Ui.Good : Ui.Accent;
                m_ringText.text = Mathf.RoundToInt(m_shake * 100f) + "%";
                m_ringCaption.text = m_shake >= 1f ? "Ready!" : "Shake!";
            }

            if (m_bottleTags != null)
                for (int i = 0; i < m_bottleTags.Length; i++)
                {
                    bool on = m_picked.Contains(i);
                    m_bottleTags[i].text = (on ? "✓ " : "") + BarCartData.Flavors[i].Name;
                    m_bottleTagBgs[i].color = on ? new Color(0.88f, 0.97f, 0.9f, 0.97f) : CardBg;
                }

            if (Hud != null)
            {
                Hud.SetHint(Hint(step));
                Hud.SetScore(step <= 5 ? "Step " + step + "/5" : "Cheers!");
            }
        }

        private string Instruction(int step)
        {
            switch (step)
            {
                case 1: return "<b>" + BarCartData.Carts[m_cart] + "</b>\nTap the cart for another style";
                case 2: return "<b>Sodas " + m_picked.Count + "/3</b>\nTap bottles on the shelf";
                case 3: return "<b>Glass & garnish</b>\n" + (m_glass < 0 ? "Glass: tap one, left" : "Garnish: tap one, right");
                case 4: return "<b>Shake it!</b>\nTap or wiggle the shaker";
                case 5: return m_pouring ? "<b>Pouring...</b>\nWatch it fizz" : "<b>Shaken!</b>\nPour into the glass";
                default: return "<b>" + DrinkName + "</b>\nRecipe card is ready";
            }
        }

        private static string Hint(int step)
        {
            switch (step)
            {
                case 1: return "Tap the cart to restyle it, then press Next";
                case 2: return "Tap 3 Lumo sodas to load the cart (tap again to put one back)";
                case 3: return "Choose a glass (left tray) and a garnish (right tray)";
                case 4: return "Tap the shaker again and again - or drag to wiggle it";
                case 5: return "Press Pour, or tap the glass";
                default: return "Your mocktail is ready - check the recipe card";
            }
        }

        private void EnsureUi()
        {
            if (s_circle == null) s_circle = MakeSprite(false);
            if (s_ring == null) s_ring = MakeSprite(true);
            if (m_stepDots == null && TrackerAnchor != null) BuildTracker();
            if (m_ring == null) BuildRing();
            if (m_recipe == null && RecipeAnchor != null) BuildRecipe();
            if (m_bottleTags == null && ShelfTagAnchor != null) BuildTags();
        }

        private static Sprite MakeSprite(bool ring)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = ring ? "Bar Ring" : "Bar Circle", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f, outer = size * 0.5f - 1f, inner = ring ? outer - size * 0.14f : -10f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private Image Dot(Transform parent, Color color, float size, Vector2 anchor, Vector2 pos, string name)
        {
            var img = Ui.Panel(parent, color, name);
            img.sprite = s_circle;
            img.raycastTarget = false;
            Ui.Place(img.rectTransform, anchor, new Vector2(size, size), pos);
            return img;
        }

        private void BuildTracker()
        {
            const float w = 1500f, h = 300f;
            var canvas = Ui.WorldCanvas("Step Tracker", TrackerAnchor, new Vector2(w, h), 1.9f);
            canvas.transform.localPosition = Vector3.zero;
            // Tilted back toward the default viewing angle so it reads like a counter sign.
            canvas.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
            var bg = Ui.Panel(canvas.transform, CardBg, "Background");
            bg.raycastTarget = false;
            Ui.Stretch(bg.rectTransform);
            var stripe = Ui.Panel(bg.transform, Ui.Accent, "Stripe");
            stripe.raycastTarget = false;
            Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(w, 14), Vector2.zero);

            m_stepDots = new Image[5];
            m_stepNums = new Text[5];
            m_stepLabels = new Text[5];
            m_stepLinks = new Image[4];
            const float x0 = 84f, dx = 140f, dotY = -40f, dot = 96f;
            for (int i = 0; i < 4; i++)
            {
                var link = Ui.Panel(bg.transform, Upcoming, "Link");
                link.raycastTarget = false;
                m_stepLinks[i] = link;
                Ui.Place(link.rectTransform, new Vector2(0f, 1f), new Vector2(dx - dot + 16, 10), new Vector2(x0 + i * dx + dot * 0.5f - 8, dotY - dot * 0.5f + 5));
            }
            for (int i = 0; i < 5; i++)
            {
                float cx = x0 + i * dx;
                m_stepDots[i] = Dot(bg.transform, Upcoming, dot, new Vector2(0f, 1f), new Vector2(cx - dot * 0.5f, dotY), "Step " + (i + 1));
                m_stepNums[i] = Ui.Label(m_stepDots[i].transform, (i + 1).ToString(), 58, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Number");
                Ui.Stretch(m_stepNums[i].rectTransform);
                m_stepLabels[i] = Ui.Label(bg.transform, StepNames[i], 40, Ui.Muted, TextAnchor.UpperCenter, FontStyle.Normal, "Name");
                Ui.Place(m_stepLabels[i].rectTransform, new Vector2(0f, 1f), new Vector2(dx, 56), new Vector2(cx - dx * 0.5f, dotY - dot - 14));
            }

            m_instruction = Ui.Label(bg.transform, "", 40, Ui.Ink, TextAnchor.MiddleLeft, FontStyle.Normal, "Instruction");
            Ui.Place(m_instruction.rectTransform, new Vector2(0f, 0.5f), new Vector2(500, 230), new Vector2(x0 + 4 * dx + 76, -4));

            m_next = Ui.Button(bg.transform, "Next ►", Ui.Accent, NextPressed, 50, "Next Button");
            Ui.Place((RectTransform)m_next.transform, new Vector2(1f, 0.5f), new Vector2(230, 150), new Vector2(-24, -4));
        }

        private void BuildRing()
        {
            var holder = new GameObject("Shake Ring").transform;
            holder.SetParent(CartTop, false);
            holder.localPosition = ShakerSlot + new Vector3(0f, 0.44f, 0f);
            holder.gameObject.AddComponent<Billboard>();
            var canvas = Ui.WorldCanvas("Canvas", holder, new Vector2(300, 360), 0.2f);
            canvas.sortingOrder = 5;
            var disc = Dot(canvas.transform, new Color(0.13f, 0.12f, 0.2f, 0.85f), 280, new Vector2(0.5f, 1f), new Vector2(0, -10), "Disc");
            var track = Dot(disc.transform, new Color(1f, 1f, 1f, 0.22f), 250, new Vector2(0.5f, 0.5f), Vector2.zero, "Track");
            track.sprite = s_ring;
            m_ringFill = Dot(disc.transform, Ui.Accent, 250, new Vector2(0.5f, 0.5f), Vector2.zero, "Fill");
            m_ringFill.sprite = s_ring;
            m_ringFill.type = Image.Type.Filled;
            m_ringFill.fillMethod = Image.FillMethod.Radial360;
            m_ringFill.fillOrigin = (int)Image.Origin360.Top;
            m_ringFill.fillClockwise = true;
            m_ringText = Ui.Label(disc.transform, "0%", 70, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Percent");
            Ui.Stretch(m_ringText.rectTransform);
            var capBg = Ui.Panel(canvas.transform, Ui.Accent, "Caption Bg");
            capBg.raycastTarget = false;
            Ui.Place(capBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(220, 60), new Vector2(0, 0));
            m_ringCaption = Ui.Label(capBg.transform, "Shake!", 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Caption");
            Ui.Stretch(m_ringCaption.rectTransform);
            m_ring = holder;
            m_ring.gameObject.SetActive(false);
        }

        private void BuildRecipe()
        {
            const float w = 640f, h = 540f;
            var holder = new GameObject("Recipe Card").transform;
            holder.SetParent(RecipeAnchor, false);
            // Full facing (not yaw-only) so the card stays readable from the steep tabletop angle.
            holder.gameObject.AddComponent<Billboard>().YawOnly = false;
            var canvas = Ui.WorldCanvas("Canvas", holder, new Vector2(w, h), 0.76f);
            canvas.sortingOrder = 6;
            var bg = Ui.Panel(canvas.transform, new Color(1f, 0.98f, 0.95f, 1f), "Background");
            bg.raycastTarget = false;
            Ui.Stretch(bg.rectTransform);
            var stripe = Ui.Panel(bg.transform, Ui.Accent, "Stripe");
            stripe.raycastTarget = false;
            Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(w, 16), Vector2.zero);

            var brand = Ui.Label(bg.transform, BarCartData.Brand.ToUpperInvariant() + "  -  RECIPE CARD", 26, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold, "Brand");
            Ui.Place(brand.rectTransform, new Vector2(0f, 1f), new Vector2(w - 60, 40), new Vector2(30, -32));
            m_recipeName = Ui.Label(bg.transform, "", 58, Ui.Ink, TextAnchor.UpperLeft, FontStyle.Bold, "Name");
            Ui.Place(m_recipeName.rectTransform, new Vector2(0f, 1f), new Vector2(w - 180, 76), new Vector2(30, -70));
            m_recipeGlass = Ui.Label(bg.transform, "", 30, Ui.Muted, TextAnchor.UpperLeft, FontStyle.Italic, "Glass");
            Ui.Place(m_recipeGlass.rectTransform, new Vector2(0f, 1f), new Vector2(w - 180, 44), new Vector2(30, -146));
            m_recipeSwatch = Dot(bg.transform, Color.white, 120, new Vector2(1f, 1f), new Vector2(-30, -50), "Swatch");
            var line = Ui.Panel(bg.transform, new Color(0.13f, 0.12f, 0.2f, 0.25f), "Divider");
            line.raycastTarget = false;
            Ui.Place(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(w - 60, 3), new Vector2(0, -200));
            m_recipeBody = Ui.Label(bg.transform, "", 32, Ui.Ink, TextAnchor.UpperLeft, FontStyle.Normal, "Ingredients");
            Ui.Place(m_recipeBody.rectTransform, new Vector2(0f, 1f), new Vector2(w - 60, 270), new Vector2(30, -214));
            var foot = Ui.Label(bg.transform, "0% alcohol  -  100% fizz", 28, Ui.Good, TextAnchor.LowerCenter, FontStyle.Bold, "Footer");
            Ui.Place(foot.rectTransform, new Vector2(0.5f, 0f), new Vector2(w - 60, 44), new Vector2(0, 22));
            m_recipe = holder;
            m_recipe.gameObject.SetActive(false);
        }

        private void FillRecipe()
        {
            if (m_recipeName == null) return;
            Color c = BarCartData.Blend(m_picked);
            m_recipeName.text = DrinkName;
            m_recipeGlass.text = BarCartData.Glasses[m_glass].name + " glass  -  " + BarCartData.Carts[m_cart] + " cart";
            m_recipeSwatch.color = c;
            var lines = m_picked.Select(i => "<color=#" + ColorUtility.ToHtmlStringRGB(BarCartData.Flavors[i].Color) + ">●</color> 60 ml " +
                                             BarCartData.Flavors[i].Name + "  <size=24><color=#77748A>" + BarCartData.Flavors[i].Note + "</color></size>").ToList();
            lines.Add("• Shake with ice, pour");
            lines.Add("• Garnish: " + BarCartData.Garnishes[m_garnish].ToLowerInvariant());
            m_recipeBody.text = string.Join("\n", lines);
        }

        private void BuildTags()
        {
            m_bottleTags = new Text[Bottles.Length];
            m_bottleTagBgs = new Image[Bottles.Length];
            for (int i = 0; i < Bottles.Length; i++)
            {
                var f = BarCartData.Flavors[i];
                float x = ShelfTagAnchor.InverseTransformPoint(m_home[Bottles[i].transform].Parent.TransformPoint(m_home[Bottles[i].transform].Pos)).x;
                var (bg, label) = Tag(ShelfTagAnchor, new Vector3(x, 0f, 0f), Quaternion.identity, f.Name, f.Color, 0.5f);
                m_bottleTags[i] = label;
                m_bottleTagBgs[i] = bg;
            }
            for (int i = 0; i < Glasses.Length && GlassTagAnchor != null; i++)
            {
                float x = GlassTagAnchor.InverseTransformPoint(Glasses[i].transform.position).x;
                Tag(GlassTagAnchor, new Vector3(x, 0f, 0f), Quaternion.Euler(50f, 0f, 0f), BarCartData.Glasses[i].name, Ui.Accent2, 0.33f);
            }
            for (int i = 0; i < Garnishes.Length && GarnishTagAnchor != null; i++)
            {
                float x = GarnishTagAnchor.InverseTransformPoint(Garnishes[i].transform.position).x;
                Tag(GarnishTagAnchor, new Vector3(x, 0f, 0f), Quaternion.Euler(50f, 0f, 0f), BarCartData.Garnishes[i], Ui.Good, 0.33f);
            }
        }

        private (Image, Text) Tag(Transform parent, Vector3 localPos, Quaternion rot, string text, Color dot, float worldWidth)
        {
            const float w = 340f, h = 84f;
            var canvas = Ui.WorldCanvas("Tag " + text, parent, new Vector2(w, h), worldWidth * 0.62f);
            canvas.transform.localPosition = localPos;
            canvas.transform.localRotation = rot;
            var bg = Ui.Panel(canvas.transform, CardBg, "Background");
            bg.raycastTarget = false;
            Ui.Stretch(bg.rectTransform);
            Dot(bg.transform, dot, 46, new Vector2(0f, 0.5f), new Vector2(14, 0), "Dot");
            var label = Ui.Label(bg.transform, text, 36, Ui.Ink, TextAnchor.MiddleLeft, FontStyle.Bold, "Label");
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(w - 80, h), new Vector2(70, 0));
            return (bg, label);
        }

        // ---------- Automated play-through ----------

        public override void DebugComplete()
        {
            if (m_done) return;
            CacheHomes();
            EnsureUi();
            m_cartConfirmed = true;
            for (int i = 0; m_picked.Count < 3 && i < Bottles.Length; i++)
                if (!m_picked.Contains(i)) m_picked.Add(i);
            if (m_glass < 0) m_glass = 0;
            if (m_garnish < 0) m_garnish = 0;
            m_shake = 1f;
            StopAllCoroutines();
            m_anims.Clear();
            for (int k = 0; k < m_picked.Count; k++)
            {
                var t = Bottles[m_picked[k]].transform;
                Place(t, CartTop, BottleSlots[k] + m_home[t].PivotOffset, m_home[t].Rot, m_home[t].Scale);
            }
            FinishDrink(true);
            ShowResultPanel();
        }

        public override IEnumerator SelfTest()
        {
            ResetAll();
            yield return new WaitForSeconds(0.3f);
            if (Step != 1) Debug.LogError("[SELFTEST] fresh state should be step 1, got " + Step);

            // 1. Cart style
            CycleCart();
            yield return new WaitForSeconds(0.4f);
            if (CartIndex != 1 || !Carts[1].activeSelf || Carts[0].activeSelf) Debug.LogError("[SELFTEST] cart swap did not show cart 2 only");
            if (Mathf.Abs(CartTop.localPosition.y - CartTops[1]) > 0.001f) Debug.LogError("[SELFTEST] cart top did not move to the new cart's height");
            NextPressed();
            if (Step != 2) Debug.LogError("[SELFTEST] Next did not move on to step 2");

            // 2. Sodas, including one change of mind
            TapBottle(0); TapBottle(2); TapBottle(4);
            yield return new WaitForSeconds(0.6f);
            TapBottle(2);
            yield return new WaitForSeconds(0.6f);
            if (Picked.Count != 2 || Picked.Contains(2)) Debug.LogError("[SELFTEST] un-picking a bottle failed");
            if (Bottles[2].transform.parent == CartTop) Debug.LogError("[SELFTEST] un-picked bottle is still on the cart");
            TapBottle(5);
            TapBottle(1);   // a fourth one must be refused
            yield return new WaitForSeconds(0.6f);
            var expected = new[] { 0, 4, 5 };
            if (!Picked.OrderBy(i => i).SequenceEqual(expected)) Debug.LogError("[SELFTEST] picked " + string.Join(",", Picked) + ", expected 0,4,5");
            foreach (int i in expected)
                if (Bottles[i].transform.parent != CartTop) Debug.LogError("[SELFTEST] " + BarCartData.Flavors[i].Name + " is not on the cart");
            if (Step != 3) Debug.LogError("[SELFTEST] three sodas should lead to step 3, got " + Step);

            // 3. Glass and garnish
            ShakeTap();
            if (ShakeProgress > 0f) Debug.LogError("[SELFTEST] shaking was allowed before glass/garnish");
            TapGlass(0);
            TapGarnish(2);
            yield return new WaitForSeconds(0.6f);
            TapGlass(1);
            yield return new WaitForSeconds(0.7f);
            if (GlassIndex != 1 || GarnishIndex != 2) Debug.LogError("[SELFTEST] glass/garnish choice wrong");
            if (Garnishes[2].transform.parent != GlassRims[1]) Debug.LogError("[SELFTEST] garnish did not follow the new glass");
            if (Glasses[0].transform.parent == CartTop) Debug.LogError("[SELFTEST] replaced glass did not go back to its tray");
            if (Step != 4) Debug.LogError("[SELFTEST] expected step 4 (shake), got " + Step);

            // 4. Shake to 100%
            int taps = 0;
            while (ShakeProgress < 1f && taps < 20)
            {
                ShakeTap();
                taps++;
                yield return new WaitForSeconds(0.12f);
            }
            if (ShakeProgress < 1f) Debug.LogError("[SELFTEST] shake did not reach 100%");
            if (m_ringText != null && m_ringText.text != "100%") Debug.LogError("[SELFTEST] ring shows " + m_ringText.text);
            TapBottle(1);
            if (Picked.Contains(1)) Debug.LogError("[SELFTEST] bottles changed after shaking");
            if (Step != 5) Debug.LogError("[SELFTEST] expected step 5 (pour), got " + Step);

            // 5. Pour
            NextPressed();
            float waited = 0f;
            while (!IsDone && waited < 8f) { waited += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(1.2f);
            if (!IsDone) Debug.LogError("[SELFTEST] pour never finished");
            if (Liquids[1].localScale.y < LiquidFull[1].y * 0.95f) Debug.LogError("[SELFTEST] liquid did not fill the glass");
            if (Glasses[1].transform.parent != PedestalTop) Debug.LogError("[SELFTEST] drink is not on the pedestal");
            if (!RecipeVisible) Debug.LogError("[SELFTEST] recipe card not shown");
            foreach (int i in expected)
                if (!RecipeText.Contains(BarCartData.Flavors[i].Name)) Debug.LogError("[SELFTEST] recipe misses " + BarCartData.Flavors[i].Name);
            if (RecipeText.Contains(BarCartData.Flavors[2].Name)) Debug.LogError("[SELFTEST] recipe lists the un-picked soda");
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel not shown");
            Debug.Log("[SELFTEST] barcart: made '" + DrinkName + "' with " + string.Join(", ", Picked.Select(i => BarCartData.Flavors[i].Name)));

            // Reset must return to an empty cart.
            ResetAll();
            yield return new WaitForSeconds(0.3f);
            if (Step != 1 || Picked.Count != 0 || GlassIndex >= 0 || GarnishIndex >= 0 || ShakeProgress > 0f || IsDone)
                Debug.LogError("[SELFTEST] reset left state behind");
            if (Bottles.Any(b => b.transform.parent == CartTop) || Glasses.Any(g => g.transform.parent != m_home[g.transform].Parent))
                Debug.LogError("[SELFTEST] reset left props on the cart");
            if (Liquids.Any(l => l.GetComponent<Renderer>().enabled)) Debug.LogError("[SELFTEST] reset left liquid in a glass");
            if (RecipeVisible || (Hud != null && Hud.ResultVisible)) Debug.LogError("[SELFTEST] reset left the recipe/result visible");

            // Finish on the completed state for the screenshot.
            DebugComplete();
            yield return new WaitForSeconds(0.5f);
        }
    }
}
