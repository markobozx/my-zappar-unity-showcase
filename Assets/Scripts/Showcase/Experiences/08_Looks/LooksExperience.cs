using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Showcase.Experiences.Looks
{
    // Look Switcher Gallery: a small gallery of props from several packs. The style tray (bottom sheet)
    // switches the whole screen between four looks (URP renderer features, same set as StyleSwitcher);
    // the big dial buttons on the floor do the same. Each look reveals one secret object that exists
    // only in that look; tap all four to finish. Tray tiles get a check badge once their secret is found.
    // UI split: screen tray = choosing a look, hint, next; world = the restyled scene, secrets, hint tag, board.
    // The renderer features are restored to how they were found when the experience exits.
    public class LooksExperience : ShowcaseExperience
    {
        public const int LookCount = 4;
        public static readonly string[] LookNames = { "Toon", "Ink", "Game Boy", "Pixel" };
        public static readonly string[] LookBlurbs =
        {
            "Flat Kit cel shading, 3 steps", "Edge-detect ink outlines", "4-colour palette pass", "Low-res pixels + outlines"
        };
        public static readonly Color[] LookColors =
        {
            new Color(1f, 0.45f, 0.38f),     // Toon
            new Color(0.17f, 0.17f, 0.24f),  // Ink
            new Color(0.42f, 0.66f, 0.18f),  // Game Boy
            new Color(0.6f, 0.38f, 0.95f),   // Pixel
        };
        // Same feature sets as StyleSwitcher's Toon / Toon + Outline / 4-Colour / Pixel Art.
        public static readonly string[][] LookFeatures =
        {
            new string[0], new[] { "Outline" }, new[] { "Palette 4-Colour" }, new[] { "Pixelation", "Outline" }
        };
        public static readonly string[] ManagedFeatures = { "Outline", "Pixelation", "Palette 4-Colour" };

        [SerializeField] private ScriptableRendererData m_renderer;
        [SerializeField] private LookButton[] m_buttons;
        [SerializeField] private LookSecret[] m_secrets;       // indexed by look
        [SerializeField] private LookExhibit[] m_exhibits;
        [SerializeField] private Text m_boardCount;
        [SerializeField] private Text[] m_boardRows;           // indexed by look
        [SerializeField] private Text m_boardNow;
        [SerializeField] private Transform m_fxPoint;

        private readonly Dictionary<ScriptableRendererFeature, bool> m_savedStates = new Dictionary<ScriptableRendererFeature, bool>();
        private int m_look;
        private int m_found;
        private int m_hints;
        private bool m_finished;
        private Coroutine m_finishRoutine;
        private List<TrayOption> m_options;
        private bool m_trayOurs;
        private WorldTag m_hintTag;
        private int m_hintLook = -1;

        public int CurrentLook => m_look;
        public int FoundCount => m_found;
        public LookSecret[] Secrets => m_secrets;
        public bool HintVisible => m_hintTag != null && m_hintTag.gameObject.activeSelf;
        private static ShowcaseTray Tray => ShowcaseTray.Instance;

        protected override void OnEnter()
        {
            if (Tray != null)
            {
                Tray.Closed -= OnTrayClosed;
                Tray.Closed += OnTrayClosed;
            }
            ResetGame();
        }

        protected override void OnExit()
        {
            if (m_finishRoutine != null) StopCoroutine(m_finishRoutine);
            m_finishRoutine = null;
            CloseTray();
            HideHint();
            RestoreFeatures();
        }

        void OnDisable()
        {
            RestoreFeatures();
        }

        // Quitting / unloading: the toggles live on the renderer asset (written to disk in the editor),
        // so leave every managed full-screen look off, whatever order the global StyleSwitcher cleans up in.
        void OnDestroy()
        {
            if (Tray != null) Tray.Closed -= OnTrayClosed;
            m_savedStates.Clear();
            if (m_renderer == null)
                return;
            foreach (ScriptableRendererFeature f in m_renderer.rendererFeatures)
                if (f != null && System.Array.IndexOf(ManagedFeatures, f.name) >= 0)
                    f.SetActive(false);
        }

        private void ResetGame()
        {
            if (m_finishRoutine != null) StopCoroutine(m_finishRoutine);
            m_finishRoutine = null;
            if (Hud != null) Hud.HideResult();
            WorldCard.CloseOpen();
            m_found = 0;
            m_hints = 0;
            m_finished = false;
            HideHint();
            foreach (LookSecret s in m_secrets)
            {
                if (s == null) continue;
                s.Found = false;
                s.Collecting = false;
                s.ResetPose();
            }
            ApplyLook(0, false);
            UpdateBoard();
            if (Hud != null)
            {
                Hud.SetHint(Hint);
                Hud.SetTimer("");
            }
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            OpenTray();
        }

        // ---- Tray (screen space: choosing) ----

        public void OpenTray()
        {
            if (Tray == null || m_finished)
                return;
            m_options = new List<TrayOption>();
            for (int i = 0; i < LookCount; i++)
                m_options.Add(new TrayOption(LookNames[i]) { Swatch = LookColors[i] });
            FillOptions();
            m_trayOurs = true;
            Tray.Open(TrayTitle(), m_options, m_look, SelectLook,
                ("? Hint", ShowHint),
                ("Next look ›", NextLook));
            RefreshTray();
        }

        private void CloseTray()
        {
            if (m_trayOurs && Tray != null && Tray.IsOpen)
            {
                m_trayOurs = false;
                Tray.Close();
            }
            m_trayOurs = false;
        }

        private void OnTrayClosed()
        {
            m_trayOurs = false;
        }

        private string TrayTitle() => "Style dial  ·  Secrets " + m_found + "/" + LookCount;

        private void FillOptions()
        {
            if (m_options == null) return;
            for (int i = 0; i < m_options.Count; i++)
            {
                LookSecret s = SecretFor(i);
                bool found = s != null && s.Found;
                var o = m_options[i];
                o.Badge = found ? "√" : null;
                o.Price = found ? "√ Found: " + s.Title : (i == m_look ? "A secret is hiding here - find it!" : "1 secret hides in this look");
                o.Detail = LookBlurbs[i] + "  ·  Lumo style engine";
            }
        }

        private void RefreshTray()
        {
            if (!m_trayOurs || Tray == null || !Tray.IsOpen)
                return;
            FillOptions();
            Tray.SetTitle(TrayTitle());
            if (Tray.Selected != m_look) Tray.Select(m_look, false);
            Tray.Refresh();
            LookSecret s = SecretFor(m_look);
            bool hereFound = s == null || s.Found;
            Tray.SetAction(0, "? Hint", hereFound ? Ui.Muted : Ui.Accent);
            Tray.SetAction(1, "Next look ›", hereFound ? Ui.Good : Ui.Accent2);
        }

        public void NextLook()
        {
            SelectLook(NextUnfoundLook());
        }

        // Puts a pulsing "tap me" tag over this look's secret and (free camera) brings it close.
        public void ShowHint()
        {
            LookSecret s = SecretFor(m_look);
            if (s == null || s.Found)
            {
                Hud?.Toast(m_found >= LookCount ? "All secrets found!" : "Found here already - try " + LookNames[NextUnfoundLook()]);
                return;
            }
            m_hints++;
            if (m_hintTag == null)
                m_hintTag = WorldTag.Create(transform, Vector3.zero, "", 0.13f, Ui.Accent);
            m_hintTag.transform.localPosition = transform.InverseTransformPoint(s.Visual.position) + Vector3.up * 0.36f;
            m_hintTag.Set("Tap me!  " + LookNames[m_look] + " secret", Ui.Accent);
            m_hintTag.Show(true);
            m_hintTag.Pulse(true);
            m_hintLook = m_look;
            Fx.Play(FxKind.Sparkle, s.Visual.position, 0.8f);
            FreeCamera.Instance?.Frame(transform.InverseTransformPoint(s.Visual.position), 2.8f, 44f);
        }

        private void HideHint()
        {
            if (m_hintTag != null) m_hintTag.Show(false);
            m_hintLook = -1;
        }

        // ---- Looks ----

        // Tray tile and floor dial handler.
        public void SelectLook(int look)
        {
            if (look < 0 || look >= LookCount || m_finished)
                return;
            bool changed = look != m_look;
            ApplyLook(look, true);
            if (m_buttons != null && look < m_buttons.Length && m_buttons[look] != null)
                Fx.Play(FxKind.Magic, m_buttons[look].transform.position + transform.up * 0.15f * transform.lossyScale.y, 0.8f);
            if (changed && m_fxPoint != null)
                Fx.Play(FxKind.Sparkle, m_fxPoint.position, 1.6f);
            if (changed)
            {
                if (m_hintLook != look) HideHint();
                FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            }
            // The tray already says which look is on; the toast is for when it's closed (3D dial taps in AR).
            bool trayShowing = m_trayOurs && Tray != null && Tray.IsOpen;
            if (Hud != null && !trayShowing)
            {
                LookSecret s = SecretFor(look);
                string extra = s != null && !s.Found ? "\nSomething new appeared!" : "";
                Hud.Toast("Look: " + LookNames[look] + extra);
            }
            // Tapping the 3D dial (re)opens the tray on that look.
            if (!m_trayOurs || Tray == null || !Tray.IsOpen) OpenTray();
            else RefreshTray();
        }

        private void ApplyLook(int look, bool animate)
        {
            m_look = look;
            SetFeatures(LookFeatures[look]);
            if (m_buttons != null)
                foreach (LookButton b in m_buttons)
                    if (b != null) b.SetSelected(b.Look == look);
            foreach (LookSecret s in m_secrets)
            {
                if (s == null) continue;
                bool show = s.Look == look && !s.Found;
                bool wasShown = s.gameObject.activeSelf;
                if (s.Collecting) continue;
                s.gameObject.SetActive(show);
                if (show && !wasShown && animate)
                {
                    s.ResetPose();
                    StartCoroutine(Tween.PopIn(s.transform, 0.4f));
                }
            }
            UpdateBoard();
        }

        private void SetFeatures(string[] active)
        {
            if (m_renderer == null)
                return;
            foreach (ScriptableRendererFeature f in m_renderer.rendererFeatures)
            {
                if (f == null || System.Array.IndexOf(ManagedFeatures, f.name) < 0)
                    continue;
                if (!m_savedStates.ContainsKey(f))
                    m_savedStates[f] = f.isActive;
                f.SetActive(System.Array.IndexOf(active, f.name) >= 0);
            }
        }

        // Puts every feature this experience touched back the way it was before Enter.
        private void RestoreFeatures()
        {
            foreach (var kv in m_savedStates)
                if (kv.Key != null) kv.Key.SetActive(kv.Value);
            m_savedStates.Clear();
        }

        public bool IsFeatureActive(string featureName)
        {
            if (m_renderer == null)
                return false;
            foreach (ScriptableRendererFeature f in m_renderer.rendererFeatures)
                if (f != null && f.name == featureName)
                    return f.isActive;
            return false;
        }

        public LookSecret SecretFor(int look)
        {
            foreach (LookSecret s in m_secrets)
                if (s != null && s.Look == look) return s;
            return null;
        }

        public void ShowExhibit(LookExhibit exhibit)
        {
            if (exhibit == null)
                return;
            StartCoroutine(Tween.Punch(exhibit.transform, 0.12f, 0.25f));
            Fx.Play(FxKind.Hit, exhibit.transform.position + transform.up * exhibit.CardHeight * 0.5f * transform.lossyScale.y, 0.5f);
            WorldCard.Show(exhibit.transform, exhibit.transform.InverseTransformVector(transform.up * exhibit.CardHeight * transform.lossyScale.y),
                exhibit.Title, exhibit.Credit + "\nNow viewing: <b>" + LookNames[m_look] + "</b>", 1.0f);
        }

        // Secret tap handler (LookSecret.OnTap calls this). Only counts in the secret's own look.
        public void Collect(LookSecret secret)
        {
            if (secret == null || secret.Found || secret.Collecting || secret.Look != m_look)
                return;
            secret.Found = true;
            secret.Collecting = true;
            m_found++;
            if (m_hintLook == secret.Look) HideHint();
            Vector3 pos = secret.Visual.position;
            Fx.Play(FxKind.Stars, pos, 1.2f);
            Fx.Play(FxKind.Pickup, pos, 0.9f);
            UpdateBoard();
            StartCoroutine(CollectAnim(secret));
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);

            bool done = m_found >= LookCount;
            if (Hud != null)
                Hud.Toast(done ? "All four secrets found!" : "Secret found: " + secret.Title + "\n" + m_found + "/" + LookCount + " · try " + LookNames[NextUnfoundLook()] + " next");
            RefreshTray();
            if (done)
            {
                m_finished = true;
                m_finishRoutine = StartCoroutine(Finish());
            }
        }

        private int NextUnfoundLook()
        {
            for (int k = 1; k <= LookCount; k++)
            {
                int l = (m_look + k) % LookCount;
                LookSecret s = SecretFor(l);
                if (s != null && !s.Found) return l;
            }
            return (m_look + 1) % LookCount;
        }

        private IEnumerator CollectAnim(LookSecret secret)
        {
            Transform v = secret.Visual;
            Vector3 from = secret.transform.localScale;
            Vector3 start = v.localPosition;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.6f)
            {
                if (secret == null) yield break;
                v.localPosition = start + Vector3.up * 0.25f * Mathf.Sin(a * Mathf.PI * 0.5f) / Mathf.Max(0.0001f, secret.transform.localScale.y);
                v.Rotate(0f, 900f * Time.deltaTime, 0f, Space.Self);
                float k = a < 0.25f ? 1f + 0.4f * (a / 0.25f) : 1.4f * (1f - (a - 0.25f) / 0.75f);
                secret.transform.localScale = from * Mathf.Max(0.01f, k);
                yield return null;
            }
            if (secret == null) yield break;
            Fx.Play(FxKind.Sparkle, v.position, 0.7f);
            secret.Collecting = false;
            secret.ResetPose();
            secret.gameObject.SetActive(false);
        }

        private IEnumerator Finish()
        {
            yield return new WaitForSeconds(1.3f);
            if (m_fxPoint != null) Fx.Play(FxKind.Confetti, m_fxPoint.position, 1.8f);
            WorldCard.CloseOpen();
            ShowResultPanel();
            m_finishRoutine = null;
        }

        private void ShowResultPanel()
        {
            if (Hud == null)
                return;
            m_trayOurs = false;   // ShowResult closes the tray
            string hints = m_hints == 0 ? "No hints needed - sharp eyes!" : m_hints + " hint" + (m_hints == 1 ? "" : "s") + " used";
            Hud.ShowResult("You saw every style",
                "4/4 secrets found · " + hints + "\n<size=30>Toon · Ink · Game Boy · Pixel - one scene, four looks,\nswitched live by the Lumo style engine.</size>",
                ("Play again", ResetGame),
                ("Hub", () => { if (ShowcaseHub.Instance != null) ShowcaseHub.Instance.ReturnToHub(); }));
        }

        private void UpdateBoard()
        {
            if (m_boardCount != null) m_boardCount.text = m_found + "/" + LookCount;
            if (m_boardNow != null) m_boardNow.text = "Now showing: <b>" + LookNames[m_look] + "</b>";
            if (m_boardRows != null)
                for (int i = 0; i < m_boardRows.Length && i < LookCount; i++)
                {
                    if (m_boardRows[i] == null) continue;
                    LookSecret s = SecretFor(i);
                    bool found = s != null && s.Found;
                    string name = i == m_look ? "<color=#FF7361>" + LookNames[i].ToUpper() + "</color>" : LookNames[i].ToUpper();
                    m_boardRows[i].text = "<b>" + name + "</b>   " +
                        (found ? "<color=#3DAA5A><b>" + s.Title + "</b></color>" : "<color=#A8A6B4>? ? ?</color>");
                }
            if (Hud != null) Hud.SetScore("√ " + m_found + "/" + LookCount);
        }

        public override void DebugComplete()
        {
            if (m_finishRoutine != null) StopCoroutine(m_finishRoutine);
            m_finishRoutine = null;
            foreach (LookSecret s in m_secrets)
            {
                if (s == null) continue;
                s.Found = true;
                s.Collecting = false;
                s.gameObject.SetActive(false);
            }
            m_found = LookCount;
            m_finished = true;
            HideHint();
            UpdateBoard();
            WorldCard.CloseOpen();
            ShowResultPanel();
        }

        public override IEnumerator SelfTest()
        {
            ResetGame();
            yield return new WaitForSeconds(0.6f);
            if (m_found != 0) Debug.LogError("[SELFTEST] found count " + m_found + " after reset");
            if (m_renderer == null) Debug.LogError("[SELFTEST] no renderer data assigned");
            if (Tray == null || !Tray.IsOpen) { Debug.LogError("[SELFTEST] style tray not open on start"); yield break; }
            if (Tray.Options.Count != LookCount) Debug.LogError("[SELFTEST] tray shows " + Tray.Options.Count + " looks");
            if (Tray.Selected != 0) Debug.LogError("[SELFTEST] tray selection " + Tray.Selected + " on start, expected Toon");
            Snapshot("tray");

            if (m_exhibits != null && m_exhibits.Length > 0 && m_exhibits[0] != null)
            {
                m_exhibits[0].OnTap(new RaycastHit { point = m_exhibits[0].transform.position });
                yield return new WaitForSeconds(0.4f);
                WorldCard.CloseOpen();
            }

            for (int look = 0; look < LookCount; look++)
            {
                if (look == 2)
                {
                    // The 3D dial still works and keeps the tray in sync.
                    LookButton button = System.Array.Find(m_buttons, b => b != null && b.Look == look);
                    if (button == null) { Debug.LogError("[SELFTEST] no dial button for look " + look); continue; }
                    button.OnTap(new RaycastHit { point = button.transform.position });
                }
                else if (look > 0)
                    Tray.Select(look);   // like tapping the tile
                yield return new WaitForSeconds(0.6f);
                if (m_look != look) Debug.LogError("[SELFTEST] picking " + LookNames[look] + " -> current look " + m_look);
                if (!Tray.IsOpen || Tray.Selected != look) Debug.LogError("[SELFTEST] tray not showing " + LookNames[look] + " (open " + Tray.IsOpen + ", selected " + Tray.Selected + ")");
                foreach (string f in ManagedFeatures)
                {
                    bool expected = System.Array.IndexOf(LookFeatures[look], f) >= 0;
                    if (IsFeatureActive(f) != expected)
                        Debug.LogError("[SELFTEST] look " + LookNames[look] + ": feature '" + f + "' active=" + IsFeatureActive(f) + ", expected " + expected);
                }
                foreach (LookSecret s in m_secrets)
                {
                    bool expected = s.Look == look;
                    if (s.gameObject.activeSelf != expected)
                        Debug.LogError("[SELFTEST] look " + LookNames[look] + ": secret '" + s.Title + "' visible=" + s.gameObject.activeSelf + ", expected " + expected);
                    if (expected && s.transform.localScale.sqrMagnitude < 1e-4f)
                        Debug.LogError("[SELFTEST] secret '" + s.Title + "' is active but scaled to " + s.transform.localScale);
                }
                if (look == 1)
                {
                    Tray.Press(0);   // "? Hint"
                    if (!HintVisible) Debug.LogError("[SELFTEST] hint tag not shown");
                    yield return new WaitForSeconds(0.8f);
                    Snapshot("hint");
                }
                else
                    Snapshot("look-" + look);
                LookSecret secret = SecretFor(look);
                if (secret == null) { Debug.LogError("[SELFTEST] no secret for look " + look); continue; }
                // A secret of another look must not count.
                LookSecret other = SecretFor((look + 1) % LookCount);
                if (other != null && !other.Found)
                {
                    Collect(other);
                    if (other.Found) Debug.LogError("[SELFTEST] '" + other.Title + "' collected outside its look");
                }
                secret.OnTap(new RaycastHit { point = secret.Visual.position });
                if (!secret.Found || m_found != look + 1)
                    Debug.LogError("[SELFTEST] collecting '" + secret.Title + "': found=" + secret.Found + ", count " + m_found + ", expected " + (look + 1));
                if (HintVisible) Debug.LogError("[SELFTEST] hint tag still up after collecting");
                if (look < LookCount - 1 && Tray.Options[look].Badge != "√")
                    Debug.LogError("[SELFTEST] tray tile " + LookNames[look] + " has no check badge after its secret was found");
                yield return new WaitForSeconds(look == LookCount - 1 ? 0.2f : 0.9f);
                if (look < LookCount - 1 && secret.gameObject.activeSelf)
                    Debug.LogError("[SELFTEST] '" + secret.Title + "' still visible after collecting");
                if (look == 0)
                {
                    Snapshot("found");
                    // "Next look ›" jumps to the next look with a secret left.
                    Tray.Press(1);
                    yield return new WaitForSeconds(0.3f);
                    if (m_look != 1) Debug.LogError("[SELFTEST] Next look went to " + m_look + ", expected 1");
                }
            }

            float t0 = Time.time;
            while ((Hud == null || !Hud.ResultVisible) && Time.time - t0 < 4f)
                yield return null;
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result not shown after all secrets found");
            if (Tray.IsOpen) Debug.LogError("[SELFTEST] tray still open on the result screen");
            if (m_boardCount != null && m_boardCount.text != LookCount + "/" + LookCount)
                Debug.LogError("[SELFTEST] board shows " + m_boardCount.text);

            // Leaving must switch every full-screen look off again (they started off).
            int lookBefore = m_look;
            OnExit();
            foreach (string f in ManagedFeatures)
                if (IsFeatureActive(f))
                    Debug.LogError("[SELFTEST] feature '" + f + "' still active after OnExit");
            ApplyLook(lookBefore, false);   // still open: keep the screen consistent until the hub closes it
            ShowResultPanel();
        }
    }
}
