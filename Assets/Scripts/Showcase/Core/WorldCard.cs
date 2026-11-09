using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase
{
    // A world-space info card that pops up above an object: title, body, optional buttons.
    // It turns to face the active camera and closes with its × button or Close().
    //   var card = WorldCard.Show(prop.transform, new Vector3(0, 0.35f, 0), "Sofa", "Velvet, 3 seats\n€ 899",
    //                             ("Try colour", () => NextColour()), ("Add to room", AddToRoom));
    public class WorldCard : MonoBehaviour
    {
        private static WorldCard s_open;   // one card at a time keeps the tabletop readable

        public Text Title { get; private set; }
        public Text Body { get; private set; }

        public static WorldCard Show(Transform anchor, Vector3 localOffset, string title, string body, params (string label, Action action)[] buttons)
        {
            return Show(anchor, localOffset, title, body, 0.75f, buttons);
        }

        public static WorldCard Show(Transform anchor, Vector3 localOffset, string title, string body, float worldWidth, params (string label, Action action)[] buttons)
        {
            if (s_open != null)
                s_open.Close();

            Transform world = ShowcaseRig.Instance != null ? ShowcaseRig.Instance.World : null;
            var root = new GameObject("World Card");
            root.transform.SetParent(world != null ? world : anchor, false);
            root.transform.position = anchor.TransformPoint(localOffset);
            root.AddComponent<Billboard>();
            var card = root.AddComponent<WorldCard>();

            int lines = Mathf.Max(1, body.Split('\n').Length);
            float height = 130 + lines * 44 + (buttons.Length > 0 ? 110 : 20);
            var canvas = Ui.WorldCanvas("Canvas", root.transform, new Vector2(600, height), worldWidth);
            // A world canvas reads correctly when its forward points away from the camera, which is
            // exactly what Billboard does, so no extra rotation is needed.
            canvas.sortingOrder = 10;

            var bg = Ui.Panel(canvas.transform, Ui.Paper, "Background");
            Ui.Stretch(bg.rectTransform);
            var stripe = Ui.Panel(bg.transform, Ui.Accent, "Stripe");
            Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(600, 12), Vector2.zero);

            card.Title = Ui.Label(bg.transform, title, 46, Ui.Ink, TextAnchor.UpperLeft, FontStyle.Bold, "Title");
            Ui.Place(card.Title.rectTransform, new Vector2(0f, 1f), new Vector2(490, 70), new Vector2(28, -26));
            card.Body = Ui.Label(bg.transform, body, 34, Ui.Muted, TextAnchor.UpperLeft, FontStyle.Normal, "Body");
            Ui.Place(card.Body.rectTransform, new Vector2(0f, 1f), new Vector2(544, lines * 44 + 10), new Vector2(28, -100));
            // Size the card from the wrapped text, not just the line breaks, so long lines never spill out.
            float bodyHeight = Mathf.Max(lines * 44f, card.Body.preferredHeight) + 10f;
            card.Body.rectTransform.sizeDelta = new Vector2(544, bodyHeight);
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(600, 120 + bodyHeight + (buttons.Length > 0 ? 110 : 20));

            var close = Ui.Button(bg.transform, "×", Ui.Muted, card.Close, 44, "Close");
            Ui.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(72, 72), new Vector2(-14, -22));

            if (buttons.Length > 0)
            {
                float w = (600 - 28 * 2 - 16 * (buttons.Length - 1)) / buttons.Length;
                for (int i = 0; i < buttons.Length; i++)
                {
                    var (label, action) = buttons[i];
                    var b = Ui.Button(bg.transform, label, i == 0 ? Ui.Accent : Ui.Accent2, () => action?.Invoke(), 32);
                    Ui.Place((RectTransform)b.transform, new Vector2(0f, 0f), new Vector2(w, 80), new Vector2(28 + i * (w + 16), 22));
                }
            }

            s_open = card;
            card.StartCoroutine(card.Pop(root.transform));
            return card;
        }

        public static void CloseOpen()
        {
            if (s_open != null)
                s_open.Close();
        }

        public void Close()
        {
            if (s_open == this)
                s_open = null;
            if (this != null)
                Destroy(gameObject);
        }

        private IEnumerator Pop(Transform t)
        {
            Vector3 target = Vector3.one;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.22f)
            {
                float k = 1f + 0.12f * Mathf.Sin(a * Mathf.PI);
                t.localScale = target * Mathf.SmoothStep(0.2f, 1f, a) * k;
                yield return null;
            }
            t.localScale = target;
        }
    }
}
