using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Nightclub
{
    // Hidden-Object Nightclub: find 8 lost items in a dim club diorama. The bottom tray is the
    // checklist (a rendered thumbnail of every item, √ when found); picking a tile that is still
    // hidden is the hint: the camera swings to its area and the item sparkles. Found items fly to
    // the inventory shelf at the front edge with a floating tag, the board on the back wall fills
    // in, and a "flashlight" spot follows the player's taps.
    public class NightclubExperience : ShowcaseExperience
    {
        public NightclubItem[] Items;
        public NightclubDecoy[] Decoys;
        public Transform[] ShelfSlots;

        [Header("Checklist board")]
        public Text BoardHeader;
        public Image[] BoardIcons;
        public Text[] BoardLabels;

        [Header("Mood")]
        public Light Flashlight;
        public Light[] ClubLights;
        public Renderer[] DanceTiles;
        public Color[] DanceColors;

        const string HintText = "Tap the lost items hidden around the club";

        private int m_found;
        private int m_wrongTaps;
        private bool m_playing;
        private float m_startTime, m_endTime;
        private float m_lastWrongToast = -10f;
        private int m_lastTimerSecond = -1;
        private Vector3 m_aimLocal;   // flashlight target, local so it follows the tracked target
        private Vector3 AimPoint { get => transform.TransformPoint(m_aimLocal); set => m_aimLocal = transform.InverseTransformPoint(value); }
        private float[] m_lightBase;
        private MaterialPropertyBlock m_block;
        private Coroutine m_finishRoutine;

        // Tray checklist + world tags
        private List<TrayOption> m_options;
        private bool m_trayOwned;          // the tray currently shows our checklist
        private NightclubItem m_lookingAt; // camera is framed on this item's area
        private readonly HashSet<NightclubItem> m_hinted = new HashSet<NightclubItem>();
        private readonly Dictionary<NightclubItem, WorldTag> m_tags = new Dictionary<NightclubItem, WorldTag>();
        private readonly Dictionary<NightclubItem, Coroutine> m_tagRoutines = new Dictionary<NightclubItem, Coroutine>();
        private GameObject m_screenUi;
        private Button m_listButton;

        private static readonly string[] s_wrongLines = { "Not this one", "Nope, keep looking", "Just club decor", "Not on the list" };
        private static readonly int s_baseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color TagFound = new Color(0.24f, 0.6f, 0.36f, 0.92f);
        private static ShowcaseTray Tray => ShowcaseTray.Instance;

        public int FoundCount => m_found;
        public int WrongTaps => m_wrongTaps;
        public int HintsUsed => m_hinted.Count;
        public bool ListOpen => Tray != null && Tray.IsOpen && m_trayOwned;

        protected override void OnEnter()
        {
            ShowcaseInput.Tapped -= OnAnyTap;
            ShowcaseInput.Tapped += OnAnyTap;
            if (Tray != null)
            {
                Tray.Closed -= OnTrayClosed;
                Tray.Closed += OnTrayClosed;
            }
            EnsureScreenUi();
            Restart();
        }

        protected override void OnExit()
        {
            ShowcaseInput.Tapped -= OnAnyTap;
            if (Tray != null) Tray.Closed -= OnTrayClosed;
            m_playing = false;
            CloseList();
            if (Hud != null) { Hud.HideResult(); Hud.SetTimer(""); }
            if (m_screenUi != null) m_screenUi.SetActive(false);
        }

        void OnDisable()
        {
            ShowcaseInput.Tapped -= OnAnyTap;
            if (m_screenUi != null) m_screenUi.SetActive(false);
        }

        void OnDestroy()
        {
            if (m_screenUi != null) Destroy(m_screenUi);
        }

        public void Restart()
        {
            if (m_finishRoutine != null) { StopCoroutine(m_finishRoutine); m_finishRoutine = null; }
            if (Hud != null) { Hud.HideResult(); Hud.SetHint(HintText); }
            foreach (var d in Decoys) if (d != null) d.ResetDecoy();
            foreach (var item in Items) item.ResetItem();
            foreach (var kv in m_tagRoutines) if (kv.Value != null) StopCoroutine(kv.Value);
            m_tagRoutines.Clear();
            foreach (var tag in m_tags.Values) if (tag != null) Destroy(tag.gameObject);
            m_tags.Clear();
            m_found = 0;
            m_wrongTaps = 0;
            m_hinted.Clear();
            m_lookingAt = null;
            m_lastWrongToast = -10f;
            m_startTime = Time.time;
            m_lastTimerSecond = -1;
            m_playing = true;
            m_aimLocal = new Vector3(0f, 0f, 0.2f);
            if (Flashlight != null)
                Flashlight.transform.rotation = Quaternion.LookRotation(AimPoint - Flashlight.transform.position, transform.up);
            m_options = new List<TrayOption>();
            foreach (var item in Items)
                m_options.Add(new TrayOption(item.DisplayName, null, null, item.gameObject));
            RefreshBoard();
            UpdateScore();
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            OpenList(FirstUnfound());
        }

        // ---- checklist tray ----

        public void OpenList(int index)
        {
            if (!m_playing || Tray == null || m_options == null)
                return;
            RefreshOptions();
            m_trayOwned = true;
            if (m_lookingAt == null) FrameList();
            Tray.Open(ListTitle(), m_options, index, PickTile, ("Go look ›", GoLook));
            RefreshAction();
        }

        // Overview while the list is open: aim a little further back so the stage sits lower, in
        // the space right above the tray instead of under the HUD title.
        private void FrameList() => FreeCamera.Instance?.Frame(FocusPoint + new Vector3(0f, 0.17f, 0.18f), FocusDistance, FocusPitch);

        // A tile was picked: a hidden item gives its hint, a found one just shows its story.
        public void PickTile(int index)
        {
            if (!m_playing || index < 0 || index >= Items.Length)
                return;
            var item = Items[index];
            if (item.Found)
            {
                m_lookingAt = null;
                FreeCamera.Instance?.Frame(transform.InverseTransformPoint(item.transform.position) + Vector3.up * 0.1f, 2.4f, 40f);
                Fx.Play(FxKind.Sparkle, item.transform.position + transform.up * 0.06f * transform.lossyScale.y, 0.5f);
            }
            else
            {
                ShowHint(item);
            }
            RefreshAction();
        }

        // Legacy entry point (old hint pad): open the checklist on the nearest hidden item.
        public void ShowHint()
        {
            var item = NearestUnfound(AimPoint, float.MaxValue, false);
            if (item == null) return;
            int i = System.Array.IndexOf(Items, item);
            OpenList(i);
            PickTile(i);
        }

        private void ShowHint(NightclubItem item)
        {
            m_hinted.Add(item);
            m_lookingAt = item;
            AimPoint = item.transform.position;
            item.Pulse();
            // Frame the rough area, not the exact spot, so there is still something to find.
            Vector3 local = transform.InverseTransformPoint(item.transform.position);
            Vector3 area = new Vector3(Mathf.Lerp(local.x, 0f, 0.25f), 0.12f, Mathf.Lerp(local.z, 0.1f, 0.25f));
            FreeCamera.Instance?.Frame(area, 2.5f, 44f);
        }

        // "Go look": hide the tray so the whole screen shows the area the camera is framing.
        public void GoLook()
        {
            int i = Tray != null ? Tray.Selected : -1;
            if (i >= 0 && i < Items.Length && !Items[i].Found)
            {
                if (m_lookingAt != Items[i]) ShowHint(Items[i]);
                if (Hud != null) Hud.Toast("Hint: " + Items[i].HintText, 1.8f);
            }
            else
                m_lookingAt = null;
            CloseList();
        }

        private void CloseList()
        {
            if (m_trayOwned && Tray != null && Tray.IsOpen)
                Tray.Close();
            m_trayOwned = false;
        }

        private void OnTrayClosed()
        {
            if (!m_trayOwned)
                return;
            m_trayOwned = false;
            if (m_lookingAt == null)
                FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
        }

        private void RefreshOptions()
        {
            if (m_options == null) return;
            for (int i = 0; i < Items.Length; i++)
            {
                var item = Items[i];
                var o = m_options[i];
                o.Badge = item.Found ? "√" : null;
                o.Price = item.Found ? "√ Found" : "Still hidden";
                o.Detail = item.Found ? item.Brand + "\n" + item.Blurb.Replace("\n", " ") : "Hint: " + item.HintText + ".\nTap the tile to look there.";
            }
        }

        private void RefreshAction()
        {
            if (!ListOpen) return;
            int i = Tray.Selected;
            bool hidden = i >= 0 && i < Items.Length && !Items[i].Found;
            Tray.SetAction(0, hidden ? "Go look ›" : "Keep hunting ›", hidden ? Ui.Accent : Ui.Accent2);
        }

        private void RefreshList(int select)
        {
            if (!ListOpen) return;
            RefreshOptions();
            Tray.SetTitle(ListTitle());
            if (select >= 0) Tray.Select(select, false);
            Tray.Refresh();
            RefreshAction();
        }

        private string ListTitle() => "Lost & Found  ·  " + m_found + "/" + Items.Length + " found";

        private int FirstUnfound()
        {
            for (int i = 0; i < Items.Length; i++)
                if (!Items[i].Found) return i;
            return 0;
        }

        // ---- taps ----

        public void OnItemTapped(NightclubItem item)
        {
            if (!m_playing)
                return;
            int itemIndex = System.Array.IndexOf(Items, item);
            if (item.Found)
            {
                // Found items on the shelf open their checklist entry.
                StartCoroutine(Tween.Punch(item.transform, 0.15f, 0.25f));
                OpenList(itemIndex);
                return;
            }
            int index = m_found;
            m_found++;
            Fx.Play(FxKind.Pickup, item.transform.position + transform.up * 0.05f * transform.lossyScale.y, 0.7f);
            AimPoint = item.transform.position;
            Transform slot = ShelfSlots[Mathf.Min(index, ShelfSlots.Length - 1)];
            item.Collect(slot, false);
            ShowFoundTag(item, slot);
            RefreshBoard();
            UpdateScore();
            if (m_lookingAt != null)
            {
                m_lookingAt = null;
                if (ListOpen) FrameList(); else FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            }

            if (m_found >= Items.Length)
                m_finishRoutine = StartCoroutine(FinishRoutine());
            else if (ListOpen)
                RefreshList(itemIndex);
            else if (Hud != null)
                Hud.Toast("√ " + item.DisplayName + "  ·  " + m_found + "/" + Items.Length, 1.4f);
        }

        public void OnDecoyTapped(NightclubDecoy decoy, RaycastHit hit)
        {
            if (!m_playing)
                return;
            // Hidden items are partly tucked behind props; a tap that lands on the prop right next
            // to an unfound item counts for the item, so occlusion never feels unfair.
            NightclubItem near = NearestUnfound(hit.point, 0.09f * transform.lossyScale.x, true);
            if (near != null)
            {
                OnItemTapped(near);
                return;
            }
            m_wrongTaps++;
            decoy.Hop();
            Fx.Play(FxKind.Poof, hit.point, 0.35f);
            if (Hud != null && Time.time - m_lastWrongToast > 1.5f)
            {
                m_lastWrongToast = Time.time;
                Hud.Toast(s_wrongLines[m_wrongTaps % s_wrongLines.Length], 1.0f);
            }
        }

        private void OnAnyTap(Ray ray, bool hitAny, RaycastHit hit)
        {
            if (!m_playing)
                return;
            if (hitAny && hit.collider != null && hit.collider.transform.IsChildOf(transform))
            {
                AimPoint = hit.point;
                return;
            }
            var floor = new Plane(transform.up, transform.position);
            if (floor.Raycast(ray, out float enter))
            {
                Vector3 local = transform.InverseTransformPoint(ray.GetPoint(enter));
                local.x = Mathf.Clamp(local.x, -1.4f, 1.4f);
                local.z = Mathf.Clamp(local.z, -0.9f, 0.9f);
                AimPoint = transform.TransformPoint(local);
            }
        }

        private NightclubItem NearestUnfound(Vector3 point, float maxDistance, bool useCollider)
        {
            NightclubItem best = null;
            float bestD = maxDistance;
            foreach (var item in Items)
            {
                if (item == null || item.Found) continue;
                Vector3 p = item.transform.position;
                if (useCollider)
                {
                    var col = item.GetComponent<Collider>();
                    if (col != null && col.enabled) p = col.ClosestPoint(point);
                }
                float d = Vector3.Distance(p, point);
                if (d < bestD) { bestD = d; best = item; }
            }
            return best;
        }

        // ---- world tags ----

        // The name pops up over the shelf slot, then shrinks to a √ so eight tags never crowd the edge.
        private void ShowFoundTag(NightclubItem item, Transform slot)
        {
            foreach (var kv in m_tags)
                if (kv.Key != item && kv.Value != null) kv.Value.Set("√", TagFound);
            if (!m_tags.TryGetValue(item, out var tag) || tag == null)
            {
                float y = System.Array.IndexOf(ShelfSlots, slot) % 2 == 0 ? 0.2f : 0.3f;
                m_tags[item] = tag = WorldTag.Create(slot, new Vector3(0f, y, 0f), item.DisplayName, 0.1f, TagFound);
            }
            tag.Set("√ " + item.DisplayName, TagFound);
            tag.Show(true);
            if (m_tagRoutines.TryGetValue(item, out var r) && r != null) StopCoroutine(r);
            m_tagRoutines[item] = StartCoroutine(ShrinkTag(tag, 2.2f));
        }

        private IEnumerator ShrinkTag(WorldTag tag, float delay)
        {
            yield return Tween.PopIn(tag.transform, 0.25f);
            yield return new WaitForSeconds(delay);
            if (tag != null) tag.Set("√", TagFound);
        }

        // ---- UI ----

        private void EnsureScreenUi()
        {
            if (m_screenUi == null)
            {
                // A big "open the list" button for when the tray is hidden; lives at the scene root
                // because overlay canvases ignore the tracked World's transform anyway.
                m_screenUi = new GameObject("Nightclub Screen UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = m_screenUi.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 45;   // under the HUD (toast/result draw on top)
                var scaler = m_screenUi.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;
                m_listButton = Ui.Button(m_screenUi.transform, "", Ui.Accent, () => OpenList(m_lookingAt != null ? System.Array.IndexOf(Items, m_lookingAt) : FirstUnfound()), 46, "List Button");
                Ui.Round(m_listButton.GetComponent<Image>(), 60f);
                m_listButton.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -6f);
                // Bottom-left: clear of the HUD hint above and the Look button at the bottom right.
                Ui.Place((RectTransform)m_listButton.transform, new Vector2(0f, 0f), new Vector2(600, 130), new Vector2(30, 40));
            }
            m_screenUi.SetActive(true);
        }

        private void RefreshBoard()
        {
            if (BoardHeader != null)
                BoardHeader.text = "LOST & FOUND   <color=#FFD54A>" + m_found + " / " + Items.Length + "</color>";
            for (int i = 0; i < Items.Length; i++)
            {
                bool found = Items[i].Found;
                if (BoardIcons != null && i < BoardIcons.Length && BoardIcons[i] != null)
                    BoardIcons[i].color = found ? Items[i].Color : new Color(0.22f, 0.2f, 0.32f, 1f);
                if (BoardLabels != null && i < BoardLabels.Length && BoardLabels[i] != null)
                {
                    BoardLabels[i].text = found ? Items[i].DisplayName : "? ? ?";
                    BoardLabels[i].color = found ? Color.white : new Color(0.6f, 0.58f, 0.75f);
                }
            }
            if (m_listButton != null)
                Ui.SetButtonLabel(m_listButton, "Checklist  ·  " + m_found + "/" + Items.Length + "  ›");
        }

        private void UpdateScore()
        {
            if (Hud != null) Hud.SetScore(m_found + " / " + Items.Length);
        }

        private static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // ---- end state ----

        private IEnumerator FinishRoutine()
        {
            m_playing = false;
            m_endTime = Time.time;
            CloseList();
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            Fx.Play(FxKind.Confetti, transform.TransformPoint(new Vector3(-0.6f, 0.5f, 0f)), 1.4f);
            yield return new WaitForSeconds(0.25f);
            Fx.Play(FxKind.Confetti, transform.TransformPoint(new Vector3(0.6f, 0.5f, 0f)), 1.4f);
            Fx.Play(FxKind.Stars, transform.TransformPoint(new Vector3(0f, 0.55f, 0.6f)), 1.2f);
            yield return new WaitForSeconds(0.9f);
            ShowEndResult();
            m_finishRoutine = null;
        }

        private void ShowEndResult()
        {
            float t = m_endTime - m_startTime;
            if (Hud == null) return;
            Hud.SetTimer(FormatTime(t));
            string hints = HintsUsed == 0 ? "no hints" : HintsUsed + " hint" + (HintsUsed == 1 ? "" : "s");
            Hud.ShowResult("Set-list unlocked!",
                "All " + Items.Length + " items found in " + FormatTime(t) +
                "\n" + hints + " · " + (m_wrongTaps == 0 ? "no wrong guesses" : m_wrongTaps + " wrong guess" + (m_wrongTaps == 1 ? "" : "es")) +
                "\n<size=32>Nova Night thanks you · see you on the floor</size>",
                ("Play again", Restart),
                ("Hub", () => { if (ShowcaseHub.Instance != null) ShowcaseHub.Instance.ReturnToHub(); }));
        }

        public override void DebugComplete()
        {
            if (m_finishRoutine != null) { StopCoroutine(m_finishRoutine); m_finishRoutine = null; }
            CloseList();
            for (int i = 0; i < Items.Length; i++)
                if (!Items[i].Found)
                {
                    Transform slot = ShelfSlots[Mathf.Min(m_found, ShelfSlots.Length - 1)];
                    Items[i].Collect(slot, true);
                    ShowFoundTag(Items[i], slot);
                    m_found++;
                }
            if (m_playing) { m_playing = false; m_endTime = Time.time; }
            m_lookingAt = null;
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            RefreshBoard();
            UpdateScore();
            Fx.Play(FxKind.Confetti, transform.TransformPoint(new Vector3(0f, 0.5f, 0f)), 1.4f);
            ShowEndResult();
        }

        // ---- mood ----

        void Update()
        {
            if (m_screenUi != null)
            {
                bool show = m_playing && (Tray == null || !Tray.IsOpen) && (Hud == null || !Hud.ResultVisible);
                if (m_listButton.gameObject.activeSelf != show) m_listButton.gameObject.SetActive(show);
            }

            if (m_playing && Hud != null)
            {
                // Only rebuild the string when the second changes: no per-frame garbage on WebGL.
                int second = Mathf.FloorToInt(Time.time - m_startTime);
                if (second != m_lastTimerSecond)
                {
                    m_lastTimerSecond = second;
                    Hud.SetTimer(FormatTime(second));
                }
            }

            if (Flashlight != null)
            {
                // Slight idle wobble makes the spot feel hand-held.
                Vector3 wobble = (transform.right * (Mathf.PerlinNoise(Time.time * 0.6f, 0f) - 0.5f) +
                                  transform.forward * (Mathf.PerlinNoise(0f, Time.time * 0.6f) - 0.5f)) * 0.12f * transform.lossyScale.x;
                Quaternion want = Quaternion.LookRotation(AimPoint + wobble - Flashlight.transform.position, transform.up);
                Flashlight.transform.rotation = Quaternion.Slerp(Flashlight.transform.rotation, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
            }

            float beat = Time.time * 2.1f;   // ~126 bpm
            if (ClubLights != null)
            {
                if (m_lightBase == null || m_lightBase.Length != ClubLights.Length)
                {
                    m_lightBase = new float[ClubLights.Length];
                    for (int i = 0; i < ClubLights.Length; i++) m_lightBase[i] = ClubLights[i] != null ? ClubLights[i].intensity : 0f;
                }
                for (int i = 0; i < ClubLights.Length; i++)
                    if (ClubLights[i] != null)
                        ClubLights[i].intensity = m_lightBase[i] * (0.75f + 0.25f * Mathf.Abs(Mathf.Sin((beat + i * 0.33f) * Mathf.PI)));
            }

            if (DanceTiles != null && DanceColors != null && DanceColors.Length > 0)
            {
                if (m_block == null) m_block = new MaterialPropertyBlock();
                int step = Mathf.FloorToInt(beat);
                float flash = 1f - (beat - step);
                for (int i = 0; i < DanceTiles.Length; i++)
                {
                    if (DanceTiles[i] == null) continue;
                    int k = (i * 3 + step + (i / 4)) % DanceColors.Length;
                    Color c = Color.Lerp(DanceColors[k] * 0.45f, DanceColors[k], flash * 0.8f);
                    c.a = 1f;
                    m_block.SetColor(s_baseColor, c);
                    DanceTiles[i].SetPropertyBlock(m_block);
                }
            }
        }

        // ---- automated play-through ----

        public override IEnumerator SelfTest()
        {
            Restart();
            yield return new WaitForSeconds(0.7f);
            if (m_found != 0) Debug.LogError("[SELFTEST] expected 0 found after restart, got " + m_found);
            if (Items.Length != 8) Debug.LogError("[SELFTEST] expected 8 items, got " + Items.Length);
            if (!ListOpen) Debug.LogError("[SELFTEST] checklist tray not open on enter");
            else if (Tray.Options.Count != Items.Length) Debug.LogError("[SELFTEST] tray lists " + Tray.Options.Count + " items");
            Snapshot("tray");

            // Hint: pick the third tile like a player would.
            Tray.Select(2);
            yield return new WaitForSeconds(0.8f);
            if (m_lookingAt != Items[2]) Debug.LogError("[SELFTEST] picking a hidden tile did not frame its area");
            if (HintsUsed != 1) Debug.LogError("[SELFTEST] hint count " + HintsUsed + ", expected 1");
            Snapshot("hint");
            Tray.Press(0);   // "Go look ›"
            yield return new WaitForSeconds(0.8f);
            Snapshot("hunt");
            if (Tray.IsOpen) Debug.LogError("[SELFTEST] Go look did not hide the tray");
            if (m_listButton == null || !m_listButton.gameObject.activeInHierarchy) Debug.LogError("[SELFTEST] checklist button not shown with the tray hidden");

            // Two wrong taps on props far away from any item.
            int tapped = 0;
            foreach (var d in Decoys)
            {
                if (d == null || tapped >= 2) continue;
                var col = d.GetComponentInChildren<Collider>();
                Vector3 p = col != null ? col.bounds.center : d.transform.position;
                if (NearestUnfound(p, 0.3f * transform.lossyScale.x, true) != null) continue;
                d.OnTap(new RaycastHit { point = p });
                tapped++;
                yield return new WaitForSeconds(0.4f);
            }
            if (tapped != 2 || m_wrongTaps != 2) Debug.LogError("[SELFTEST] expected 2 wrong taps, got " + m_wrongTaps + " (tapped " + tapped + ")");
            if (m_found != 0) Debug.LogError("[SELFTEST] a wrong tap counted as found");

            for (int i = 0; i < Items.Length; i++)
            {
                var item = Items[i];
                if (i == 4)
                {
                    // Reopen the list from the screen button and find the next one with it open.
                    m_listButton.onClick.Invoke();
                    yield return new WaitForSeconds(0.3f);
                    if (!ListOpen) Debug.LogError("[SELFTEST] checklist button did not open the tray");
                }
                item.OnTap(new RaycastHit { point = item.transform.position });
                yield return new WaitForSeconds(0.5f);
                if (!item.Found) Debug.LogError("[SELFTEST] " + item.DisplayName + " not marked found");
                if (m_found != i + 1) Debug.LogError("[SELFTEST] counter " + m_found + " after " + (i + 1) + " finds");
                if (item.transform.parent != ShelfSlots[i]) Debug.LogError("[SELFTEST] " + item.DisplayName + " not on shelf slot " + i);
                if (!m_tags.TryGetValue(item, out var tag) || tag == null || !tag.gameObject.activeSelf) Debug.LogError("[SELFTEST] no tag over found " + item.DisplayName);
                if (i == 4)
                {
                    if (!ListOpen) Debug.LogError("[SELFTEST] tray closed while finding with it open");
                    else
                    {
                        if (Tray.Selected != 4 || Tray.Options[4].Badge != "√") Debug.LogError("[SELFTEST] tray did not tick the found item");
                        yield return new WaitForSeconds(0.4f);
                        Snapshot("found");
                    }
                    Tray.Close();
                    yield return new WaitForSeconds(0.3f);
                }
                if (i < Items.Length - 1)
                {
                    item.OnTap(new RaycastHit { point = item.transform.position });
                    if (m_found != i + 1) Debug.LogError("[SELFTEST] re-tapping a found item changed the counter");
                    if (!ListOpen || Tray.Selected != i) Debug.LogError("[SELFTEST] tapping a found item did not open its checklist entry");
                    Tray.Close();
                }
            }
            yield return new WaitForSeconds(1.0f);
            for (int i = 0; i < Items.Length; i++)
                if (Items[i].transform.localPosition.sqrMagnitude > 1e-4f)
                    Debug.LogError("[SELFTEST] " + Items[i].DisplayName + " did not land on its shelf slot");
            yield return new WaitForSeconds(0.8f);
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel not shown after finding all items");
            if (Tray.IsOpen) Debug.LogError("[SELFTEST] tray still open on the result");
        }
    }
}
