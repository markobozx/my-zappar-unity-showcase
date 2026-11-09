using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Detective
{
    // Kitchen Detective: the last Nova ice cream is gone. The tray opens on the case and its three
    // suspects; then the player taps around the kitchen to find 4 clues. Each find opens the tray's
    // case file (3D thumbnail + live preview of the clue) and is written into the notebook on the wall;
    // a "Hint" makes a tag pulse over a clue still hidden. With all 4, the tray turns into the line-up:
    // pick a suspect (or tap them in the kitchen) to question them, then "Accuse". Wrong guesses clear
    // that suspect and give a hint.
    public class DetectiveExperience : ShowcaseExperience
    {
        public const string Brand = "Nova";
        const float NothingToastGap = 1.2f;
        const float MaxCardTop = 1.25f;
        const float MaxCardX = 0.5f;

        enum TrayMode { None, Intro, CaseFile, LineUp }

        [SerializeField] private DetectiveClue[] m_clues;
        [SerializeField] private DetectiveSuspect[] m_suspects;
        [SerializeField] private DetectiveProp[] m_props;
        [SerializeField] private Transform m_board;            // notebook backing on the back wall
        [SerializeField] private Vector2 m_boardSize = new Vector2(0.92f, 0.5f);
        [SerializeField] private GameObject m_markerTemplate;  // inactive magnifier shown over found clues
        [SerializeField] private Vector3 m_storyPoint = new Vector3(0f, 0.62f, 0.1f);
        [SerializeField] private GameObject m_caseModel;       // the empty tub, shown on the intro tile

        private Transform m_boardCanvas;
        private Text[] m_noteRows;
        private Text m_noteCount, m_noteFooter;
        private readonly List<Transform> m_markers = new List<Transform>();
        private readonly List<Vector3> m_markerBase = new List<Vector3>();

        private bool m_started, m_solved, m_resultShown;
        private float m_startTime, m_elapsed;
        private int m_shownSeconds = -1;
        private int m_wrong, m_hints;
        private float m_lastNothing = -10f;
        private DetectiveSuspect m_culprit;
        private readonly List<Coroutine> m_routines = new List<Coroutine>();

        // Screen: tray modes + one pill button. World: tags over suspects, found clues and the hint spot.
        private TrayMode m_mode;
        private List<TrayOption> m_options;
        private bool[] m_cleared;
        private WorldTag[] m_suspectTags;
        private WorldTag[] m_clueTags;
        private WorldTag m_hintTag;
        private DetectiveClue m_hintClue;
        private RectTransform m_caseButtonCanvas;
        private Button m_caseButton;
        private static ShowcaseTray Tray => ShowcaseTray.Instance;

        public int FoundCount => m_clues.Count(c => c.Found);
        public bool Unlocked => FoundCount >= m_clues.Length;
        public bool Solved => m_solved;
        public int WrongGuesses => m_wrong;
        public int HintsUsed => m_hints;
        public int NothingTaps { get; private set; }
        public int NotebookCount => m_noteRows == null ? 0 : m_noteRows.Where((r, i) => r.text.Contains(m_clues[i].NoteLine)).Count();
        public bool CaseButtonVisible => m_caseButton != null && m_caseButton.gameObject.activeSelf;
        public bool HintVisible => m_hintTag != null && m_hintTag.gameObject.activeSelf;

        protected override void OnEnter()
        {
            EnsureUi();
            if (Tray != null)
            {
                Tray.Closed -= OnTrayClosed;
                Tray.Closed += OnTrayClosed;
            }
            ResetCase();
        }

        protected override void OnExit()
        {
            if (Tray != null)
            {
                Tray.Closed -= OnTrayClosed;
                if (m_mode != TrayMode.None && Tray.IsOpen) Tray.Close();
            }
            m_mode = TrayMode.None;
            ClearMarkers();
            ClearHint();
            RestoreScales();
            UpdateCaseButton();
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
            ClearHint();
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
            m_cleared = new bool[m_suspects.Length];
            m_started = m_solved = m_resultShown = false;
            m_elapsed = 0f;
            m_shownSeconds = -1;
            m_wrong = 0;
            m_hints = 0;
            NothingTaps = 0;
            m_lastNothing = -10f;
            if (Hud != null)
            {
                Hud.HideResult();
                Hud.SetTimer("0:00");
                Hud.SetHint(Hint);
            }
            RefreshBoard();
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            OpenIntro();
            RefreshTags();
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

        private static string Flat(string text) => text.Replace("\n", " ").Replace("  ", " ");

        // ---- Tray: intro, case file, line-up ----

        private void OpenIntro()
        {
            if (Tray == null) return;
            m_options = new List<TrayOption>
            {
                new TrayOption("The case", "The Missing " + Brand,
                    "The last " + Brand + " ice cream is gone! Find 4 clues in the kitchen, then accuse one of the 3 suspects.", m_caseModel),
            };
            foreach (var s in m_suspects)
                m_options.Add(new TrayOption(s.Name, "Suspect", Flat(s.Alibi), s.Body != null ? s.Body.gameObject : s.gameObject));
            m_mode = TrayMode.Intro;
            Tray.Open("Who ate the last " + Brand + "?", m_options, 0, i => { if (i > 0) PointAt(m_suspects[i - 1]); },
                ("Start investigating  ►", Begin));
            UpdateCaseButton();
        }

        public void Begin()
        {
            StartClock();
            WorldCard.CloseOpen();
            CloseTray();
            Hud?.SetHint("Tap anything suspicious in the kitchen");
        }

        private void StartClock()
        {
            if (m_started) return;
            m_started = true;
            m_startTime = Time.time;
        }

        // Case file: the 4 clue slots; found ones show the clue object as thumbnail and live preview.
        public void OpenCaseFile(int select)
        {
            if (m_solved || Tray == null)
                return;
            m_options = new List<TrayOption>();
            for (int i = 0; i < m_clues.Length; i++)
            {
                var c = m_clues[i];
                m_options.Add(c.Found
                    ? new TrayOption(c.Title, "Clue " + (i + 1) + "  ·  found", Flat(c.Body), c.gameObject) { Badge = "√" }
                    : new TrayOption("? ? ?", "Clue " + (i + 1) + "  ·  hidden", "Still hidden somewhere in the kitchen. Tap things to search, or ask for a hint."));
            }
            if (select < 0)
            {
                select = System.Array.FindIndex(m_clues, c => !c.Found);
                if (select < 0) select = 0;
            }
            m_mode = TrayMode.CaseFile;
            if (Unlocked)
                Tray.Open("Case file  ·  all 4 clues!", m_options, select, PickClue,
                    ("To the line-up  ►", () => OpenLineUp(-1)));
            else
                Tray.Open("Case file  ·  " + FoundCount + "/" + m_clues.Length + " clues", m_options, select, PickClue,
                    ("Keep looking", CloseTray),
                    ("Hint", GiveHint));
            PickClue(select);
            UpdateCaseButton();
        }

        private void PickClue(int i)
        {
            if (i < 0 || i >= m_clues.Length || !m_clues[i].Found)
                return;
            var c = m_clues[i];
            Bump(c.transform, 0.12f);
            // Pull toward the clue but keep enough room around it that tall ones (the freezer) stay in view.
            Vector3 p = StagePoint(c.transform);
            FreeCamera.Instance?.Frame(new Vector3(p.x * 0.6f, 0.2f, p.z * 0.6f), 3.3f, 46f);
        }

        // Line-up: the 3 suspects as 3D thumbnails; pick one to question, "Accuse" to name them.
        public void OpenLineUp(int select)
        {
            if (m_solved || Tray == null)
                return;
            m_options = new List<TrayOption>();
            for (int i = 0; i < m_suspects.Length; i++)
                m_options.Add(new TrayOption(m_suspects[i].Name, "", "", m_suspects[i].Body != null ? m_suspects[i].Body.gameObject : m_suspects[i].gameObject));
            if (select < 0)
            {
                select = System.Array.FindIndex(m_cleared, c => !c);
                if (select < 0) select = 0;
            }
            m_mode = TrayMode.LineUp;
            Tray.Open(Unlocked ? "Line-up  ·  who did it?" : "Line-up  ·  find 4 clues to accuse", m_options, select, QuestionAt,
                ("Accuse", AccuseSelected),
                ("Case file", () => OpenCaseFile(-1)));
            QuestionAt(select);
            UpdateCaseButton();
        }

        // Questioning a suspect: their alibi in the tray, the figure hops and is framed above the tray.
        private void QuestionAt(int i)
        {
            if (i < 0 || i >= m_suspects.Length || m_mode != TrayMode.LineUp)
                return;
            var s = m_suspects[i];
            Bump(s.Body != null ? s.Body : s.transform, 0.14f);
            Fx.Play(FxKind.Sparkle, s.transform.position + transform.up * (s.CardHeight + 0.05f) * transform.lossyScale.y, 0.5f);
            Vector3 p = StagePoint(s.transform);
            // Lean toward the suspect but keep the whole line-up in view.
            FreeCamera.Instance?.Frame(new Vector3(p.x * 0.3f, 0.28f, p.z * 0.6f), 3.3f, 42f);
            RefreshLineUp();
        }

        private void RefreshLineUp()
        {
            if (m_mode != TrayMode.LineUp || m_options == null || Tray == null || !Tray.IsOpen)
                return;
            for (int i = 0; i < m_suspects.Length; i++)
            {
                var s = m_suspects[i];
                var o = m_options[i];
                o.Badge = m_cleared[i] ? "×" : null;
                o.Price = m_cleared[i] ? "Cleared" : Unlocked ? "Suspect" : "Suspect  ·  clues " + FoundCount + "/" + m_clues.Length;
                o.Detail = m_cleared[i] ? Flat(s.Denial) + "  Hint: " + s.Hint : Flat(s.Alibi);
            }
            Tray.Refresh();
            int sel = Tray.Selected;
            bool can = Unlocked && sel >= 0 && !m_cleared[sel];
            string first = sel >= 0 ? m_suspects[sel].Name.Split(' ').Last() : "";
            Tray.SetAction(0, !Unlocked ? "Find 4 clues first" : m_cleared[sel] ? "Cleared" : "Accuse " + first + "!", can ? Ui.Accent : Ui.Muted, can);
            RefreshTags();
        }

        public void AccuseSelected()
        {
            if (Tray == null || m_mode != TrayMode.LineUp) return;
            int i = Tray.Selected;
            if (i >= 0 && i < m_suspects.Length) Accuse(m_suspects[i]);
        }

        private void CloseTray()
        {
            if (m_mode != TrayMode.None && Tray != null && Tray.IsOpen)
                Tray.Close();
            else
                OnTrayClosed();
        }

        private void OnTrayClosed()
        {
            if (m_mode == TrayMode.None)
                return;
            m_mode = TrayMode.None;
            m_options = null;
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            UpdateCaseButton();
        }

        // ---- Hints ----

        public void GiveHint()
        {
            var clue = m_clues.FirstOrDefault(c => !c.Found);
            if (clue == null) return;
            m_hints++;
            CloseTray();
            ClearHint();
            m_hintClue = clue;
            m_hintTag = WorldTag.Create(clue.transform, Vector3.up * (clue.CardHeight + 0.14f), "? Look here", 0.13f, Ui.Accent);
            m_hintTag.Pulse(true);
            Fx.Play(FxKind.Sparkle, clue.transform.position + transform.up * (clue.CardHeight + 0.1f) * transform.lossyScale.y, 0.8f);
            Hud?.Toast("Psst... look under the pulsing tag!");
        }

        private void ClearHint()
        {
            if (m_hintTag != null) Destroy(m_hintTag.gameObject);
            m_hintTag = null;
            m_hintClue = null;
        }

        // ---- Interaction (world taps and tray buttons all come through here) ----

        public void Inspect(DetectiveClue clue)
        {
            if (m_solved || clue == null)
                return;
            StartClock();
            Vector3 top = StagePoint(clue.transform) + Vector3.up * clue.CardHeight;
            Bump(clue.transform, 0.15f);
            Fx.Play(FxKind.Sparkle, transform.TransformPoint(top), 0.8f);

            int index = System.Array.IndexOf(m_clues, clue);
            if (!clue.Found)
            {
                clue.Found = true;
                if (m_hintClue == clue) ClearHint();
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
                    Hud?.Toast("All 4 clues found!");
                    Hud?.SetHint("Question the suspects · then accuse one");
                    Run(LightUpSuspects());
                }
                else
                {
                    Hud?.Toast("Clue found! " + FoundCount + "/" + m_clues.Length);
                }
                RefreshTags();
            }
            OpenCaseFile(index);
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

        // Tapping a suspect in the kitchen opens the line-up with them selected.
        public void Question(DetectiveSuspect s)
        {
            if (m_solved || s == null)
                return;
            StartClock();
            OpenLineUp(System.Array.IndexOf(m_suspects, s));
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
            int i = System.Array.IndexOf(m_suspects, s);
            if (i >= 0 && m_cleared[i])
                return;
            m_wrong++;
            if (i >= 0) m_cleared[i] = true;
            Fx.Play(FxKind.Poof, s.transform.position + transform.up * 0.2f, 0.7f);
            if (s.Body != null)
                Run(ShakeHead(s.Body));
            s.SetLit(false);
            if (s.Body != null) s.Body.localPosition = Vector3.zero;
            Hud?.Toast(s.Denial + "\n<size=30>Hint: " + s.Hint + "</size>", 3.2f);
            if (m_noteFooter != null)
            {
                m_noteFooter.text = "Hint: " + s.Hint;
                m_noteFooter.color = Ui.Accent;
            }
            RefreshLineUp();
            RefreshTags();
        }

        private void Solve(DetectiveSuspect s)
        {
            m_solved = true;
            if (m_started)
                m_elapsed = Time.time - m_startTime;
            Hud?.SetTimer(Clock(m_elapsed));
            CloseTray();
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
            Hud?.SetHint("");
            Hud?.Toast("Case closed!", 2.4f);
            RefreshTags();
            UpdateCaseButton();
            Card(new Vector3(0f, s.CardHeight + 0.2f, StagePoint(s.transform).z), "Case closed!",
                "Bolt's fan overheated at\nmidnight, so he \"borrowed\"\nthe last " + Brand + " cone to cool\nhis circuits. Brain freeze!",
                1.3f);
            Run(ResultSoon());
        }

        private IEnumerator ResultSoon()
        {
            yield return new WaitForSeconds(3.5f);
            ShowResultNow();
        }

        private void ShowResultNow()
        {
            if (m_resultShown || Hud == null)
                return;
            m_resultShown = true;
            WorldCard.CloseOpen();
            string guesses = m_wrong == 0 ? "first guess, Detective!" : m_wrong + (m_wrong == 1 ? " wrong guess" : " wrong guesses");
            string hints = m_hints == 0 ? "no hints" : m_hints + (m_hints == 1 ? " hint" : " hints");
            string who = m_culprit != null ? m_culprit.Name : "The culprit";
            Hud.ShowResult("Case closed in " + Clock(m_elapsed),
                who + " ate the last " + Brand + " cone!\n<size=32>4/4 clues · " + guesses + " · " + hints + "</size>",
                ("Play again", ResetCase),
                ("Hub", () => ShowcaseHub.Instance?.ReturnToHub()));
        }

        // ---- World tags and the screen button ----

        private void RefreshTags()
        {
            if (m_suspectTags != null)
                for (int i = 0; i < m_suspects.Length; i++)
                {
                    var s = m_suspects[i];
                    var tag = m_suspectTags[i];
                    if (tag == null) continue;
                    bool selected = m_mode == TrayMode.LineUp && Tray != null && Tray.Selected == i;
                    if (m_solved)
                        tag.Set(s.IsCulprit ? "Guilty! " + s.Name : s.Name, s.IsCulprit ? Ui.Accent : new Color(0.45f, 0.44f, 0.52f, 0.85f));
                    else if (m_cleared != null && m_cleared[i])
                        tag.Set("× " + s.Name, new Color(0.45f, 0.44f, 0.52f, 0.85f));
                    else
                        tag.Set(s.Name, selected ? Ui.Accent2 : new Color(0.13f, 0.12f, 0.2f, 0.88f));
                    tag.Pulse(Unlocked && !m_solved && !(m_cleared != null && m_cleared[i]) && !selected);
                }
            if (m_clueTags != null)
                for (int i = 0; i < m_clues.Length; i++)
                {
                    var c = m_clues[i];
                    if (c.Found && m_clueTags[i] == null)
                    {
                        // Pull tags of clues near the side walls inward so they don't run off a phone screen.
                        float x = StagePoint(c.transform).x;
                        float inward = -Mathf.Sign(x) * Mathf.Max(0f, Mathf.Abs(x) - 0.85f);
                        m_clueTags[i] = WorldTag.Create(c.transform, new Vector3(inward, c.CardHeight + 0.22f, 0f), "√ " + c.Title, 0.11f, Ui.Good);
                    }
                    if (m_clueTags[i] != null) m_clueTags[i].Show(c.Found);
                }
        }

        // One pill button above the hint: "Case file 2/4" while searching, "Line-up ►" once all are found.
        private void UpdateCaseButton()
        {
            if (m_caseButton == null)
                return;
            bool show = isActiveAndEnabled && m_started && !m_solved && m_mode == TrayMode.None;
            m_caseButton.gameObject.SetActive(show);
            if (!show) return;
            Ui.SetButtonLabel(m_caseButton, Unlocked ? "Line-up  ►" : "Case file  " + FoundCount + "/" + m_clues.Length);
            m_caseButton.GetComponent<Image>().color = Unlocked ? Ui.Accent : new Color(0.13f, 0.12f, 0.2f, 0.85f);
        }

        public void PressCaseButton()
        {
            if (Unlocked) OpenLineUp(-1);
            else OpenCaseFile(-1);
        }

        // ---- Effects ----

        private IEnumerator LightUpSuspects()
        {
            yield return new WaitForSeconds(0.5f);
            if (m_noteFooter != null)
            {
                m_noteFooter.text = "Line-up ready: who did it?";
                m_noteFooter.color = Ui.Accent;
            }
            for (int i = 0; i < m_suspects.Length; i++)
            {
                var s = m_suspects[i];
                if (m_solved) yield break;
                if (m_cleared[i]) continue;
                s.SetLit(true);
                Fx.Play(FxKind.Stars, s.transform.position + transform.up * 0.3f, 0.6f);
                Bump(s.Body != null ? s.Body : s.transform, 0.2f);
                yield return new WaitForSeconds(0.25f);
            }
        }

        private void PointAt(DetectiveSuspect s)
        {
            Bump(s.Body != null ? s.Body : s.transform, 0.14f);
            Fx.Play(FxKind.Sparkle, s.transform.position + transform.up * (s.CardHeight + 0.05f) * transform.lossyScale.y, 0.5f);
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
            if (t == null || !isActiveAndEnabled || !m_bumping.Add(t))
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
                    m_noteFooter.text = "Tap this notebook for your case file";
                    m_noteFooter.color = Ui.Muted;
                }
            }
            Hud?.SetScore("Clues " + FoundCount + "/" + m_clues.Length);
            UpdateCaseButton();
        }

        // World UI is built once on first entry and kept; ResetCase clears its contents.
        private void EnsureUi()
        {
            if (m_boardCanvas == null && m_board != null)
                BuildBoard();
            if (m_suspectTags == null)
            {
                m_suspectTags = new WorldTag[m_suspects.Length];
                for (int i = 0; i < m_suspects.Length; i++)
                    m_suspectTags[i] = WorldTag.Create(m_suspects[i].transform, Vector3.up * (m_suspects[i].CardHeight + 0.09f), m_suspects[i].Name, 0.12f);
            }
            if (m_clueTags == null)
                m_clueTags = new WorldTag[m_clues.Length];
            if (m_caseButton == null)
                BuildCaseButton();
        }

        private void BuildCaseButton()
        {
            var go = new GameObject("Case Button Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;   // under the HUD (50), so toasts and the result draw on top
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            m_caseButtonCanvas = (RectTransform)go.transform;
            m_caseButton = Ui.Button(go.transform, "Case file", new Color(0.13f, 0.12f, 0.2f, 0.85f), PressCaseButton, 42, "Case Button");
            Ui.Round(m_caseButton.GetComponent<Image>(), 50f);
            // Bottom right, above the HUD hint box (which starts 200 px up and grows with its text).
            Ui.Place((RectTransform)m_caseButton.transform, new Vector2(1f, 0f), new Vector2(360, 110), new Vector2(-30, 400));
            m_caseButton.gameObject.SetActive(false);
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
            Accuse(m_culprit);
            ShowResultNow();
        }

        public override IEnumerator SelfTest()
        {
            if (Tray == null) { Debug.LogError("[SELFTEST] no tray"); yield break; }
            ResetCase();
            yield return new WaitForSeconds(1.8f);   // let toasts from the harness taps fade
            if (!Tray.IsOpen || m_mode != TrayMode.Intro) Debug.LogError("[SELFTEST] intro tray not open on entry");
            if (Tray.Options.Count != m_suspects.Length + 1) Debug.LogError("[SELFTEST] intro tray shows " + Tray.Options.Count + " tiles");
            Snapshot("intro");
            Tray.Press(0);   // "Start investigating"
            yield return null;
            if (!m_started) Debug.LogError("[SELFTEST] Start did not start the clock");
            if (Tray.IsOpen) Debug.LogError("[SELFTEST] tray still open after Start");
            if (!CaseButtonVisible) Debug.LogError("[SELFTEST] case-file button not shown while searching");

            // A non-clue prop gives feedback but no clue.
            m_props[0].OnTap(default);
            if (NothingTaps != 1) Debug.LogError("[SELFTEST] non-clue tap gave no 'Nothing here' feedback");
            if (FoundCount != 0) Debug.LogError("[SELFTEST] non-clue tap counted as a clue");
            m_props[1 % m_props.Length].OnTap(default);
            if (NothingTaps != 1) Debug.LogError("[SELFTEST] 'Nothing here' feedback was not throttled");
            yield return new WaitForSeconds(0.4f);

            // Accusing before all clues are found does nothing; the line-up can't accuse yet.
            Accuse(m_culprit);
            if (Solved) Debug.LogError("[SELFTEST] case solved before the clues were found");
            m_suspects[0].OnTap(default);
            if (!Tray.IsOpen || m_mode != TrayMode.LineUp) Debug.LogError("[SELFTEST] tapping a suspect did not open the line-up");
            if (Tray.Action(0) != null && Tray.Action(0).interactable) Debug.LogError("[SELFTEST] Accuse enabled before the clues were found");

            // Case file -> Hint: a tag pulses over a hidden clue.
            Tray.Press(1);   // "Case file"
            yield return null;
            if (m_mode != TrayMode.CaseFile) Debug.LogError("[SELFTEST] Case file action did not open the case file");
            Tray.Press(1);   // "Hint"
            yield return null;
            if (!HintVisible || HintsUsed != 1) Debug.LogError("[SELFTEST] hint tag not shown");
            if (Tray.IsOpen) Debug.LogError("[SELFTEST] tray still open after Hint");
            var hinted = m_hintClue;
            yield return new WaitForSeconds(0.5f);
            Snapshot("hint");

            for (int i = 0; i < m_clues.Length; i++)
            {
                m_clues[i].OnTap(default);
                yield return new WaitForSeconds(0.7f);
                if (!m_clues[i].Found) Debug.LogError("[SELFTEST] clue " + (i + 1) + " not found after tap");
                if (NotebookCount != i + 1) Debug.LogError("[SELFTEST] notebook shows " + NotebookCount + " clues, expected " + (i + 1));
                if (m_markers.Count != i + 1) Debug.LogError("[SELFTEST] magnifier marker missing for clue " + (i + 1));
                if (!Tray.IsOpen || m_mode != TrayMode.CaseFile || Tray.Selected != i) Debug.LogError("[SELFTEST] case file not open on clue " + (i + 1));
                if (m_clueTags[i] == null || !m_clueTags[i].gameObject.activeSelf) Debug.LogError("[SELFTEST] no world tag over found clue " + (i + 1));
                if (hinted == m_clues[i] && HintVisible) Debug.LogError("[SELFTEST] hint tag still up after its clue was found");
                if (i == 1) Snapshot("casefile");
                if (i < m_clues.Length - 1)
                {
                    Tray.Press(0);   // "Keep looking"
                    yield return null;
                    if (Tray.IsOpen) Debug.LogError("[SELFTEST] Keep looking did not close the tray");
                }
            }
            var door = m_clues.FirstOrDefault(c => c.Animated != null);
            if (door != null && Quaternion.Angle(door.Animated.localRotation, door.OpenRotation) > 1f)
                Debug.LogError("[SELFTEST] freezer door did not open");
            yield return new WaitForSeconds(1.0f);
            if (!Unlocked) Debug.LogError("[SELFTEST] suspects not unlocked after 4 clues");
            if (m_suspects.Any(s => !s.Lit)) Debug.LogError("[SELFTEST] not every suspect lit up");
            Debug.Log("[SELFTEST] detective: notebook " + NotebookCount + "/4");

            Tray.Press(0);   // "To the line-up"
            yield return null;
            if (m_mode != TrayMode.LineUp) Debug.LogError("[SELFTEST] line-up not open");
            int wrong = System.Array.FindIndex(m_suspects, s => !s.IsCulprit);
            int culprit = System.Array.IndexOf(m_suspects, m_culprit);
            Tray.Select(wrong);   // like tapping their tile: question them
            yield return new WaitForSeconds(0.6f);
            Snapshot("lineup");
            Tray.Press(0);   // "Accuse ...!"
            yield return new WaitForSeconds(0.8f);
            if (Solved) Debug.LogError("[SELFTEST] wrong accusation solved the case");
            if (WrongGuesses != 1) Debug.LogError("[SELFTEST] wrong guess not counted");
            if (!m_cleared[wrong] || Tray.Action(0).interactable) Debug.LogError("[SELFTEST] wrongly accused suspect not cleared");

            Tray.Select(culprit);
            yield return new WaitForSeconds(0.4f);
            if (!Tray.Action(0).interactable) Debug.LogError("[SELFTEST] Accuse disabled for the culprit");
            Tray.Press(0);
            if (!Solved) Debug.LogError("[SELFTEST] accusing the culprit did not solve the case");
            yield return new WaitForSeconds(1.0f);
            Snapshot("solved");
            yield return new WaitForSeconds(3.0f);
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result screen not shown");
            Debug.Log("[SELFTEST] detective solved in " + Clock(m_elapsed) + " with " + WrongGuesses + " wrong guess(es), " + HintsUsed + " hint(s)");
        }
    }
}
