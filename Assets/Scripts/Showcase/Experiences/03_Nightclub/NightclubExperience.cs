using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Nightclub
{
    // Hidden-Object Nightclub: find 8 lost items in a dim club diorama. Found items fly to the
    // inventory shelf at the front edge, the checklist board on the back wall fills in, and a
    // "flashlight" spot follows the player's taps.
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

        private int m_found;
        private int m_wrongTaps;
        private bool m_playing;
        private float m_startTime, m_endTime;
        private float m_lastWrongToast = -10f, m_lastHint = -10f;
        private Vector3 m_aimLocal;   // flashlight target, local so it follows the tracked target
        private Vector3 AimPoint { get => transform.TransformPoint(m_aimLocal); set => m_aimLocal = transform.InverseTransformPoint(value); }
        private float[] m_lightBase;
        private MaterialPropertyBlock m_block;
        private Coroutine m_finishRoutine, m_cardRoutine;
        private WorldCard m_card;

        private static readonly string[] s_wrongLines = { "Not this one", "Nope, keep looking", "Just club decor", "Not on the list" };
        private static readonly int s_baseColor = Shader.PropertyToID("_BaseColor");

        public int FoundCount => m_found;
        public int WrongTaps => m_wrongTaps;

        protected override void OnEnter()
        {
            ShowcaseInput.Tapped -= OnAnyTap;
            ShowcaseInput.Tapped += OnAnyTap;
            Restart();
        }

        protected override void OnExit()
        {
            ShowcaseInput.Tapped -= OnAnyTap;
            m_playing = false;
            CloseCard();
            if (Hud != null) { Hud.HideResult(); Hud.SetTimer(""); }
        }

        void OnDisable() => ShowcaseInput.Tapped -= OnAnyTap;

        public void Restart()
        {
            if (m_finishRoutine != null) { StopCoroutine(m_finishRoutine); m_finishRoutine = null; }
            CloseCard();
            if (Hud != null) Hud.HideResult();
            foreach (var d in Decoys) if (d != null) d.ResetDecoy();
            foreach (var item in Items) item.ResetItem();
            m_found = 0;
            m_wrongTaps = 0;
            m_lastWrongToast = m_lastHint = -10f;
            m_startTime = Time.time;
            m_playing = true;
            m_aimLocal = new Vector3(0f, 0f, 0.2f);
            if (Flashlight != null)
                Flashlight.transform.rotation = Quaternion.LookRotation(AimPoint - Flashlight.transform.position, transform.up);
            RefreshBoard();
            UpdateScore();
        }

        // ---- taps ----

        public void OnItemTapped(NightclubItem item)
        {
            if (!m_playing || item.Found)
                return;
            int index = m_found;
            m_found++;
            Fx.Play(FxKind.Pickup, item.transform.position + transform.up * 0.05f * transform.lossyScale.y, 0.7f);
            AimPoint = item.transform.position;
            item.Collect(ShelfSlots[Mathf.Min(index, ShelfSlots.Length - 1)], false);
            RefreshBoard();
            UpdateScore();

            if (m_found >= Items.Length)
                m_finishRoutine = StartCoroutine(FinishRoutine());
            else
                ShowItemCard(item, ShelfSlots[Mathf.Min(index, ShelfSlots.Length - 1)]);
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

        public void ShowHint()
        {
            if (!m_playing || Time.time - m_lastHint < 2.5f)
                return;
            m_lastHint = Time.time;
            NightclubItem item = NearestUnfound(AimPoint, float.MaxValue, false);
            if (item == null)
                return;
            AimPoint = item.transform.position;
            item.Pulse();
            if (Hud != null) Hud.Toast("Hint: " + item.HintText, 1.8f);
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

        // ---- UI ----

        private void ShowItemCard(NightclubItem item, Transform slot)
        {
            CloseCard();
            m_card = WorldCard.Show(slot, new Vector3(0f, 0.42f, 0.05f), item.DisplayName, item.Brand + "\n" + item.Blurb, 0.62f);
            if (m_card != null)
            {
                m_card.Title.color = Color.Lerp(item.Color, Ui.Ink, 0.35f);
                m_cardRoutine = StartCoroutine(AutoCloseCard(m_card, 3.2f));
            }
        }

        private IEnumerator AutoCloseCard(WorldCard card, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (card != null && card == m_card)
                CloseCard();
        }

        private void CloseCard()
        {
            if (m_cardRoutine != null) { StopCoroutine(m_cardRoutine); m_cardRoutine = null; }
            if (m_card != null) m_card.Close();
            m_card = null;
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
            CloseCard();
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
            Hud.ShowResult("Set-list unlocked!",
                "All " + Items.Length + " items found in " + FormatTime(t) +
                (m_wrongTaps > 0 ? "\n" + m_wrongTaps + " wrong guess" + (m_wrongTaps == 1 ? "" : "es") : "\nNo wrong guesses!") +
                "\n<size=32>Nova Night thanks you · see you on the floor</size>",
                ("Play again", Restart),
                ("Hub", () => { if (ShowcaseHub.Instance != null) ShowcaseHub.Instance.ReturnToHub(); }));
        }

        public override void DebugComplete()
        {
            if (m_finishRoutine != null) { StopCoroutine(m_finishRoutine); m_finishRoutine = null; }
            CloseCard();
            for (int i = 0; i < Items.Length; i++)
                if (!Items[i].Found) { Items[i].Collect(ShelfSlots[m_found], true); m_found++; }
            if (m_playing) { m_playing = false; m_endTime = Time.time; }
            RefreshBoard();
            UpdateScore();
            Fx.Play(FxKind.Confetti, transform.TransformPoint(new Vector3(0f, 0.5f, 0f)), 1.4f);
            ShowEndResult();
        }

        // ---- mood ----

        void Update()
        {
            if (m_playing && Hud != null)
                Hud.SetTimer(FormatTime(Time.time - m_startTime));

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
            yield return new WaitForSeconds(0.3f);
            if (m_found != 0) Debug.LogError("[SELFTEST] expected 0 found after restart, got " + m_found);
            if (Items.Length != 8) Debug.LogError("[SELFTEST] expected 8 items, got " + Items.Length);

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

            ShowHint();
            yield return new WaitForSeconds(0.6f);

            for (int i = 0; i < Items.Length; i++)
            {
                var item = Items[i];
                item.OnTap(new RaycastHit { point = item.transform.position });
                yield return new WaitForSeconds(0.5f);
                if (!item.Found) Debug.LogError("[SELFTEST] " + item.DisplayName + " not marked found");
                if (m_found != i + 1) Debug.LogError("[SELFTEST] counter " + m_found + " after " + (i + 1) + " finds");
                if (item.transform.parent != ShelfSlots[i]) Debug.LogError("[SELFTEST] " + item.DisplayName + " not on shelf slot " + i);
                item.OnTap(new RaycastHit { point = item.transform.position });
                if (m_found != i + 1) Debug.LogError("[SELFTEST] re-tapping a found item changed the counter");
            }
            yield return new WaitForSeconds(1.0f);
            for (int i = 0; i < Items.Length; i++)
                if (Items[i].transform.localPosition.sqrMagnitude > 1e-4f)
                    Debug.LogError("[SELFTEST] " + Items[i].DisplayName + " did not land on its shelf slot");
            yield return new WaitForSeconds(0.8f);
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel not shown after finding all items");
        }
    }
}
