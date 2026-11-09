using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Detective
{
    // Kitchen Detective: the last Nova ice cream is gone. Tap around the kitchen to find 4 clues
    // (each is written into the case notebook on the wall), then the line-up lights up and the
    // player questions the suspects and accuses one. Wrong guesses get a denial and a hint.
    public class DetectiveExperience : ShowcaseExperience
    {
        public const string Brand = "Nova";
        const float NothingToastGap = 1.2f;
        const float MaxCardTop = 1.25f;
        const float MaxCardX = 0.5f;

        [SerializeField] private DetectiveClue[] m_clues;
        [SerializeField] private DetectiveSuspect[] m_suspects;
        [SerializeField] private DetectiveProp[] m_props;
        [SerializeField] private Transform m_board;            // notebook backing on the back wall
        [SerializeField] private Vector2 m_boardSize = new Vector2(0.92f, 0.5f);
        [SerializeField] private GameObject m_markerTemplate;  // inactive magnifier shown over found clues
        [SerializeField] private Vector3 m_storyPoint = new Vector3(0f, 0.62f, 0.1f);

        private Transform m_boardCanvas;
        private Text[] m_noteRows;
        private Text m_noteCount, m_noteFooter;
        private readonly List<Transform> m_markers = new List<Transform>();
        private readonly List<Vector3> m_markerBase = new List<Vector3>();

        private bool m_started, m_solved, m_resultShown;
        private float m_startTime, m_elapsed;
        private int m_shownSeconds = -1;
        private int m_wrong;
        private float m_lastNothing = -10f;
        private DetectiveSuspect m_culprit;
        private readonly List<Coroutine> m_routines = new List<Coroutine>();

        public int FoundCount => m_clues.Count(c => c.Found);
        public bool Unlocked => FoundCount >= m_clues.Length;
        public bool Solved => m_solved;
        public int WrongGuesses => m_wrong;
        public int NothingTaps { get; private set; }
        public int NotebookCount => m_noteRows == null ? 0 : m_noteRows.Where((r, i) => r.text.Contains(m_clues[i].NoteLine)).Count();

        protected override void OnEnter()
        {
            EnsureUi();
            ResetCase();
        }

        protected override void OnExit()
        {
            ClearMarkers();
            RestoreScales();
        }

        // Exit stops coroutines mid-punch; put every punched object back to its real size.
        private void RestoreScales()
        {
            foreach (var kv in m_baseScale)
                if (kv.Key != null) kv.Key.localScale = kv.Value;
            m_bumping.Clear();
        }

        public void ResetCase()
        {
            // Not StopAllCoroutines: SelfTest runs as a coroutine on this component and calls ResetCase.
            foreach (var r in m_routines)
                if (r != null) StopCoroutine(r);
            m_routines.Clear();
            WorldCard.CloseOpen();
            ClearMarkers();
            foreach (var c in m_clues)
                c.ResetClue();
            foreach (var s in m_suspects)
            {
                s.SetLit(false);
                if (s.Body != null)
                {
                    s.Body.localPosition = Vector3.zero;
                    s.Body.localRotation = Quaternion.identity;
                }
            }
            m_culprit = m_suspects.FirstOrDefault(s => s.IsCulprit);
            m_started = m_solved = m_resultShown = false;
            m_elapsed = 0f;
            m_shownSeconds = -1;
            m_wrong = 0;
            NothingTaps = 0;
            m_lastNothing = -10f;
            if (Hud != null)
            {
                Hud.HideResult();
                Hud.SetTimer("0:00");
            }
            RefreshBoard();
            ShowIntro();
        }

        void Update()
        {
            if (m_started && !m_solved)
            {
                m_elapsed = Time.time - m_startTime;
                int secs = Mathf.FloorToInt(m_elapsed);
                if (secs != m_shownSeconds)
                {
                    m_shownSeconds = secs;
                    Hud?.SetTimer(Clock(m_elapsed));
                }
            }

            float t = Time.time;
            for (int i = 0; i < m_markers.Count; i++)
            {
                if (m_markers[i] == null) continue;
                m_markers[i].localPosition = m_markerBase[i] + Vector3.up * (0.025f * Mathf.Sin(t * 2.4f + i));
                m_markers[i].localRotation = Quaternion.Euler(0f, t * 70f + i * 40f, 0f);
            }
            foreach (var s in m_suspects)
                if (s.Lit && s.Body != null && !m_solved)
                    s.Body.localPosition = Vector3.up * (0.012f * (1f + Mathf.Sin(t * 5f + s.transform.position.x * 3f)));
        }

        public static string Clock(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // ---- Story beats ----

        private void ShowIntro()
        {
            WorldCard.Show(transform, m_storyPoint, "The Missing " + Brand,
                "The last " + Brand + " ice cream\nis gone! Find 4 clues, then\nname the culprit.",
                0.95f, ("Start investigating ►", Begin));
        }

        public void Begin()
        {
            StartClock();
            WorldCard.CloseOpen();
        }

        private void StartClock()
        {
            if (m_started) return;
            m_started = true;
            m_startTime = Time.time;
        }

        // ---- Interaction (taps and card buttons all come through here) ----

        public void Inspect(DetectiveClue clue)
        {
            if (m_solved || clue == null)
                return;
            StartClock();
            Vector3 top = StagePoint(clue.transform) + Vector3.up * clue.CardHeight;
            Bump(clue.transform, 0.15f);
            Fx.Play(FxKind.Sparkle, transform.TransformPoint(top), 0.8f);

            int number = System.Array.IndexOf(m_clues, clue) + 1;
            if (!clue.Found)
            {
                clue.Found = true;
                Fx.Play(FxKind.Magic, clue.transform.position + transform.up * 0.05f, 0.7f);
                SpawnMarker(top + Vector3.up * 0.09f);
                if (clue.Animated != null)
                    Run(Swing(clue.Animated, clue.OpenRotation, 0.6f));
                RefreshBoard();
                if (m_boardCanvas != null)
                {
                    Bump(m_boardCanvas, 0.06f);
                    Fx.Play(FxKind.Sparkle, m_boardCanvas.position, 0.6f);
                }
                if (Unlocked)
                {
                    Hud?.Toast("All 4 clues found!\n<size=32>Tap a suspect to question them</size>", 2.4f);
                    Run(LightUpSuspects());
                }
                else
                {
                    Hud?.Toast("Clue found! " + FoundCount + "/" + m_clues.Length);
                }
            }

            var buttons = Unlocked
                ? new (string, System.Action)[] { ("To the line-up ►", WorldCard.CloseOpen) }
                : new (string, System.Action)[] { ("Noted ►", WorldCard.CloseOpen) };
            Card(top + Vector3.up * 0.16f, "Clue " + number + ": " + clue.Title, clue.Body, 0.9f, buttons);
        }

        public void Nothing(DetectiveProp prop)
        {
            if (m_solved || prop == null)
                return;
            StartClock();
            Bump(prop.transform, 0.1f);
            if (Time.time - m_lastNothing < NothingToastGap)
                return;
            m_lastNothing = Time.time;
            NothingTaps++;
            Fx.Play(FxKind.Poof, prop.transform.position + transform.up * 0.1f, 0.5f);
            Hud?.Toast("Nothing here\n<size=32>" + prop.Quip + "</size>", 1.4f);
        }

        public void Question(DetectiveSuspect s)
        {
            if (m_solved || s == null)
                return;
            StartClock();
            Bump(s.Body != null ? s.Body : s.transform, 0.14f);
            Vector3 at = StagePoint(s.transform) + Vector3.up * (s.CardHeight + 0.14f);
            if (Unlocked)
            {
                Card(at, s.Name, s.Alibi, 0.9f,
                    ("Accuse!", () => Accuse(s)),
                    ("Hmm, not yet", WorldCard.CloseOpen));
            }
            else
            {
                Card(at, s.Name, s.Alibi + "\n<size=28>Find all 4 clues to accuse (" + FoundCount + "/" + m_clues.Length + ")</size>", 0.9f,
                    ("Keep looking", WorldCard.CloseOpen));
            }
        }

        public void Accuse(DetectiveSuspect s)
        {
            if (m_solved || s == null)
                return;
            if (!Unlocked)
            {
                Hud?.Toast("Find all 4 clues first!");
                return;
            }
            WorldCard.CloseOpen();
            if (s.IsCulprit)
            {
                Solve(s);
                return;
            }
            m_wrong++;
            Fx.Play(FxKind.Poof, s.transform.position + transform.up * 0.2f, 0.7f);
            if (s.Body != null)
                Run(ShakeHead(s.Body));
            Hud?.Toast(s.Denial + "\n<size=30>Hint: " + s.Hint + "</size>", 3.2f);
            if (m_noteFooter != null)
            {
                m_noteFooter.text = "Hint: " + s.Hint;
                m_noteFooter.color = Ui.Accent;
            }
        }

        private void Solve(DetectiveSuspect s)
        {
            m_solved = true;
            if (m_started)
                m_elapsed = Time.time - m_startTime;
            Hud?.SetTimer(Clock(m_elapsed));
            foreach (var other in m_suspects)
                if (other != s) other.SetLit(false);
            if (s.Body != null) s.Body.localPosition = Vector3.zero;
            if (s.Body != null) StartCoroutine(Tween.Hop(s.Body, 0.12f, 0.45f));
            Fx.Play(FxKind.Confetti, s.transform.position + transform.up * 0.35f, 1.4f);
            Fx.Play(FxKind.Stars, transform.TransformPoint(new Vector3(0f, 0.5f, 0.2f)), 1.3f);
            if (m_noteFooter != null)
            {
                m_noteFooter.text = "CASE CLOSED: " + s.Name + "!";
                m_noteFooter.color = Ui.Good;
            }
            Card(new Vector3(0f, s.CardHeight + 0.14f, StagePoint(s.transform).z), "Case closed!",
                "Bolt's fan overheated at\nmidnight, so he \"borrowed\"\nthe last " + Brand + " cone to cool\nhis circuits. Brain freeze!",
                0.95f, ("See result ►", ShowResultNow));
            Run(ResultSoon());
        }

        private IEnumerator ResultSoon()
        {
            yield return new WaitForSeconds(4f);
            ShowResultNow();
        }

        private void ShowResultNow()
        {
            if (m_resultShown || Hud == null)
                return;
            m_resultShown = true;
            string guesses = m_wrong == 0 ? "first guess, Detective!" : m_wrong + (m_wrong == 1 ? " wrong guess" : " wrong guesses");
            string who = m_culprit != null ? m_culprit.Name : "The culprit";
            Hud.ShowResult("Case closed in " + Clock(m_elapsed),
                who + " ate the last " + Brand + " cone!\n<size=32>4/4 clues · " + guesses + "</size>",
                ("Play again", ResetCase),
                ("Hub", () => ShowcaseHub.Instance?.ReturnToHub()));
        }

        // ---- Effects ----

        private IEnumerator LightUpSuspects()
        {
            yield return new WaitForSeconds(0.5f);
            if (m_noteFooter != null)
            {
                m_noteFooter.text = "Line-up ready: tap a suspect!";
                m_noteFooter.color = Ui.Accent;
            }
            foreach (var s in m_suspects)
            {
                if (m_solved) yield break;
                s.SetLit(true);
                Fx.Play(FxKind.Stars, s.transform.position + transform.up * 0.3f, 0.6f);
                Bump(s.Body != null ? s.Body : s.transform, 0.2f);
                yield return new WaitForSeconds(0.25f);
            }
        }

        private IEnumerator Swing(Transform t, Quaternion to, float time)
        {
            Quaternion from = t.localRotation;
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                if (t == null) yield break;
                t.localRotation = Quaternion.SlerpUnclamped(from, to, Tween.EaseOutBack(a));
                yield return null;
            }
            if (t != null) t.localRotation = to;
        }

        private IEnumerator ShakeHead(Transform t)
        {
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.6f)
            {
                if (t == null) yield break;
                t.localRotation = Quaternion.Euler(0f, 28f * Mathf.Sin(a * Mathf.PI * 6f) * (1f - a), 0f);
                yield return null;
            }
            if (t != null) t.localRotation = Quaternion.identity;
        }

        private void SpawnMarker(Vector3 stagePos)
        {
            if (m_markerTemplate == null)
                return;
            var go = Instantiate(m_markerTemplate, transform);
            go.name = "Clue Marker";
            go.transform.localPosition = stagePos;
            go.SetActive(true);
            m_markers.Add(go.transform);
            m_markerBase.Add(stagePos);
            StartCoroutine(Tween.PopIn(go.transform, 0.35f));
        }

        private void ClearMarkers()
        {
            foreach (var m in m_markers)
                if (m != null) Destroy(m.gameObject);
            m_markers.Clear();
            m_markerBase.Clear();
        }

        // Punch that ignores re-taps while one is running, so rapid taps can't drift the base scale.
        private readonly HashSet<Transform> m_bumping = new HashSet<Transform>();
        private readonly Dictionary<Transform, Vector3> m_baseScale = new Dictionary<Transform, Vector3>();

        private void Bump(Transform t, float amount)
        {
            if (t == null || !m_bumping.Add(t))
                return;
            if (!m_baseScale.ContainsKey(t))
                m_baseScale[t] = t.localScale;
            StartCoroutine(BumpRoutine(t, amount));
        }

        private IEnumerator BumpRoutine(Transform t, float amount)
        {
            yield return Tween.Punch(t, amount, 0.28f);
            m_bumping.Remove(t);
        }

        // WorldCard centres on its anchor point; lift it by half its height so it sits on `bottom`.
        private WorldCard Card(Vector3 bottom, string title, string body, float width, params (string, System.Action)[] buttons)
        {
            int lines = Mathf.Max(1, body.Split('\n').Length);
            float h = (130 + lines * 44 + (buttons.Length > 0 ? 110 : 20)) * width / 600f;
            // Keep the card on the stage and low enough to stay in view; when a tall object pushes it
            // down, bring it forward so it is not buried inside that object.
            // Narrow clamp: on a portrait phone the stage edges are close to the screen edges and an
            // edge card gets cut off and skewed by perspective.
            bottom.x = Mathf.Clamp(bottom.x, -MaxCardX, MaxCardX);
            if (bottom.y + h > MaxCardTop)
            {
                bottom.y = Mathf.Max(0.05f, MaxCardTop - h);
                bottom.z = Mathf.Min(bottom.z, 0.15f);
            }
            return WorldCard.Show(transform, bottom + Vector3.up * (h * 0.5f), title, body, width, buttons);
        }

        // Story coroutines that must not survive a reset.
        private void Run(IEnumerator routine) => m_routines.Add(StartCoroutine(routine));

        private Vector3 StagePoint(Transform t) => transform.InverseTransformPoint(t.position);

        // ---- Notebook board ----

        private void RefreshBoard()
        {
            if (m_noteRows != null)
            {
                for (int i = 0; i < m_clues.Length; i++)
                {
                    bool found = m_clues[i].Found;
                    m_noteRows[i].text = (i + 1) + ".  " + (found ? m_clues[i].NoteLine : "? ? ?");
                    m_noteRows[i].color = found ? Ui.Ink : new Color(0.65f, 0.63f, 0.7f);
                }
                m_noteCount.text = FoundCount + "/" + m_clues.Length;
                if (!Unlocked)
                {
                    m_noteFooter.text = "Suspects: find all 4 clues first";
                    m_noteFooter.color = Ui.Muted;
                }
            }
            Hud?.SetScore("Clues " + FoundCount + "/" + m_clues.Length);
        }

        // World UI is built once on first entry and kept; ResetCase clears its contents.
        private void EnsureUi()
        {
            if (m_boardCanvas == null && m_board != null)
                BuildBoard();
            foreach (var s in m_suspects)
                if (s.transform.Find("Name Tag") == null)
                    BuildNameTag(s);
        }

        private void BuildBoard()
        {
            const float w = 900f;
            float h = w * m_boardSize.y / m_boardSize.x;
            var canvas = Ui.WorldCanvas("Notebook Canvas", m_board.parent, new Vector2(w, h), m_boardSize.x);
            canvas.transform.localPosition = m_board.localPosition - new Vector3(0f, 0f, m_board.localScale.z * 0.5f + 0.004f);
            canvas.transform.localRotation = Quaternion.identity;
            m_boardCanvas = canvas.transform;

            var bg = Ui.Panel(canvas.transform, new Color(1f, 0.97f, 0.86f, 1f), "Paper");
            bg.raycastTarget = false;
            Ui.Stretch(bg.rectTransform, 14, 14, 14, 14);
            // Ruled lines and a red margin so it reads as a detective's notebook page.
            for (int i = 0; i < 4; i++)
            {
                var rule = Ui.Panel(bg.transform, new Color(0.55f, 0.72f, 0.92f, 0.55f), "Rule");
                rule.raycastTarget = false;
                Ui.Place(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(w - 28, 3), new Vector2(0, -182 - i * 66));
            }
            var margin = Ui.Panel(bg.transform, new Color(0.95f, 0.45f, 0.45f, 0.7f), "Margin");
            margin.raycastTarget = false;
            Ui.Place(margin.rectTransform, new Vector2(0f, 0.5f), new Vector2(4, h - 28), new Vector2(84, 0));

            var title = Ui.Label(bg.transform, "CASE NOTES", 56, Ui.Ink, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
            Ui.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(520, 80), new Vector2(104, -18));
            m_noteCount = Ui.Label(bg.transform, "0/4", 56, Ui.Accent, TextAnchor.MiddleRight, FontStyle.Bold, "Count");
            Ui.Place(m_noteCount.rectTransform, new Vector2(1f, 1f), new Vector2(220, 80), new Vector2(-30, -18));
            var sub = Ui.Label(bg.transform, "Who ate the last " + Brand + "?", 32, Ui.Muted, TextAnchor.MiddleLeft, FontStyle.Italic, "Subtitle");
            Ui.Place(sub.rectTransform, new Vector2(0f, 1f), new Vector2(700, 50), new Vector2(104, -92));

            m_noteRows = new Text[m_clues.Length];
            for (int i = 0; i < m_clues.Length; i++)
            {
                m_noteRows[i] = Ui.Label(bg.transform, "", 42, Ui.Ink, TextAnchor.LowerLeft, FontStyle.Bold, "Row " + (i + 1));
                Ui.Place(m_noteRows[i].rectTransform, new Vector2(0f, 1f), new Vector2(w - 150, 60), new Vector2(104, -122 - i * 66));
            }
            m_noteFooter = Ui.Label(bg.transform, "", 36, Ui.Muted, TextAnchor.MiddleCenter, FontStyle.Bold, "Footer");
            Ui.Place(m_noteFooter.rectTransform, new Vector2(0.5f, 0f), new Vector2(w - 60, 56), new Vector2(0, 8));
        }

        private void BuildNameTag(DetectiveSuspect s)
        {
            var holder = new GameObject("Name Tag").transform;
            holder.SetParent(s.transform, false);
            holder.position = transform.TransformPoint(StagePoint(s.transform) + Vector3.up * (s.CardHeight + 0.05f));
            holder.gameObject.AddComponent<Billboard>();
            var canvas = Ui.WorldCanvas("Canvas", holder, new Vector2(320, 76), 0.3f);
            canvas.sortingOrder = 5;
            var bg = Ui.Panel(canvas.transform, new Color(0.13f, 0.12f, 0.2f, 0.85f), "Background");
            bg.raycastTarget = false;
            Ui.Stretch(bg.rectTransform);
            var label = Ui.Label(bg.transform, s.Name, 44, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Name");
            Ui.Stretch(label.rectTransform, 8, 8, 4, 4);
        }

        // ---- Automated play-through ----

        public override void DebugComplete()
        {
            if (m_solved)
            {
                ShowResultNow();
                return;
            }
            StartClock();
            foreach (var c in m_clues)
                if (!c.Found) Inspect(c);
            WorldCard.CloseOpen();
            Accuse(m_culprit);
            ShowResultNow();
        }

        public override IEnumerator SelfTest()
        {
            ResetCase();
            yield return new WaitForSeconds(0.4f);
            Begin();
            if (!m_started) Debug.LogError("[SELFTEST] Start button did not start the clock");

            // A non-clue prop gives feedback but no clue.
            m_props[0].OnTap(default);
            if (NothingTaps != 1) Debug.LogError("[SELFTEST] non-clue tap gave no 'Nothing here' feedback");
            if (FoundCount != 0) Debug.LogError("[SELFTEST] non-clue tap counted as a clue");
            m_props[1 % m_props.Length].OnTap(default);
            if (NothingTaps != 1) Debug.LogError("[SELFTEST] 'Nothing here' feedback was not throttled");
            yield return new WaitForSeconds(0.4f);

            // Accusing before all clues are found does nothing.
            Accuse(m_culprit);
            if (Solved) Debug.LogError("[SELFTEST] case solved before the clues were found");

            for (int i = 0; i < m_clues.Length; i++)
            {
                m_clues[i].OnTap(default);
                yield return new WaitForSeconds(0.7f);
                if (!m_clues[i].Found) Debug.LogError("[SELFTEST] clue " + (i + 1) + " not found after tap");
                if (NotebookCount != i + 1) Debug.LogError("[SELFTEST] notebook shows " + NotebookCount + " clues, expected " + (i + 1));
                if (m_markers.Count != i + 1) Debug.LogError("[SELFTEST] magnifier marker missing for clue " + (i + 1));
            }
            var door = m_clues.FirstOrDefault(c => c.Animated != null);
            if (door != null && Quaternion.Angle(door.Animated.localRotation, door.OpenRotation) > 1f)
                Debug.LogError("[SELFTEST] freezer door did not open");
            yield return new WaitForSeconds(1.0f);
            if (!Unlocked) Debug.LogError("[SELFTEST] suspects not unlocked after 4 clues");
            if (m_suspects.Any(s => !s.Lit)) Debug.LogError("[SELFTEST] not every suspect lit up");
            Debug.Log("[SELFTEST] detective: notebook " + NotebookCount + "/4");

            var wrong = m_suspects.First(s => !s.IsCulprit);
            wrong.OnTap(default);
            yield return new WaitForSeconds(0.5f);
            Accuse(wrong);
            yield return new WaitForSeconds(0.8f);
            if (Solved) Debug.LogError("[SELFTEST] wrong accusation solved the case");
            if (WrongGuesses != 1) Debug.LogError("[SELFTEST] wrong guess not counted");

            m_culprit.OnTap(default);
            yield return new WaitForSeconds(0.5f);
            Accuse(m_culprit);
            if (!Solved) Debug.LogError("[SELFTEST] accusing the culprit did not solve the case");
            yield return new WaitForSeconds(4.6f);
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result screen not shown");
            Debug.Log("[SELFTEST] detective solved in " + Clock(m_elapsed) + " with " + WrongGuesses + " wrong guess(es)");
        }
    }
}
