using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Treasure
{
    // Treasure Rain: tap START, then tap the treasure raining onto the vault floor for 45 seconds.
    // Gold 1, coins 2, gems 5, golden product box 20, bomb crate -10. Three quick collects in a row
    // double the points until the streak breaks. Items are pooled (at most MaxAlive at once).
    public class TreasureRainExperience : ShowcaseExperience
    {
        public const float RoundSeconds = 45f;
        public const int MaxAlive = 14;
        public const float ComboWindow = 1.6f;
        public const int ComboStreak = 3;
        const string BestKey = "showcase.treasure.best";

        [SerializeField] private TreasureItem[] m_templates;       // indexed by TreasureKind
        [SerializeField] private TreasureStartButton m_startButton;
        [SerializeField] private Transform m_chestMouth;
        [SerializeField] private Transform m_chest;
        [SerializeField] private Vector2 m_rainHalfSize = new Vector2(0.95f, 0.55f);
        [SerializeField] private float m_rainCenterZ = -0.1f;
        [SerializeField] private float m_rainHeight = 1.15f;
        [SerializeField] private float m_itemLifetime = 7f;
        [SerializeField] private float m_gravity = 5.5f;           // stage units / s², slower than real for tapping

        private readonly List<TreasureItem> m_all = new List<TreasureItem>();
        private readonly List<TreasureItem> m_alive = new List<TreasureItem>();
        private readonly List<TreasureItem> m_free = new List<TreasureItem>();
        private readonly List<TreasureItem> m_busy = new List<TreasureItem>();   // flying to the chest / vanishing
        private readonly List<PopText> m_pops = new List<PopText>();
        private Transform m_popRoot;
        private Transform m_itemRoot;

        private bool m_running;
        private float m_timeLeft;
        private float m_spawnTimer;
        private int m_score;
        private int m_streak;
        private float m_lastCollect = -99f;
        private int m_spawnedThisRound;
        private int m_lastShownSecond = -1;
        private Vector3 m_startScale = Vector3.one;
        private Vector3 m_chestScale = Vector3.one;
        private Coroutine m_startRoutine;

        public bool Running => m_running;
        public int Score => m_score;
        public int Multiplier => m_streak >= ComboStreak ? 2 : 1;
        public int AliveCount => m_alive.Count;
        public int PooledCount => m_all.Count;
        public int SpawnedThisRound => m_spawnedThisRound;

        private class PopText
        {
            public Canvas Canvas;
            public Text Label;
            public Outline Outline;
            public Coroutine Routine;
        }

        void Awake()
        {
            if (m_startButton != null) m_startScale = m_startButton.transform.localScale;
            if (m_chest != null) m_chestScale = m_chest.localScale;
        }

        protected override void OnEnter()
        {
            Cleanup();
            m_running = false;
            m_score = 0;
            m_streak = 0;
            m_timeLeft = RoundSeconds;
            ShowStartButton(true);
            if (m_chest != null) m_chest.localScale = m_chestScale;
            if (Hud != null)
            {
                Hud.SetScore("");
                Hud.SetTimer("");
                Hud.SetHint("Tap START, then tap the falling treasure.\nAvoid the bomb crates!");
            }
        }

        protected override void OnExit()
        {
            m_running = false;
            Cleanup();
        }

        // Called by the START button and the "Play again" result button.
        public void StartRound()
        {
            if (Hud != null) Hud.HideResult();
            Cleanup();
            if (m_chest != null) m_chest.localScale = m_chestScale;
            m_running = true;
            m_timeLeft = RoundSeconds;
            m_spawnTimer = 0.4f;
            m_score = 0;
            m_streak = 0;
            m_lastCollect = -99f;
            m_spawnedThisRound = 0;
            m_lastShownSecond = -1;
            if (m_startButton != null)
            {
                Fx.Play(FxKind.Confetti, m_startButton.transform.position + transform.up * 0.1f * transform.lossyScale.y, 1.2f);
                if (m_startRoutine != null) StopCoroutine(m_startRoutine);
                m_startRoutine = StartCoroutine(HideStartButton());
            }
            if (Hud != null)
            {
                Hud.SetHint("Tap treasure fast: 3 in a row = x2\nGold 1 · Coins 2 · Gems 5 · Golden box 20 · Bomb -10");
                Hud.Toast("GO! 45 seconds");
            }
            UpdateHud();
        }

        void Update()
        {
            if (!m_running)
                return;

            m_timeLeft -= Time.deltaTime;
            if (m_streak > 0 && Time.time - m_lastCollect > ComboWindow)
            {
                bool hadCombo = Multiplier > 1;
                m_streak = 0;
                if (hadCombo) UpdateHud();
            }

            float progress = 1f - Mathf.Clamp01(m_timeLeft / RoundSeconds);
            m_spawnTimer -= Time.deltaTime;
            if (m_spawnTimer <= 0f)
            {
                m_spawnTimer = Mathf.Lerp(0.9f, 0.3f, Mathf.Pow(progress, 1.1f)) * Random.Range(0.8f, 1.2f);
                if (m_alive.Count + m_busy.Count < MaxAlive)
                    Spawn(PickKind(progress));
            }

            int sec = Mathf.CeilToInt(Mathf.Max(0f, m_timeLeft));
            if (sec != m_lastShownSecond)
            {
                m_lastShownSecond = sec;
                UpdateHud();
            }

            if (m_timeLeft <= 0f)
                EndRound();
        }

        void FixedUpdate()
        {
            float scale = transform.lossyScale.y;
            Vector3 g = -transform.up * m_gravity * scale;
            for (int i = m_alive.Count - 1; i >= 0; i--)
            {
                TreasureItem item = m_alive[i];
                if (item == null) { m_alive.RemoveAt(i); continue; }
                if (item.Body != null && !item.Body.isKinematic)
                    item.Body.AddForce(g, ForceMode.Acceleration);

                if (transform.InverseTransformPoint(item.transform.position).y < -2f)
                    Release(item);
                else if (Time.time - item.SpawnTime > m_itemLifetime)
                {
                    MakeBusy(item);
                    StartCoroutine(Vanish(item));
                }
            }
        }

        private TreasureKind PickKind(float progress)
        {
            bool goldenAllowed = progress > 0.1f && !m_alive.Exists(a => a.Kind == TreasureKind.Golden);
            float bomb = Mathf.Lerp(8f, 16f, progress);
            float golden = goldenAllowed ? 5f : 0f;
            float r = Random.Range(0f, 40f + 28f + 16f + golden + bomb);
            if ((r -= 40f) < 0f) return TreasureKind.Gold;
            if ((r -= 28f) < 0f) return TreasureKind.Coins;
            if ((r -= 16f) < 0f) return TreasureKind.Gems;
            if ((r -= golden) < 0f) return TreasureKind.Golden;
            return TreasureKind.Bomb;
        }

        // Spawns one item of `kind` from the pool above the stage. At the cap the oldest item makes room.
        public TreasureItem Spawn(TreasureKind kind)
        {
            if (m_templates == null || (int)kind >= m_templates.Length || m_templates[(int)kind] == null)
                return null;
            if (m_alive.Count >= MaxAlive)
                Release(m_alive[0]);

            TreasureItem item = m_free.Find(f => f.Kind == kind);
            if (item != null)
                m_free.Remove(item);
            else
                item = CreateInstance(kind);

            Vector3 local = new Vector3(Random.Range(-m_rainHalfSize.x, m_rainHalfSize.x), m_rainHeight,
                m_rainCenterZ + Random.Range(-m_rainHalfSize.y, m_rainHalfSize.y));
            item.transform.position = transform.TransformPoint(local);
            item.transform.rotation = transform.rotation * Random.rotation;
            item.transform.localScale = item.BaseScale;
            item.Body.isKinematic = false;
            foreach (Collider c in item.GetComponentsInChildren<Collider>(true)) c.enabled = true;
            item.gameObject.SetActive(true);
            item.Body.linearVelocity = -transform.up * 0.3f * transform.lossyScale.y;
            item.Body.angularVelocity = Random.insideUnitSphere * 5f;
            item.SpawnTime = Time.time;
            item.Live = true;
            m_alive.Add(item);
            m_spawnedThisRound++;
            return item;
        }

        private TreasureItem CreateInstance(TreasureKind kind)
        {
            if (m_itemRoot == null)
            {
                m_itemRoot = new GameObject("Rain Items").transform;
                m_itemRoot.SetParent(transform, false);
            }
            TreasureItem template = m_templates[(int)kind];
            GameObject go = Instantiate(template.gameObject, m_itemRoot);
            go.name = template.name.Replace("Template ", "") + " (pooled)";
            go.transform.localScale = template.transform.localScale;
            TreasureItem item = go.GetComponent<TreasureItem>();
            item.Owner = this;
            item.BaseScale = go.transform.localScale;

            // Physics box matches the mesh; a larger trigger child makes taps forgiving on phones.
            BoxCollider box = TreasurePhysics.FitBoxCollider(go);
            var tapZone = new GameObject("Tap Zone");
            tapZone.transform.SetParent(go.transform, false);
            BoxCollider tap = tapZone.AddComponent<BoxCollider>();
            tap.isTrigger = true;
            tap.center = box.center;
            tap.size = box.size * 1.45f;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.useGravity = false;
            rb.linearDamping = 0.15f;
            rb.angularDamping = 0.4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            item.Body = rb;
            m_all.Add(item);
            return item;
        }

        private void Release(TreasureItem item)
        {
            if (item == null)
                return;
            item.Live = false;
            m_alive.Remove(item);
            m_busy.Remove(item);
            if (item.Body != null)
            {
                if (!item.Body.isKinematic)
                {
                    item.Body.linearVelocity = Vector3.zero;
                    item.Body.angularVelocity = Vector3.zero;
                }
                item.Body.isKinematic = true;
            }
            item.gameObject.SetActive(false);
            if (!m_free.Contains(item))
                m_free.Add(item);
        }

        // Takes a live item out of play (no taps, no physics) while it animates away.
        private void MakeBusy(TreasureItem item)
        {
            item.Live = false;
            m_alive.Remove(item);
            if (!m_busy.Contains(item)) m_busy.Add(item);
            if (item.Body != null) item.Body.isKinematic = true;
            foreach (Collider c in item.GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        // Tap handler for every item (TreasureItem.OnTap calls this).
        public void Collect(TreasureItem item, Vector3 point)
        {
            if (!m_running || item == null || !item.Live)
                return;
            Vector3 pos = item.transform.position;
            float s = transform.lossyScale.y;

            if (item.Kind == TreasureKind.Bomb)
            {
                m_score = Mathf.Max(0, m_score + item.Value);
                m_streak = 0;
                Fx.Play(FxKind.Explosion, pos, 0.9f);
                ShowPop(pos, item.Value.ToString(), new Color(1f, 0.3f, 0.25f), 1.15f);
                foreach (TreasureItem other in m_alive)
                    if (other != item && other.Body != null && !other.Body.isKinematic)
                        other.Body.AddExplosionForce(2.2f * s, pos, 0.7f * s, 0.6f, ForceMode.VelocityChange);
                if (Hud != null) Hud.Toast("BOOM!  -10");
                Release(item);
            }
            else
            {
                m_streak = Time.time - m_lastCollect <= ComboWindow ? m_streak + 1 : 1;
                m_lastCollect = Time.time;
                int mult = Multiplier;
                int pts = item.Value * mult;
                m_score += pts;
                bool golden = item.Kind == TreasureKind.Golden;
                Fx.Play(golden ? FxKind.Stars : FxKind.Pickup, pos, golden ? 1.6f : 1f);
                if (golden) Fx.Play(FxKind.Confetti, pos, 1.2f);
                Color c = golden ? new Color(1f, 0.85f, 0.2f) : item.Kind == TreasureKind.Gems ? new Color(0.55f, 0.9f, 1f) : Color.white;
                ShowPop(pos, "+" + pts + (mult > 1 ? "  x" + mult : ""), c, golden ? 1.3f : 1f);
                if (m_streak == ComboStreak && Hud != null) Hud.Toast("COMBO x2!");
                else if (golden && Hud != null) Hud.Toast("Golden Nova box!  +" + pts);
                MakeBusy(item);
                StartCoroutine(FlyToChest(item));
            }
            UpdateHud();
        }

        private IEnumerator FlyToChest(TreasureItem item)
        {
            Transform t = item.transform;
            Vector3 from = t.position;
            Vector3 fromScale = t.localScale;
            float up = 0.35f * transform.lossyScale.y;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.45f)
            {
                if (item == null) yield break;
                Vector3 to = m_chestMouth != null ? m_chestMouth.position : transform.position;
                float e = a * a * (3f - 2f * a);
                t.position = Vector3.Lerp(from, to, e) + transform.up * up * 4f * a * (1f - a);
                t.localScale = Vector3.Lerp(fromScale, fromScale * 0.35f, e);
                t.Rotate(0f, 540f * Time.deltaTime, 0f, Space.Self);
                yield return null;
            }
            if (item == null) yield break;
            if (m_chestMouth != null) Fx.Play(FxKind.Sparkle, m_chestMouth.position, 0.8f);
            if (m_chest != null)
            {
                m_chest.localScale = m_chestScale;
                StartCoroutine(Tween.Punch(m_chest, 0.12f, 0.25f));
            }
            Release(item);
        }

        private IEnumerator Vanish(TreasureItem item)
        {
            Transform t = item.transform;
            Vector3 from = t.localScale;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.3f)
            {
                if (item == null) yield break;
                t.localScale = from * (1f - a);
                yield return null;
            }
            if (item == null) yield break;
            Fx.Play(FxKind.Poof, t.position, 0.5f);
            Release(item);
        }

        public void EndRound()
        {
            if (!m_running && Hud != null && Hud.ResultVisible)
                return;
            m_running = false;
            m_timeLeft = 0f;
            m_streak = 0;
            int shown = 0;
            for (int i = m_alive.Count - 1; i >= 0; i--)
            {
                if (shown++ < 5) Fx.Play(FxKind.Poof, m_alive[i].transform.position, 0.5f);
                Release(m_alive[i]);
            }
            if (m_chestMouth != null) Fx.Play(FxKind.Confetti, m_chestMouth.position, 1.5f);
            if (m_chest != null) StartCoroutine(Tween.Punch(m_chest, 0.2f, 0.4f));
            ShowStartButton(true);
            UpdateHud();
            ShowResultPanel();
        }

        public int BestScore
        {
            get
            {
                try { return PlayerPrefs.GetInt(BestKey, 0); }
                catch { return 0; }
            }
        }

        private void ShowResultPanel()
        {
            int best = BestScore;
            bool record = m_score > best;
            if (record)
            {
                best = m_score;
                try { PlayerPrefs.SetInt(BestKey, best); PlayerPrefs.Save(); }
                catch { }
            }
            if (Hud == null)
                return;

            string title = m_score >= 120 ? "Vault master!" : m_score >= 60 ? "Big haul!" : m_score >= 25 ? "Nice catch!" : "Keep digging!";
            string reward = DiscountText(m_score);
            string next = m_score >= 120 ? "Top tier unlocked" : m_score >= 60 ? "Score 120+: 15% off at Nova Store" : m_score >= 25 ? "Score 60+: 10% off at Nova Store" : "Score 25+: 5% off at Nova Store";
            string body = "Score <b>" + m_score + "</b>   ·   Best " + best + (record && m_score > 0 ? "  <color=#FF7361>NEW!</color>" : "") +
                          "\n<b>" + reward + "</b>\n<size=30>" + next + "</size>";
            Hud.ShowResult(title, body, ("Play again", StartRound), ("Hub", () => { if (ShowcaseHub.Instance != null) ShowcaseHub.Instance.ReturnToHub(); }));
        }

        public static string DiscountText(int score)
        {
            if (score >= 120) return "You won 15% off at Nova Store!";
            if (score >= 60) return "You won 10% off at Nova Store!";
            if (score >= 25) return "You won 5% off at Nova Store!";
            return "No voucher this time";
        }

        private void UpdateHud()
        {
            if (Hud == null)
                return;
            int sec = Mathf.CeilToInt(Mathf.Max(0f, m_timeLeft));
            string time = "0:" + sec.ToString("00");
            Hud.SetTimer(m_running && sec <= 5 ? "<color=#FF7361>" + time + "</color>" : time);
            Hud.SetScore((Multiplier > 1 ? "<color=#FFD23F>x2</color>  " : "") + m_score + " pts");
        }

        private void ShowStartButton(bool show)
        {
            if (m_startButton == null)
                return;
            m_startButton.gameObject.SetActive(show);
            m_startButton.transform.localScale = m_startScale;
            if (m_startRoutine != null) StopCoroutine(m_startRoutine);
            m_startRoutine = null;
            if (show && isActiveAndEnabled)
                m_startRoutine = StartCoroutine(Tween.PopIn(m_startButton.transform, 0.35f));
        }

        private IEnumerator HideStartButton()
        {
            Transform t = m_startButton.transform;
            yield return Tween.Punch(t, 0.25f, 0.15f);
            yield return Tween.ScaleTo(t, m_startScale * 0.01f, 0.2f);
            t.gameObject.SetActive(false);
            t.localScale = m_startScale;
            m_startRoutine = null;
        }

        private void ShowPop(Vector3 worldPos, string text, Color color, float size)
        {
            PopText pop = null;
            foreach (PopText p in m_pops)
                if (p.Routine == null) { pop = p; break; }
            if (pop == null && m_pops.Count < 8)
                pop = CreatePop();
            if (pop == null)
            {
                pop = m_pops[0];
                StopCoroutine(pop.Routine);
            }
            pop.Label.text = text;
            pop.Label.color = color;
            pop.Routine = StartCoroutine(PopRoutine(pop, transform.InverseTransformPoint(worldPos) + Vector3.up * 0.12f, size));
        }

        private PopText CreatePop()
        {
            if (m_popRoot == null)
            {
                m_popRoot = new GameObject("Pop Texts").transform;
                m_popRoot.SetParent(transform, false);
            }
            Canvas canvas = Ui.WorldCanvas("Pop", m_popRoot, new Vector2(400, 140), 0.5f);
            Text label = Ui.Label(canvas.transform, "", 96, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(label.rectTransform);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.13f, 0.12f, 0.2f, 0.9f);
            outline.effectDistance = new Vector2(4f, -4f);
            canvas.gameObject.AddComponent<Billboard>().YawOnly = false;
            canvas.gameObject.SetActive(false);
            var pop = new PopText { Canvas = canvas, Label = label, Outline = outline };
            m_pops.Add(pop);
            return pop;
        }

        private IEnumerator PopRoutine(PopText pop, Vector3 localStart, float size)
        {
            Transform t = pop.Canvas.transform;
            float baseScale = 0.5f / 400f * size;
            pop.Canvas.gameObject.SetActive(true);
            Color c = pop.Label.color;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.9f)
            {
                t.localPosition = localStart + Vector3.up * 0.3f * (1f - (1f - a) * (1f - a));
                float pop01 = a < 0.15f ? Tween.EaseOutBack(a / 0.15f) : 1f;
                t.localScale = Vector3.one * baseScale * Mathf.Max(0.01f, pop01);
                c.a = a < 0.6f ? 1f : 1f - (a - 0.6f) / 0.4f;
                pop.Label.color = c;
                Color oc = pop.Outline.effectColor;
                oc.a = 0.9f * c.a;
                pop.Outline.effectColor = oc;
                yield return null;
            }
            pop.Canvas.gameObject.SetActive(false);
            pop.Routine = null;
        }

        // Destroys every pooled item and hides floating texts.
        private void Cleanup()
        {
            foreach (TreasureItem item in m_all)
                if (item != null) Destroy(item.gameObject);
            m_all.Clear();
            m_alive.Clear();
            m_free.Clear();
            m_busy.Clear();
            foreach (PopText p in m_pops)
            {
                if (p.Routine != null) StopCoroutine(p.Routine);
                p.Routine = null;
                if (p.Canvas != null) p.Canvas.gameObject.SetActive(false);
            }
        }

        public override void DebugComplete()
        {
            if (m_running)
            {
                EndRound();
                return;
            }
            UpdateHud();
            ShowResultPanel();
        }

        // Extra play-test screenshots of mid-round states (editor only; the harness only shoots start/end).
        private static void DebugShot(string name)
        {
#if UNITY_EDITOR
            Camera cam = ShowcaseRig.ActiveCamera;
            if (cam == null) return;
            string dir = "Builds/Preview/treasure";
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
            StartRound();
            if (!m_running) Debug.LogError("[SELFTEST] round did not start");
            if (m_startButton != null && m_startButton.gameObject.activeSelf)
            {
                yield return new WaitForSeconds(0.5f);
                if (m_startButton.gameObject.activeSelf) Debug.LogError("[SELFTEST] START button still visible during the round");
            }

            float t0 = Time.time;
            while (m_spawnedThisRound < 5 && Time.time - t0 < 20f)
                yield return null;
            if (m_spawnedThisRound < 5) Debug.LogError("[SELFTEST] only " + m_spawnedThisRound + " items spawned in 20 s");
            if (m_alive.Count > MaxAlive) Debug.LogError("[SELFTEST] " + m_alive.Count + " items alive, cap is " + MaxAlive);
            yield return new WaitForSeconds(1.2f);   // let a few land
            DebugShot("play-rain");

            // Three quick non-bomb collects: the third gets the x2 combo.
            for (int i = 0; i < ComboStreak; i++)
            {
                TreasureItem item = m_alive.Find(a => a.Kind != TreasureKind.Bomb) ?? Spawn(TreasureKind.Gems);
                int before = m_score;
                int expected = item.Value * (i + 1 >= ComboStreak ? 2 : 1);
                item.OnTap(new RaycastHit { point = item.transform.position });
                if (m_score != before + expected)
                    Debug.LogError("[SELFTEST] collect " + item.Kind + " #" + (i + 1) + ": score " + before + " -> " + m_score + ", expected +" + expected);
                if (item.Live) Debug.LogError("[SELFTEST] collected item still live");
            }
            if (Multiplier != 2) Debug.LogError("[SELFTEST] multiplier " + Multiplier + " after " + ComboStreak + " quick collects, expected 2");
            yield return new WaitForSeconds(0.3f);
            DebugShot("play-collect");

            TreasureItem bomb = Spawn(TreasureKind.Bomb);
            TreasureItem golden = Spawn(TreasureKind.Golden);
            yield return new WaitForSeconds(0.9f);
            DebugShot("play-specials");
            int beforeBomb = m_score;
            bomb.OnTap(new RaycastHit { point = bomb.transform.position });
            if (m_score != Mathf.Max(0, beforeBomb - 10)) Debug.LogError("[SELFTEST] bomb: score " + beforeBomb + " -> " + m_score + ", expected -10");
            if (Multiplier != 1) Debug.LogError("[SELFTEST] bomb should reset the combo, multiplier is " + Multiplier);
            yield return new WaitForSeconds(0.25f);
            DebugShot("play-bomb");

            if (golden == null || !golden.Live) golden = Spawn(TreasureKind.Golden);   // the blast may have knocked it off the stage
            int beforeGolden = m_score;
            golden.OnTap(new RaycastHit { point = golden.transform.position });
            if (m_score != beforeGolden + 20) Debug.LogError("[SELFTEST] golden box: score " + beforeGolden + " -> " + m_score + ", expected +20");
            yield return new WaitForSeconds(1.2f);

            int finalScore = m_score;
            EndRound();
            yield return null;
            if (m_running) Debug.LogError("[SELFTEST] round still running after EndRound");
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel not shown");
            if (m_alive.Count != 0) Debug.LogError("[SELFTEST] " + m_alive.Count + " items still alive after EndRound");
            if (BestScore < finalScore) Debug.LogError("[SELFTEST] best score " + BestScore + " < " + finalScore);
            if (m_startButton != null && !m_startButton.gameObject.activeSelf) Debug.LogError("[SELFTEST] START button not back after the round");
            if (m_all.Count > MaxAlive * 5) Debug.LogError("[SELFTEST] pool grew to " + m_all.Count);
            Debug.Log("[SELFTEST] treasure: score " + finalScore + ", best " + BestScore + ", pooled " + m_all.Count);

            OnExit();
            yield return null;
            if (PooledCount != 0 || AliveCount != 0 || m_busy.Count != 0) Debug.LogError("[SELFTEST] pool not empty after OnExit: " + PooledCount + " pooled, " + AliveCount + " alive");
            if (m_itemRoot != null && m_itemRoot.childCount != 0) Debug.LogError("[SELFTEST] " + m_itemRoot.childCount + " item objects left after OnExit");

            // Leave the end state on screen.
            ShowResultPanel();
        }
    }

    // Pack prefabs ship without colliders; wrap all meshes in one box in the root's local space
    // (runtime copy of the editor's ShowcaseBuildContext.FitBoxCollider).
    public static class TreasurePhysics
    {
        public static BoxCollider FitBoxCollider(GameObject go)
        {
            Bounds local = new Bounds();
            bool any = false;
            Matrix4x4 toLocal = go.transform.worldToLocalMatrix;
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            if (any)
            {
                box.center = local.center;
                box.size = local.size;
            }
            return box;
        }
    }
}
