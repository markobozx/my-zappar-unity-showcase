using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Diner
{
    // An order ticket clipped to the rail above the pass: order number, dish and its ingredients
    // in build order (done = green tick, next = highlighted).
    public class DinerTicket : MonoBehaviour
    {
        private const float WidthPx = 420f;
        private RectTransform m_canvas;
        private Image m_bg, m_stripe;
        private Text m_number, m_dish, m_steps;
        private float m_worldWidth;

        public static DinerTicket Create(Transform parent, Vector3 localPos, float worldWidth)
        {
            var go = new GameObject("Ticket");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<Billboard>();
            var t = go.AddComponent<DinerTicket>();
            t.Build(worldWidth);
            return t;
        }

        private void Build(float worldWidth)
        {
            m_worldWidth = worldWidth;
            var canvas = Ui.WorldCanvas("Canvas", transform, new Vector2(WidthPx, 400), worldWidth);
            m_canvas = (RectTransform)canvas.transform;
            // Grow upward from the rail.
            m_canvas.pivot = new Vector2(0.5f, 0f);
            m_bg = Ui.Panel(m_canvas, Ui.Paper, "Background");
            Ui.Stretch(m_bg.rectTransform);
            m_stripe = Ui.Panel(m_bg.transform, Ui.Accent, "Stripe");
            Ui.Place(m_stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(WidthPx, 14), Vector2.zero);
            var pin = Ui.Panel(m_bg.transform, new Color(0.55f, 0.56f, 0.62f), "Clip");
            Ui.Place(pin.rectTransform, new Vector2(0.5f, 1f), new Vector2(90, 26), new Vector2(0, 10));
            // Display only: don't let the tickets swallow taps meant for the props behind them.
            m_bg.raycastTarget = m_stripe.raycastTarget = pin.raycastTarget = false;

            m_number = Ui.Label(m_bg.transform, "", 34, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold, "Number");
            Ui.Place(m_number.rectTransform, new Vector2(0f, 1f), new Vector2(WidthPx - 48, 44), new Vector2(26, -28));
            m_dish = Ui.Label(m_bg.transform, "", 48, Ui.Ink, TextAnchor.UpperLeft, FontStyle.Bold, "Dish");
            Ui.Place(m_dish.rectTransform, new Vector2(0f, 1f), new Vector2(WidthPx - 40, 64), new Vector2(26, -70));
            m_steps = Ui.Label(m_bg.transform, "", 40, Ui.Muted, TextAnchor.UpperLeft, FontStyle.Normal, "Steps");
            m_steps.lineSpacing = 1.05f;
            Ui.Place(m_steps.rectTransform, new Vector2(0f, 1f), new Vector2(WidthPx - 48, 320), new Vector2(30, -140));
        }

        // progress = ingredients already on the plate; current = the ticket being cooked now.
        public void Show(int number, Recipe recipe, int progress, bool current)
        {
            gameObject.SetActive(recipe != null);
            if (recipe == null) return;
            m_number.text = (current ? "NOW · " : "NEXT · ") + "ORDER #" + number;
            m_dish.text = recipe.Name;
            var sb = new StringBuilder();
            for (int i = 0; i < recipe.Steps.Length; i++)
            {
                string label = DinerMenu.Info(recipe.Steps[i]).label;
                if (i > 0) sb.Append('\n');
                if (!current) sb.Append("  ").Append(label);
                else if (i < progress) sb.Append("<color=#3FA35A>√ ").Append(label).Append("</color>");
                else if (i == progress) sb.Append("<color=#222033><b>› ").Append(label).Append("</b></color>");
                else sb.Append("  ").Append(label);
            }
            m_steps.text = sb.ToString();
            float h = 150 + recipe.Steps.Length * 46 + 20;
            m_canvas.sizeDelta = new Vector2(WidthPx, h);
            m_stripe.color = current ? Ui.Accent : Ui.Accent2;
            m_bg.color = current ? Ui.Paper : new Color(0.93f, 0.93f, 0.96f, 0.94f);
        }

        public float WorldWidth => m_worldWidth;
    }
}
