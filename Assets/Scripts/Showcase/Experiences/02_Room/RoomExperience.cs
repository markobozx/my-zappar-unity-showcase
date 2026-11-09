using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Room
{
    // Room Makeover: a cut-away living room with five furniture slots. Tap a piece for its card,
    // swap through the Lumo Home range, save favourites to the "My room" board, then finish.
    public class RoomExperience : ShowcaseExperience
    {
        public const string Brand = "Lumo Home";

        [SerializeField] private RoomSlot[] m_slots;
        [SerializeField] private Transform m_board;          // board backing on the back wall; the canvas sits in front of it
        [SerializeField] private Transform m_finish;         // finish plinth root
        [SerializeField] private Vector2 m_boardSize = new Vector2(0.96f, 0.6f);

        private Text[] m_rowNames, m_rowPrices;
        private Text m_total;
        private Transform m_boardCanvas;
        private RoomSlot m_selected;
        private WorldCard m_card;
        private bool m_finished;

        public IReadOnlyList<RoomSlot> Slots => m_slots;
        public int SavedCount => m_slots.Count(s => s.Saved != null);
        public int Total => m_slots.Where(s => s.Saved != null).Sum(s => s.Saved.Price);
        public bool Finished => m_finished;
        public RoomSlot Selected => m_card != null ? m_selected : null;

        public static string Euro(int amount) => "€" + amount.ToString("N0", CultureInfo.InvariantCulture);

        protected override void OnEnter()
        {
            EnsureUi();
            ResetRoom();
        }

        protected override void OnExit()
        {
            m_card = null;
            m_selected = null;
        }

        public void ResetRoom()
        {
            WorldCard.CloseOpen();
            m_card = null;
            m_selected = null;
            m_finished = false;
            foreach (var s in m_slots)
                s.ResetSlot();
            if (Hud != null)
                Hud.HideResult();
            RefreshBoard();
        }

        // ---- Interaction (taps and card buttons all come through here) ----

        public void Select(RoomSlot slot)
        {
            if (m_finished || slot == null)
                return;
            m_selected = slot;
            StartCoroutine(Tween.Punch(slot.transform, 0.12f));
            Fx.Play(FxKind.Sparkle, slot.transform.position + slot.transform.up * 0.15f, 0.6f);
            m_card = WorldCard.Show(slot.transform, slot.CardOffset, slot.Current.Name, CardBody(slot), 0.82f,
                ("◄ Prev", () => Cycle(slot, -1)),
                ("Next ►", () => Cycle(slot, 1)),
                (SaveLabel(slot), () => Save(slot)));
        }

        public void Cycle(RoomSlot slot, int step)
        {
            if (m_finished)
                return;
            Fx.Play(FxKind.Poof, slot.transform.position + slot.transform.up * 0.12f, 0.8f);
            slot.Cycle(step);
            RefreshCard(slot);
        }

        public void Save(RoomSlot slot)
        {
            if (m_finished)
                return;
            if (slot.IsCurrentSaved)
            {
                Hud?.Toast("Already in your room ♥");
                return;
            }
            bool replaced = slot.Saved != null;
            slot.Save();
            Fx.Play(FxKind.Heart, slot.transform.position + slot.transform.up * 0.3f, 0.8f);
            if (m_boardCanvas != null)
            {
                Fx.Play(FxKind.Sparkle, m_boardCanvas.position, 0.7f);
                StartCoroutine(Tween.Punch(m_boardCanvas, 0.08f));
            }
            Hud?.Toast((replaced ? "Swapped in " : "♥ Saved ") + slot.Current.Name);
            RefreshBoard();
            RefreshCard(slot);
        }

        public void Finish()
        {
            if (m_finished)
                return;
            if (SavedCount == 0)
            {
                Hud?.Toast("Save at least one piece first ♥");
                if (m_finish != null) StartCoroutine(Tween.Punch(m_finish, 0.1f));
                return;
            }
            m_finished = true;
            WorldCard.CloseOpen();
            m_card = null;
            if (m_finish != null)
            {
                StartCoroutine(Tween.Punch(m_finish, 0.15f));
                Fx.Play(FxKind.Confetti, m_finish.position + m_finish.up * 0.1f, 1.2f);
            }
            Fx.Play(FxKind.Stars, transform.TransformPoint(new Vector3(0f, 0.4f, 0.2f)), 1.4f);
            StartCoroutine(ShowResultSoon());
        }

        private IEnumerator ShowResultSoon()
        {
            yield return new WaitForSeconds(0.6f);
            ShowResultNow();
        }

        private void ShowResultNow()
        {
            if (Hud == null)
                return;
            var names = m_slots.Where(s => s.Saved != null).Select(s => s.Saved.Name);
            string verdict = SavedCount == m_slots.Length ? "Fully furnished — looking great!" : SavedCount + " of " + m_slots.Length + " pieces chosen";
            Hud.ShowResult("Your " + Brand + " room",
                string.Join(" · ", names) + "\n<b>Total " + Euro(Total) + "</b>\n<size=30>" + verdict + "</size>",
                ("Play again", ResetRoom),
                ("Hub", () => ShowcaseHub.Instance?.ReturnToHub()));
        }

        // ---- Card & board ----

        private static string CardBody(RoomSlot slot)
        {
            return Brand + " · <b>" + Euro(slot.Current.Price) + "</b>   <size=26>" + (slot.Index + 1) + "/" + slot.Variants.Length + "</size>\n" + slot.Current.Material;
        }

        private static string SaveLabel(RoomSlot slot) => slot.IsCurrentSaved ? "♥ Saved" : "♥ Save";

        private void RefreshCard(RoomSlot slot)
        {
            if (m_card == null || m_selected != slot)
                return;
            m_card.Title.text = slot.Current.Name;
            m_card.Body.text = CardBody(slot);
            var save = m_card.GetComponentsInChildren<Button>().LastOrDefault(b => b.name != "Close");
            if (save != null)
            {
                Ui.SetButtonLabel(save, SaveLabel(slot));
                save.GetComponent<Image>().color = slot.IsCurrentSaved ? Ui.Good : Ui.Accent2;
            }
        }

        private void RefreshBoard()
        {
            if (m_rowNames != null)
            {
                for (int i = 0; i < m_slots.Length; i++)
                {
                    var saved = m_slots[i].Saved;
                    m_rowNames[i].text = saved != null ? "♥ " + saved.Name : m_slots[i].Category + "  <size=34>—  tap to choose</size>";
                    m_rowNames[i].color = saved != null ? Ui.Ink : Ui.Muted;
                    m_rowPrices[i].text = saved != null ? Euro(saved.Price) : "";
                }
                m_total.text = Euro(Total);
            }
            Hud?.SetScore("♥ " + SavedCount + "/" + m_slots.Length + " · " + Euro(Total));
        }

        public string BoardTotalText => m_total != null ? m_total.text : "";

        // World UI is built once on first entry and kept; ResetRoom clears its contents.
        private void EnsureUi()
        {
            if (m_boardCanvas == null && m_board != null)
                BuildBoard();
            if (m_finish != null && m_finish.Find("Label") == null)
                BuildFinishLabel();
        }

        private void BuildBoard()
        {
            const float w = 900f;
            float h = w * m_boardSize.y / m_boardSize.x;
            var canvas = Ui.WorldCanvas("Board Canvas", m_board.parent, new Vector2(w, h), m_boardSize.x);
            canvas.transform.localPosition = m_board.localPosition - new Vector3(0f, 0f, m_board.localScale.z * 0.5f + 0.004f);
            canvas.transform.localRotation = Quaternion.identity;
            m_boardCanvas = canvas.transform;

            var bg = Ui.Panel(canvas.transform, Ui.Paper, "Background");
            bg.raycastTarget = false;
            Ui.Stretch(bg.rectTransform);
            var stripe = Ui.Panel(bg.transform, Ui.Accent, "Stripe");
            stripe.raycastTarget = false;
            Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(w, 16), Vector2.zero);

            var title = Ui.Label(bg.transform, "My room", 64, Ui.Ink, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
            Ui.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(420, 84), new Vector2(34, -26));
            var brand = Ui.Label(bg.transform, Brand, 36, Ui.Accent, TextAnchor.MiddleRight, FontStyle.Bold, "Brand");
            Ui.Place(brand.rectTransform, new Vector2(1f, 1f), new Vector2(380, 84), new Vector2(-34, -26));

            m_rowNames = new Text[m_slots.Length];
            m_rowPrices = new Text[m_slots.Length];
            float rowH = (h - 120 - 110) / m_slots.Length;
            for (int i = 0; i < m_slots.Length; i++)
            {
                float y = -120 - i * rowH;
                if (i % 2 == 0)
                {
                    var band = Ui.Panel(bg.transform, new Color(0.93f, 0.9f, 0.86f, 0.6f), "Band");
                    band.raycastTarget = false;
                    Ui.Place(band.rectTransform, new Vector2(0.5f, 1f), new Vector2(w - 40, rowH), new Vector2(0, y));
                }
                m_rowNames[i] = Ui.Label(bg.transform, "", 44, Ui.Ink, TextAnchor.MiddleLeft, FontStyle.Bold, "Name");
                Ui.Place(m_rowNames[i].rectTransform, new Vector2(0f, 1f), new Vector2(640, rowH), new Vector2(34, y));
                m_rowPrices[i] = Ui.Label(bg.transform, "", 44, Ui.Ink, TextAnchor.MiddleRight, FontStyle.Bold, "Price");
                Ui.Place(m_rowPrices[i].rectTransform, new Vector2(1f, 1f), new Vector2(200, rowH), new Vector2(-34, y));
            }

            var line = Ui.Panel(bg.transform, Ui.Ink, "Divider");
            line.raycastTarget = false;
            Ui.Place(line.rectTransform, new Vector2(0.5f, 0f), new Vector2(w - 68, 4), new Vector2(0, 104));
            var totalLabel = Ui.Label(bg.transform, "Total", 50, Ui.Ink, TextAnchor.MiddleLeft, FontStyle.Bold, "Total Label");
            Ui.Place(totalLabel.rectTransform, new Vector2(0f, 0f), new Vector2(300, 90), new Vector2(34, 10));
            m_total = Ui.Label(bg.transform, Euro(0), 60, Ui.Accent, TextAnchor.MiddleRight, FontStyle.Bold, "Total");
            Ui.Place(m_total.rectTransform, new Vector2(1f, 0f), new Vector2(400, 90), new Vector2(-34, 10));
        }

        private void BuildFinishLabel()
        {
            // Tilted toward the default viewing angle so it reads like a floor sign.
            var canvas = Ui.WorldCanvas("Label", m_finish, new Vector2(620, 150), 0.6f);
            canvas.transform.localPosition = new Vector3(0f, 0.11f, 0f);
            canvas.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
            var b = Ui.Button(canvas.transform, "Finish room  ►", Ui.Good, Finish, 60, "Finish Button");
            Ui.Stretch((RectTransform)b.transform);
        }

        // ---- Automated play-through ----

        public override void DebugComplete()
        {
            if (m_finished)
                return;
            foreach (var s in m_slots)
                if (s.Saved == null) s.Save();
            RefreshBoard();
            Finish();
        }

        public override IEnumerator SelfTest()
        {
            ResetRoom();
            yield return new WaitForSeconds(0.3f);

            // "Play again" must wipe saves and variants.
            m_slots[0].Cycle(1);
            Save(m_slots[0]);
            if (SavedCount != 1) Debug.LogError("[SELFTEST] save did not register");
            ResetRoom();
            if (SavedCount != 0 || Total != 0 || m_slots.Any(s => s.Index != 0))
                Debug.LogError("[SELFTEST] reset left state behind");
            yield return new WaitForSeconds(0.3f);

            foreach (var slot in m_slots)
            {
                slot.OnTap(default);
                if (Selected != slot) Debug.LogError("[SELFTEST] tapping " + slot.Category + " did not open its card");
                Cycle(slot, 1);
                yield return new WaitForSeconds(0.45f);
                if (slot.Index != 1) Debug.LogError("[SELFTEST] " + slot.Category + " did not advance to variant 2");
                for (int i = 0; i < slot.Variants.Length; i++)
                    if (slot.Variants[i].Model.activeSelf != (i == 1))
                        Debug.LogError("[SELFTEST] " + slot.Category + " variant " + i + " visibility wrong");
                if (slot.Current.Model.transform.localScale != slot.Current.BaseScale)
                    Debug.LogError("[SELFTEST] " + slot.Category + " pop-in did not settle");
            }

            int expected = 0;
            for (int i = 0; i < 3; i++)
            {
                Select(m_slots[i]);
                Save(m_slots[i]);
                expected += m_slots[i].Variants[1].Price;
                yield return new WaitForSeconds(0.3f);
            }
            if (SavedCount != 3) Debug.LogError("[SELFTEST] expected 3 saved items, got " + SavedCount);
            if (Total != expected) Debug.LogError("[SELFTEST] total " + Total + " != expected " + expected);
            if (BoardTotalText != Euro(expected)) Debug.LogError("[SELFTEST] board shows '" + BoardTotalText + "', expected " + Euro(expected));
            Debug.Log("[SELFTEST] room: 3 saved, total " + Euro(Total));

            Finish();
            yield return new WaitForSeconds(1.0f);
            if (!m_finished) Debug.LogError("[SELFTEST] finish did not end the round");
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result screen not shown");
        }
    }
}
