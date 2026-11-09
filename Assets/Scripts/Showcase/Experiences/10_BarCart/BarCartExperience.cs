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
    // UI split: one bottom-tray page per step (thumbnails of that step's props, "‹ Back" / "Next step ›")
    // and the cart in the world updates live with every pick. Tapping a prop jumps to its step.
    // Players can change their mind freely until they start shaking.
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

        public const int StageCount = 5;
        private static readonly string[] StepNames = { "Cart", "Sodas", "Glass", "Garnish", "Shake" };
        private static readonly string[] StageTitles = { "Cart style", "Pick 3 sodas", "Glass", "Garnish", "Shake & pour" };
        private static readonly string[] CartBlurbs = { "Warm walnut with turned legs. Timeless.", "Mint-fresh pastel frame. Pool-party ready.", "Glossy cherry lacquer. Loud in the best way." };
        private static readonly string[] GlassBlurbs = { "Tall and straight: lots of fizz, lots of ice.", "Wide and elegant: for sipping slowly.", "Short and sturdy: the everyday cooler." };
        private static readonly string[] GarnishBlurbs = { "A bright citrus wheel on the rim.", "A glossy cherry on top. Classic.", "A fresh sprig for a garden-cool nose." };
        // Free-camera framing per step (stage-local focus, distance): brings that step's props up close.
        private static readonly Vector4[] StageFrames =
        {
            new Vector4(0f, 0.3f, 0.15f, 3.0f), new Vector4(0f, 0.3f, 0.45f, 3.4f), new Vector4(-0.2f, 0.22f, 0.05f, 3.1f),
            new Vector4(0.2f, 0.22f, 0.05f, 3.1f), new Vector4(0f, 0.32f, 0.1f, 2.8f),
        };
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
        private int m_stage;                 // 0 cart, 1 sodas, 2 glass, 3 garnish, 4 shake & pour
        private int m_lastSoda;
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
        private Transform m_ring;
        private Image m_ringFill;
        private Text m_ringText, m_ringCaption;
        private Transform m_recipe;
        private Text m_recipeName, m_recipeBody, m_recipeGlass;
        private Image m_recipeSwatch;
        private WorldTag[] m_bottleTags;
        private WorldTag m_cartTag, m_glassTag, m_garnishTag;

        // Screen UI
        private List<TrayOption> m_options;
        private bool m_trayOwned;
        private GameObject m_screenUi;
        private Button m_stepButton;
        private static ShowcaseTray Tray => ShowcaseTray.Instance;
        private static readonly Color TagIdle = new Color(0.13f, 0.12f, 0.2f, 0.88f);
        private static readonly Color TagDone = new Color(0.24f, 0.6f, 0.36f, 0.92f);

        private static Sprite s_circle, s_ring;

        // ---- Public state (used by SelfTest and the UI) ----
        public int Step => m_done ? StageCount + 1 : m_stage + 1;
        public bool StepOpen => Tray != null && Tray.IsOpen && m_trayOwned;
        private bool ReadyToShake => m_picked.Count == 3 && m_glass >= 0 && m_garnish >= 0;
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
            CloseStep();
            if (m_screenUi != null) m_screenUi.SetActive(false);
        }

        void OnDisable()
        {
            if (m_screenUi != null) m_screenUi.SetActive(false);
        }

        void OnDestroy()
        {
            if (m_liquidMats != null)
                foreach (var m in m_liquidMats)
                    if (m != null) Destroy(m);
            if (m_screenUi != null) Destroy(m_screenUi);
        }

        void Update()
        {
            if (m_done && PedestalTop != null)
                PedestalTop.Rotate(0f, 40f * Time.deltaTime, 0f, Space.Self);
            // The front step sign is for when the tray is hidden (AR); the open tray already says the step.
            if (TrackerAnchor != null && TrackerAnchor.gameObject.activeSelf == StepOpen)
                TrackerAnchor.gameObject.SetActive(!StepOpen);
            if (m_stepButton != null)
            {
                bool show = !m_done && !m_pouring && (Tray == null || !Tray.IsOpen) && (Hud == null || !Hud.ResultVisible);
                if (m_stepButton.gameObject.activeSelf != show) m_stepButton.gameObject.SetActive(show);
            }
        }

        private static void EnsureEventSystem()
        {
            // Screen buttons need an EventSystem; full scene builds add one, single builds may not.
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
            m_stage = 0;
            m_lastSoda = 0;
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
            GoToStage(0);
        }

        // ---------- Steps (tray pages) ----------

        // Opens the tray page for a step and frames its props. Glass and garnish always have a pick
        // (the first one, or `pick`) so the cart in the world never disagrees with the tray.
        public void GoToStage(int stage, int pick = -1)
        {
            if (m_done) return;
            m_stage = Mathf.Clamp(stage, 0, StageCount - 1);
            if (m_stage == 2 && m_glass < 0) PickGlass(Mathf.Max(0, pick));
            if (m_stage == 3 && m_garnish < 0) PickGarnish(Mathf.Max(0, pick));
            var f = StageFrames[m_stage];
            FreeCamera.Instance?.Frame(new Vector3(f.x, f.y, f.z), f.w, 44f);
            OpenStep();
        }

        public void OpenStep()
        {
            if (Tray == null || m_done) { Refresh(); return; }
            var o = new List<TrayOption>();
            int selected = 0;
            System.Action<int> onSelect = null;
            var back = ("‹ Back", (System.Action)Back);
            var next = ("Next step ›", (System.Action)Next);
            switch (m_stage)
            {
                case 0:
                    for (int i = 0; i < Carts.Length; i++)
                        o.Add(new TrayOption(BarCartData.Carts[i], "Lumo bar cart", CartBlurbs[i % CartBlurbs.Length], Carts[i]));
                    selected = m_cart;
                    onSelect = SetCart;
                    break;
                case 1:
                    for (int i = 0; i < Bottles.Length; i++)
                        o.Add(new TrayOption(BarCartData.Flavors[i].Name, null, BarCartData.Flavors[i].Note + "\n" + BarCartData.Brand + " · 330 ml", Bottles[i].gameObject));
                    selected = m_lastSoda;
                    onSelect = ToggleBottle;
                    break;
                case 2:
                    for (int i = 0; i < Glasses.Length; i++)
                        o.Add(new TrayOption(BarCartData.Glasses[i].name, "Makes it a " + BarCartData.Glasses[i].word, GlassBlurbs[i % GlassBlurbs.Length], Glasses[i].gameObject));
                    selected = m_glass;
                    onSelect = PickGlass;
                    break;
                case 3:
                    for (int i = 0; i < Garnishes.Length; i++)
                        o.Add(new TrayOption(BarCartData.Garnishes[i], null, GarnishBlurbs[i % GarnishBlurbs.Length], Garnishes[i].gameObject));
                    selected = m_garnish;
                    onSelect = PickGarnish;
                    break;
                default:
                    o.Add(new TrayOption("Lumo Shaker", null, "Press Shake! (or grab and wiggle the shaker) until it's 100%, then Pour.", Shaker.gameObject));
                    if (m_glass >= 0)
                        o.Add(new TrayOption("Your " + BarCartData.Glasses[m_glass].name, DrinkName, string.Join(" + ", m_picked.Select(i => BarCartData.Flavors[i].Name)), Glasses[m_glass].gameObject));
                    break;
            }
            m_options = o;
            m_trayOwned = true;
            if (m_stage == 0) Tray.Open(StageTitle(), o, selected, onSelect, next);
            else if (m_stage < 4) Tray.Open(StageTitle(), o, selected, onSelect, back, next);
            else Tray.Open(StageTitle(), o, 0, null, back, ("Shake!", (System.Action)ShakeTap), ("Pour ›", (System.Action)Pour));
            Refresh();
        }

        public void Next()
        {
            if (m_done || m_pouring) return;
            switch (m_stage)
            {
                case 0: GoToStage(1); break;
                case 1:
                    if (m_picked.Count < 3) { Toast("Pick 3 sodas first (" + m_picked.Count + "/3)"); return; }
                    GoToStage(2);
                    break;
                case 2: GoToStage(3); break;
                case 3:
                    if (m_picked.Count < 3) { Toast("The cart still needs 3 sodas"); GoToStage(1); return; }
                    GoToStage(4);
                    break;
            }
        }

        public void Back()
        {
            if (m_stage == 0 || m_done) return;
            if (Locked) { Toast("Already shaken - pour it!"); return; }
            GoToStage(m_stage - 1);
        }

        private void CloseStep()
        {
            if (m_trayOwned && Tray != null && Tray.IsOpen) Tray.Close();
            m_trayOwned = false;
        }

        private void OnTrayClosed()
        {
            if (!m_trayOwned) return;
            m_trayOwned = false;
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
        }

        private string StageTitle()
        {
            if (m_pouring) return "Step 5/5  ·  Pouring...";
            string t = "Step " + (m_stage + 1) + "/" + StageCount + "  ·  " + StageTitles[m_stage];
            if (m_stage == 1) t += "  (" + m_picked.Count + "/3)";
            return t;
        }

        // Selecting a tile of the open step, or applying directly if the tray isn't showing it.
        private void Choose(int stage, int index, System.Action<int> apply)
        {
            if (StepOpen && m_stage == stage) Tray.Select(index);
            else apply(index);
        }

        // ---------- Taps (world props jump to their step) ----------

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
            if (m_stage != 0 || !StepOpen) GoToStage(0);
            Choose(0, (m_cart + 1) % Carts.Length, SetCart);
        }

        public void SetCart(int index)
        {
            if (index < 0 || index >= Carts.Length) return;
            if (index == m_cart) { Wiggle(Carts[m_cart].transform); return; }
            if (CheckLocked(Carts[m_cart].transform)) return;
            Carts[m_cart].SetActive(false);
            m_cart = index;
            var cart = Carts[m_cart];
            cart.SetActive(true);
            Run(cart.transform, Pop(cart.transform, m_home[cart.transform].Scale));
            Run(CartTop, MoveY(CartTop, CartTops[m_cart], 0.3f));
            Fx.Play(FxKind.Poof, cart.transform.position + transform.up * 0.15f * transform.lossyScale.y, 0.9f);
            Refresh();
        }

        public void TapBottle(int i)
        {
            if (i < 0 || i >= Bottles.Length) return;
            if (CheckLocked(Bottles[i].transform)) return;
            if (m_stage != 1 || !StepOpen) { m_lastSoda = i; GoToStage(1); }
            Choose(1, i, ToggleBottle);
        }

        // Tray tile in the soda step: on the cart / back to the shelf.
        public void ToggleBottle(int i)
        {
            if (i < 0 || i >= Bottles.Length) return;
            var t = Bottles[i].transform;
            if (CheckLocked(t)) return;
            m_lastSoda = i;
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
                Toast("The cart holds 3 - tap a √ soda to put it back");
                Refresh();
                return;
            }
            else
            {
                m_picked.Add(i);
                LayoutBottles();
                Fx.Play(FxKind.Pickup, t.position + transform.up * 0.1f * transform.lossyScale.y, 0.7f);
                if (m_picked.Count == 3) Toast("Cart loaded! Next step ›");
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
            if (m_stage == 4 && m_shake >= 1f && i == m_glass && !m_pouring && !m_done) { Pour(); return; }
            if (CheckLocked(t)) return;
            if (m_stage != 2 || !StepOpen) GoToStage(2, i);
            Choose(2, i, PickGlass);
        }

        // The chosen glass rides on the cart (its garnish follows); the old one goes back to the tray.
        public void PickGlass(int i)
        {
            if (i < 0 || i >= Glasses.Length) return;
            var t = Glasses[i].transform;
            if (CheckLocked(t)) return;
            if (m_glass == i) { Refresh(); return; }
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
            Refresh();
        }

        public void TapGarnish(int i)
        {
            if (i < 0 || i >= Garnishes.Length) return;
            if (CheckLocked(Garnishes[i].transform)) return;
            if (m_stage != 3 || !StepOpen) GoToStage(3, i);
            Choose(3, i, PickGarnish);
        }

        public void PickGarnish(int i)
        {
            if (i < 0 || i >= Garnishes.Length) return;
            var t = Garnishes[i].transform;
            if (CheckLocked(t)) return;
            if (m_garnish == i) { Refresh(); return; }
            if (m_garnish >= 0)
            {
                var o = Garnishes[m_garnish].transform;
                Run(o, Arc(o, m_home[o].Parent, m_home[o].Pos, m_home[o].Rot, m_home[o].Scale, 0.18f, 0.45f));
            }
            m_garnish = i;
            MoveGarnish();
            Fx.Play(FxKind.Pickup, t.position, 0.5f);
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
            if (m_done || m_pouring)
            {
                Wiggle(Shaker.transform);
                if (m_done) Toast("Cheers! Tap New drink to mix another");
                return;
            }
            if (!ReadyToShake)
            {
                Wiggle(Shaker.transform);
                // Send the player straight to whatever is missing.
                if (m_picked.Count < 3) { Toast("Load 3 sodas onto the cart first"); GoToStage(1); }
                else if (m_glass < 0) { Toast("Pick a glass first"); GoToStage(2); }
                else { Toast("Pick a garnish first"); GoToStage(3); }
                return;
            }
            if (m_shake >= 1f)
            {
                Wiggle(Shaker.transform);
                Toast("Shaken! Now press Pour");
                return;
            }
            AddShake(0.2f, true);
        }

        public void AddShake(float amount, bool wobble)
        {
            if (!ReadyToShake || m_done || m_pouring || m_shake >= 1f || amount <= 0f) return;
            if (m_stage != 4 || !StepOpen) GoToStage(4);
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
            if (m_done || m_pouring) return;
            if (m_shake < 1f) { Toast(ReadyToShake ? "Shake it to 100% first" : "Finish the earlier steps first"); return; }
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
            bool shakeTime = m_stage == 4 && !m_done;
            if (step != m_lastStep && shakeTime)
            {
                // Draw attention to the shaker once everything is in place.
                Run(Shaker.transform, Wobble(Shaker.transform, 16f));
                Fx.Play(FxKind.Stars, Shaker.transform.position + transform.up * 0.25f * transform.lossyScale.y, 0.6f);
            }
            m_lastStep = step;

            if (m_stepDots != null)
            {
                for (int i = 0; i < StageCount; i++)
                {
                    bool done = m_done || StageDone(i), current = !m_done && i == m_stage;
                    m_stepDots[i].color = current ? Ui.Accent : done ? Done : Upcoming;
                    m_stepNums[i].text = done && !current ? "√" : (i + 1).ToString();
                    m_stepNums[i].color = done || current ? Color.white : Ui.Muted;
                    m_stepLabels[i].color = current ? Ui.Ink : Ui.Muted;
                    m_stepLabels[i].fontStyle = current ? FontStyle.Bold : FontStyle.Normal;
                    if (i < StageCount - 1) m_stepLinks[i].color = m_done || StageDone(i) ? Done : Upcoming;
                }
                m_instruction.text = Instruction();
            }

            if (m_ring != null)
            {
                m_ring.gameObject.SetActive(shakeTime && !m_pouring && ReadyToShake);
                m_ringFill.fillAmount = m_shake;
                m_ringFill.color = m_shake >= 1f ? Ui.Good : Ui.Accent;
                m_ringText.text = Mathf.RoundToInt(m_shake * 100f) + "%";
                m_ringCaption.text = m_shake >= 1f ? "Ready!" : "Shake!";
            }

            RefreshTags();
            RefreshStep();

            if (Hud != null)
            {
                Hud.SetHint(m_done ? "" : "Tap a prop or open the steps to build your drink");
                Hud.SetScore(m_done ? "Cheers!" : "Step " + step + "/" + StageCount);
            }
            if (m_stepButton != null)
                Ui.SetButtonLabel(m_stepButton, "Step " + Mathf.Min(step, StageCount) + "/" + StageCount + "  ·  " + StepNames[Mathf.Min(step, StageCount) - 1] + "  ›");
        }

        private bool StageDone(int i)
        {
            switch (i)
            {
                case 0: return m_stage > 0 || m_picked.Count > 0;
                case 1: return m_picked.Count == 3;
                case 2: return m_glass >= 0;
                case 3: return m_garnish >= 0;
                default: return m_shake >= 1f;
            }
        }

        // Tray texts, √ badges and button states for the open step.
        private void RefreshStep()
        {
            if (!StepOpen || m_options == null) return;
            Tray.SetTitle(StageTitle());
            switch (m_stage)
            {
                case 0:
                    for (int i = 0; i < m_options.Count; i++) m_options[i].Badge = i == m_cart ? "√" : null;
                    break;
                case 1:
                    for (int i = 0; i < m_options.Count; i++)
                    {
                        bool on = m_picked.Contains(i);
                        m_options[i].Badge = on ? "√" : null;
                        m_options[i].Price = on ? "√ On the cart · tap to remove" : m_picked.Count >= 3 ? "Cart is full" : "Tap to add to the cart";
                    }
                    Tray.SetAction(1, "Next step ›", m_picked.Count == 3 ? Ui.Good : Ui.Muted);
                    break;
                case 2:
                    for (int i = 0; i < m_options.Count; i++) m_options[i].Badge = i == m_glass ? "√" : null;
                    break;
                case 3:
                    for (int i = 0; i < m_options.Count; i++)
                    {
                        m_options[i].Badge = i == m_garnish ? "√" : null;
                        m_options[i].Price = i == m_garnish ? "√ On the glass" : "Tap to garnish";
                    }
                    break;
                default:
                    m_options[0].Price = m_pouring ? "Pouring..." : m_shake >= 1f ? "Shaken 100% · ready to pour" : "Shaken " + Mathf.RoundToInt(m_shake * 100f) + "%";
                    bool ready = m_shake >= 1f && !m_pouring;
                    Tray.SetAction(0, "‹ Back", Locked ? Ui.Muted : Ui.Accent, !m_pouring);
                    Tray.SetAction(1, m_shake >= 1f ? "√ Shaken" : "Shake!", m_shake >= 1f || m_pouring ? Ui.Muted : Ui.Accent, !m_pouring);
                    Tray.SetAction(2, "Pour ›", ready ? Ui.Good : Ui.Muted, !m_pouring);
                    break;
            }
            Tray.Refresh();
        }

        // World tags: names over the sodas (√ once on the cart), and the current step's area pulses.
        private void RefreshTags()
        {
            if (m_bottleTags != null)
                for (int i = 0; i < m_bottleTags.Length; i++)
                {
                    if (m_bottleTags[i] == null) continue;
                    bool on = m_picked.Contains(i);
                    m_bottleTags[i].Set((on ? "√ " : "") + BarCartData.Flavors[i].Name, on ? TagDone : TagIdle);
                    m_bottleTags[i].Show(!m_done);
                }
            if (m_cartTag != null)
            {
                m_cartTag.Set(BarCartData.Carts[m_cart]);
                m_cartTag.Show(m_stage == 0 && !m_done);
                m_cartTag.Pulse(m_stage == 0);
            }
            if (m_glassTag != null)
            {
                m_glassTag.Set(m_glass >= 0 ? "√ " + BarCartData.Glasses[m_glass].name : "Glasses", m_glass >= 0 ? TagDone : TagIdle);
                m_glassTag.Show(!m_done && m_stage < 4);
                m_glassTag.Pulse(m_stage == 2);
            }
            if (m_garnishTag != null)
            {
                m_garnishTag.Set(m_garnish >= 0 ? "√ " + BarCartData.Garnishes[m_garnish] : "Garnish", m_garnish >= 0 ? TagDone : TagIdle);
                m_garnishTag.Show(!m_done && m_stage < 4);
                m_garnishTag.Pulse(m_stage == 3);
            }
        }

        private string Instruction()
        {
            if (m_done) return "<b>" + DrinkName + "</b>\nRecipe card is ready";
            switch (m_stage)
            {
                case 0: return "<b>" + BarCartData.Carts[m_cart] + "</b>\nChoose a cart style";
                case 1: return "<b>Sodas " + m_picked.Count + "/3</b>\nLoad three Lumo sodas";
                case 2: return "<b>Glass</b>\n" + (m_glass >= 0 ? BarCartData.Glasses[m_glass].name : "Pick a glass");
                case 3: return "<b>Garnish</b>\n" + (m_garnish >= 0 ? BarCartData.Garnishes[m_garnish] : "Pick a garnish");
                default: return m_pouring ? "<b>Pouring...</b>\nWatch it fizz" : m_shake >= 1f ? "<b>Shaken!</b>\nPour into the glass" : "<b>Shake it!</b>\nShake to 100%";
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
            if (m_screenUi == null)
            {
                // Big "reopen the steps" button for when the tray is hidden (scene root: overlay canvas).
                m_screenUi = new GameObject("BarCart Screen UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = m_screenUi.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 45;   // under the HUD (toast/result draw on top)
                var scaler = m_screenUi.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;
                m_stepButton = Ui.Button(m_screenUi.transform, "Steps  ›", Ui.Accent, () => GoToStage(m_stage), 44, "Step Button");
                Ui.Round(m_stepButton.GetComponent<Image>(), 60f);
                m_stepButton.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -6f);
                // Bottom-left: clear of the HUD hint above and the Look button at the bottom right.
                Ui.Place((RectTransform)m_stepButton.transform, new Vector2(0f, 0f), new Vector2(600, 130), new Vector2(30, 40));
            }
            m_screenUi.SetActive(true);
            if (Tray != null)
            {
                Tray.Closed -= OnTrayClosed;
                Tray.Closed += OnTrayClosed;
            }
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

            m_stepDots = new Image[StageCount];
            m_stepNums = new Text[StageCount];
            m_stepLabels = new Text[StageCount];
            m_stepLinks = new Image[StageCount - 1];
            const float x0 = 84f, dx = 140f, dotY = -40f, dot = 96f;
            for (int i = 0; i < StageCount - 1; i++)
            {
                var link = Ui.Panel(bg.transform, Upcoming, "Link");
                link.raycastTarget = false;
                m_stepLinks[i] = link;
                Ui.Place(link.rectTransform, new Vector2(0f, 1f), new Vector2(dx - dot + 16, 10), new Vector2(x0 + i * dx + dot * 0.5f - 8, dotY - dot * 0.5f + 5));
            }
            for (int i = 0; i < StageCount; i++)
            {
                float cx = x0 + i * dx;
                m_stepDots[i] = Dot(bg.transform, Upcoming, dot, new Vector2(0f, 1f), new Vector2(cx - dot * 0.5f, dotY), "Step " + (i + 1));
                m_stepNums[i] = Ui.Label(m_stepDots[i].transform, (i + 1).ToString(), 58, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Number");
                Ui.Stretch(m_stepNums[i].rectTransform);
                m_stepLabels[i] = Ui.Label(bg.transform, StepNames[i], 40, Ui.Muted, TextAnchor.UpperCenter, FontStyle.Normal, "Name");
                Ui.Place(m_stepLabels[i].rectTransform, new Vector2(0f, 1f), new Vector2(dx, 56), new Vector2(cx - dx * 0.5f, dotY - dot - 14));
            }

            // Read-only: choosing and acting happen in the screen tray.
            m_instruction = Ui.Label(bg.transform, "", 50, Ui.Ink, TextAnchor.MiddleLeft, FontStyle.Normal, "Instruction");
            Ui.Place(m_instruction.rectTransform, new Vector2(0f, 0.5f), new Vector2(w - (x0 + 4 * dx + 90) - 30, 240), new Vector2(x0 + 4 * dx + 90, -4));
            m_instruction.resizeTextForBestFit = true;
            m_instruction.resizeTextMinSize = 34;
            m_instruction.resizeTextMaxSize = 50;
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
            var canvas = Ui.WorldCanvas("Canvas", holder, new Vector2(w, h), 0.9f);
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
            m_recipeGlass.text = BarCartData.Glasses[m_glass].name + " glass  -  " + BarCartData.Carts[m_cart];
            m_recipeSwatch.color = c;
            var lines = m_picked.Select(i => "<color=#" + ColorUtility.ToHtmlStringRGB(BarCartData.Flavors[i].Color) + ">●</color> 60 ml " +
                                             BarCartData.Flavors[i].Name + "  <size=24><color=#77748A>" + BarCartData.Flavors[i].Note + "</color></size>").ToList();
            lines.Add("• Shake with ice, pour");
            lines.Add("• Garnish: " + BarCartData.Garnishes[m_garnish].ToLowerInvariant());
            m_recipeBody.text = string.Join("\n", lines);
        }

        private void BuildTags()
        {
            m_bottleTags = new WorldTag[Bottles.Length];
            for (int i = 0; i < Bottles.Length; i++)
            {
                var h = m_home[Bottles[i].transform];
                float x = ShelfTagAnchor.InverseTransformPoint(h.Parent.TransformPoint(h.Pos)).x;
                m_bottleTags[i] = WorldTag.Create(ShelfTagAnchor, new Vector3(x, 0f, -0.02f), BarCartData.Flavors[i].Name, 0.075f);
            }
            if (Glasses.Length > 0)
                m_glassTag = WorldTag.Create(m_home[Glasses[0].transform].Parent, new Vector3(0f, 0.3f, 0f), "Glasses", 0.1f);
            if (Garnishes.Length > 0)
                m_garnishTag = WorldTag.Create(m_home[Garnishes[0].transform].Parent, new Vector3(0f, 0.22f, 0f), "Garnish", 0.1f);
            var cartTagAnchor = new GameObject("Cart Tag Anchor").transform;
            cartTagAnchor.SetParent(CartTop.parent, false);
            cartTagAnchor.localPosition = new Vector3(CartTop.localPosition.x, 0.6f, CartTop.localPosition.z - 0.1f);
            m_cartTag = WorldTag.Create(cartTagAnchor, Vector3.zero, BarCartData.Carts[0], 0.1f);
        }

        // ---------- Automated play-through ----------

        public override void DebugComplete()
        {
            if (m_done) return;
            CacheHomes();
            EnsureUi();
            for (int i = 0; m_picked.Count < 3 && i < Bottles.Length; i++)
                if (!m_picked.Contains(i)) m_picked.Add(i);
            if (m_glass < 0) m_glass = 0;
            if (m_garnish < 0) m_garnish = 0;
            m_shake = 1f;
            m_stage = StageCount - 1;
            StopAllCoroutines();
            m_anims.Clear();
            for (int k = 0; k < m_picked.Count; k++)
            {
                var t = Bottles[m_picked[k]].transform;
                Place(t, CartTop, BottleSlots[k] + m_home[t].PivotOffset, m_home[t].Rot, m_home[t].Scale);
            }
            CloseStep();
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            FinishDrink(true);
            ShowResultPanel();
        }

        public override IEnumerator SelfTest()
        {
            ResetAll();
            yield return new WaitForSeconds(0.7f);
            if (Step != 1) Debug.LogError("[SELFTEST] fresh state should be step 1, got " + Step);
            if (!StepOpen || Tray.Options.Count != Carts.Length) Debug.LogError("[SELFTEST] cart step tray not open with " + Carts.Length + " styles");
            Snapshot("tray");

            // 1. Cart style from the tray.
            Tray.Select(1);
            yield return new WaitForSeconds(0.4f);
            if (CartIndex != 1 || !Carts[1].activeSelf || Carts[0].activeSelf) Debug.LogError("[SELFTEST] cart swap did not show cart 2 only");
            if (Mathf.Abs(CartTop.localPosition.y - CartTops[1]) > 0.001f) Debug.LogError("[SELFTEST] cart top did not move to the new cart's height");
            Tray.Press(0);   // "Next step ›"
            if (Step != 2 || !StepOpen || Tray.Options.Count != Bottles.Length) Debug.LogError("[SELFTEST] Next did not open the soda step");
            Tray.Press(1);   // Next without sodas must be refused
            if (Step != 2) Debug.LogError("[SELFTEST] moved on without 3 sodas");

            // 2. Sodas from tray tiles, one change of mind, one tap in the world.
            Tray.Select(0); Tray.Select(2); Tray.Select(4);
            yield return new WaitForSeconds(0.6f);
            Tray.Select(2);
            yield return new WaitForSeconds(0.6f);
            if (Picked.Count != 2 || Picked.Contains(2)) Debug.LogError("[SELFTEST] un-picking a soda failed");
            if (Bottles[2].transform.parent == CartTop) Debug.LogError("[SELFTEST] un-picked bottle is still on the cart");
            TapBottle(5);
            if (Tray.Selected != 5) Debug.LogError("[SELFTEST] tapping a bottle did not select it in the tray");
            TapBottle(1);   // a fourth one must be refused
            yield return new WaitForSeconds(0.6f);
            var expected = new[] { 0, 4, 5 };
            if (!Picked.OrderBy(i => i).SequenceEqual(expected)) Debug.LogError("[SELFTEST] picked " + string.Join(",", Picked) + ", expected 0,4,5");
            foreach (int i in expected)
            {
                if (Bottles[i].transform.parent != CartTop) Debug.LogError("[SELFTEST] " + BarCartData.Flavors[i].Name + " is not on the cart");
                if (Tray.Options[i].Badge != "√") Debug.LogError("[SELFTEST] tray has no √ on " + BarCartData.Flavors[i].Name);
            }
            Snapshot("sodas");

            // 3. Glass: the step pre-picks one so the cart is never empty-handed.
            Tray.Press(1);   // "Next step ›"
            yield return new WaitForSeconds(0.6f);
            if (Step != 3 || GlassIndex != 0) Debug.LogError("[SELFTEST] glass step: step " + Step + ", glass " + GlassIndex);
            if (Glasses[0].transform.parent != CartTop) Debug.LogError("[SELFTEST] pre-picked glass is not on the cart");
            ShakeTap();   // too early: jumps to the missing garnish step instead
            if (ShakeProgress > 0f) Debug.LogError("[SELFTEST] shaking was allowed before the garnish");
            if (Step != 4) Debug.LogError("[SELFTEST] early shake did not jump to the garnish step");
            Tray.Press(0);   // "‹ Back" to the glass
            if (Step != 3) Debug.LogError("[SELFTEST] Back did not return to the glass step");
            Tray.Select(1);
            Tray.Press(1);   // "Next step ›" -> garnish
            Tray.Select(2);
            yield return new WaitForSeconds(0.7f);
            if (GlassIndex != 1 || GarnishIndex != 2) Debug.LogError("[SELFTEST] glass/garnish choice wrong (" + GlassIndex + ", " + GarnishIndex + ")");
            if (Garnishes[2].transform.parent != GlassRims[1]) Debug.LogError("[SELFTEST] garnish did not follow the new glass");
            if (Glasses[0].transform.parent == CartTop) Debug.LogError("[SELFTEST] replaced glass did not go back to its tray");
            Snapshot("garnish");

            // 4. Shake to 100% with the big button.
            Tray.Press(1);
            if (Step != 5) Debug.LogError("[SELFTEST] expected step 5 (shake), got " + Step);
            Tray.Press(2);   // Pour before shaking must be refused
            if (m_pouring || IsDone) Debug.LogError("[SELFTEST] poured before shaking");
            int taps = 0;
            while (ShakeProgress < 1f && taps < 20)
            {
                Tray.Press(1);   // "Shake!"
                taps++;
                yield return new WaitForSeconds(0.12f);
            }
            if (ShakeProgress < 1f) Debug.LogError("[SELFTEST] shake did not reach 100%");
            if (m_ringText != null && m_ringText.text != "100%") Debug.LogError("[SELFTEST] ring shows " + m_ringText.text);
            TapBottle(1);
            if (Picked.Contains(1)) Debug.LogError("[SELFTEST] bottles changed after shaking");
            Tray.Press(0);   // Back is locked once shaken
            if (Step != 5) Debug.LogError("[SELFTEST] went back after shaking");
            yield return new WaitForSeconds(0.5f);
            Snapshot("shake");

            // 5. Pour
            Tray.Press(2);
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
            if (Tray.IsOpen) Debug.LogError("[SELFTEST] tray still open on the result");
            Debug.Log("[SELFTEST] barcart: made '" + DrinkName + "' with " + string.Join(", ", Picked.Select(i => BarCartData.Flavors[i].Name)));

            // Reset must return to an empty cart and the first step.
            ResetAll();
            yield return new WaitForSeconds(0.3f);
            if (Step != 1 || Picked.Count != 0 || GlassIndex >= 0 || GarnishIndex >= 0 || ShakeProgress > 0f || IsDone)
                Debug.LogError("[SELFTEST] reset left state behind");
            if (Bottles.Any(b => b.transform.parent == CartTop) || Glasses.Any(g => g.transform.parent != m_home[g.transform].Parent))
                Debug.LogError("[SELFTEST] reset left props on the cart");
            if (Liquids.Any(l => l.GetComponent<Renderer>().enabled)) Debug.LogError("[SELFTEST] reset left liquid in a glass");
            if (RecipeVisible || (Hud != null && Hud.ResultVisible)) Debug.LogError("[SELFTEST] reset left the recipe/result visible");
            if (!StepOpen) Debug.LogError("[SELFTEST] reset did not reopen the first step");

            // Finish on the completed state for the screenshot.
            DebugComplete();
            yield return new WaitForSeconds(0.5f);
        }
    }
}
