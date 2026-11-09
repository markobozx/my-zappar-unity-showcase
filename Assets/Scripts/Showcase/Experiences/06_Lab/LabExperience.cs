using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Lab
{
    // Little Lab: pour three ingredient vials into the big beaker. Every 3-ingredient mix reacts with
    // its own colour, effect and formula; Aloe + Citrus + Spark (any order) is the hero formula.
    // UI split: the bottom tray is the ingredient rack (thumbnails, info, big "Add" / "Empty"
    // buttons) and shows the result after each reaction; the world shows what's happening (tags over
    // the vials and the beaker, the MIX read-out, the clue board). Tapping a vial pours it directly.
    public class LabExperience : ShowcaseExperience
    {
        public const int MixSize = 3;
        const string HintText = "Tap the vials or open the rack to mix 3 ingredients.\nThe clue board hints at the hero formula!";

        [SerializeField] private LabVial[] m_vials;
        [SerializeField] private Transform m_beaker;
        [SerializeField] private Transform m_beakerMouth;
        [SerializeField] private Transform m_liquid;
        [SerializeField] private float m_liquidBottom = 0.02f;
        [SerializeField] private float m_liquidMax = 0.34f;
        [SerializeField] private Transform m_stream;
        [SerializeField] private LabResetButton m_resetButton;
        [SerializeField] private Image[] m_mixSlots;
        [SerializeField] private Text m_mixStatus;

        private readonly List<int> m_mix = new List<int>();
        private readonly HashSet<int> m_found = new HashSet<int>();
        private Renderer m_liquidRenderer, m_streamRenderer;
        private MaterialPropertyBlock m_block;
        private Coroutine m_routine;
        private Color m_liquidColor;
        private float m_level;
        private bool m_busy, m_reacted, m_won;
        private int m_attempts;
        private LabFormula m_last;
        private Vector3 m_beakerScale;

        // Tray + world tags
        private enum TrayMode { None, Rack, Result }
        private TrayMode m_mode;
        private List<TrayOption> m_options;
        private WorldTag[] m_vialTags;
        private WorldTag m_beakerTag;
        private GameObject m_screenUi;
        private Button m_rackButton;
        private readonly List<Material> m_ownedMats = new List<Material>();
        private readonly Dictionary<Color, Texture2D> m_swatches = new Dictionary<Color, Texture2D>();
        private static ShowcaseTray Tray => ShowcaseTray.Instance;
        private static readonly Color TagIdle = new Color(0.13f, 0.12f, 0.2f, 0.88f);

        public int Attempts => m_attempts;
        public bool Won => m_won;
        public bool Reacted => m_reacted;
        public bool Busy => m_busy;
        public int MixCount => m_mix.Count;
        public LabFormula LastFormula => m_last;
        public bool RackOpen => Tray != null && Tray.IsOpen && m_mode == TrayMode.Rack;
        public bool ResultOpen => Tray != null && Tray.IsOpen && m_mode == TrayMode.Result;

        void Awake()
        {
            m_block = new MaterialPropertyBlock();
            if (m_liquid != null) m_liquidRenderer = m_liquid.GetComponent<Renderer>();
            if (m_stream != null) m_streamRenderer = m_stream.GetComponent<Renderer>();
            if (m_beaker != null) m_beakerScale = m_beaker.localScale;
        }

        protected override void OnEnter()
        {
            EnsureUi();
            ResetAll();
            if (Hud != null) Hud.SetHint(HintText);
            OpenRack(0);
        }

        protected override void OnExit()
        {
            CloseTray();
            ResetAll();
            if (m_screenUi != null) m_screenUi.SetActive(false);
        }

        void OnDisable()
        {
            if (m_screenUi != null) m_screenUi.SetActive(false);
        }

        void OnDestroy()
        {
            if (m_screenUi != null) Destroy(m_screenUi);
            foreach (var m in m_ownedMats) if (m != null) Destroy(m);
            foreach (var t in m_swatches.Values) if (t != null) Destroy(t);
        }

        void Update()
        {
            if (m_rackButton != null)
            {
                bool show = !m_won && (Tray == null || !Tray.IsOpen) && (Hud == null || !Hud.ResultVisible);
                if (m_rackButton.gameObject.activeSelf != show) m_rackButton.gameObject.SetActive(show);
            }
        }

        private void ResetAll()
        {
            StopRoutine();
            WorldCard.CloseOpen();
            m_attempts = 0;
            m_found.Clear();
            m_won = false;
            m_last = null;
            ClearBeaker();
            if (Hud != null)
            {
                Hud.HideResult();
                UpdateScore();
            }
        }

        // ---- Player actions --------------------------------------------------------------------

        public void TapVial(int index)
        {
            if (index < 0 || m_vials == null || index >= m_vials.Length || m_vials[index] == null) return;
            LabVial vial = m_vials[index];
            if (m_won || m_busy)
            {
                Punch(vial.transform);
                if (m_busy && !m_won) Toast("Wait for the pour to finish");
                return;
            }
            if (m_reacted)
            {
                Toast("Empty the beaker first!");
                Punch(vial.transform);
                if (m_resetButton != null) Punch(m_resetButton.transform);
                OpenResult();
                return;
            }
            // Keep the rack in sync with the vial being used.
            if (RackOpen) Tray.Select(index, false);
            else OpenRack(index);
            if (m_mix.Contains(index))
            {
                Toast(LabData.Ingredients[index].Name + " is already in the mix");
                Punch(vial.transform);
                return;
            }
            WorldCard.CloseOpen();
            m_mix.Add(index);
            m_routine = StartCoroutine(PourRoutine(vial));
            RefreshUi();
        }

        // Tray "Add" button: pour the selected ingredient.
        public void AddSelected()
        {
            if (Tray == null || Tray.Selected < 0) return;
            TapVial(Tray.Selected);
        }

        // Tray tile picked: point at the vial in the world, don't pour yet.
        public void PreviewVial(int index)
        {
            if (index < 0 || index >= m_vials.Length || m_vials[index] == null) return;
            if (!m_busy) Punch(m_vials[index].transform);
            Fx.Play(FxKind.Sparkle, m_vials[index].transform.position + transform.up * 0.12f * transform.lossyScale.y, 0.4f);
            RefreshUi();
        }

        public void ShowInfo(int index)
        {
            if (index < 0 || m_vials == null || index >= m_vials.Length || m_vials[index] == null) return;
            LabVial vial = m_vials[index];
            Fx.Play(FxKind.Pickup, vial.transform.position, 0.5f);
            Punch(vial.transform);
            if (m_reacted && !m_won) { OpenResult(); return; }
            OpenRack(index);
        }

        // Called by the reset button ("fx" = player feedback) and by the formula card.
        public void ResetBeaker(bool fx)
        {
            if (m_won)
            {
                Restart();
                return;
            }
            StopRoutine();
            WorldCard.CloseOpen();
            bool hadSomething = m_mix.Count > 0;
            ClearBeaker();
            if (fx)
            {
                if (m_resetButton != null) Punch(m_resetButton.transform);
                if (m_beaker != null) Punch(m_beaker);
                if (hadSomething && m_beakerMouth != null) Fx.Play(FxKind.Poof, m_beakerMouth.position, 0.9f);
                Toast(hadSomething ? "Beaker rinsed. Try a new mix!" : "Beaker is already clean");
            }
            if (fx || m_mode == TrayMode.Result) OpenRack(RackOpen ? Tray.Selected : 0);
        }

        public void Restart()
        {
            ResetAll();
            if (m_beakerMouth != null) Fx.Play(FxKind.Poof, m_beakerMouth.position, 0.9f);
            if (Hud != null) Hud.SetHint(HintText);
            OpenRack(0);
        }

        private void ClearBeaker()
        {
            m_mix.Clear();
            m_busy = false;
            m_reacted = false;
            m_level = 0f;
            if (m_vials != null)
                foreach (var v in m_vials)
                    if (v != null) v.ResetPose();
            if (m_stream != null) m_stream.gameObject.SetActive(false);
            if (m_beaker != null) m_beaker.localScale = m_beakerScale;
            if (m_resetButton != null)
            {
                m_resetButton.Pulsing = false;
                m_resetButton.ResetScale();
            }
            SetLevel(0f);
            UpdateMixPanel();
            RefreshUi();
        }

        // ---- Pour & react ----------------------------------------------------------------------

        private IEnumerator PourRoutine(LabVial vial)
        {
            m_busy = true;
            int index = vial.Index;
            Color c = LabData.Ingredients[index].Color;
            Transform t = vial.transform;
            Transform parent = t.parent;
            Vector3 homePos = vial.HomePosition;
            Quaternion homeRot = vial.HomeRotation;
            UpdateMixPanel();

            // Tip toward the beaker so the mouth ends just above its opening.
            Vector3 mouthLocal = parent.InverseTransformPoint(m_beakerMouth.position);
            float side = homePos.x < mouthLocal.x ? -1f : 1f;
            Quaternion pourRot = homeRot * Quaternion.Euler(0f, 0f, side < 0f ? -118f : 118f);
            Vector3 mouthTarget = mouthLocal + new Vector3(side * 0.04f, 0.1f, -0.02f);
            Vector3 pourPos = mouthTarget - pourRot * new Vector3(0f, vial.MouthY, 0f);

            Fx.Play(FxKind.Pickup, t.position, 0.5f);
            if (vial.Cork != null) vial.Cork.gameObject.SetActive(false);

            yield return MoveVial(t, homePos, homeRot, pourPos, pourRot, 0.5f, 0.18f);

            // Pour: a coloured stream drops into the beaker while the liquid rises and blends.
            float fromLevel = m_level;
            float toLevel = m_liquidMax * m_mix.Count / MixSize;
            Color fromColor = m_level > 0.001f ? m_liquidColor : c;
            Color toColor = MixColor();
            SetColor(m_streamRenderer, c);
            if (m_stream != null) m_stream.gameObject.SetActive(true);
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.7f)
            {
                SetLevel(Mathf.Lerp(fromLevel, toLevel, Mathf.SmoothStep(0f, 1f, a)));
                SetLiquidColor(Color.Lerp(fromColor, toColor, a));
                t.localRotation = pourRot * Quaternion.Euler(0f, 0f, Mathf.Sin(a * Mathf.PI * 4f) * 4f);
                UpdateStream(t.TransformPoint(new Vector3(0f, vial.MouthY, 0f)));
                yield return null;
            }
            SetLevel(toLevel);
            SetLiquidColor(toColor);
            if (m_stream != null) m_stream.gameObject.SetActive(false);
            Fx.Play(FxKind.Sparkle, m_beakerMouth.position, 0.5f);
            if (m_beaker != null) StartCoroutine(Tween.Punch(m_beaker, 0.1f, 0.25f));

            yield return MoveVial(t, pourPos, pourRot, homePos, homeRot, 0.45f, 0.12f);
            if (vial.Cork != null)
            {
                vial.Cork.gameObject.SetActive(true);
                StartCoroutine(Tween.PopIn(vial.Cork, 0.2f));
            }
            vial.SetUsed(true);
            m_busy = false;
            m_routine = null;

            if (m_mix.Count >= MixSize)
                m_routine = StartCoroutine(ReactRoutine());
            else
                Toast(LabData.Ingredients[index].Name + " added · " + m_mix.Count + "/" + MixSize);
            RefreshUi();
        }

        private IEnumerator MoveVial(Transform t, Vector3 fromPos, Quaternion fromRot, Vector3 toPos, Quaternion toRot, float time, float arc)
        {
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                float k = Mathf.SmoothStep(0f, 1f, a);
                t.localPosition = Vector3.Lerp(fromPos, toPos, k) + Vector3.up * (arc * Mathf.Sin(a * Mathf.PI));
                t.localRotation = Quaternion.Slerp(fromRot, toRot, k);
                yield return null;
            }
            t.localPosition = toPos;
            t.localRotation = toRot;
        }

        private IEnumerator ReactRoutine()
        {
            m_busy = true;
            int mask = MixMask();
            LabFormula f = LabData.Find(mask);
            m_last = f;
            m_attempts++;
            m_found.Add(mask);
            UpdateScore();
            UpdateMixPanel();
            RefreshUi();

            // Bubble up: the beaker wobbles, then flashes into the formula's colour with its effect.
            Color from = m_liquidColor;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.6f)
            {
                float k = 1f + 0.06f * Mathf.Sin(a * Mathf.PI * 6f);
                if (m_beaker != null) m_beaker.localScale = new Vector3(m_beakerScale.x * k, m_beakerScale.y / k, m_beakerScale.z * k);
                SetLiquidColor(Color.Lerp(from, f.Color, a));
                yield return null;
            }
            if (m_beaker != null) m_beaker.localScale = m_beakerScale;
            SetLiquidColor(f.Color);
            Vector3 top = m_beakerMouth.position;
            Fx.Play(f.Fx, top, f.Hero ? 2.4f : 1.5f);
            if (m_beaker != null) StartCoroutine(Tween.Punch(m_beaker, 0.22f, 0.4f));

            if (f.Hero)
            {
                m_won = true;
                m_reacted = true;
                Fx.Play(FxKind.Confetti, top + transform.up * 0.25f * transform.lossyScale.y, 2.2f);
                Fx.Play(FxKind.Stars, top, 1.6f);
                Toast("Hero formula!", 1.2f);
                RefreshUi();
                yield return new WaitForSeconds(1.2f);
                m_busy = false;
                m_routine = null;
                ShowWinResult();
                yield break;
            }

            yield return new WaitForSeconds(0.6f);
            m_reacted = true;
            m_busy = false;
            m_routine = null;
            if (m_resetButton != null) m_resetButton.Pulsing = true;
            OpenResult();
            RefreshUi();
        }

        // ---- Tray -------------------------------------------------------------------------------

        public void OpenRack(int selected)
        {
            if (Tray == null || m_vials == null || m_won) return;
            m_options = new List<TrayOption>();
            for (int i = 0; i < m_vials.Length; i++)
                m_options.Add(new TrayOption(LabData.Ingredients[i].Name, null, LabData.Ingredients[i].Info.Replace("\n", " "), m_vials[i].gameObject));
            m_mode = TrayMode.Rack;
            FrameBench();
            Tray.Open(RackTitle(), m_options, selected, PreviewVial,
                ("+ Add", AddSelected),
                ("Empty", () => ResetBeaker(true)));
            RefreshUi();
        }

        // After a (non-hero) reaction: the formula, what went in, and one big "Empty & retry".
        private void OpenResult()
        {
            if (Tray == null || m_last == null || m_won) return;
            LabFormula f = m_last;
            m_options = new List<TrayOption>
            {
                new TrayOption(f.Name, MixNames(" + "), f.Copy.Replace("\n", " ") + "\n<color=#E0584A>Not the hero formula, try again!</color>", m_beaker.gameObject)
                    { Icon = Swatch(f.Color) },
            };
            for (int k = 0; k < m_mix.Count; k++)
            {
                int i = m_mix[k];
                m_options.Add(new TrayOption(LabData.Ingredients[i].Name, "Ingredient " + (k + 1), LabData.Ingredients[i].Info.Replace("\n", " "), m_vials[i].gameObject) { Badge = (k + 1).ToString() });
            }
            m_mode = TrayMode.Result;
            FrameBench();
            Tray.Open("Result  ·  try " + m_attempts, m_options, 0, null, ("Empty & try again", () => ResetBeaker(true)));
        }

        private void CloseTray()
        {
            if (m_mode != TrayMode.None && Tray != null && Tray.IsOpen) Tray.Close();
            m_mode = TrayMode.None;
        }

        private string RackTitle() => m_mix.Count == 0 ? "Pick 3 ingredients  ·  0/3" : "Mixing  ·  " + m_mix.Count + "/" + MixSize + " in the beaker";

        // Tray texts, badges and buttons, plus the world tags, from the current state.
        private void RefreshUi()
        {
            if (RackOpen && m_options != null)
            {
                for (int i = 0; i < m_options.Count; i++)
                {
                    int pos = m_mix.IndexOf(i);
                    m_options[i].Badge = pos >= 0 ? (pos + 1).ToString() : null;
                    m_options[i].Price = pos >= 0 ? "√ In the beaker" : "Nova Lab ingredient";
                }
                Tray.SetTitle(RackTitle());
                Tray.Refresh();
                int sel = Tray.Selected;
                bool canAdd = !m_busy && !m_reacted && sel >= 0 && !m_mix.Contains(sel);
                string label = m_busy ? "Pouring..." : sel >= 0 && m_mix.Contains(sel) ? "√ Added" : "+ Add " + LabData.Ingredients[Mathf.Max(sel, 0)].Name;
                Tray.SetAction(0, label, canAdd ? Ui.Accent : Ui.Muted);
                Tray.SetAction(1, "Empty", m_mix.Count > 0 && !m_busy ? Ui.Accent2 : Ui.Muted);
            }

            if (m_vialTags != null)
                for (int i = 0; i < m_vialTags.Length; i++)
                {
                    var tag = m_vialTags[i];
                    if (tag == null) continue;
                    int pos = m_mix.IndexOf(i);
                    Color c = LabData.Ingredients[i].Color;
                    if (pos >= 0) tag.Set((pos + 1) + " · " + LabData.Ingredients[i].Name, new Color(c.r * 0.75f, c.g * 0.75f, c.b * 0.75f, 0.95f));
                    else tag.Set(LabData.Ingredients[i].Name, TagIdle);
                    tag.Pulse(RackOpen && Tray.Selected == i && pos < 0 && !m_busy);
                }

            if (m_beakerTag != null)
            {
                if (m_won && m_last != null) m_beakerTag.Set(m_last.Name + "!", new Color(0.85f, 0.6f, 0.1f, 0.95f));
                else if (m_reacted && m_last != null) m_beakerTag.Set(m_last.Name, new Color(0.75f, 0.27f, 0.25f, 0.92f));
                else m_beakerTag.Set("Beaker  " + m_mix.Count + "/" + MixSize, TagIdle);
            }
            if (m_rackButton != null)
                Ui.SetButtonLabel(m_rackButton, m_reacted ? "See result  ›" : "Ingredients  ·  " + m_mix.Count + "/" + MixSize + "  ›");
        }

        private void EnsureUi()
        {
            if (m_vialTags == null && m_vials != null)
            {
                m_vialTags = new WorldTag[m_vials.Length];
                for (int i = 0; i < m_vials.Length; i++)
                {
                    if (m_vials[i] == null) continue;
                    // Fixed anchors (not children of the vial) so tags stay put while a vial pours.
                    var anchor = new GameObject("Tag Anchor " + LabData.Ingredients[i].Name).transform;
                    anchor.SetParent(m_vials[i].transform.parent, false);
                    // The middle vial of each rack gets a higher tag so neighbours never overlap.
                    anchor.localPosition = m_vials[i].HomePosition + new Vector3(0f, i % 3 == 1 ? 0.34f : 0.24f, 0f);
                    m_vialTags[i] = WorldTag.Create(anchor, Vector3.zero, LabData.Ingredients[i].Name, 0.1f);
                }
            }
            if (m_beakerTag == null && m_beaker != null)
                m_beakerTag = WorldTag.Create(m_beaker, new Vector3(0f, 0.6f, 0f), "Beaker", 0.11f);
            if (m_screenUi == null)
            {
                // Big "open the rack" button for when the tray is hidden (scene root: overlay canvas).
                m_screenUi = new GameObject("Lab Screen UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = m_screenUi.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 45;   // under the HUD (toast/result draw on top)
                var scaler = m_screenUi.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;
                m_rackButton = Ui.Button(m_screenUi.transform, "Ingredients  ›", Ui.Accent, () =>
                {
                    if (m_reacted && !m_won) OpenResult(); else OpenRack(0);
                }, 46, "Rack Button");
                Ui.Round(m_rackButton.GetComponent<Image>(), 60f);
                m_rackButton.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -6f);
                // Bottom-left: clear of the HUD hint above and the Look button at the bottom right.
                Ui.Place((RectTransform)m_rackButton.transform, new Vector2(0f, 0f), new Vector2(600, 130), new Vector2(30, 40));
            }
            m_screenUi.SetActive(true);
            if (Tray != null)
            {
                Tray.Closed -= OnTrayClosed;
                Tray.Closed += OnTrayClosed;
            }
        }

        private void OnTrayClosed()
        {
            if (m_mode == TrayMode.None) return;
            m_mode = TrayMode.None;
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            RefreshUi();
        }

        // While the tray is up: just the bench, a bit closer, sitting right above the tray.
        private void FrameBench() => FreeCamera.Instance?.Frame(new Vector3(0f, 0.42f, 0.3f), 3.6f, FocusPitch);

        // A round colour swatch texture for the formula tile (the beaker model's thumbnail is cached empty).
        private Texture2D Swatch(Color c)
        {
            if (m_swatches.TryGetValue(c, out var tex) && tex != null) return tex;
            const int size = 96;
            tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Formula Swatch", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float centre = (size - 1) * 0.5f, r = size * 0.36f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * size + x] = new Color(c.r, c.g, c.b, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            m_swatches[c] = tex;
            return tex;
        }

        private void ShowWinResult()
        {
            if (Hud == null) return;
            Hud.SetHint("");
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            int others = m_found.Count - 1;
            string tries = m_attempts == 1 ? "1 try" : m_attempts + " tries";
            string body = "Aloe + Citrus + Spark\nCalm, bright and glowing.\nFound in " + tries +
                          (others > 0 ? " · " + others + " other formula" + (others == 1 ? "" : "s") + " discovered" : " · first try, genius!");
            Hud.ShowResult("Nova Glow serum\ndiscovered!", body,
                ("Play again", Restart),
                ("Hub", () => { if (ShowcaseHub.Instance != null) ShowcaseHub.Instance.ReturnToHub(); }));
        }

        // ---- Debug / tests ---------------------------------------------------------------------

        public override void DebugComplete()
        {
            StopRoutine();
            WorldCard.CloseOpen();
            ClearBeaker();
            m_mix.AddRange(new[] { LabData.Aloe, LabData.Citrus, LabData.Spark });
            foreach (int i in m_mix) m_vials[i].SetUsed(true);
            m_last = LabData.Find(LabData.HeroMask);
            m_found.Add(LabData.HeroMask);
            if (m_attempts == 0) m_attempts = 1;
            m_won = true;
            m_reacted = true;
            SetLevel(m_liquidMax);
            SetLiquidColor(m_last.Color);
            UpdateScore();
            UpdateMixPanel();
            RefreshUi();
            if (m_beakerMouth != null)
            {
                Fx.Play(FxKind.Magic, m_beakerMouth.position, 2.4f);
                Fx.Play(FxKind.Confetti, m_beakerMouth.position, 2f);
            }
            ShowWinResult();
        }

        public override IEnumerator SelfTest()
        {
            // The harness has already tapped everything once; start from a clean beaker.
            ResetBeaker(false);
            Hud?.HideResult();
            OpenRack(0);
            yield return new WaitForSeconds(0.7f);
            int attempts0 = m_attempts;
            if (!RackOpen) Debug.LogError("[SELFTEST] ingredient rack not open");
            else if (Tray.Options.Count != m_vials.Length) Debug.LogError("[SELFTEST] rack shows " + Tray.Options.Count + " vials");
            Snapshot("tray");

            // Wrong mix: Aloe (tray), Mint (tap the vial in the world), Charcoal (tray).
            Tray.Select(LabData.Aloe);
            Tray.Press(0);   // "+ Add Aloe"
            TapVial(LabData.Berry);   // ignored while pouring
            if (m_mix.Count != 1) Debug.LogError("[SELFTEST] a second vial was accepted mid-pour (" + m_mix.Count + " in mix)");
            yield return WaitIdle();
            if (Tray.Options[LabData.Aloe].Badge != "1") Debug.LogError("[SELFTEST] rack did not number Aloe as ingredient 1");
            TapVial(LabData.Mint);
            if (!RackOpen || Tray.Selected != LabData.Mint) Debug.LogError("[SELFTEST] tapping a vial did not select it in the rack");
            yield return new WaitForSeconds(0.3f);
            Snapshot("pour");
            yield return WaitIdle();
            Tray.Select(LabData.Charcoal);
            Tray.Press(0);
            yield return WaitIdle();

            if (!m_reacted) Debug.LogError("[SELFTEST] beaker did not react after 3 ingredients");
            if (m_last == null || m_last.Hero) Debug.LogError("[SELFTEST] wrong mix gave " + (m_last == null ? "no formula" : "the hero formula"));
            else if (m_last.Mask != ((1 << LabData.Aloe) | (1 << LabData.Mint) | (1 << LabData.Charcoal))) Debug.LogError("[SELFTEST] wrong formula " + m_last.Name);
            if (m_won) Debug.LogError("[SELFTEST] won with a wrong mix");
            if (m_attempts != attempts0 + 1) Debug.LogError("[SELFTEST] attempts " + attempts0 + " -> " + m_attempts + ", expected +1");
            if (m_level < m_liquidMax * 0.95f) Debug.LogError("[SELFTEST] liquid level " + m_level + " after 3 pours");
            if (Hud != null && Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel shown for a wrong mix");
            if (!ResultOpen) Debug.LogError("[SELFTEST] tray does not show the formula result");
            else if (m_last != null && Tray.Options[0].Label != m_last.Name) Debug.LogError("[SELFTEST] result tile says " + Tray.Options[0].Label);
            yield return new WaitForSeconds(0.7f);
            Snapshot("result");

            TapVial(LabData.Spark);   // must be refused until emptied
            if (m_mix.Count != MixSize || m_busy) Debug.LogError("[SELFTEST] vial accepted while the beaker was full");

            // Hide the tray, then bring the result back with the big screen button.
            Tray.Close();
            yield return new WaitForSeconds(0.8f);
            if (m_rackButton == null || !m_rackButton.gameObject.activeInHierarchy) Debug.LogError("[SELFTEST] rack button not shown with the tray hidden");
            Snapshot("closed");
            m_rackButton.onClick.Invoke();
            if (!ResultOpen) Debug.LogError("[SELFTEST] rack button did not bring back the result");

            Tray.Press(0);   // "Empty & try again"
            if (m_mix.Count != 0 || m_reacted || m_level > 0.001f) Debug.LogError("[SELFTEST] empty left " + m_mix.Count + " ingredients, level " + m_level);
            if (!RackOpen) Debug.LogError("[SELFTEST] rack did not come back after emptying");
            yield return new WaitForSeconds(0.5f);

            // Hero mix in a different order: Spark, Aloe, (Aloe again = refused), Citrus.
            Tray.Select(LabData.Spark);
            Tray.Press(0);
            yield return WaitIdle();
            Tray.Select(LabData.Aloe);
            Tray.Press(0);
            yield return WaitIdle();
            Tray.Press(0);
            if (m_mix.Count != 2 || m_busy) Debug.LogError("[SELFTEST] duplicate ingredient accepted");
            Tray.Select(LabData.Citrus);
            Tray.Press(0);
            yield return WaitIdle();

            if (!m_won) Debug.LogError("[SELFTEST] hero formula did not win (last: " + (m_last != null ? m_last.Name : "none") + ")");
            if (m_last == null || !m_last.Hero) Debug.LogError("[SELFTEST] last formula is not the hero");
            if (m_attempts != attempts0 + 2) Debug.LogError("[SELFTEST] attempts " + m_attempts + ", expected " + (attempts0 + 2));
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel not shown after the hero formula");
            if (Tray.IsOpen) Debug.LogError("[SELFTEST] tray still open on the win screen");
            yield return new WaitForSeconds(0.5f);
        }

        private IEnumerator WaitIdle()
        {
            yield return null;
            float t0 = Time.time;
            while ((m_busy || m_routine != null) && Time.time - t0 < 8f)
                yield return null;
            if (m_busy) Debug.LogError("[SELFTEST] still busy after 8 s");
        }

        // ---- Helpers ---------------------------------------------------------------------------

        private void StopRoutine()
        {
            if (m_routine != null) StopCoroutine(m_routine);
            m_routine = null;
        }

        private int MixMask()
        {
            int mask = 0;
            foreach (int i in m_mix) mask |= 1 << i;
            return mask;
        }

        private Color MixColor()
        {
            if (m_mix.Count == 0) return Color.white;
            Color sum = Color.black;
            foreach (int i in m_mix) sum += LabData.Ingredients[i].Color;
            Color c = sum / m_mix.Count;
            c.a = 1f;
            return c;
        }

        private string MixNames(string sep)
        {
            var names = new List<string>();
            foreach (int i in m_mix) names.Add(LabData.Ingredients[i].Name);
            return string.Join(sep, names);
        }

        private void SetLevel(float level)
        {
            m_level = level;
            if (m_liquid == null) return;
            bool visible = level > 0.004f;
            m_liquid.gameObject.SetActive(visible);
            if (!visible) return;
            Vector3 s = m_liquid.localScale;
            m_liquid.localScale = new Vector3(s.x, level * 0.5f, s.z);
            Vector3 p = m_liquid.localPosition;
            m_liquid.localPosition = new Vector3(p.x, m_liquidBottom + level * 0.5f, p.z);
        }

        private void SetLiquidColor(Color c)
        {
            m_liquidColor = c;
            SetColor(m_liquidRenderer, c);
        }

        // Flat Kit shades with separate "dim" colours, so tint those too (same ratio as the material).
        private void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            Material m = r.sharedMaterial;
            if (m == null) return;
            Color shadeRatio = new Color(0.6f, 0.6f, 0.7f);
            if (m.HasProperty("_ColorDim") && m.HasProperty("_BaseColor"))
            {
                Color b = m.GetColor("_BaseColor"), d = m.GetColor("_ColorDim");
                float k = b.maxColorComponent > 0.01f ? d.maxColorComponent / b.maxColorComponent : 0.6f;
                shadeRatio = new Color(k, k, k);
            }
            Color shade = new Color(c.r * shadeRatio.r, c.g * shadeRatio.g, c.b * shadeRatio.b, 1f);
            // A per-renderer material instance (not a property block): the tray's live preview copies
            // the beaker, and copies keep materials but not property blocks.
            if (!m_ownedMats.Contains(m))
            {
                m = new Material(m) { name = m.name + " (Lab)" };
                m_ownedMats.Add(m);
                r.sharedMaterial = m;
            }
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_ColorDim")) m.SetColor("_ColorDim", shade);
            if (m.HasProperty("_ColorDimSteps")) m.SetColor("_ColorDimSteps", shade);
            if (m.HasProperty("_ColorDimCurve")) m.SetColor("_ColorDimCurve", shade);
        }

        private void UpdateStream(Vector3 mouthWorld)
        {
            if (m_stream == null || m_liquid == null) return;
            Transform p = m_stream.parent;
            Vector3 top = p.InverseTransformPoint(mouthWorld);
            Vector3 surface = p.InverseTransformPoint(m_beaker.TransformPoint(new Vector3(0f, m_liquidBottom + m_level, 0f)));
            Vector3 bottom = new Vector3(top.x, surface.y, top.z);
            float len = Mathf.Max(0.01f, top.y - bottom.y);
            m_stream.localPosition = (top + bottom) * 0.5f;
            m_stream.localRotation = Quaternion.identity;
            float w = 0.022f + 0.006f * Mathf.Sin(Time.time * 30f);
            m_stream.localScale = new Vector3(w, len * 0.5f, w);
        }

        private void UpdateMixPanel()
        {
            if (m_mixSlots != null)
                for (int k = 0; k < m_mixSlots.Length; k++)
                {
                    if (m_mixSlots[k] == null) continue;
                    bool filled = k < m_mix.Count;
                    m_mixSlots[k].color = filled ? LabData.Ingredients[m_mix[k]].Color : new Color(1f, 1f, 1f, 0.18f);
                }
            if (m_mixStatus != null)
            {
                if (m_mix.Count < MixSize)
                    m_mixStatus.text = m_mix.Count == 0 ? "Pick 3 ingredients" : MixNames(" + ") + " + ?";
                else
                    m_mixStatus.text = m_last != null && MixMask() == m_last.Mask ? m_last.Name : MixNames(" + ");
            }
        }

        private void UpdateScore()
        {
            if (Hud != null) Hud.SetScore("Tries " + m_attempts);
        }

        private void Toast(string text, float seconds = 1.4f)
        {
            if (Hud != null) Hud.Toast(text, seconds);
        }

        private void Punch(Transform t)
        {
            if (t != null && isActiveAndEnabled) StartCoroutine(Tween.Punch(t, 0.15f, 0.25f));
        }
    }
}
