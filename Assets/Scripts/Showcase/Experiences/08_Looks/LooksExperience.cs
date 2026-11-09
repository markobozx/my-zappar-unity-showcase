using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Showcase.Experiences.Looks
{
    // Look Switcher Gallery: a small gallery of props from several packs. The style dial switches the
    // whole screen between four looks (URP renderer features, same set as StyleSwitcher). Each look
    // reveals one secret object that exists only in that look; tap all four to finish.
    // The renderer features are restored to how they were found when the experience exits.
    public class LooksExperience : ShowcaseExperience
    {
        public const int LookCount = 4;
        public static readonly string[] LookNames = { "Toon", "Ink", "Game Boy", "Pixel" };
        public static readonly string[] LookBlurbs =
        {
            "Flat Kit cel shading, 3 steps", "Edge-detect ink outlines", "4-colour palette pass", "Low-res pixels + outlines"
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
        private Coroutine m_finishRoutine;

        public int CurrentLook => m_look;
        public int FoundCount => m_found;
        public LookSecret[] Secrets => m_secrets;

        protected override void OnEnter()
        {
            ResetGame();
            if (Hud != null) Hud.Toast("Turn the style dial to restyle the gallery");
        }

        protected override void OnExit()
        {
            if (m_finishRoutine != null) StopCoroutine(m_finishRoutine);
            m_finishRoutine = null;
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
                Hud.SetHint("Tap a style button: Toon, Ink, Game Boy or Pixel.\nEach look hides one secret object - tap it!");
                Hud.SetTimer("");
            }
        }

        // Style dial handler (LookButton.OnTap calls this).
        public void SelectLook(int look)
        {
            if (look < 0 || look >= LookCount)
                return;
            bool changed = look != m_look;
            ApplyLook(look, true);
            if (m_buttons != null && look < m_buttons.Length && m_buttons[look] != null)
                Fx.Play(FxKind.Magic, m_buttons[look].transform.position + transform.up * 0.15f * transform.lossyScale.y, 0.8f);
            if (changed && m_fxPoint != null)
                Fx.Play(FxKind.Sparkle, m_fxPoint.position, 1.6f);
            if (Hud != null)
            {
                LookSecret s = SecretFor(look);
                string extra = s != null && !s.Found ? "  -  something new appeared!" : "";
                Hud.Toast("Look: " + LookNames[look] + extra);
            }
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
                exhibit.Title, exhibit.Credit + "\nNow viewing: <b>" + LookNames[m_look] + "</b>", 0.7f);
        }

        // Secret tap handler (LookSecret.OnTap calls this). Only counts in the secret's own look.
        public void Collect(LookSecret secret)
        {
            if (secret == null || secret.Found || secret.Collecting || secret.Look != m_look)
                return;
            secret.Found = true;
            secret.Collecting = true;
            m_found++;
            Vector3 pos = secret.Visual.position;
            Fx.Play(FxKind.Stars, pos, 1.2f);
            Fx.Play(FxKind.Pickup, pos, 0.9f);
            UpdateBoard();
            StartCoroutine(CollectAnim(secret));

            bool done = m_found >= LookCount;
            int next = NextUnfoundLook();
            string body = secret.Title + "\nOnly visible in the <b>" + LookNames[secret.Look] + "</b> look";
            Vector3 offset = secret.transform.InverseTransformVector(transform.up * 0.3f * transform.lossyScale.y);
            if (done)
                WorldCard.Show(secret.transform, offset, "Secret " + m_found + "/" + LookCount + " found", body, 0.7f);
            else
                WorldCard.Show(secret.transform, offset, "Secret " + m_found + "/" + LookCount + " found", body, 0.7f,
                    ("Try " + LookNames[next], () => { WorldCard.CloseOpen(); SelectLook(next); }));
            if (Hud != null)
                Hud.Toast(done ? "All four secrets found!" : "Secret found!  " + m_found + "/" + LookCount);
            if (done)
                m_finishRoutine = StartCoroutine(Finish());
        }

        private int NextUnfoundLook()
        {
            for (int k = 1; k <= LookCount; k++)
            {
                int l = (m_look + k) % LookCount;
                LookSecret s = SecretFor(l);
                if (s != null && !s.Found) return l;
            }
            return m_look;
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
            Hud.ShowResult("You saw every style",
                "4/4 secrets found\n<size=30>Toon · Ink · Game Boy · Pixel - one scene, four looks,\nswitched live by the Lumo style engine.</size>",
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
            if (Hud != null) Hud.SetScore("Secrets " + m_found + "/" + LookCount);
        }

        public override void DebugComplete()
        {
            foreach (LookSecret s in m_secrets)
            {
                if (s == null) continue;
                s.Found = true;
                s.Collecting = false;
                s.gameObject.SetActive(false);
            }
            m_found = LookCount;
            UpdateBoard();
            WorldCard.CloseOpen();
            ShowResultPanel();
        }

        // Extra play-test screenshot per look (editor only; the harness only shoots start/end).
        private static void DebugShot(string name)
        {
#if UNITY_EDITOR
            Camera cam = ShowcaseRig.ActiveCamera;
            if (cam == null) return;
            string dir = "Builds/Preview/looks";
            System.IO.Directory.CreateDirectory(dir);
            var rt = new RenderTexture(720, 1280, 24);
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(dir + "/" + name + ".png", tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = prev;
            DestroyImmediate(rt);
            DestroyImmediate(tex);
#endif
        }

        public override IEnumerator SelfTest()
        {
            ResetGame();
            yield return new WaitForSeconds(0.3f);
            if (m_found != 0) Debug.LogError("[SELFTEST] found count " + m_found + " after reset");
            if (m_renderer == null) Debug.LogError("[SELFTEST] no renderer data assigned");
            if (m_exhibits != null && m_exhibits.Length > 0 && m_exhibits[0] != null)
            {
                m_exhibits[0].OnTap(new RaycastHit { point = m_exhibits[0].transform.position });
                yield return new WaitForSeconds(0.4f);
            }

            for (int look = 0; look < LookCount; look++)
            {
                LookButton button = System.Array.Find(m_buttons, b => b != null && b.Look == look);
                if (button == null) { Debug.LogError("[SELFTEST] no dial button for look " + look); continue; }
                button.OnTap(new RaycastHit { point = button.transform.position });
                yield return new WaitForSeconds(0.6f);
                if (m_look != look) Debug.LogError("[SELFTEST] dial " + LookNames[look] + " -> current look " + m_look);
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
                DebugShot("play-look-" + look);
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
                yield return new WaitForSeconds(look == LookCount - 1 ? 0.2f : 0.9f);
                if (look < LookCount - 1 && secret.gameObject.activeSelf)
                    Debug.LogError("[SELFTEST] '" + secret.Title + "' still visible after collecting");
            }

            float t0 = Time.time;
            while ((Hud == null || !Hud.ResultVisible) && Time.time - t0 < 4f)
                yield return null;
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result not shown after all secrets found");
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
