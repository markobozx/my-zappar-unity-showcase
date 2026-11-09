using UnityEngine;

namespace Showcase.Experiences.Diner
{
    // A finished dish waiting on the pick-up counter. Tap it for its menu card.
    public class DinerServedDish : MonoBehaviour, ITappable
    {
        public Recipe Recipe;
        public int OrderNumber;

        public void OnTap(RaycastHit hit)
        {
            StopAllCoroutines();
            transform.localScale = Vector3.one;
            StartCoroutine(Tween.Punch(transform, 0.15f, 0.3f));
            Fx.Play(FxKind.Sparkle, transform.position + transform.up * 0.1f, 0.6f);
            ShowInfo();
        }

        public void ShowInfo()
        {
            if (Recipe == null) return;
            string kind = Recipe.Pizza ? "Stone-baked pizza" : "Smash burger";
            WorldCard.Show(transform, new Vector3(0f, 0.45f, 0f), Recipe.Name,
                kind + " · order #" + OrderNumber + "\n" + Recipe.Price + " · " + Recipe.Kcal + " kcal\n<color=#E8735B>" + DinerMenu.Brand + " · made to order</color>",
                1.0f);
        }
    }
}
