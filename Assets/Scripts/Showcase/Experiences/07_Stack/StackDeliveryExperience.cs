using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Stack
{
    // Stack the Delivery: the next item hovers above the pallet sliding left and right; tap anywhere
    // (or the big DROP button in the bottom bar, which also shows the next two items) to drop it.
    // On entry the tray lists the delivery's item kinds; picking one swaps the hovering item. Build the tallest tower before the truck leaves (60 s from the first drop, or after
    // 3 items hit the floor). With 5+ items stacked the tower is loaded onto the truck and driven off.
    // Physics is kept phone-friendly: at most MaxItems live items, items settle on a velocity
    // threshold, and settled items more than FreezeBelowTop levels under the top become kinematic.
    public class StackDeliveryExperience : ShowcaseExperience
    {
        public const float RoundSeconds = 60f;
        public const int MaxMisses = 3;
        public const int MaxItems = 20;
        public const int ShipMinItems = 5;
        public const int FreezeBelowTop = 3;
        public const float CmPerUnit = 250f;      // the printed target is ~3 units ≈ 7.5 m of loading dock
        const string BestKey = "showcase.stack.best";
        const string StartHint = "Tap anywhere or DROP to drop the item.\nStack it high before the truck leaves!";
        const int Lookahead = 2;
        static readonly Color NovaBlue = new Color(0.25f, 0.47f, 0.98f);

        [SerializeField] private StackItem[] m_templates;          // indexed by StackKind
        [SerializeField] private Transform m_cargo;                // pallet + label; loaded onto the truck
        [SerializeField] private BoxCollider m_palletCollider;
        [SerializeField] private Transform m_truck;
        [SerializeField] private Transform[] m_wheels;
        [SerializeField] private Transform m_exhaust;
        [SerializeField] private Vector3 m_bedLocal = new Vector3(-0.12f, 0.17f, 0f);   // truck-local top centre of the bed
        [SerializeField] private Collider[] m_groundColliders;     // touching these = dropped
        [SerializeField] private Transform m_heightMarker;
        [SerializeField] private Text m_heightLabel;
        [SerializeField] private Text m_boardTime;
        [SerializeField] private Text m_boardDrops;
        [SerializeField] private Transform m_dropGuide;
        [SerializeField] private Transform m_dropTarget;
        [SerializeField] private float m_slideHalf = 0.45f;
        [SerializeField] private float m_hoverClearance = 0.24f;
        [SerializeField] private float m_gravity = 4.5f;           // stage units / s² (1 unit ≈ 2.5 m, a bit floaty)

        private readonly List<StackItem> m_all = new List<StackItem>();
        private readonly List<StackItem> m_live = new List<StackItem>();   // Hover/Falling/Settled/Lost
        private readonly List<StackItem> m_free = new List<StackItem>();
        private readonly HashSet<Collider> m_ground = new HashSet<Collider>();
        private readonly List<StackItem> m_sortBuffer = new List<StackItem>();
        private readonly List<StackKind> m_upcoming = new List<StackKind>();   // after the hovering item
        private Transform m_itemRoot;
        private PhysicsMaterial m_grip;

        private bool m_running, m_ended, m_shipping;
        private float m_timeLeft;
        private int m_misses;
        private int m_dropped;
        private StackItem m_hover;
        private float m_nextSpawnAt;
        private float m_slideT;
        private float m_hoverY;
        private float m_forcedX = float.NaN;
        private StackKind m_lastKind = StackKind.NovaBox;
        private float m_palletTop = 0.09f;
        private float m_shownHeight;
        private float m_peakHeight;
        private int m_lastMilestone;
        private int m_lastShownSecond = -1;
        private int m_lastSec = -1, m_lastCm = -1, m_lastMissesShown = -1, m_lastStacked = -1;
        private float m_finalHeight;
        private int m_finalItems;
        private bool m_finalShipped;
        private bool m_truckAway;

        private Vector3 m_cargoPos, m_truckPos, m_markerScale = Vector3.one;
        private Quaternion m_cargoRot, m_truckRot;
        private Vector3 m_cargoScale, m_truckScale = Vector3.one;
        private Coroutine m_endRoutine, m_truckRoutine, m_markerPunch;

        // Screen UI: bottom bar with the next items and DROP; tray on entry. World: tags.
        private RectTransform m_bar;
        private Button m_dropButton;
        private RawImage[] m_nextThumbs;
        private Text m_nextName, m_nextThen;
        private List<TrayOption> m_options;
        private WorldTag m_truckTag, m_heightTag;
        private static ShowcaseTray Tray => ShowcaseTray.Instance;

        public bool Running => m_running;
        public bool Ended => m_ended;
        public bool Shipping => m_shipping;
        public int Misses => m_misses;
        public int LiveCount => m_live.Count;
        public int PooledCount => m_all.Count;
        public bool CanDrop => !m_ended && m_hover != null;
        public bool BarVisible => m_bar != null && m_bar.gameObject.activeSelf;
        public StackKind? HoverKind => m_hover != null ? m_hover.Kind : (StackKind?)null;
        public StackKind NextKind(int i) { FillUpcoming(); return m_upcoming[i]; }
        public float HeightCm => TowerHeight() * CmPerUnit;
        public int StackedCount
        {
            get
            {
                int n = 0;
                foreach (StackItem it in m_live) if (it.State == StackState.Settled) n++;
                return n;
            }
        }

        void Awake()
        {
            if (m_cargo != null)
            {
                m_cargoPos = m_cargo.localPosition;
                m_cargoRot = m_cargo.localRotation;
                m_cargoScale = m_cargo.localScale;
            }
            if (m_truck != null)
            {
                m_truckPos = m_truck.localPosition;
                m_truckRot = m_truck.localRotation;
                m_truckScale = m_truck.localScale;
            }
            if (m_heightMarker != null) m_markerScale = m_heightMarker.localScale;

            m_grip = new PhysicsMaterial("Stack Grip")
            {
                dynamicFriction = 0.8f,
                staticFriction = 0.95f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            if (m_palletCollider != null) m_palletCollider.sharedMaterial = m_grip;
            if (m_groundColliders != null)
                foreach (Collider c in m_groundColliders)
                    if (c != null) m_ground.Add(c);
        }

        void OnEnable() => ShowcaseInput.Tapped += OnTapped;
        void OnDisable() => ShowcaseInput.Tapped -= OnTapped;

        protected override void OnEnter()
        {
            EnsureUi();
            Restart();
            OpenTray();
        }

        protected override void OnExit()
        {
            m_running = false;
            m_ended = true;
            m_shipping = false;
            if (Tray != null)
            {
                Tray.Closed -= OnTrayClosed;
                if (m_options != null && Tray.IsOpen) Tray.Close();
            }
            m_options = null;
            Cleanup();
            ResetStage(false);
            UpdateBar();
        }

        // ---- Tray: the delivery manifest. Picking a kind swaps the hovering item to it. ----

        private static string KindName(StackKind k)
        {
            switch (k)
            {
                case StackKind.Crate: return "Wooden crate";
                case StackKind.Box: return "Parcel";
                case StackKind.Barrel: return "Oil barrel";
                case StackKind.Basket: return "Berry basket";
                case StackKind.AppleCrate: return "Apple crate";
                default: return "Nova box";
            }
        }

        private static string KindDetail(StackKind k)
        {
            switch (k)
            {
                case StackKind.Crate: return "Flat and sturdy: a great base.";
                case StackKind.Box: return "Light parcel. Easy to land.";
                case StackKind.Barrel: return "Heavy and round: it rolls, so drop it dead centre.";
                case StackKind.Basket: return "Light, wobbly top. Best near the top.";
                case StackKind.AppleCrate: return "Big and heavy: put it low.";
                default: return "Our own box. Survives anything!";
            }
        }

        public void OpenTray()
        {
            if (Tray == null || m_templates == null)
                return;
            m_options = new List<TrayOption>();
            for (int i = 0; i < m_templates.Length; i++)
            {
                StackItem t = m_templates[i];
                var k = (StackKind)i;
                m_options.Add(new TrayOption(KindName(k), t != null ? (t.Mass * 10f).ToString("0") + " kg" : "", KindDetail(k), t != null ? t.gameObject : null));
            }
            int sel = m_hover != null ? (int)m_hover.Kind : 0;
            int best = BestCm;
            Tray.Open("Today's delivery" + (best > 0 ? "  ·  Best " + best + " cm" : "  ·  60 s"), m_options, sel, i => SwapHover((StackKind)i),
                ("Start stacking  ▼", CloseTray));
            Tray.Closed -= OnTrayClosed;
            Tray.Closed += OnTrayClosed;
            UpdateBar();
        }

        public void CloseTray()
        {
            if (Tray != null && m_options != null && Tray.IsOpen)
                Tray.Close();
            else
                OnTrayClosed();
        }

        private void OnTrayClosed()
        {
            if (Tray != null) Tray.Closed -= OnTrayClosed;
            m_options = null;
            UpdateBar();
        }

        private bool TrayOpen => m_options != null && Tray != null && Tray.IsOpen;

        // Replaces the hovering item with `kind` (tray pick), keeping the queue behind it.
        public void SwapHover(StackKind kind)
        {
            if (m_ended || m_running)
                return;
            if (m_hover != null)
            {
                Fx.Play(FxKind.Poof, m_hover.transform.position, 0.45f);
                Recycle(m_hover);
            }
            m_upcoming.Insert(0, kind);
            SpawnHover();
        }

        // Fresh round: empty pallet, truck parked, waiting for the first drop (which starts the clock).
        public void Restart()
        {
            if (Hud != null) Hud.HideResult();
            WorldCard.CloseOpen();
            Cleanup();
            bool truckWasAway = m_truckAway;
            ResetStage(truckWasAway);
            m_running = false;
            m_ended = false;
            m_shipping = false;
            m_timeLeft = RoundSeconds;
            m_misses = 0;
            m_dropped = 0;
            m_slideT = m_slideHalf;     // start in the middle
            m_forcedX = float.NaN;
            m_upcoming.Clear();
            m_peakHeight = 0f;
            m_shownHeight = 0f;
            m_finalHeight = 0f;
            m_finalItems = 0;
            m_lastMilestone = 0;
            m_lastShownSecond = -1;
            m_palletTop = PalletTop();
            m_hoverY = m_palletTop + m_hoverClearance + 0.1f;
            m_nextSpawnAt = Time.time + (truckWasAway ? 0.9f : 0.3f);
            if (Hud != null)
            {
                Hud.SetHint(StartHint);
                Hud.SetTimer(FormatTime(RoundSeconds));
            }
            UpdateHud(true);
            UpdateHeightUi(true);
            UpdateBar();
        }

        private void ResetStage(bool driveTruckIn)
        {
            if (m_endRoutine != null) StopCoroutine(m_endRoutine);
            if (m_truckRoutine != null) StopCoroutine(m_truckRoutine);
            if (m_markerPunch != null) StopCoroutine(m_markerPunch);
            m_endRoutine = m_truckRoutine = m_markerPunch = null;
            // Punch tweens stopped mid-way (Exit / Play again during loading) would leave these squashed.
            if (m_heightMarker != null) m_heightMarker.localScale = m_markerScale;
            if (m_cargo != null)
            {
                m_cargo.SetParent(transform, false);
                m_cargo.localPosition = m_cargoPos;
                m_cargo.localRotation = m_cargoRot;
                m_cargo.localScale = m_cargoScale;
                m_cargo.gameObject.SetActive(true);
            }
            if (m_truck != null)
            {
                m_truck.gameObject.SetActive(true);
                m_truck.localRotation = m_truckRot;
                m_truck.localScale = m_truckScale;
                if (driveTruckIn && isActiveAndEnabled)
                {
                    m_truck.localPosition = m_truckPos + Vector3.right * 2.4f;
                    m_truckRoutine = StartCoroutine(DriveTruckIn());
                }
                else
                    m_truck.localPosition = m_truckPos;
            }
            m_truckAway = false;
            if (m_truckTag != null) m_truckTag.Show(true);
            if (m_dropGuide != null) m_dropGuide.gameObject.SetActive(false);
            if (m_dropTarget != null) m_dropTarget.gameObject.SetActive(false);
        }

        private void OnTapped(Ray ray, bool hitAny, RaycastHit hit)
        {
            if (!isActiveAndEnabled || m_ended || TrayOpen)
                return;
            if (hitAny && hit.collider != null && hit.collider.GetComponentInParent<ITappable>() != null)
                return;   // the info tag handles its own taps
            Drop();
        }

        private void StartRound()
        {
            m_running = true;
            m_timeLeft = RoundSeconds;
            if (Hud != null)
            {
                Hud.Toast("GO! The truck leaves in 60 s");
                Hud.SetHint("Tap to drop · items that hit the floor are lost (3 max)");
            }
        }

        // Releases the hovering item straight down. Same path for taps and the self test.
        public bool Drop()
        {
            if (m_ended || m_hover == null)
                return false;
            if (!m_running)
                StartRound();

            StackItem item = m_hover;
            m_hover = null;
            SetColliders(item, true);
            item.Body.isKinematic = false;
            item.Body.interpolation = RigidbodyInterpolation.Interpolate;
            item.Body.linearVelocity = -transform.up * 0.25f * Scale;
            item.Body.angularVelocity = Vector3.zero;
            item.State = StackState.Falling;
            item.DropTime = Time.time;
            item.StillTime = 0f;
            item.Landed = false;
            m_dropped++;
            m_nextSpawnAt = Time.time + 0.75f;
            Fx.Play(FxKind.Poof, item.transform.position + transform.up * 0.08f * Scale, 0.35f);
            Animate(item, Squash(item, 0.12f, 0.2f));
            if (m_dropGuide != null) m_dropGuide.gameObject.SetActive(false);
            if (m_dropTarget != null) m_dropTarget.gameObject.SetActive(false);
            UpdateBar();
            return true;
        }

        void Update()
        {
            if (m_shipping)
                return;

            if (m_running)
            {
                m_timeLeft -= Time.deltaTime;
                if (m_timeLeft <= 0f)
                {
                    m_timeLeft = 0f;
                    EndRound("Time's up! The truck is leaving…");
                }
            }

            UpdateItems();
            if (!m_ended)
            {
                if (m_hover == null && Time.time >= m_nextSpawnAt)
                    SpawnHover();
                UpdateHover();
            }
            UpdateHeightUi(false);
            UpdateHud(false);
        }

        void FixedUpdate()
        {
            Vector3 g = -transform.up * m_gravity * Scale;
            foreach (StackItem it in m_live)
                if (it.Body != null && !it.Body.isKinematic)
                    it.Body.AddForce(g, ForceMode.Acceleration);
        }

        private float Scale => Mathf.Max(transform.lossyScale.y, 1e-4f);

        // ---------- items ----------

        private void UpdateItems()
        {
            float dt = Time.deltaTime;
            bool settledAny = false;
            for (int i = m_live.Count - 1; i >= 0; i--)
            {
                StackItem it = m_live[i];
                if (it == null) { m_live.RemoveAt(i); continue; }
                if (it.State != StackState.Falling && it.State != StackState.Settled)
                    continue;
                if (transform.InverseTransformPoint(it.transform.position).y < -0.6f)
                {
                    MarkLost(it);
                    continue;
                }
                if (it.Frozen || it.Body.isKinematic)
                    continue;

                float v = it.Body.linearVelocity.magnitude / Scale;
                float w = it.Body.angularVelocity.magnitude;
                if (it.State == StackState.Falling)
                {
                    bool still = Time.time - it.DropTime > 0.25f && v < 0.06f && w < 0.6f;
                    it.StillTime = still ? it.StillTime + dt : 0f;
                    if (it.StillTime > 0.3f)
                    {
                        it.State = StackState.Settled;
                        settledAny = true;
                        OnSettled(it);
                    }
                }
                else if (v > 0.35f)
                {
                    it.State = StackState.Falling;   // knocked loose; has to settle again
                    it.StillTime = 0f;
                }
            }
            if (settledAny)
                FreezeLowerLevels();
        }

        public void OnItemCollision(StackItem it, Collision c)
        {
            if (it.State != StackState.Falling && it.State != StackState.Settled)
                return;
            if (m_ground.Contains(c.collider))
            {
                MarkLost(it);
                return;
            }
            if (!it.Landed && it.State == StackState.Falling)
            {
                it.Landed = true;
                Vector3 p = c.contactCount > 0 ? c.GetContact(0).point : it.transform.position;
                Fx.Play(FxKind.Hit, p, 0.45f);
                Animate(it, Squash(it, 0.22f, 0.28f));
            }
        }

        private void OnSettled(StackItem it)
        {
            float h = TowerHeight();
            if (h > m_peakHeight) m_peakHeight = h;
            Fx.Play(FxKind.Sparkle, it.transform.position + transform.up * 0.1f * Scale, 0.5f);
            if (m_heightMarker != null && isActiveAndEnabled)
            {
                if (m_markerPunch != null) StopCoroutine(m_markerPunch);
                m_heightMarker.localScale = m_markerScale;
                m_markerPunch = StartCoroutine(Tween.Punch(m_heightMarker, 0.3f, 0.3f));
            }
            int milestone = Mathf.FloorToInt(h * CmPerUnit / 100f);
            if (m_running && milestone > m_lastMilestone)
            {
                m_lastMilestone = milestone;
                if (Hud != null) Hud.Toast(milestone * 100 + " cm high!");
                Fx.Play(FxKind.Stars, it.transform.position + transform.up * 0.15f * Scale, 0.8f);
            }
        }

        // Settled items more than FreezeBelowTop levels under the top become kinematic, so a tall
        // tower doesn't jitter or slowly slide apart on phones.
        private void FreezeLowerLevels()
        {
            m_sortBuffer.Clear();
            foreach (StackItem it in m_live)
                if (it.State == StackState.Settled) m_sortBuffer.Add(it);
            m_sortBuffer.Sort((a, b) => TopLocal(b).CompareTo(TopLocal(a)));
            for (int i = FreezeBelowTop + 1; i < m_sortBuffer.Count; i++)
            {
                StackItem it = m_sortBuffer[i];
                if (it.Frozen) continue;
                it.Frozen = true;
                it.Body.linearVelocity = Vector3.zero;
                it.Body.angularVelocity = Vector3.zero;
                it.Body.isKinematic = true;
            }
        }

        private void MarkLost(StackItem it)
        {
            if (it.State == StackState.Lost || it.State == StackState.Pooled)
                return;
            it.State = StackState.Lost;
            Fx.Play(FxKind.Hit, it.transform.position, 0.6f);
            if (!m_ended)
            {
                m_misses++;
                if (Hud != null) Hud.Toast(m_misses >= MaxMisses ? "Dropped! 3 / 3" : "Dropped!  " + m_misses + " / " + MaxMisses);
                UpdateHud(true);
                if (m_misses >= MaxMisses)
                    EndRound("3 drops: the truck is leaving!");
            }
            if (isActiveAndEnabled)
                StartCoroutine(RemoveLost(it));
        }

        private IEnumerator RemoveLost(StackItem it)
        {
            yield return new WaitForSeconds(0.9f);
            if (it == null || it.State != StackState.Lost) yield break;
            Fx.Play(FxKind.Poof, it.transform.position, 0.5f);
            Recycle(it);
        }

        // Self test: the next items to come, in order.
        public void SetUpcoming(params StackKind[] kinds)
        {
            m_upcoming.Clear();
            m_upcoming.AddRange(kinds);
            UpdateBar();
        }

        private void FillUpcoming()
        {
            while (m_upcoming.Count < Lookahead + 1)
                m_upcoming.Add(RandomKind());
        }

        private StackKind PickKind()
        {
            FillUpcoming();
            StackKind k = m_upcoming[0];
            m_upcoming.RemoveAt(0);
            FillUpcoming();
            return k;
        }

        private StackKind RandomKind()
        {
            StackKind k;
            int guard = 0;
            do
            {
                float r = Random.value * 100f;
                k = r < 24f ? StackKind.Crate : r < 44f ? StackKind.Box : r < 60f ? StackKind.NovaBox :
                    r < 74f ? StackKind.Barrel : r < 88f ? StackKind.AppleCrate : StackKind.Basket;
            } while (k == m_lastKind && (k == StackKind.Barrel || k == StackKind.Basket) && ++guard < 5);
            m_lastKind = k;
            return k;
        }

        private void SpawnHover()
        {
            if (m_live.Count >= MaxItems)
            {
                EndRound("Truck's full! Time to ship.");
                return;
            }
            StackKind kind = PickKind();
            StackItem item = Take(kind);
            if (item == null)
                return;
            item.State = StackState.Hover;
            item.Frozen = false;
            item.Landed = false;
            item.Body.isKinematic = true;
            item.Body.interpolation = RigidbodyInterpolation.None;   // moved by transform while hovering
            SetColliders(item, false);
            float yaw = (kind == StackKind.Barrel || kind == StackKind.AppleCrate) && Random.value < 0.35f ? 90f : 0f;
            item.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float half = HalfHeight(item);
            m_hoverY = Mathf.Max(m_hoverY, TowerTop(true) + m_hoverClearance + half);
            item.transform.localPosition = new Vector3(SlideX(), m_hoverY, 0f);
            item.gameObject.SetActive(true);
            m_live.Add(item);
            m_hover = item;
            item.Visual.localScale = item.VisualScale * 0.01f;
            Animate(item, Tween.ScaleTo(item.Visual, item.VisualScale, 0.25f));
            UpdateBar();
        }

        private float SlideX()
        {
            if (!float.IsNaN(m_forcedX))
                return m_forcedX;
            return Mathf.PingPong(m_slideT, 2f * m_slideHalf) - m_slideHalf;
        }

        private void UpdateHover()
        {
            if (m_hover == null)
                return;
            float speed = Mathf.Min(0.42f + 0.05f * StackedCount, 1f);
            m_slideT += Time.deltaTime * speed;
            float target = TowerTop(true) + m_hoverClearance + HalfHeight(m_hover);
            m_hoverY = Mathf.Lerp(m_hoverY, target, 1f - Mathf.Exp(-5f * Time.deltaTime));
            float bob = 0.012f * Mathf.Sin(Time.time * 5f);
            m_hover.transform.localPosition = new Vector3(SlideX(), m_hoverY + bob, 0f);

            // Drop guide: thin line from the item to where it will land, plus a target disc.
            if (m_dropGuide == null || m_dropTarget == null)
                return;
            Vector3 from = m_hover.transform.position - transform.up * HalfHeight(m_hover) * Scale;
            if (Physics.Raycast(from, -transform.up, out RaycastHit hit, 5f * Scale, ~0, QueryTriggerInteraction.Ignore))
            {
                Vector3 a = transform.InverseTransformPoint(from);
                Vector3 b = transform.InverseTransformPoint(hit.point);
                float len = Mathf.Max(0.001f, a.y - b.y);
                m_dropGuide.gameObject.SetActive(true);
                m_dropTarget.gameObject.SetActive(true);
                m_dropGuide.localPosition = new Vector3(a.x, (a.y + b.y) * 0.5f, a.z);
                m_dropGuide.localScale = new Vector3(0.008f, len * 0.5f, 0.008f);
                m_dropTarget.localPosition = new Vector3(a.x, b.y + 0.004f, a.z);
                bool onTower = !m_ground.Contains(hit.collider);
                float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 8f);
                m_dropTarget.localScale = new Vector3(0.11f * pulse, 0.003f, 0.11f * pulse) * (onTower ? 1f : 0.7f);
            }
            else
            {
                m_dropGuide.gameObject.SetActive(false);
                m_dropTarget.gameObject.SetActive(false);
            }
        }

        // ---------- pool ----------

        private StackItem Take(StackKind kind)
        {
            if (m_templates == null || (int)kind >= m_templates.Length || m_templates[(int)kind] == null)
                return null;
            StackItem item = m_free.Find(f => f.Kind == kind);
            if (item != null)
            {
                m_free.Remove(item);
                return item;
            }
            if (m_itemRoot == null)
            {
                m_itemRoot = new GameObject("Stack Items").transform;
                m_itemRoot.SetParent(transform, false);
            }
            StackItem template = m_templates[(int)kind];
            GameObject go = Instantiate(template.gameObject, m_itemRoot);
            go.name = template.name.Replace("Template ", "") + " (pooled)";
            item = go.GetComponent<StackItem>();
            item.Owner = this;
            item.Box = go.GetComponent<BoxCollider>();
            if (item.Box != null) item.Box.sharedMaterial = m_grip;
            if (item.Visual == null) item.Visual = go.transform;
            item.VisualScale = item.Visual.localScale;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = item.Mass;
            rb.useGravity = false;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 12;
            rb.solverVelocityIterations = 4;
            rb.maxDepenetrationVelocity = 0.6f * Scale;
            rb.isKinematic = true;
            item.Body = rb;
            m_all.Add(item);
            return item;
        }

        private void Recycle(StackItem it)
        {
            if (it == null)
                return;
            if (it.Anim != null) StopCoroutine(it.Anim);
            it.Anim = null;
            if (m_hover == it) m_hover = null;
            it.State = StackState.Pooled;
            it.Frozen = false;
            if (it.Body != null)
            {
                if (!it.Body.isKinematic)
                {
                    it.Body.linearVelocity = Vector3.zero;
                    it.Body.angularVelocity = Vector3.zero;
                }
                it.Body.isKinematic = true;
            }
            if (it.Visual != null) it.Visual.localScale = it.VisualScale;
            if (m_itemRoot != null && it.transform.parent != m_itemRoot) it.transform.SetParent(m_itemRoot, true);
            it.gameObject.SetActive(false);
            m_live.Remove(it);
            if (!m_free.Contains(it)) m_free.Add(it);
        }

        private static void SetColliders(StackItem it, bool on)
        {
            foreach (Collider c in it.GetComponentsInChildren<Collider>(true))
                c.enabled = on;
        }

        private void Cleanup()
        {
            foreach (StackItem it in m_all)
                if (it != null) Destroy(it.gameObject);
            m_all.Clear();
            m_live.Clear();
            m_free.Clear();
            m_hover = null;
        }

        // ---------- measuring (all in stage-local units, robust to a tilted/scaled World) ----------

        private float TopLocal(StackItem it) => BoxExtentY(it.Box, true);

        private float HalfHeight(StackItem it)
        {
            if (it.Box == null) return 0.1f;
            return (BoxExtentY(it.Box, true) - BoxExtentY(it.Box, false)) * 0.5f;
        }

        private float BoxExtentY(BoxCollider box, bool top)
        {
            if (box == null) return 0f;
            Transform t = box.transform;
            float best = top ? float.MinValue : float.MaxValue;
            Vector3 e = box.size * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = box.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                float y = transform.InverseTransformPoint(t.TransformPoint(c)).y;
                best = top ? Mathf.Max(best, y) : Mathf.Min(best, y);
            }
            return best;
        }

        private float PalletTop() => m_palletCollider != null ? BoxExtentY(m_palletCollider, true) : 0.09f;

        // Highest top of the tower (settled items; with includeFalling also items still moving).
        private float TowerTop(bool includeFalling)
        {
            float top = m_palletTop;
            foreach (StackItem it in m_live)
                if (it.State == StackState.Settled || (includeFalling && it.State == StackState.Falling))
                    top = Mathf.Max(top, TopLocal(it));
            return top;
        }

        private float TowerHeight() => Mathf.Max(0f, TowerTop(false) - m_palletTop);

        // ---------- UI ----------

        private void UpdateHeightUi(bool snap)
        {
            float h = m_shipping ? m_shownHeight : TowerHeight();
            m_shownHeight = snap ? h : Mathf.Lerp(m_shownHeight, h, 1f - Mathf.Exp(-8f * Time.deltaTime));
            if (m_heightMarker != null)
            {
                Vector3 p = m_heightMarker.localPosition;
                p.y = m_palletTop + m_shownHeight;
                m_heightMarker.localPosition = p;
            }
            string label = Mathf.RoundToInt(m_shownHeight * CmPerUnit) + " cm";
            if (m_heightLabel != null && m_heightLabel.text != label) m_heightLabel.text = label;
            if (m_heightTag != null && m_heightTag.Text.text != label) m_heightTag.Set(label);
        }

        private void UpdateHud(bool force)
        {
            int sec = Mathf.CeilToInt(Mathf.Max(0f, m_timeLeft));
            // Once shipped, the tower rides the truck: keep showing the measured final height.
            int cm = Mathf.RoundToInt((m_ended && m_finalHeight > 0f ? m_finalHeight : TowerHeight()) * CmPerUnit);
            int stacked = StackedCount;
            // Compared as ints: building a key string every frame is per-frame garbage on WebGL.
            if (!force && sec == m_lastSec && cm == m_lastCm && m_misses == m_lastMissesShown && stacked == m_lastStacked)
                return;
            m_lastSec = sec;
            m_lastCm = cm;
            m_lastMissesShown = m_misses;
            m_lastStacked = stacked;
            string time = FormatTime(sec);
            bool urgent = m_running && sec <= 10;
            if (Hud != null)
            {
                Hud.SetTimer(urgent ? "<color=#FF7361>" + time + "</color>" : time);
                Hud.SetScore(cm + " cm");
            }
            if (m_boardTime != null)
                m_boardTime.text = m_ended ? "GONE" : urgent ? "<color=#FF7361>" + time + "</color>" : time;
            if (m_boardDrops != null)
            {
                string drops = m_misses > 0 ? "<color=#FF7361>" + m_misses + "</color>" : "0";
                m_boardDrops.text = "Stacked " + stacked + "   ·   Drops " + drops + "/" + MaxMisses;
            }
            if (m_truckTag != null)
            {
                bool ready = stacked >= ShipMinItems;
                m_truckTag.Set(ready ? "√ Ready to ship · " + stacked : "Ships at " + ShipMinItems + " · " + stacked + "/" + ShipMinItems,
                    ready ? Ui.Good : new Color(0.13f, 0.12f, 0.2f, 0.88f));
            }
        }

        // ---------- screen bar: next items + DROP ----------

        private void EnsureUi()
        {
            if (m_bar == null)
                BuildBar();
            if (m_truckTag == null && m_truck != null)
                m_truckTag = WorldTag.Create(m_truck, new Vector3(0.05f, 0.66f, 0f), "Ships at " + ShipMinItems, 0.15f);
            if (m_heightTag == null && m_heightMarker != null)
                m_heightTag = WorldTag.Create(m_heightMarker, new Vector3(0.26f, 0.08f, -0.03f), "0 cm", 0.14f, new Color(1f, 0.45f, 0.38f, 0.95f));
        }

        private void BuildBar()
        {
            var go = new GameObject("Next Bar Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;   // under the HUD (50), so toasts and the result draw on top
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = Ui.Round(Ui.Panel(go.transform, Ui.Paper, "Next Bar"), 40f);
            bg.raycastTarget = true;   // taps on the bar never drop through to the scene
            bg.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -4f);
            m_bar = bg.rectTransform;
            m_bar.anchorMin = new Vector2(0f, 0f);
            m_bar.anchorMax = new Vector2(1f, 0f);
            m_bar.pivot = new Vector2(0.5f, 0f);
            // Bottom edge up to just under the HUD hint box (which starts 200 px up).
            m_bar.offsetMin = new Vector2(24f, 22f);
            m_bar.offsetMax = new Vector2(-24f, 192f);

            m_nextThumbs = new RawImage[Lookahead];
            float x = 18f;
            for (int i = 0; i < Lookahead; i++)
            {
                float size = i == 0 ? 140f : 104f;
                var tile = Ui.Round(Ui.Panel(bg.transform, i == 0 ? new Color(0.36f, 0.62f, 1f, 0.25f) : new Color(0.13f, 0.12f, 0.2f, 0.07f), "Next " + i), 26f);
                Ui.Place(tile.rectTransform, new Vector2(0f, 0.5f), new Vector2(size, size), new Vector2(x, i == 0 ? 0f : -8f));
                var raw = Ui.Rect("Thumb", tile.transform).gameObject.AddComponent<RawImage>();
                Ui.Stretch(raw.rectTransform, 6, 6, 6, 6);
                raw.raycastTarget = false;
                if (i > 0) raw.color = new Color(1f, 1f, 1f, 0.75f);
                m_nextThumbs[i] = raw;
                x += size + 12f;
            }
            var head = Ui.Label(bg.transform, "NEXT", 30, Ui.Muted, TextAnchor.LowerLeft, FontStyle.Bold, "Next Label");
            Ui.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(300, 44), new Vector2(x + 6f, -14f));
            m_nextName = Ui.Label(bg.transform, "", 40, Ui.Ink, TextAnchor.MiddleLeft, FontStyle.Bold, "Next Name");
            Ui.Place(m_nextName.rectTransform, new Vector2(0f, 0.5f), new Vector2(300, 56), new Vector2(x + 6f, 2f));
            m_nextName.resizeTextForBestFit = true;
            m_nextName.resizeTextMinSize = 30;
            m_nextName.resizeTextMaxSize = 40;
            m_nextThen = Ui.Label(bg.transform, "", 30, Ui.Muted, TextAnchor.UpperLeft, FontStyle.Normal, "Then");
            Ui.Place(m_nextThen.rectTransform, new Vector2(0f, 0f), new Vector2(300, 44), new Vector2(x + 6f, 14f));

            m_dropButton = Ui.Button(bg.transform, "DROP  ▼", Ui.Accent, () => Drop(), 52, "Drop");
            Ui.Place((RectTransform)m_dropButton.transform, new Vector2(1f, 0.5f), new Vector2(400, 138), new Vector2(-16f, 0f));
            m_bar.gameObject.SetActive(false);
        }

        // Shown while a round can be played and the tray is closed; shows the two items after the hover.
        private void UpdateBar()
        {
            if (m_bar == null)
                return;
            bool show = isActiveAndEnabled && !m_ended && !TrayOpen;
            if (m_bar.gameObject.activeSelf != show) m_bar.gameObject.SetActive(show);
            if (!show)
                return;
            FillUpcoming();
            for (int i = 0; i < m_nextThumbs.Length; i++)
            {
                StackItem t = m_templates[(int)m_upcoming[i]];
                m_nextThumbs[i].texture = t != null ? ModelPreview.Instance.Thumbnail(t.gameObject) : null;
            }
            m_nextName.text = KindName(m_upcoming[0]);
            m_nextThen.text = "then " + KindName(m_upcoming[1]).ToLowerInvariant();
        }

        private static string FormatTime(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // ---------- end of round ----------

        public void EndRound(string reason)
        {
            if (m_ended)
                return;
            m_ended = true;
            m_running = false;
            if (m_hover != null)
            {
                Fx.Play(FxKind.Poof, m_hover.transform.position, 0.5f);
                Recycle(m_hover);
            }
            if (m_dropGuide != null) m_dropGuide.gameObject.SetActive(false);
            if (m_dropTarget != null) m_dropTarget.gameObject.SetActive(false);
            if (Hud != null)
            {
                Hud.SetHint("");
                Hud.Toast(reason, 1.4f);
            }
            UpdateHud(true);
            UpdateBar();
            if (isActiveAndEnabled)
                m_endRoutine = StartCoroutine(EndSequence());
            else
                FinishRound(false);
        }

        private IEnumerator EndSequence()
        {
            // Give anything still falling a moment to land or drop.
            float t0 = Time.time;
            while (Time.time - t0 < 2f && m_live.Exists(i => i.State == StackState.Falling))
                yield return null;
            yield return new WaitForSeconds(0.3f);

            m_finalHeight = TowerHeight();
            m_finalItems = StackedCount;
            m_shownHeight = m_finalHeight;
            m_shipping = true;
            for (int i = m_live.Count - 1; i >= 0; i--)
            {
                StackItem it = m_live[i];
                if (it.State == StackState.Settled)
                {
                    it.Frozen = true;
                    if (!it.Body.isKinematic)
                    {
                        it.Body.linearVelocity = Vector3.zero;
                        it.Body.angularVelocity = Vector3.zero;
                    }
                    it.Body.isKinematic = true;
                    it.Body.interpolation = RigidbodyInterpolation.None;   // rides the truck by transform
                }
                else
                {
                    Fx.Play(FxKind.Poof, it.transform.position, 0.5f);
                    Recycle(it);
                }
            }

            bool ship = m_finalItems >= ShipMinItems && m_cargo != null && m_truck != null;
            if (ship)
            {
                foreach (StackItem it in m_live)
                    it.transform.SetParent(m_cargo, true);
                if (m_truckTag != null) m_truckTag.Show(false);   // the loaded tower would cover it
                if (Hud != null) Hud.Toast("Loading " + m_finalItems + " items!", 1.2f);
                yield return LoadCargo();
            }
            else if (Hud != null)
                Hud.Toast(m_finalItems == 0 ? "The truck left empty!" : "Only " + m_finalItems + " stacked: the truck left without them", 1.6f);

            if (m_truck != null)
                yield return DriveOff(ship);
            FinishRound(ship);
        }

        private IEnumerator LoadCargo()
        {
            Vector3 from = m_cargo.localPosition;
            Vector3 to = transform.InverseTransformPoint(m_truck.TransformPoint(m_bedLocal));
            Fx.Play(FxKind.Smoke, m_cargo.position, 0.6f);
            for (float a = 0f; a < 1f; a += Time.deltaTime / 1.0f)
            {
                float e = a * a * (3f - 2f * a);
                m_cargo.localPosition = Vector3.Lerp(from, to, e) + Vector3.up * 0.28f * 4f * a * (1f - a);
                yield return null;
            }
            m_cargo.localPosition = to;
            m_cargo.SetParent(m_truck, true);
            Fx.Play(FxKind.Stars, m_cargo.position + transform.up * 0.2f * Scale, 1.2f);
            yield return Tween.Punch(m_truck, 0.08f, 0.3f);
        }

        private IEnumerator DriveOff(bool loaded)
        {
            m_truckAway = true;
            Vector3 from = m_truck.localPosition;
            Vector3 to = from + Vector3.right * 2.6f;
            float puff = 0f;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 2.0f)
            {
                float e = a * a;
                Vector3 prev = m_truck.localPosition;
                m_truck.localPosition = Vector3.Lerp(from, to, e);
                SpinWheels((m_truck.localPosition - prev).x);
                puff -= Time.deltaTime;
                if (puff <= 0f && m_exhaust != null)
                {
                    puff = 0.22f;
                    Fx.Play(FxKind.Smoke, m_exhaust.position, 0.45f);
                }
                yield return null;
            }
            m_truck.gameObject.SetActive(false);
            if (loaded) Fx.Play(FxKind.Confetti, transform.TransformPoint(new Vector3(1.2f, 0.5f, 0.4f)), 1.5f);
        }

        private IEnumerator DriveTruckIn()
        {
            Vector3 from = m_truck.localPosition;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.9f)
            {
                Vector3 prev = m_truck.localPosition;
                m_truck.localPosition = Vector3.LerpUnclamped(from, m_truckPos, Tween.EaseOutBack(a));
                SpinWheels((m_truck.localPosition - prev).x);
                yield return null;
            }
            m_truck.localPosition = m_truckPos;
            m_truckRoutine = null;
        }

        private void SpinWheels(float dx)
        {
            if (m_wheels == null) return;
            foreach (Transform w in m_wheels)
                if (w != null) w.Rotate(0f, -dx / 0.07f * Mathf.Rad2Deg, 0f, Space.Self);
        }

        private void FinishRound(bool shipped)
        {
            m_shipping = false;
            m_finalShipped = shipped;
            m_endRoutine = null;
            if (m_finalItems == 0 && m_finalHeight == 0f)
            {
                m_finalHeight = TowerHeight();
                m_finalItems = StackedCount;
            }
            UpdateHud(true);
            ShowResultPanel();
        }

        public int BestCm
        {
            get
            {
                try { return PlayerPrefs.GetInt(BestKey, 0); }
                catch { return 0; }
            }
        }

        private void ShowResultPanel()
        {
            int cm = Mathf.RoundToInt(m_finalHeight * CmPerUnit);
            int best = BestCm;
            bool record = cm > best;
            if (record)
            {
                best = cm;
                try { PlayerPrefs.SetInt(BestKey, best); PlayerPrefs.Save(); }
                catch { }
            }
            if (Hud == null)
                return;
            string title = !m_finalShipped ? "Missed the truck!" : cm >= 300 ? "Skyscraper!" : cm >= 200 ? "Master stacker!" : "Delivered!";
            string body = "Tower <b>" + cm + " cm</b>   ·   " + m_finalItems + " items" +
                          "\nBest " + best + " cm" + (record && cm > 0 ? "  <color=#FF7361>NEW!</color>" : "") +
                          "\n<size=30>" + (m_finalShipped ? "Shipped by Nova Logistics: our boxes survive anything." : "Stack " + ShipMinItems + "+ items to ship your tower.") + "</size>";
            Hud.ShowResult(title, body, ("Play again", Restart), ("Hub", () => { if (ShowcaseHub.Instance != null) ShowcaseHub.Instance.ReturnToHub(); }));
        }

        public override void DebugComplete()
        {
            if (!m_ended)
            {
                EndRound("Time's up! The truck is leaving…");
                return;
            }
            if (!m_shipping && Hud != null && !Hud.ResultVisible)
                ShowResultPanel();
        }

        // ---------- juice ----------

        private void Animate(StackItem it, IEnumerator routine)
        {
            if (!isActiveAndEnabled) return;
            if (it.Anim != null) StopCoroutine(it.Anim);
            if (it.Visual != null) it.Visual.localScale = it.VisualScale;
            it.Anim = StartCoroutine(Run(it, routine));
        }

        private IEnumerator Run(StackItem it, IEnumerator routine)
        {
            yield return routine;
            if (it != null) it.Anim = null;
        }

        private IEnumerator Squash(StackItem it, float amount, float time)
        {
            Transform v = it.Visual;
            Vector3 s = it.VisualScale;
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                if (v == null) yield break;
                float k = 1f + amount * Mathf.Sin(a * Mathf.PI) * (1f - a);
                v.localScale = new Vector3(s.x * k, s.y / k, s.z * k);
                yield return null;
            }
            if (v != null) v.localScale = s;
        }

        // ---------- self test ----------

        private IEnumerator WaitForHover(float timeout)
        {
            float t0 = Time.time;
            while (!CanDrop && Time.time - t0 < timeout)
                yield return null;
            yield return new WaitForSeconds(0.35f);   // let the hover ease to its height
        }

        private IEnumerator WaitForRest(StackItem it, float timeout)
        {
            float t0 = Time.time;
            while (it != null && it.State == StackState.Falling && Time.time - t0 < timeout)
                yield return null;
        }

        public override IEnumerator SelfTest()
        {
            // Entry: the tray shows the delivery manifest; picking a kind swaps the hovering item.
            if (Tray == null) { Debug.LogError("[SELFTEST] no tray"); yield break; }
            Restart();
            OpenTray();
            yield return WaitForHover(4f);
            if (!Tray.IsOpen) Debug.LogError("[SELFTEST] tray not open on entry");
            if (Tray.Options.Count != m_templates.Length) Debug.LogError("[SELFTEST] tray shows " + Tray.Options.Count + " kinds, expected " + m_templates.Length);
            if (BarVisible) Debug.LogError("[SELFTEST] DROP bar visible under the open tray");
            Tray.Select((int)StackKind.NovaBox);   // like tapping the Nova box tile
            yield return WaitForHover(2f);
            if (HoverKind != StackKind.NovaBox) Debug.LogError("[SELFTEST] picking Nova box in the tray hovers " + HoverKind);
            yield return new WaitForSeconds(0.3f);
            Snapshot("tray");
            Tray.Press(0);   // "Start stacking"
            yield return null;
            if (Tray.IsOpen) Debug.LogError("[SELFTEST] tray still open after Start");
            if (!BarVisible) Debug.LogError("[SELFTEST] DROP bar not shown after Start");
            if (m_running || m_ended) Debug.LogError("[SELFTEST] round should wait for the first drop");

            // Five drops at the centre (slider stopped), through the DROP button like a player.
            m_forcedX = 0f;
            Recycle(m_hover);
            SetUpcoming(StackKind.Crate, StackKind.Box, StackKind.NovaBox, StackKind.AppleCrate, StackKind.Crate);
            float lastHeight = 0f;
            for (int i = 0; i < 5; i++)
            {
                yield return WaitForHover(4f);
                if (!CanDrop) { Debug.LogError("[SELFTEST] no hover item to drop (#" + (i + 1) + ")"); break; }
                StackItem item = m_hover;
                StackKind expectedNext = NextKind(0);
                if (m_nextName != null && m_nextName.text != KindName(expectedNext)) Debug.LogError("[SELFTEST] bar shows next '" + m_nextName.text + "', queue says " + expectedNext);
                m_dropButton.onClick.Invoke();   // the big DROP button
                if (m_hover == item || item.State != StackState.Falling) Debug.LogError("[SELFTEST] DROP button did not drop (#" + (i + 1) + ")");
                if (i == 0 && !m_running) Debug.LogError("[SELFTEST] first drop did not start the round");
                yield return WaitForRest(item, 5f);
                yield return new WaitForSeconds(0.2f);
                if (item.State != StackState.Settled) Debug.LogError("[SELFTEST] item #" + (i + 1) + " " + item.Kind + " ended " + item.State + ", expected Settled");
                float h = TowerHeight();
                if (h <= lastHeight + 0.03f) Debug.LogError("[SELFTEST] height did not grow on drop #" + (i + 1) + ": " + lastHeight + " -> " + h);
                lastHeight = h;
                if (i == 2) Snapshot("stack3");
                if (i < 4)
                {
                    yield return WaitForHover(4f);
                    if (HoverKind != expectedNext) Debug.LogError("[SELFTEST] hover is " + HoverKind + " but the bar promised " + expectedNext);
                }
            }
            int stacked = StackedCount;
            if (stacked < 4) Debug.LogError("[SELFTEST] only " + stacked + " items stacked after 5 centred drops");
            if (m_misses != 0) Debug.LogError("[SELFTEST] " + m_misses + " drops counted after centred drops");
            int frozen = m_live.FindAll(it => it.Frozen).Count;
            if (stacked > FreezeBelowTop + 1 && frozen == 0) Debug.LogError("[SELFTEST] no lower level frozen with " + stacked + " stacked");
            if (m_hover != null && m_hover.transform.localPosition.y < TowerTop(false)) Debug.LogError("[SELFTEST] hover item below the tower top");
            Snapshot("stack5");

            // One deliberate miss beside the pallet: hits the floor, counts a drop and is recycled.
            m_forcedX = 0.9f;
            yield return WaitForHover(4f);
            yield return new WaitForSeconds(0.4f);   // hover has to travel to the side
            StackItem miss = m_hover;
            Drop();
            float m0 = Time.time;
            while (m_misses == 0 && Time.time - m0 < 4f)
                yield return null;
            if (m_misses != 1) Debug.LogError("[SELFTEST] missed item: drops " + m_misses + ", expected 1");
            yield return new WaitForSeconds(1.2f);
            if (miss != null && miss.State != StackState.Pooled) Debug.LogError("[SELFTEST] dropped item not recycled (" + miss.State + ")");
            if (m_live.Count > MaxItems) Debug.LogError("[SELFTEST] " + m_live.Count + " live items, cap " + MaxItems);
            m_forcedX = float.NaN;

            float heightCm = HeightCm;
            int items = StackedCount;
            EndRound("Time's up! The truck is leaving…");
            float e0 = Time.time;
            bool shot = false;
            while ((m_shipping || m_endRoutine != null) && Time.time - e0 < 10f)
            {
                if (!shot && m_truckAway) { shot = true; Snapshot("ship"); }
                yield return null;
            }
            if (m_running) Debug.LogError("[SELFTEST] round still running after EndRound");
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel not shown");
            if (m_finalItems < 4) Debug.LogError("[SELFTEST] result has " + m_finalItems + " items");
            if (Mathf.Abs(m_finalHeight * CmPerUnit - heightCm) > 15f) Debug.LogError("[SELFTEST] result height " + m_finalHeight * CmPerUnit + " cm vs measured " + heightCm);
            if (items >= ShipMinItems && !m_finalShipped) Debug.LogError("[SELFTEST] " + items + " items but the truck did not ship them");
            if (m_truck != null && m_truck.gameObject.activeSelf) Debug.LogError("[SELFTEST] truck still parked after the round");
            if (BestCm < Mathf.RoundToInt(m_finalHeight * CmPerUnit)) Debug.LogError("[SELFTEST] best " + BestCm + " cm not saved");
            Debug.Log("[SELFTEST] stack: " + items + " items, " + Mathf.RoundToInt(heightCm) + " cm, shipped " + m_finalShipped + ", pooled " + m_all.Count);

            // Replay resets everything, then exit cleans up.
            Restart();
            yield return new WaitForSeconds(1.2f);
            if (StackedCount != 0 || m_misses != 0 || m_ended) Debug.LogError("[SELFTEST] Play again did not reset the round");
            if (m_truck != null && (m_truck.localPosition - m_truckPos).sqrMagnitude > 1e-4f) Debug.LogError("[SELFTEST] truck not back at the dock");
            if (m_cargo != null && m_cargo.parent != transform) Debug.LogError("[SELFTEST] pallet not back on the stage");

            // Leave the end state on screen: play a short real round and ship it.
            m_forcedX = 0f;
            for (int i = 0; i < ShipMinItems; i++)
            {
                yield return WaitForHover(4f);
                StackItem item = m_hover;
                Drop();
                yield return WaitForRest(item, 5f);
            }
            m_forcedX = float.NaN;
            yield return new WaitForSeconds(0.3f);
            Snapshot("tower");
            EndRound("Time's up! The truck is leaving…");
            e0 = Time.time;
            while ((m_shipping || m_endRoutine != null) && Time.time - e0 < 10f)
                yield return null;
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel not shown after the second round");
        }
    }
}
