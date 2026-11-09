using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Showcase.Experiences.Station
{
    // Space Station Escape: four linked puzzles in a cut-away station room.
    //  1. Radio -> 3-tone colour signal.   2. Signal on the red locker's keypad -> spanner falls out.
    //  3. Pick the right tool and fix the smoking air unit. 4. Power switches matched to the monitor -> airlock opens.
    // UI split: the bottom tray is the hand-held console for the current puzzle (signal tiles, big colour
    // keys, tool tiles with thumbnails, switch tiles); the world shows what's happening (keypad display,
    // monitor pattern, a pulsing guide tag on the next thing to look at). Tapping props still works and
    // opens the matching console page. Scene references are filled by StationBuilder; the world UI is
    // built in code on first Enter. All positions are local to this experience root.
    public class StationExperience : ShowcaseExperience
    {
        [Header("Station props")]
        public StationProp Radio;
        public StationProp Locker;
        public Transform LockerHinge;
        public StationProp Spanner;
        public StationProp AirConditioner;
        public Transform SmokePoint;
        public StationProp Monitor;
        public StationProp Airlock;
        public Transform AirlockLeft, AirlockRight;
        public Vector3 AirlockLeftOpen, AirlockRightOpen;   // local positions of the open leaves
        public StationProp[] Switches;
        public Transform[] Levers;
        public Renderer[] Lamps;
        public StationProp[] DecoyTools;                    // wrench, screwdriver: offered next to the spanner

        [Header("World UI anchors")]
        public Transform KeypadAnchor;
        public float KeypadWidth = 0.42f;
        public Transform MonitorAnchor;
        public float MonitorWidth = 0.3f;
        public Transform TraySign;
        public Transform TraySlot;

        public enum Page { None, Radio, Keypad, Tools, Power }

        private const string Product = "Nova X1";
        public static readonly string[] KeyNames = { "Red", "Blue", "Green", "Yellow" };
        public static readonly Color[] KeyColors =
        {
            new Color(0.95f, 0.33f, 0.3f), new Color(0.3f, 0.55f, 1f), new Color(0.3f, 0.8f, 0.4f), new Color(1f, 0.8f, 0.2f)
        };
        private static readonly string[] KeyHex = { "#F2544D", "#4D8CFF", "#4DCC66", "#FFCC33" };
        private static readonly Color LampOn = new Color(0.35f, 1f, 0.45f);
        private static readonly Color LampOff = new Color(0.55f, 0.12f, 0.12f);
        private static readonly Color ScreenBg = new Color(0.04f, 0.1f, 0.12f, 0.97f);
        private static readonly Color ScreenGreen = new Color(0.45f, 1f, 0.6f);
        private static readonly Color ScreenRed = new Color(1f, 0.38f, 0.32f);
        private static readonly Color TagDark = new Color(0.13f, 0.12f, 0.2f, 0.88f);
        private static readonly Color SwitchOff = new Color(0.32f, 0.33f, 0.4f);

        // state
        private readonly int[] m_code = new int[3];
        private readonly List<int> m_entry = new List<int>();
        private bool m_radioHeard, m_lockerOpen, m_spannerReady, m_hasSpanner, m_acFixed, m_fixing, m_escaped;
        private readonly bool[] m_switchOn = new bool[3];
        private readonly bool[] m_pattern = new bool[3];
        private int m_wrongCodes, m_wrongTools;
        private float m_startTime, m_endTime;
        private int m_lastTimerSecond = -1;
        private readonly List<Coroutine> m_running = new List<Coroutine>();
        private readonly Dictionary<Transform, Vector3> m_baseScale = new Dictionary<Transform, Vector3>();

        // cached closed pose
        private bool m_cached;
        private Quaternion m_hingeClosed;
        private Vector3 m_leftClosed, m_rightClosed;
        private Vector3 m_spannerHome;
        private Quaternion m_spannerRot;
        private Quaternion[] m_leverOff;
        private MaterialPropertyBlock m_mpb;

        // runtime world UI
        private bool m_uiBuilt;
        private Text m_keypadDisplay;
        private Image m_keypadScreen;
        private Text m_monitorHeader, m_monitorStatus;
        private Image[] m_monitorCells;
        private Text[] m_monitorCellText;
        private Text m_trayLabel;
        private WorldTag m_guide, m_signalTag, m_patternTag;

        // console (screen tray)
        private Page m_page;
        private List<TrayOption> m_options;
        private int m_toolPick;

        public bool RadioHeard => m_radioHeard;
        public bool LockerOpen => m_lockerOpen;
        public bool HasSpanner => m_hasSpanner;
        public bool AcFixed => m_acFixed;
        public bool Escaped => m_escaped;
        public Page CurrentPage => TrayOpen ? m_page : Page.None;
        public string Code => string.Join(",", m_code);
        private static ShowcaseTray Tray => ShowcaseTray.Instance;
        private bool TrayOpen => m_page != Page.None && Tray != null && Tray.IsOpen;

        protected override void OnEnter()
        {
            EnsureEventSystem();
            Cache();
            BuildUi();
            if (Tray != null)
            {
                Tray.Closed -= OnTrayClosed;
                Tray.Closed += OnTrayClosed;
            }
            ResetState();
        }

        protected override void OnExit()
        {
            StopRunning();
            CloseConsole();
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
        }

        void OnDestroy()
        {
            if (Tray != null) Tray.Closed -= OnTrayClosed;
        }

        void Update()
        {
            if (Hud == null || m_escaped)
                return;
            // Only rebuild the string when the second changes: no per-frame garbage on WebGL.
            int second = Mathf.FloorToInt(Time.time - m_startTime);
            if (second == m_lastTimerSecond)
                return;
            m_lastTimerSecond = second;
            Hud.SetTimer(FormatTime(second));
        }

        // ---------- reset ----------

        public void ResetState()
        {
            StopRunning();
            WorldCard.CloseOpen();
            CloseConsole();
            if (Hud != null) Hud.HideResult();

            for (int i = 0; i < 3; i++)
                m_code[i] = UnityEngine.Random.Range(0, KeyNames.Length);
            m_entry.Clear();
            m_radioHeard = m_lockerOpen = m_spannerReady = m_hasSpanner = m_acFixed = m_fixing = m_escaped = false;
            m_wrongCodes = m_wrongTools = 0;
            m_toolPick = 0;
            int mask = UnityEngine.Random.Range(1, 8);   // never all-off, which is how the switches start
            for (int i = 0; i < 3; i++)
            {
                m_switchOn[i] = false;
                m_pattern[i] = (mask & (1 << i)) != 0;
            }

            foreach (var kv in m_baseScale)
                if (kv.Key != null) kv.Key.localScale = kv.Value;
            if (LockerHinge != null) LockerHinge.localRotation = m_hingeClosed;
            if (AirlockLeft != null) AirlockLeft.localPosition = m_leftClosed;
            if (AirlockRight != null) AirlockRight.localPosition = m_rightClosed;
            if (Spanner != null)
            {
                Spanner.transform.localPosition = m_spannerHome;
                Spanner.transform.localRotation = m_spannerRot;
                Spanner.gameObject.SetActive(false);
            }
            for (int i = 0; i < Switches.Length; i++)
                ApplySwitchVisual(i, false);

            m_startTime = Time.time;
            m_lastTimerSecond = 0;
            UpdateScore();
            UpdateKeypad(null);
            UpdateMonitor();
            UpdateTray();
            if (Hud != null) Hud.SetTimer("00:00");
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            Guide();
            Run(SmokeLoop());
        }

        private void Cache()
        {
            if (m_cached)
                return;
            m_cached = true;
            m_mpb = new MaterialPropertyBlock();
            if (LockerHinge != null) m_hingeClosed = LockerHinge.localRotation;
            if (AirlockLeft != null) m_leftClosed = AirlockLeft.localPosition;
            if (AirlockRight != null) m_rightClosed = AirlockRight.localPosition;
            if (Spanner != null)
            {
                m_spannerHome = Spanner.transform.localPosition;
                m_spannerRot = Spanner.transform.localRotation;
            }
            m_leverOff = new Quaternion[Levers.Length];
            for (int i = 0; i < Levers.Length; i++)
                m_leverOff[i] = Levers[i] != null ? Levers[i].localRotation : Quaternion.identity;
            foreach (var p in GetComponentsInChildren<StationProp>(true))
                if (!m_baseScale.ContainsKey(p.transform)) m_baseScale[p.transform] = p.transform.localScale;
        }

        private static void EnsureEventSystem()
        {
            // Fallback only: the Showcase scene has a root EventSystem, and a second one would break UI input.
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
                return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ---------- taps on props (each opens its console page) ----------

        public void HandleTap(StationProp prop)
        {
            if (prop == null || m_escaped)
                return;
            switch (prop.Kind)
            {
                case StationPropKind.Radio: TapRadio(); break;
                case StationPropKind.Locker: TapLocker(); break;
                case StationPropKind.Keypad: TapKeypad(); break;
                case StationPropKind.Spanner: TapSpanner(); break;
                case StationPropKind.AirConditioner: TapAirConditioner(); break;
                case StationPropKind.Switch: TapSwitch(prop.Index); break;
                case StationPropKind.Airlock: TapAirlock(); break;
                case StationPropKind.Monitor: TapMonitor(); break;
                case StationPropKind.Decoy:
                    Punch(prop.transform);
                    Fx.Play(FxKind.Hit, Center(prop.transform), 0.5f);
                    if (m_lockerOpen && !m_acFixed)
                    {
                        int i = Array.IndexOf(DecoyTools, prop);
                        OpenPage(Page.Tools, i >= 0 ? i + 1 : 0);
                    }
                    else
                        Toast(string.IsNullOrEmpty(prop.Message) ? "Not useful right now." : prop.Message);
                    break;
            }
        }

        private void TapRadio()
        {
            Punch(Radio.transform);
            Fx.Play(FxKind.Stars, Top(Radio.transform), 0.6f);
            if (!m_radioHeard)
            {
                m_radioHeard = true;
                UpdateScore();
                Fx.Play(FxKind.Sparkle, Top(Radio.transform), 0.8f);
                Guide();
            }
            OpenPage(Page.Radio);
        }

        private void TapLocker()
        {
            Punch(Locker.transform);
            if (!m_lockerOpen)
            {
                Fx.Play(FxKind.Hit, Center(Locker.transform), 0.5f);
                OpenPage(Page.Keypad);
            }
            else if (!m_acFixed)
                OpenPage(Page.Tools);
            else
                Toast("Empty - you already used the spanner.");
        }

        private void TapKeypad()
        {
            if (KeypadAnchor != null) Fx.Play(FxKind.Hit, KeypadAnchor.position, 0.3f);
            if (m_lockerOpen) TapLocker();
            else OpenPage(Page.Keypad);
        }

        private void TapSpanner()
        {
            if (!m_spannerReady || m_fixing)
                return;
            if (!m_hasSpanner) GrabSpanner();
            if (!m_acFixed) OpenPage(Page.Tools, 0);
        }

        private void GrabSpanner()
        {
            if (m_hasSpanner || !m_spannerReady)
                return;
            m_hasSpanner = true;
            Fx.Play(FxKind.Pickup, Center(Spanner.transform), 0.7f);
            Run(Tween.MoveLocal(Spanner.transform, transform.InverseTransformPoint(TraySlot.position)));
            UpdateTray();
            Guide();
        }

        private void TapAirConditioner()
        {
            Punch(AirConditioner.transform);
            if (m_acFixed)
            {
                Fx.Play(FxKind.Sparkle, Top(AirConditioner.transform), 0.6f);
                Toast("Humming nicely now.");
                return;
            }
            if (m_fixing)
                return;
            if (m_hasSpanner)
            {
                Run(FixRoutine());
                return;
            }
            Fx.Play(FxKind.Hit, Top(AirConditioner.transform), 0.6f);
            if (m_lockerOpen) OpenPage(Page.Tools);
            else Toast("Its bolts are stuck. A tool would help - check the locker?");
        }

        private void TapSwitch(int i)
        {
            if (i < 0 || i >= Switches.Length)
                return;
            Punch(Switches[i].transform, 0.25f);
            if (!m_acFixed)
            {
                Fx.Play(FxKind.Hit, Center(Switches[i].transform), 0.4f);
                Toast("No power. The air unit is overheating!");
                return;
            }
            if (!TrayOpen || m_page != Page.Power) OpenPage(Page.Power, i);
            ToggleSwitch(i);
        }

        private void TapAirlock()
        {
            Fx.Play(FxKind.Hit, Center(Airlock.transform), 0.6f);
            if (m_acFixed) OpenPage(Page.Power);
            else Toast("Airlock sealed. No power to open it.");
        }

        private void TapMonitor()
        {
            Punch(Monitor.transform, 0.1f);
            if (m_acFixed) OpenPage(Page.Power);
            else Toast("Cooling failure! Power is locked until the air unit is fixed.");
        }

        // ---------- console pages (screen tray) ----------

        // The page that fits the current puzzle (also what the guide tag points at).
        public Page StepPage()
        {
            if (!m_radioHeard) return Page.Radio;
            if (!m_lockerOpen) return Page.Keypad;
            if (!m_acFixed) return Page.Tools;
            return Page.Power;
        }

        public void OpenPage(Page page, int select = -1)
        {
            if (Tray == null || m_escaped || page == Page.None)
                return;
            m_page = page;
            m_options = new List<TrayOption>();
            switch (page)
            {
                case Page.Radio:
                    for (int i = 0; i < 3; i++)
                        m_options.Add(new TrayOption(KeyNames[m_code[i]], "Tone " + (i + 1) + " of 3: " + KeyNames[m_code[i]],
                            Product + " feature 1/4 · deep-space radio, crystal clear") { Swatch = KeyColors[m_code[i]] });
                    Tray.Open(PageTitle(), m_options, Mathf.Max(0, select), _ => { },
                        ("Close", CloseConsole), ("To the keypad ›", () => OpenPage(Page.Keypad)));
                    break;
                case Page.Keypad:
                    for (int i = 0; i < KeyNames.Length; i++)
                        m_options.Add(new TrayOption(KeyNames[i]) { Swatch = KeyColors[i] });
                    FillKeypadOptions();
                    Tray.Open(PageTitle(), m_options, Mathf.Max(0, select), PressKey,
                        ("Clear", ClearEntry), ("Radio signal", () => { if (m_radioHeard) OpenPage(Page.Radio); else Toast("Nothing heard yet. Is anything crackling nearby?"); }));
                    break;
                case Page.Tools:
                    m_options.Add(new TrayOption("Spanner", "Fits the air unit's bolts", Product + " multi-spanner · feature 2/4: SmartLock kept it safe", Spanner != null ? Spanner.gameObject : null));
                    foreach (var d in DecoyTools)
                        if (d != null)
                            m_options.Add(new TrayOption(d.name.Contains("Wrench") ? "Wrench" : "Screwdriver", "Hmm, will it fit?", d.Message, d.gameObject));
                    if (select >= 0) m_toolPick = Mathf.Clamp(select, 0, m_options.Count - 1);
                    Tray.Open(PageTitle(), m_options, m_toolPick, PickTool,
                        ("Use on air unit ›", UseTool));
                    break;
                case Page.Power:
                    for (int i = 0; i < 3; i++)
                        m_options.Add(new TrayOption((i + 1) + " · " + (m_switchOn[i] ? "ON" : "OFF"),
                            "Switch " + (i + 1) + " is " + (m_switchOn[i] ? "ON" : "OFF"), "Match the monitor: " + PatternText() + " · feature 3/4: self-healing climate core")
                            { Swatch = m_switchOn[i] ? LampOn : SwitchOff });
                    Tray.Open(PageTitle(), m_options, Mathf.Max(0, select), ToggleSwitch);
                    break;
            }
            RefreshPage();
            FramePage(page);
            Guide();
        }

        private string PageTitle()
        {
            switch (m_page)
            {
                case Page.Radio: return "Puzzle 1/4 · Radio signal";
                case Page.Keypad: return "Locker code   " + EntryText(true);
                case Page.Tools: return m_hasSpanner ? "Puzzle 3/4 · Use a tool" : "Puzzle 3/4 · Pick the right tool";
                case Page.Power: return "Puzzle 4/4 · Power switches";
                default: return Product;
            }
        }

        private void FillKeypadOptions()
        {
            if (m_page != Page.Keypad || m_options == null) return;
            foreach (var o in m_options)
            {
                o.Price = "Code: " + EntryText(true);
                o.Detail = m_radioHeard ? "Type the radio's 3-tone signal" : "3 colours. Is anything broadcasting nearby?";
            }
        }

        private void RefreshPage()
        {
            if (!TrayOpen)
                return;
            Tray.SetTitle(PageTitle());
            if (m_page == Page.Keypad)
            {
                FillKeypadOptions();
                Tray.Refresh();
                Tray.SetAction(1, "Radio signal", m_radioHeard ? Ui.Accent2 : Ui.Muted);
            }
            else if (m_page == Page.Radio)
            {
                Tray.SetAction(0, null, Ui.Muted);
                Tray.SetAction(1, null, Ui.Good);
            }
            else if (m_page == Page.Tools)
            {
                if (m_options.Count > 0)
                    m_options[0].Price = m_hasSpanner ? "In your tool tray · fits the bolts" : "Fits the air unit's bolts";
                Tray.Refresh();
                bool spanner = m_toolPick == 0;
                Tray.SetAction(0, spanner ? "Use spanner on air unit ›" : "Use " + m_options[m_toolPick].Label.ToLower() + " ›", spanner ? Ui.Good : Ui.Accent);
            }
        }

        // Free camera only: bring the puzzle's props up close above the tray (in AR the user moves).
        private void FramePage(Page page)
        {
            Transform t = null;
            switch (page)
            {
                case Page.Radio: t = Radio != null ? Radio.transform : null; break;
                case Page.Keypad: t = KeypadAnchor; break;
                case Page.Tools: t = m_hasSpanner || m_fixing ? (AirConditioner != null ? AirConditioner.transform : null) : (Locker != null ? Locker.transform : null); break;
                case Page.Power: t = Monitor != null ? Monitor.transform : null; break;
            }
            if (t == null || FreeCamera.Instance == null)
                return;
            Vector3 p = transform.InverseTransformPoint(Center(t));
            if (page == Page.Power) p = new Vector3(p.x, 0.25f, p.z - 0.25f);   // monitor and switch bank together
            if (page == Page.Keypad) p += new Vector3(-0.15f, -0.1f, 0f);       // keypad and locker together
            FreeCamera.Instance.Frame(p, 2.9f, 40f);
        }

        private void CloseConsole()
        {
            if (m_page != Page.None && Tray != null && Tray.IsOpen)
            {
                m_page = Page.None;
                Tray.Close();
            }
            m_page = Page.None;
        }

        private void OnTrayClosed()
        {
            if (m_page == Page.None)
                return;
            m_page = Page.None;
            if (!m_escaped) FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            Guide();
        }

        // ---------- keypad ----------

        public void PressKey(int key)
        {
            if (m_lockerOpen || key < 0 || key >= KeyNames.Length)
                return;
            if (m_entry.Count < 3)
                m_entry.Add(key);
            if (KeypadAnchor != null)
                Fx.Play(FxKind.Hit, KeypadAnchor.position, 0.25f);
            UpdateKeypad(null);
            RefreshPage();
            if (m_entry.Count == 3)
                CheckCode();
        }

        public void ClearEntry()
        {
            if (m_lockerOpen) return;
            m_entry.Clear();
            UpdateKeypad(null);
            RefreshPage();
        }

        private void CheckCode()
        {
            bool ok = true;
            for (int i = 0; i < 3; i++)
                if (m_entry[i] != m_code[i]) ok = false;
            if (ok)
            {
                OpenLocker();
                return;
            }
            m_wrongCodes++;
            m_entry.Clear();
            UpdateKeypad("ERROR");
            Run(ClearKeypadLater());
            RefreshPage();
            Toast(m_radioHeard ? "Wrong code. Check the radio signal again." : "Wrong code. Is anything broadcasting nearby?");
        }

        private IEnumerator ClearKeypadLater()
        {
            yield return new WaitForSeconds(0.9f);
            if (!m_lockerOpen) UpdateKeypad(null);
        }

        private void OpenLocker()
        {
            m_lockerOpen = true;
            m_entry.Clear();
            UpdateKeypad("OPEN");
            UpdateScore();
            Fx.Play(FxKind.Poof, Center(Locker.transform), 0.8f);
            Toast("Locker open!  " + Product + " SmartLock");
            Guide();
            Run(OpenLockerRoutine());
        }

        private IEnumerator OpenLockerRoutine()
        {
            if (LockerHinge != null)
                yield return Swing(LockerHinge, m_hingeClosed, m_hingeClosed * Quaternion.Euler(0f, 105f, 0f), 0.6f);
            Spanner.gameObject.SetActive(true);
            Spanner.transform.localScale = m_baseScale[Spanner.transform];
            Fx.Play(FxKind.Sparkle, Center(Spanner.transform), 0.7f);
            Run(Tween.PopIn(Spanner.transform, 0.35f));
            yield return Tween.Hop(Spanner.transform, 0.12f, 0.4f);
            m_spannerReady = true;
            Guide();
            // The console moves on to the tools page by itself if the player is still on the keypad.
            if (TrayOpen && m_page == Page.Keypad) OpenPage(Page.Tools, 0);
        }

        // ---------- tools / air unit ----------

        private void PickTool(int index)
        {
            m_toolPick = index;
            if (index == 0 && m_spannerReady && !m_hasSpanner) GrabSpanner();
            Transform t = index == 0 ? (Spanner != null ? Spanner.transform : null)
                : (index - 1 < DecoyTools.Length && DecoyTools[index - 1] != null ? DecoyTools[index - 1].transform : null);
            if (t != null && t.gameObject.activeInHierarchy)
            {
                Punch(t, 0.2f);
                Fx.Play(FxKind.Sparkle, Center(t), 0.4f);
            }
            RefreshPage();
        }

        public void UseTool()
        {
            if (m_acFixed || m_fixing)
                return;
            if (m_toolPick != 0)
            {
                m_wrongTools++;
                Punch(AirConditioner.transform, 0.1f);
                Fx.Play(FxKind.Hit, Top(AirConditioner.transform), 0.6f);
                var d = m_toolPick - 1 < DecoyTools.Length ? DecoyTools[m_toolPick - 1] : null;
                Toast(d != null && !string.IsNullOrEmpty(d.Message) ? d.Message : "That won't fit.");
                return;
            }
            if (!m_spannerReady)
            {
                Toast("Where's the spanner? Open the locker first.");
                return;
            }
            GrabSpanner();
            Run(FixRoutine());
        }

        private IEnumerator FixRoutine()
        {
            m_fixing = true;
            FramePage(Page.Tools);
            Transform sp = Spanner.transform;
            Vector3 acLocal = transform.InverseTransformPoint(Center(AirConditioner.transform));
            Vector3 front = acLocal + new Vector3(-0.05f, 0.08f, -0.22f);
            yield return Tween.MoveLocal(sp, front, 0.4f);
            for (int i = 0; i < 3; i++)
            {
                Punch(AirConditioner.transform, 0.12f);
                Fx.Play(FxKind.Hit, transform.TransformPoint(front), 0.5f);
                yield return Tween.Hop(sp, 0.05f, 0.18f);
            }
            m_acFixed = true;
            m_fixing = false;
            Fx.Play(FxKind.Sparkle, Top(AirConditioner.transform), 1.2f);
            Fx.Play(FxKind.Stars, Top(AirConditioner.transform), 0.8f);
            UpdateScore();
            UpdateMonitor();
            UpdateTray();
            Toast("Fixed - power is back!");
            Guide();
            OpenPage(Page.Power);
            yield return Tween.MoveLocal(sp, transform.InverseTransformPoint(TraySlot.position), 0.45f);
        }

        private IEnumerator SmokeLoop()
        {
            yield return new WaitForSeconds(0.3f);
            while (!m_acFixed)
            {
                if (SmokePoint != null)
                {
                    Fx.Play(FxKind.Smoke, SmokePoint.position, 0.7f);
                    if (UnityEngine.Random.value < 0.4f)
                        Fx.Play(FxKind.Hit, SmokePoint.position, 0.3f);
                }
                yield return new WaitForSeconds(1.2f);
            }
        }

        // ---------- switches / airlock ----------

        public void ToggleSwitch(int i)
        {
            if (i < 0 || i >= Switches.Length || m_escaped)
                return;
            if (!m_acFixed)
            {
                Toast("No power. The air unit is overheating!");
                return;
            }
            m_switchOn[i] = !m_switchOn[i];
            ApplySwitchVisual(i, true);
            Punch(Switches[i].transform, 0.25f);
            Fx.Play(FxKind.Hit, Center(Switches[i].transform), 0.35f);
            UpdateMonitor();
            Guide();
            if (Matches())
            {
                Run(EscapeRoutine());
                return;
            }
            // Re-open swaps the tiles in place so their colours follow the switches.
            if (TrayOpen && m_page == Page.Power) OpenPage(Page.Power, i);
        }

        private bool Matches()
        {
            for (int i = 0; i < 3; i++)
                if (m_switchOn[i] != m_pattern[i]) return false;
            return true;
        }

        public bool SwitchOn(int i) => m_switchOn[i];
        public bool PatternOn(int i) => m_pattern[i];

        private string PatternText()
        {
            return (m_pattern[0] ? "ON" : "OFF") + " · " + (m_pattern[1] ? "ON" : "OFF") + " · " + (m_pattern[2] ? "ON" : "OFF");
        }

        private void ApplySwitchVisual(int i, bool animate)
        {
            bool on = m_switchOn[i];
            if (i < Levers.Length && Levers[i] != null)
            {
                Quaternion target = m_leverOff[i] * Quaternion.Euler(on ? -70f : 0f, 0f, 0f);
                if (animate) Run(Swing(Levers[i], Levers[i].localRotation, target, 0.15f));
                else Levers[i].localRotation = target;
            }
            if (i < Lamps.Length && Lamps[i] != null)
            {
                Lamps[i].GetPropertyBlock(m_mpb);
                m_mpb.SetColor("_BaseColor", on ? LampOn : LampOff);
                m_mpb.SetColor("_Color", on ? LampOn : LampOff);
                Lamps[i].SetPropertyBlock(m_mpb);
            }
        }

        private IEnumerator EscapeRoutine()
        {
            m_escaped = true;
            m_endTime = Time.time;
            CloseConsole();
            UpdateScore();
            UpdateMonitor();
            Guide();
            if (Hud != null)
            {
                Hud.SetTimer(FormatTime(m_endTime - m_startTime));
                Hud.SetHint("");
            }
            FreeCamera.Instance?.Frame(transform.InverseTransformPoint(Center(Airlock.transform)) + new Vector3(0f, -0.1f, -0.3f), 3.4f, 36f);
            Vector3 door = Center(Airlock.transform);
            Fx.Play(FxKind.Magic, door, 1.4f);
            yield return new WaitForSeconds(0.35f);
            if (AirlockLeft != null) Run(Tween.MoveLocal(AirlockLeft, AirlockLeftOpen, 0.9f));
            if (AirlockRight != null) Run(Tween.MoveLocal(AirlockRight, AirlockRightOpen, 0.9f));
            yield return new WaitForSeconds(0.6f);
            Fx.Play(FxKind.Confetti, door, 1.4f);
            Fx.Play(FxKind.Stars, door + Vector3.up * 0.1f, 1f);
            Toast("Airlock open!  " + Product + " SafeSeal");
            yield return new WaitForSeconds(1.8f);
            ShowEscapedResult();
        }

        private void ShowEscapedResult()
        {
            WorldCard.CloseOpen();
            if (Hud == null || Hud.ResultVisible)
                return;
            int slips = m_wrongCodes + m_wrongTools;
            string extra = slips == 0 ? "No wrong codes or tools - flawless!" : slips + " slip" + (slips == 1 ? "" : "s") + " on the way.";
            Hud.ShowResult("Escaped!",
                "Time: " + FormatTime(m_endTime - m_startTime) + "\n" + extra + "\nAll 4 " + Product + " features unlocked.",
                ("Play again", ResetState),
                ("Hub", () => { if (ShowcaseHub.Instance != null) ShowcaseHub.Instance.ReturnToHub(); }));
        }

        // ---------- world guidance ----------

        // One pulsing tag on whatever to look at next, plus status tags: the heard signal over the radio
        // and the wanted switch pattern over the monitor. Hint line matches the step.
        private void Guide()
        {
            if (m_guide == null)
            {
                m_guide = WorldTag.Create(transform, Vector3.zero, "", 0.13f, Ui.Accent);
                m_signalTag = WorldTag.Create(transform, Vector3.zero, "", 0.12f, TagDark);
                m_patternTag = WorldTag.Create(transform, Vector3.zero, "", 0.12f, TagDark);
            }
            Transform target = null;
            string label = null, hint = null;
            if (m_escaped) { }
            else if (!m_radioHeard) { target = Radio.transform; label = "Radio · tap to listen"; hint = "Escape the station! Start with the crackling radio."; }
            else if (!m_lockerOpen) { target = KeypadAnchor; label = "Keypad · enter the signal"; hint = "Enter the radio signal on the red locker's keypad."; }
            else if (!m_hasSpanner && m_spannerReady) { target = Spanner.transform; label = "Spanner · grab it"; hint = "Grab the spanner that fell out of the locker."; }
            else if (!m_acFixed) { target = AirConditioner.transform; label = "Air unit · fix it"; hint = "Use the right tool on the smoking air unit."; }
            else { target = Switches.Length > 1 ? Switches[1].transform : Monitor.transform; label = "Switches · match the screen"; hint = "Power is back! Match the 3 switches to the monitor."; }

            // On the keypad page the world keypad itself is the focus (and the tag would sit under the wall lamp).
            m_guide.Show(target != null && !(TrayOpen && m_page == Page.Keypad && target == KeypadAnchor));
            if (target != null)
            {
                m_guide.Set(label, Ui.Accent);
                m_guide.transform.localPosition = transform.InverseTransformPoint(Top(target)) + Vector3.up * 0.13f;
                m_guide.Pulse(true);
            }
            if (hint != null && Hud != null) Hud.SetHint(hint);

            m_signalTag.Show(m_radioHeard && !m_lockerOpen && !m_escaped);
            if (m_radioHeard)
            {
                m_signalTag.Set("Signal  " + CodeText());
                m_signalTag.transform.localPosition = transform.InverseTransformPoint(Top(Radio.transform)) + Vector3.up * 0.13f;
            }
            m_patternTag.Show(m_acFixed && !m_escaped);
            if (m_acFixed && MonitorAnchor != null)
            {
                m_patternTag.Set("Screen wants  " + PatternText());
                m_patternTag.transform.localPosition = transform.InverseTransformPoint(Top(Monitor.transform)) + Vector3.up * 0.1f;
            }
        }

        private string Dot(int key) => "<color=" + KeyHex[key] + ">■</color>";

        private string Word(int key) => "<color=" + KeyHex[key] + ">" + KeyNames[key] + "</color>";

        private string CodeText() => Word(m_code[0]) + " · " + Word(m_code[1]) + " · " + Word(m_code[2]);

        private string EntryText(bool rich)
        {
            string s = "";
            for (int i = 0; i < 3; i++)
                s += (i < m_entry.Count ? (rich ? Dot(m_entry[i]) : KeyNames[m_entry[i]]) : "_") + (i < 2 ? " " : "");
            return s;
        }

        // ---------- world UI ----------

        private void BuildUi()
        {
            if (m_uiBuilt)
                return;
            m_uiBuilt = true;

            if (KeypadAnchor != null)
            {
                // Display only: the keys are pressed in the console tray. Taps on it reach the keypad's collider.
                var canvas = Ui.WorldCanvas("Keypad Canvas", KeypadAnchor, new Vector2(360, 540), KeypadWidth);
                var bg = Ui.Panel(canvas.transform, new Color(0.17f, 0.18f, 0.24f, 1f), "Background");
                Ui.Stretch(bg.rectTransform);
                var stripe = Ui.Panel(bg.transform, Ui.Accent, "Stripe");
                Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(360, 10), Vector2.zero);
                var head = Ui.Label(bg.transform, "LOCKER CODE", 40, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold, "Header");
                Ui.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(340, 60), new Vector2(0, -18));
                m_keypadScreen = Ui.Panel(bg.transform, ScreenBg, "Display");
                Ui.Place(m_keypadScreen.rectTransform, new Vector2(0.5f, 1f), new Vector2(316, 120), new Vector2(0, -84));
                m_keypadDisplay = Ui.Label(m_keypadScreen.transform, "_ _ _", 84, ScreenGreen, TextAnchor.MiddleCenter, FontStyle.Bold, "Digits");
                Ui.Stretch(m_keypadDisplay.rectTransform);
                for (int k = 0; k < KeyNames.Length; k++)
                {
                    var key = Ui.Round(Ui.Panel(bg.transform, KeyColors[k], "Key " + KeyNames[k]), 20f);
                    Ui.Place(key.rectTransform, new Vector2(0f, 1f), new Vector2(150, 130), new Vector2(22 + (k % 2) * 166, -228 - (k / 2) * 146));
                }
            }

            if (MonitorAnchor != null)
            {
                var canvas = Ui.WorldCanvas("Monitor Canvas", MonitorAnchor, new Vector2(420, 300), MonitorWidth);
                var bg = Ui.Panel(canvas.transform, ScreenBg, "Screen");
                Ui.Stretch(bg.rectTransform);
                m_monitorHeader = Ui.Label(bg.transform, "NOVA X1 · POWER", 34, new Color(0.5f, 0.9f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold, "Header");
                Ui.Place(m_monitorHeader.rectTransform, new Vector2(0.5f, 1f), new Vector2(400, 50), new Vector2(0, -10));
                m_monitorStatus = Ui.Label(bg.transform, "", 52, ScreenRed, TextAnchor.MiddleCenter, FontStyle.Bold, "Status");
                Ui.Place(m_monitorStatus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(400, 200), new Vector2(0, -20));
                m_monitorCells = new Image[3];
                m_monitorCellText = new Text[3];
                for (int i = 0; i < 3; i++)
                {
                    m_monitorCells[i] = Ui.Panel(bg.transform, Color.gray, "Cell " + (i + 1));
                    Ui.Place(m_monitorCells[i].rectTransform, new Vector2(0f, 0f), new Vector2(112, 150), new Vector2(28 + i * 128, 36));
                    m_monitorCellText[i] = Ui.Label(m_monitorCells[i].transform, "", 34, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Text");
                    Ui.Stretch(m_monitorCellText[i].rectTransform);
                }
            }

            if (TraySign != null)
            {
                var canvas = Ui.WorldCanvas("Tray Canvas", TraySign, new Vector2(440, 130), 0.44f);
                var bg = Ui.Panel(canvas.transform, Ui.Paper, "Background");
                Ui.Stretch(bg.rectTransform);
                var stripe = Ui.Panel(bg.transform, Ui.Accent2, "Stripe");
                Ui.Place(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(14, 130), Vector2.zero);
                var head = Ui.Label(bg.transform, "TOOL TRAY", 30, Ui.Muted, TextAnchor.UpperLeft, FontStyle.Bold, "Header");
                Ui.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(400, 44), new Vector2(32, -12));
                m_trayLabel = Ui.Label(bg.transform, "", 40, Ui.Ink, TextAnchor.UpperLeft, FontStyle.Bold, "Slot");
                Ui.Place(m_trayLabel.rectTransform, new Vector2(0f, 1f), new Vector2(400, 60), new Vector2(32, -56));
            }
        }

        private void UpdateKeypad(string message)
        {
            if (m_keypadDisplay == null)
                return;
            if (message != null)
            {
                m_keypadDisplay.text = message;
                m_keypadDisplay.color = message == "OPEN" ? ScreenGreen : ScreenRed;
                return;
            }
            m_keypadDisplay.text = EntryText(true);
            m_keypadDisplay.color = ScreenGreen;
        }

        private void UpdateMonitor()
        {
            if (m_monitorStatus == null)
                return;
            bool showPattern = m_acFixed && !m_escaped;
            for (int i = 0; i < 3; i++)
            {
                m_monitorCells[i].gameObject.SetActive(showPattern);
                bool want = m_pattern[i];
                bool ok = m_switchOn[i] == want;
                m_monitorCells[i].color = want ? new Color(0.2f, 0.75f, 0.35f) : new Color(0.3f, 0.3f, 0.36f);
                m_monitorCellText[i].text = (i + 1) + "\n" + (want ? "ON" : "OFF") + (ok ? "\n<size=24>OK</size>" : "\n<size=24>--</size>");
            }
            if (m_escaped)
            {
                m_monitorStatus.text = "AIRLOCK\nOPEN";
                m_monitorStatus.color = ScreenGreen;
            }
            else if (!m_acFixed)
            {
                m_monitorStatus.text = "COOLING\nFAILURE";
                m_monitorStatus.color = ScreenRed;
            }
            else
                m_monitorStatus.text = "";
        }

        private void UpdateTray()
        {
            if (m_trayLabel == null)
                return;
            m_trayLabel.text = !m_hasSpanner ? "<color=#8E8C99>empty</color>" : (m_acFixed ? "Spanner (used)" : "Spanner");
        }

        private void UpdateScore()
        {
            if (Hud == null)
                return;
            int solved = (m_radioHeard ? 1 : 0) + (m_lockerOpen ? 1 : 0) + (m_acFixed ? 1 : 0) + (m_escaped ? 1 : 0);
            Hud.SetScore("√ " + solved + "/4");
        }

        // ---------- helpers ----------

        private void Run(IEnumerator routine)
        {
            if (isActiveAndEnabled)
                m_running.Add(StartCoroutine(routine));
        }

        private void StopRunning()
        {
            foreach (var c in m_running)
                if (c != null) StopCoroutine(c);
            m_running.Clear();
        }

        private void Punch(Transform t, float amount = 0.18f)
        {
            if (t == null)
                return;
            if (m_baseScale.TryGetValue(t, out var s))
                t.localScale = s;
            Run(Tween.Punch(t, amount));
        }

        private static IEnumerator Swing(Transform t, Quaternion from, Quaternion to, float time)
        {
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                if (t == null) yield break;
                t.localRotation = Quaternion.SlerpUnclamped(from, to, Tween.EaseOutBack(a));
                yield return null;
            }
            if (t != null) t.localRotation = to;
        }

        private void Toast(string text)
        {
            if (Hud != null) Hud.Toast(text, 1.6f);
        }

        private static Bounds WorldBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static Vector3 Center(Transform t) => WorldBounds(t).center;

        private static Vector3 Top(Transform t)
        {
            Bounds b = WorldBounds(t);
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }

        private static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        // ---------- tests ----------

        public override void DebugComplete()
        {
            if (m_escaped)
                return;
            StopRunning();
            m_radioHeard = m_lockerOpen = m_spannerReady = m_hasSpanner = m_acFixed = true;
            m_fixing = false;
            if (LockerHinge != null) LockerHinge.localRotation = m_hingeClosed * Quaternion.Euler(0f, 105f, 0f);
            Spanner.gameObject.SetActive(true);
            Spanner.transform.localScale = m_baseScale[Spanner.transform];
            Spanner.transform.localPosition = transform.InverseTransformPoint(TraySlot.position);
            UpdateKeypad("OPEN");
            for (int i = 0; i < 3; i++)
            {
                m_switchOn[i] = m_pattern[i];
                ApplySwitchVisual(i, false);
            }
            UpdateTray();
            UpdateMonitor();
            Run(EscapeRoutine());
        }

        public override IEnumerator SelfTest()
        {
            ResetState();
            yield return new WaitForSeconds(0.5f);
            if (m_radioHeard || m_lockerOpen || m_hasSpanner || m_acFixed || m_escaped)
                Debug.LogError("[SELFTEST] state not reset on restart");
            if (m_guide == null || !m_guide.gameObject.activeSelf) Debug.LogError("[SELFTEST] no guide tag on the radio");

            // Locker tap opens the keypad console; a wrong code keeps it shut.
            Locker.OnTap(default);
            if (CurrentPage != Page.Keypad) { Debug.LogError("[SELFTEST] locker tap did not open the keypad console (" + CurrentPage + ")"); yield break; }
            if (Tray.Options.Count != KeyNames.Length) Debug.LogError("[SELFTEST] keypad shows " + Tray.Options.Count + " keys");
            int wrongKey = (m_code[0] + 1) % KeyNames.Length;
            for (int i = 0; i < 3; i++) { Tray.Select(wrongKey); yield return new WaitForSeconds(0.15f); }
            if (m_lockerOpen) Debug.LogError("[SELFTEST] locker opened with a wrong code");
            if (m_wrongCodes != 1) Debug.LogError("[SELFTEST] wrong code not counted");

            // Switches are dead before the air unit is fixed.
            Switches[0].OnTap(default);
            if (m_switchOn[0]) Debug.LogError("[SELFTEST] switch toggled without power");

            // 1. Radio: the console shows the signal.
            Radio.OnTap(default);
            if (!m_radioHeard) Debug.LogError("[SELFTEST] radio not heard");
            if (CurrentPage != Page.Radio) Debug.LogError("[SELFTEST] radio tap did not open the signal page");
            yield return new WaitForSeconds(0.7f);
            Snapshot("radio");
            for (int i = 0; i < 3; i++)
                if (Tray.Options[i].Label != KeyNames[m_code[i]]) Debug.LogError("[SELFTEST] signal tile " + i + " shows " + Tray.Options[i].Label);

            // 2. Keypad through the console's big colour keys.
            Tray.Press(1);   // "To the keypad ›"
            if (CurrentPage != Page.Keypad) Debug.LogError("[SELFTEST] 'To the keypad' did not switch pages");
            Tray.Select(m_code[0]);
            Tray.Select(m_code[1]);
            yield return new WaitForSeconds(0.6f);
            Snapshot("keypad");
            Tray.Select(m_code[2]);
            if (!m_lockerOpen) Debug.LogError("[SELFTEST] locker did not open with code " + Code);
            yield return WaitFor(() => m_spannerReady, 3f);
            if (!Spanner.gameObject.activeInHierarchy || !m_spannerReady) Debug.LogError("[SELFTEST] spanner not revealed");
            yield return new WaitForSeconds(0.3f);
            if (CurrentPage != Page.Tools) Debug.LogError("[SELFTEST] console did not move on to the tools page (" + CurrentPage + ")");

            // 3. Tools: a decoy doesn't fit, the spanner does.
            Tray.Select(1);
            yield return new WaitForSeconds(0.6f);
            Snapshot("tools");
            Tray.Press(0);
            if (m_acFixed || m_fixing) Debug.LogError("[SELFTEST] air unit fixed with the wrong tool");
            if (m_wrongTools != 1) Debug.LogError("[SELFTEST] wrong tool not counted");
            Tray.Select(0);
            if (!m_hasSpanner) Debug.LogError("[SELFTEST] picking the spanner tile did not grab it");
            yield return new WaitForSeconds(0.5f);
            Tray.Press(0);
            yield return WaitFor(() => m_acFixed, 5f);
            if (!m_acFixed) Debug.LogError("[SELFTEST] air unit not fixed");
            yield return new WaitForSeconds(0.4f);
            if (CurrentPage != Page.Power) Debug.LogError("[SELFTEST] console did not open the power page");

            // 4. Power: one wrong flip via the 3D switch, then fix the pattern with the tray tiles.
            int wrongIdx = Array.FindIndex(m_pattern, p => !p);
            if (wrongIdx < 0) wrongIdx = 0;
            Switches[wrongIdx].OnTap(default);
            yield return new WaitForSeconds(0.3f);
            if (m_escaped != Matches()) Debug.LogError("[SELFTEST] airlock state does not match the switch pattern");
            if (CurrentPage != Page.Power || Tray.Selected != wrongIdx) Debug.LogError("[SELFTEST] tray not synced with the 3D switch");
            yield return new WaitForSeconds(0.4f);
            Snapshot("power");
            for (int i = 0; i < 3 && !m_escaped; i++)
            {
                if (m_switchOn[i] != m_pattern[i])
                {
                    Tray.Select(i);
                    yield return new WaitForSeconds(0.25f);
                }
            }
            if (!m_escaped) Debug.LogError("[SELFTEST] airlock did not open after matching the pattern");
            if (Tray.IsOpen) Debug.LogError("[SELFTEST] console still open during the escape");
            yield return WaitFor(() => Hud != null && Hud.ResultVisible, 5f);
            if (Hud == null || !Hud.ResultVisible) Debug.LogError("[SELFTEST] result not shown");
            Debug.Log("[SELFTEST] station chain complete, code " + Code + ", time " + FormatTime(m_endTime - m_startTime));
        }

        private static IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            for (float t = 0f; t < timeout && !condition(); t += Time.deltaTime)
                yield return null;
        }
    }
}
