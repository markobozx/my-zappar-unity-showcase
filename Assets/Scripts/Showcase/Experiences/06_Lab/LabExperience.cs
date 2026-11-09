using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Lab
{
    // Little Lab: tap three ingredient vials to pour them into the big beaker. Every 3-ingredient mix
    // reacts with its own colour, effect and formula card; Aloe + Citrus + Spark (any order) is the
    // hero formula. Taps on vials, "i" badges and the reset button all come through the methods below.
    public class LabExperience : ShowcaseExperience
    {
        public const int MixSize = 3;
        const string HintText = "Tap 3 vials to mix a formula.\nRead the clue board to find the hero formula!";

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

        public int Attempts => m_attempts;
        public bool Won => m_won;
        public bool Reacted => m_reacted;
        public bool Busy => m_busy;
        public int MixCount => m_mix.Count;
        public LabFormula LastFormula => m_last;

        void Awake()
        {
            m_block = new MaterialPropertyBlock();
            if (m_liquid != null) m_liquidRenderer = m_liquid.GetComponent<Renderer>();
            if (m_stream != null) m_streamRenderer = m_stream.GetComponent<Renderer>();
            if (m_beaker != null) m_beakerScale = m_beaker.localScale;
        }

        protected override void OnEnter()
        {
            ResetAll();
            if (Hud != null) Hud.SetHint(HintText);
        }

        protected override void OnExit()
        {
            ResetAll();
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
                return;
            }
            if (m_reacted)
            {
                Toast("Reset the beaker first!");
                Punch(vial.transform);
                if (m_resetButton != null) Punch(m_resetButton.transform);
                return;
            }
            if (m_mix.Contains(index))
            {
                Toast(LabData.Ingredients[index].Name + " is already in the mix");
                Punch(vial.transform);
                return;
            }
            WorldCard.CloseOpen();
            m_mix.Add(index);
            m_routine = StartCoroutine(PourRoutine(vial));
        }

        public void ShowInfo(int index)
        {
            if (index < 0 || m_vials == null || index >= m_vials.Length || m_vials[index] == null) return;
            LabVial vial = m_vials[index];
            LabIngredient ing = LabData.Ingredients[index];
            Fx.Play(FxKind.Pickup, vial.transform.position, 0.5f);
            Punch(vial.transform);
            string status = m_mix.Contains(index) ? "\n<color=#3DA35D>In the beaker.</color>" : "";
            WorldCard.Show(vial.transform, new Vector3(0f, 0.42f, 0f), ing.Name, ing.Info + status,
                ("Pour it in", () => { WorldCard.CloseOpen(); TapVial(index); }));
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
        }

        public void Restart()
        {
            ResetAll();
            if (m_beakerMouth != null) Fx.Play(FxKind.Poof, m_beakerMouth.position, 0.9f);
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
            ShowFormulaCard(f);
        }

        private void ShowFormulaCard(LabFormula f)
        {
            if (m_beaker == null) return;
            string hex = ColorUtility.ToHtmlStringRGB(f.Color.grayscale > 0.75f ? f.Color * 0.7f : f.Color);
            string body = "<color=#" + hex + ">" + MixNames(" + ") + "</color>\n" + f.Copy + "\n<color=#E0584A>Not the hero formula. Try again!</color>";
            var card = WorldCard.Show(m_beaker, new Vector3(0f, 0.66f, -0.12f), f.Name, body, 0.8f,
                ("Reset beaker", () => ResetBeaker(true)));
            if (card != null && card.Title != null)
            {
                card.Title.resizeTextForBestFit = true;
                card.Title.resizeTextMinSize = 26;
                card.Title.resizeTextMaxSize = 46;
            }
        }

        private void ShowWinResult()
        {
            if (Hud == null) return;
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
            yield return new WaitForSeconds(0.3f);
            int attempts0 = m_attempts;

            ShowInfo(LabData.Mint);
            yield return new WaitForSeconds(0.5f);
            WorldCard.CloseOpen();

            // Wrong mix: Aloe + Mint + Charcoal.
            foreach (int i in new[] { LabData.Aloe, LabData.Mint, LabData.Charcoal })
            {
                TapVial(i);
                if (i == LabData.Aloe)
                {
                    TapVial(LabData.Berry);   // ignored while pouring
                    if (m_mix.Count != 1) Debug.LogError("[SELFTEST] a second vial was accepted mid-pour (" + m_mix.Count + " in mix)");
                }
                yield return WaitIdle();
            }
            if (!m_reacted) Debug.LogError("[SELFTEST] beaker did not react after 3 ingredients");
            if (m_last == null || m_last.Hero) Debug.LogError("[SELFTEST] wrong mix gave " + (m_last == null ? "no formula" : "the hero formula"));
            else if (m_last.Mask != ((1 << LabData.Aloe) | (1 << LabData.Mint) | (1 << LabData.Charcoal))) Debug.LogError("[SELFTEST] wrong formula " + m_last.Name);
            if (m_won) Debug.LogError("[SELFTEST] won with a wrong mix");
            if (m_attempts != attempts0 + 1) Debug.LogError("[SELFTEST] attempts " + attempts0 + " -> " + m_attempts + ", expected +1");
            if (m_level < m_liquidMax * 0.95f) Debug.LogError("[SELFTEST] liquid level " + m_level + " after 3 pours");
            if (Hud != null && Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel shown for a wrong mix");
            yield return new WaitForSeconds(0.8f);

            TapVial(LabData.Spark);   // must be refused until reset
            if (m_mix.Count != MixSize || m_busy) Debug.LogError("[SELFTEST] vial accepted while the beaker was full");

            ResetBeaker(true);
            if (m_mix.Count != 0 || m_reacted || m_level > 0.001f) Debug.LogError("[SELFTEST] reset left " + m_mix.Count + " ingredients, level " + m_level);
            yield return new WaitForSeconds(0.5f);

            // Hero mix in a different order: Spark, Aloe, (Aloe again = refused), Citrus.
            TapVial(LabData.Spark);
            yield return WaitIdle();
            TapVial(LabData.Aloe);
            yield return WaitIdle();
            TapVial(LabData.Aloe);
            if (m_mix.Count != 2 || m_busy) Debug.LogError("[SELFTEST] duplicate ingredient accepted");
            TapVial(LabData.Citrus);
            yield return WaitIdle();

            if (!m_won) Debug.LogError("[SELFTEST] hero formula did not win (last: " + (m_last != null ? m_last.Name : "none") + ")");
            if (m_last == null || !m_last.Hero) Debug.LogError("[SELFTEST] last formula is not the hero");
            if (m_attempts != attempts0 + 2) Debug.LogError("[SELFTEST] attempts " + m_attempts + ", expected " + (attempts0 + 2));
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel not shown after the hero formula");
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
            Color shadeRatio = new Color(0.6f, 0.6f, 0.7f);
            if (m != null && m.HasProperty("_ColorDim") && m.HasProperty("_BaseColor"))
            {
                Color b = m.GetColor("_BaseColor"), d = m.GetColor("_ColorDim");
                float k = b.maxColorComponent > 0.01f ? d.maxColorComponent / b.maxColorComponent : 0.6f;
                shadeRatio = new Color(k, k, k);
            }
            Color shade = new Color(c.r * shadeRatio.r, c.g * shadeRatio.g, c.b * shadeRatio.b, 1f);
            r.GetPropertyBlock(m_block);
            m_block.SetColor("_BaseColor", c);
            m_block.SetColor("_ColorDim", shade);
            m_block.SetColor("_ColorDimSteps", shade);
            m_block.SetColor("_ColorDimCurve", shade);
            r.SetPropertyBlock(m_block);
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
