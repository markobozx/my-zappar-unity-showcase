using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Showcase.Experiences.Station
{
    // Space Station Escape: four linked puzzles in a cut-away station room.
    //  1. Radio -> 3-digit frequency code.   2. Code on the red locker's keypad -> spanner falls out.
    //  3. Spanner fixes the smoking air unit. 4. Power switches matched to the monitor -> airlock opens.
    // Scene references are filled by StationBuilder; the world UI (keypad, monitor, tool tray) is
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

        [Header("World UI anchors")]
        public Transform KeypadAnchor;
        public float KeypadWidth = 0.42f;
        public Transform MonitorAnchor;
        public float MonitorWidth = 0.3f;
        public Transform TraySign;
        public Transform TraySlot;

        private const string Product = "Nova X1";
        private static readonly Color LampOn = new Color(0.35f, 1f, 0.45f);
        private static readonly Color LampOff = new Color(0.55f, 0.12f, 0.12f);
        private static readonly Color ScreenBg = new Color(0.04f, 0.1f, 0.12f, 0.97f);
        private static readonly Color ScreenGreen = new Color(0.45f, 1f, 0.6f);
        private static readonly Color ScreenRed = new Color(1f, 0.38f, 0.32f);

        // state
        private string m_code = "731";
        private string m_entry = "";
        private bool m_radioHeard, m_lockerOpen, m_spannerReady, m_hasSpanner, m_acFixed, m_fixing, m_escaped;
        private readonly bool[] m_switchOn = new bool[3];
        private readonly bool[] m_pattern = new bool[3];
        private int m_wrongCodes;
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

        public bool RadioHeard => m_radioHeard;
        public bool LockerOpen => m_lockerOpen;
        public bool HasSpanner => m_hasSpanner;
        public bool AcFixed => m_acFixed;
        public bool Escaped => m_escaped;

        protected override void OnEnter()
        {
            EnsureEventSystem();
            Cache();
            BuildUi();
            ResetState();
        }

        protected override void OnExit()
        {
            StopRunning();
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
            if (Hud != null) Hud.HideResult();

            m_code = "" + UnityEngine.Random.Range(1, 10) + UnityEngine.Random.Range(1, 10) + UnityEngine.Random.Range(1, 10);
            m_entry = "";
            m_radioHeard = m_lockerOpen = m_spannerReady = m_hasSpanner = m_acFixed = m_fixing = m_escaped = false;
            m_wrongCodes = 0;
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
            if (Hud != null)
            {
                Hud.SetHint("Escape the station! Start with the crackling radio.");
                Hud.SetTimer("00:00");
            }
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
            foreach (var p in new[] { Radio, Locker, Spanner, AirConditioner, Monitor, Airlock })
                if (p != null) m_baseScale[p.transform] = p.transform.localScale;
            foreach (var s in Switches)
                if (s != null) m_baseScale[s.transform] = s.transform.localScale;
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

        // ---------- taps ----------

        public void HandleTap(StationProp prop)
        {
            if (prop == null || m_escaped)
                return;
            switch (prop.Kind)
            {
                case StationPropKind.Radio: TapRadio(); break;
                case StationPropKind.Locker: TapLocker(); break;
                case StationPropKind.Spanner: TapSpanner(); break;
                case StationPropKind.AirConditioner: TapAirConditioner(); break;
                case StationPropKind.Switch: TapSwitch(prop.Index); break;
                case StationPropKind.Airlock: TapAirlock(); break;
                case StationPropKind.Monitor: TapMonitor(); break;
                case StationPropKind.Decoy:
                    Punch(prop.transform);
                    Fx.Play(FxKind.Hit, Center(prop.transform), 0.5f);
                    Toast(string.IsNullOrEmpty(prop.Message) ? "Not useful right now." : prop.Message);
                    break;
            }
        }

        private void TapRadio()
        {
            Punch(Radio.transform);
            Fx.Play(FxKind.Stars, Top(Radio.transform), 0.6f);
            string code = m_code[0] + " - " + m_code[1] + " - " + m_code[2];
            if (!m_radioHeard)
            {
                m_radioHeard = true;
                UpdateScore();
                Fx.Play(FxKind.Sparkle, Top(Radio.transform), 0.8f);
                if (Hud != null) Hud.SetHint("Enter the frequency on the red locker's keypad.");
            }
            ShowCard(Radio.transform, 0.32f, "Incoming signal…",
                "Frequency: <b><color=#E8584A>" + code + "</color></b>\n" + Product + " feature 1/4:\nDeep-space radio, crystal clear.",
                ("Got it", WorldCard.CloseOpen));
        }

        private void TapLocker()
        {
            Punch(Locker.transform);
            if (!m_lockerOpen)
            {
                Fx.Play(FxKind.Hit, Center(Locker.transform), 0.5f);
                Toast(m_radioHeard ? "Locked. Type the radio code on the keypad." : "Locked tight. The keypad wants a 3-digit code.");
            }
            else
                Toast(m_hasSpanner ? "Empty — you already have the spanner." : "The spanner fell out. Grab it!");
        }

        private void TapSpanner()
        {
            if (!m_spannerReady || m_hasSpanner || m_fixing)
                return;
            m_hasSpanner = true;
            Fx.Play(FxKind.Pickup, Center(Spanner.transform), 0.7f);
            Run(Tween.MoveLocal(Spanner.transform, transform.InverseTransformPoint(TraySlot.position)));
            UpdateTray();
            Toast("Spanner added to the tool tray");
            if (Hud != null) Hud.SetHint("Use the spanner on the smoking air unit.");
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
            if (!m_hasSpanner)
            {
                Fx.Play(FxKind.Hit, Top(AirConditioner.transform), 0.6f);
                Toast("It's stuck. Maybe a tool would help?");
                return;
            }
            Run(FixRoutine());
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
            m_switchOn[i] = !m_switchOn[i];
            ApplySwitchVisual(i, true);
            Fx.Play(FxKind.Hit, Center(Switches[i].transform), 0.35f);
            UpdateMonitor();
            if (Matches())
                Run(EscapeRoutine());
        }

        private void TapAirlock()
        {
            Fx.Play(FxKind.Hit, Center(Airlock.transform), 0.6f);
            Toast(m_acFixed ? "Sealed. Set the switches to match the monitor." : "Airlock sealed. No power to open it.");
        }

        private void TapMonitor()
        {
            Punch(Monitor.transform, 0.1f);
            if (!m_acFixed)
                ShowCard(Monitor.transform, 0.3f, "Power console", "Cooling failure!\nPower is locked until the\nair unit is repaired.", ("OK", WorldCard.CloseOpen));
            else
                ShowCard(Monitor.transform, 0.3f, "Power console", "Set the 3 switches to the\nON / OFF pattern shown\non the screen.", ("OK", WorldCard.CloseOpen));
        }

        // ---------- keypad ----------

        public void PressKey(char key)
        {
            if (m_lockerOpen)
                return;
            if (key == 'C')
                m_entry = "";
            else if (m_entry.Length < 3)
                m_entry += key;
            if (KeypadAnchor != null)
                Fx.Play(FxKind.Hit, KeypadAnchor.position, 0.25f);
            UpdateKeypad(null);
            if (m_entry.Length == 3)
                CheckCode();
        }

        private void CheckCode()
        {
            if (m_entry == m_code)
            {
                OpenLocker();
                return;
            }
            m_wrongCodes++;
            m_entry = "";
            UpdateKeypad("ERROR");
            Run(ClearKeypadLater());
            Toast(m_radioHeard ? "Wrong code. Check the radio frequency again." : "Wrong code. Is anything broadcasting nearby?");
        }

        private IEnumerator ClearKeypadLater()
        {
            yield return new WaitForSeconds(0.9f);
            if (!m_lockerOpen) UpdateKeypad(null);
        }

        private void OpenLocker()
        {
            m_lockerOpen = true;
            m_entry = "";
            UpdateKeypad("OPEN");
            UpdateScore();
            Fx.Play(FxKind.Poof, Center(Locker.transform), 0.8f);
            Run(OpenLockerRoutine());
            if (Hud != null) Hud.SetHint("Grab the spanner, then fix the smoking air unit.");
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
            ShowCard(Locker.transform, 0.3f, "Locker open!",
                "A spanner clattered out.\n" + Product + " feature 2/4:\nSmartLock code security.",
                ("Nice", WorldCard.CloseOpen));
        }

        // ---------- air unit ----------

        private IEnumerator FixRoutine()
        {
            m_fixing = true;
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
            if (Hud != null) Hud.SetHint("Power is back! Match the 3 switches to the monitor.");
            ShowCard(AirConditioner.transform, 0.28f, "Air unit fixed!",
                "Cooling restored, power online.\n" + Product + " feature 3/4:\nSelf-healing climate core.",
                ("Great", WorldCard.CloseOpen));
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

        private bool Matches()
        {
            for (int i = 0; i < 3; i++)
                if (m_switchOn[i] != m_pattern[i]) return false;
            return true;
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
            UpdateScore();
            UpdateMonitor();
            if (Hud != null)
            {
                Hud.SetTimer(FormatTime(m_endTime - m_startTime));
                Hud.SetHint("");
            }
            Vector3 door = Center(Airlock.transform);
            Fx.Play(FxKind.Magic, door, 1.4f);
            yield return new WaitForSeconds(0.35f);
            if (AirlockLeft != null) Run(Tween.MoveLocal(AirlockLeft, AirlockLeftOpen, 0.9f));
            if (AirlockRight != null) Run(Tween.MoveLocal(AirlockRight, AirlockRightOpen, 0.9f));
            yield return new WaitForSeconds(0.6f);
            Fx.Play(FxKind.Confetti, door, 1.4f);
            Fx.Play(FxKind.Stars, door + Vector3.up * 0.1f, 1f);
            ShowCard(Airlock.transform, 0.15f, "Airlock open!",
                Product + " feature 4/4:\nSafeSeal one-touch airlock.", 0.65f, ("Escape!", ShowEscapedResult));
            yield return new WaitForSeconds(1.6f);
            ShowEscapedResult();
        }

        private void ShowEscapedResult()
        {
            WorldCard.CloseOpen();
            if (Hud == null || Hud.ResultVisible)
                return;
            string extra = m_wrongCodes == 0 ? "No wrong codes — flawless!" : m_wrongCodes + " wrong code" + (m_wrongCodes == 1 ? "" : "s") + " on the way.";
            Hud.ShowResult("Escaped!",
                "Time: " + FormatTime(m_endTime - m_startTime) + "\n" + extra + "\nAll 4 " + Product + " features unlocked.",
                ("Play again", ResetState),
                ("Hub", () => { if (ShowcaseHub.Instance != null) ShowcaseHub.Instance.ReturnToHub(); }));
        }

        // ---------- world UI ----------

        private void BuildUi()
        {
            if (m_uiBuilt)
                return;
            m_uiBuilt = true;

            if (KeypadAnchor != null)
            {
                var canvas = Ui.WorldCanvas("Keypad Canvas", KeypadAnchor, new Vector2(360, 540), KeypadWidth);
                var bg = Ui.Panel(canvas.transform, new Color(0.17f, 0.18f, 0.24f, 1f), "Background");
                Ui.Stretch(bg.rectTransform);
                var stripe = Ui.Panel(bg.transform, Ui.Accent, "Stripe");
                Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(360, 10), Vector2.zero);
                // Only the keys catch UI raycasts; world canvases otherwise eat taps meant for props in front of them.
                bg.raycastTarget = stripe.raycastTarget = false;
                var head = Ui.Label(bg.transform, "LOCKER CODE", 30, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter, FontStyle.Bold, "Header");
                Ui.Place(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(340, 50), new Vector2(0, -16));
                m_keypadScreen = Ui.Panel(bg.transform, ScreenBg, "Display");
                Ui.Place(m_keypadScreen.rectTransform, new Vector2(0.5f, 1f), new Vector2(316, 90), new Vector2(0, -70));
                m_keypadScreen.raycastTarget = false;
                m_keypadDisplay = Ui.Label(m_keypadScreen.transform, "_ _ _", 62, ScreenGreen, TextAnchor.MiddleCenter, FontStyle.Bold, "Digits");
                Ui.Stretch(m_keypadDisplay.rectTransform);
                string keys = "123456789C0";
                for (int k = 0; k < keys.Length; k++)
                {
                    char key = keys[k];
                    int col = k % 3, row = k / 3;
                    if (key == '0') col = 1;
                    Color c = key == 'C' ? Ui.Accent : new Color(0.32f, 0.35f, 0.45f);
                    var b = Ui.Button(bg.transform, key.ToString(), c, () => PressKey(key), 46, "Key " + key);
                    Ui.Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(96, 76), new Vector2(22 + col * 112, -178 - row * 88));
                }
                var lockIcon = Ui.Label(bg.transform, "3 digits", 24, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleCenter, FontStyle.Italic, "Tip");
                Ui.Place(lockIcon.rectTransform, new Vector2(0f, 1f), new Vector2(96, 76), new Vector2(22 + 2 * 112, -178 - 3 * 88));
            }

            if (MonitorAnchor != null)
            {
                var canvas = Ui.WorldCanvas("Monitor Canvas", MonitorAnchor, new Vector2(420, 300), MonitorWidth);
                var bg = Ui.Panel(canvas.transform, ScreenBg, "Screen");
                Ui.Stretch(bg.rectTransform);
                // Display only, so taps on the screen reach the monitor's collider (TapMonitor).
                bg.raycastTarget = false;
                m_monitorHeader = Ui.Label(bg.transform, "NOVA X1 · POWER", 34, new Color(0.5f, 0.9f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold, "Header");
                Ui.Place(m_monitorHeader.rectTransform, new Vector2(0.5f, 1f), new Vector2(400, 50), new Vector2(0, -10));
                m_monitorStatus = Ui.Label(bg.transform, "", 52, ScreenRed, TextAnchor.MiddleCenter, FontStyle.Bold, "Status");
                Ui.Place(m_monitorStatus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(400, 200), new Vector2(0, -20));
                m_monitorCells = new Image[3];
                m_monitorCellText = new Text[3];
                for (int i = 0; i < 3; i++)
                {
                    m_monitorCells[i] = Ui.Panel(bg.transform, Color.gray, "Cell " + (i + 1));
                    m_monitorCells[i].raycastTarget = false;
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
                bg.raycastTarget = stripe.raycastTarget = false;
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
            string s = "";
            for (int i = 0; i < 3; i++)
                s += (i < m_entry.Length ? m_entry[i].ToString() : "_") + (i < 2 ? " " : "");
            m_keypadDisplay.text = s;
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
            Hud.SetScore("Puzzles " + solved + "/4");
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
            if (Hud != null) Hud.Toast(text, 2.2f);
        }

        private void ShowCard(Transform prop, float above, string title, string body, params (string, Action)[] buttons) =>
            ShowCard(prop, above, title, body, 0.75f, buttons);

        private void ShowCard(Transform prop, float above, string title, string body, float width, params (string, Action)[] buttons)
        {
            Vector3 top = transform.InverseTransformPoint(Top(prop));
            WorldCard.Show(transform, top + Vector3.up * above, title, body, width, buttons);
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

            // Wrong code first: the locker must stay shut.
            string wrong = new string(m_code[0] == '1' ? '2' : '1', 3);
            foreach (char c in wrong) { PressKey(c); yield return new WaitForSeconds(0.15f); }
            if (m_lockerOpen) Debug.LogError("[SELFTEST] locker opened with a wrong code");
            if (m_wrongCodes != 1) Debug.LogError("[SELFTEST] wrong code not counted");

            // Switches are dead before the air unit is fixed.
            Switches[0].OnTap(default);
            if (m_switchOn[0]) Debug.LogError("[SELFTEST] switch toggled without power");

            // 1. Radio
            Radio.OnTap(default);
            if (!m_radioHeard) Debug.LogError("[SELFTEST] radio not heard");
            yield return new WaitForSeconds(0.8f);

            // 2. Keypad
            foreach (char c in m_code) { PressKey(c); yield return new WaitForSeconds(0.15f); }
            if (!m_lockerOpen) Debug.LogError("[SELFTEST] locker did not open with code " + m_code);
            yield return WaitFor(() => m_spannerReady, 3f);
            if (!Spanner.gameObject.activeInHierarchy || !m_spannerReady) Debug.LogError("[SELFTEST] spanner not revealed");
            yield return new WaitForSeconds(0.6f);

            // 3. Air unit: no tool first, then spanner.
            AirConditioner.OnTap(default);
            if (m_acFixed || m_fixing) Debug.LogError("[SELFTEST] air unit fixed without a tool");
            Spanner.OnTap(default);
            if (!m_hasSpanner) Debug.LogError("[SELFTEST] spanner not picked up");
            yield return new WaitForSeconds(0.6f);
            AirConditioner.OnTap(default);
            yield return WaitFor(() => m_acFixed, 5f);
            if (!m_acFixed) Debug.LogError("[SELFTEST] air unit not fixed");
            yield return new WaitForSeconds(0.8f);

            // 4. Power switches (one deliberate wrong toggle first, then match the pattern).
            int wrongIdx = 0;
            Switches[wrongIdx].OnTap(default);
            yield return new WaitForSeconds(0.25f);
            if (m_escaped != Matches()) Debug.LogError("[SELFTEST] airlock state does not match the switch pattern");
            for (int i = 0; i < 3 && !m_escaped; i++)
            {
                if (m_switchOn[i] != m_pattern[i])
                {
                    Switches[i].OnTap(default);
                    yield return new WaitForSeconds(0.25f);
                }
            }
            if (!m_escaped) Debug.LogError("[SELFTEST] airlock did not open after matching the pattern");
            yield return WaitFor(() => Hud != null && Hud.ResultVisible, 5f);
            if (Hud == null || !Hud.ResultVisible) Debug.LogError("[SELFTEST] result not shown");
            Debug.Log("[SELFTEST] station chain complete, code " + m_code + ", time " + FormatTime(m_endTime - m_startTime));
        }

        private static IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            for (float t = 0f; t < timeout && !condition(); t += Time.deltaTime)
                yield return null;
        }
    }
}
