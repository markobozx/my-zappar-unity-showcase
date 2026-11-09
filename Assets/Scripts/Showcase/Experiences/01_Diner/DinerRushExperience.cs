using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Showcase.Experiences.Diner
{
    // Pop-Up Diner Rush: tap ingredient crates in the order the ticket lists them to build burgers
    // and pizzas on the plate. A wrong ingredient bins the plate; a finished dish slides to the
    // pick-up counter for coins. 60-second rounds.
    public class DinerRushExperience : ShowcaseExperience
    {
        [SerializeField] private float m_roundSeconds = 60f;
        [Tooltip("Indexed by Piece.")]
        [SerializeField] private GameObject[] m_piecePrefabs;
        [SerializeField] private GameObject m_platePrefab;
        [SerializeField] private Transform m_plate;
        [Tooltip("Unscaled transform at the plate's top surface; pieces stack in its local space.")]
        [SerializeField] private Transform m_stack;
        [Tooltip("Current and next ticket positions.")]
        [SerializeField] private Transform[] m_ticketAnchors;
        [Tooltip("First pick-up slot; further dishes queue along +X.")]
        [SerializeField] private Transform m_pickup;
        [SerializeField] private float m_pickupSpacing = 0.27f;

        private const int PickupSlots = 3;
        private const float FlyTime = 0.35f;

        private DinerBin[] m_bins;
        private DinerTicket[] m_tickets;
        private Vector3 m_plateScale;

        private readonly List<Recipe> m_queue = new List<Recipe>();
        private readonly List<GameObject> m_stackPieces = new List<GameObject>();
        private readonly List<DinerServedDish> m_dishes = new List<DinerServedDish>();
        private readonly List<GameObject> m_spawned = new List<GameObject>();
        private bool m_running;
        private float m_timeLeft;
        private float m_busyUntil;
        private int m_score, m_served, m_mistakes, m_orderNumber, m_progress, m_best;
        private float m_stackTop;
        private int m_lastTimerSecond = -1;
        private int m_round;

        public Recipe Current => m_queue.Count > 0 ? m_queue[0] : null;
        public bool Running => m_running;

        void Awake()
        {
            m_bins = GetComponentsInChildren<DinerBin>(true);
            m_plateScale = m_plate != null ? m_plate.localScale : Vector3.one;
            m_tickets = new DinerTicket[m_ticketAnchors.Length];
            for (int i = 0; i < m_ticketAnchors.Length; i++)
                m_tickets[i] = DinerTicket.Create(m_ticketAnchors[i], Vector3.zero, i == 0 ? 0.5f : 0.34f);
        }

        protected override void OnEnter()
        {
            StartRound();
        }

        protected override void OnExit()
        {
            m_running = false;
            ClearSpawned();
        }

        public override void DebugComplete()
        {
            if (m_running)
                EndRound();
        }

        // ---------------------------------------------------------------- round flow

        public void StartRound()
        {
            // No StopAllCoroutines here: the play-test harness runs SelfTest as a coroutine on this
            // component. Leftover routines check m_round instead.
            m_round++;
            ClearSpawned();
            WorldCard.CloseOpen();
            if (Hud != null) Hud.HideResult();

            foreach (var b in m_bins) { b.ResetVisual(); b.Flash(Ui.Paper); }
            if (m_plate != null) m_plate.localScale = m_plateScale;
            foreach (var a in m_ticketAnchors) a.localScale = Vector3.one;

            m_score = m_served = m_mistakes = m_progress = 0;
            m_orderNumber = 1;
            m_stackTop = 0f;
            m_busyUntil = 0f;
            m_timeLeft = m_roundSeconds;
            m_lastTimerSecond = -1;
            m_queue.Clear();
            // A gentle opener (one burger, one pizza), then the rush.
            m_queue.Add(DinerMenu.Recipes.First(r => r.Name == "Classic Burger"));
            m_queue.Add(DinerMenu.Recipes.First(r => r.Name == "Margherita"));
            m_running = true;

            RefreshTickets();
            RefreshScore();
            UpdateTimer();
            if (Hud != null) Hud.Toast("Rush hour! Build the ticket's order", 1.4f);
        }

        void Update()
        {
            if (!m_running) return;
            m_timeLeft -= Time.deltaTime;
            UpdateTimer();
            if (m_timeLeft <= 0f)
                EndRound();
        }

        private void UpdateTimer()
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(m_timeLeft));
            if (s == m_lastTimerSecond || Hud == null) return;
            m_lastTimerSecond = s;
            string t = (s / 60) + ":" + (s % 60).ToString("00");
            Hud.SetTimer(s <= 10 ? "<color=#FF6B5B>" + t + "</color>" : t);
        }

        private void RefreshScore()
        {
            if (Hud != null) Hud.SetScore(m_score + " coins");
        }

        private void EndRound()
        {
            m_running = false;
            m_timeLeft = 0f;
            UpdateTimer();
            m_best = Mathf.Max(m_best, m_score);
            WorldCard.CloseOpen();
            if (m_served > 0 && m_plate != null)
                Fx.Play(FxKind.Confetti, m_plate.position + m_plate.up * 0.3f, 1.4f);
            string title = m_served >= 6 ? "Head chef!" : m_served >= 3 ? "Great shift!" : "Time's up!";
            string body = "Orders served: " + m_served + "\nCoins: " + m_score + "   ·   Best: " + m_best
                          + (m_mistakes > 0 ? "\n<size=32>Plates binned: " + m_mistakes + "</size>" : "");
            if (Hud != null)
                Hud.ShowResult(title, body, ("Play again", StartRound), ("Hub", () => { if (ShowcaseHub.Instance != null) ShowcaseHub.Instance.ReturnToHub(); }));
        }

        private Recipe RandomRecipe(Recipe avoid)
        {
            Recipe r;
            int guard = 0;
            do r = DinerMenu.Recipes[Random.Range(0, DinerMenu.Recipes.Length)];
            while ((r == avoid || r.Steps.Length < 4 && guard < 2) && guard++ < 10);
            return r;
        }

        private void RefreshTickets()
        {
            for (int i = 0; i < m_tickets.Length; i++)
                m_tickets[i].Show(m_orderNumber + i, i < m_queue.Count ? m_queue[i] : null, i == 0 ? m_progress : 0, i == 0);
        }

        // ---------------------------------------------------------------- taps

        public DinerBin BinFor(Ingredient i) => m_bins.FirstOrDefault(b => b.Ingredient == i);

        // Called by DinerBin.OnTap (and by SelfTest).
        public void TapIngredient(DinerBin bin)
        {
            StartCoroutine(Bounce(bin.transform, bin.BaseScale, 0.16f, 0.25f));
            Fx.Play(FxKind.Hit, bin.SpawnPoint, 0.5f);
            if (!m_running || Time.time < m_busyUntil || Current == null)
                return;

            Recipe recipe = Current;
            Ingredient expected = recipe.Steps[m_progress];
            if (bin.Ingredient != expected)
            {
                WrongIngredient(bin, expected);
                return;
            }

            Piece piece = DinerMenu.PieceFor(bin.Ingredient, recipe, m_progress);
            GameObject go = SpawnPiece(piece, bin.SpawnPoint);
            m_stackPieces.Add(go);
            m_progress++;
            StartCoroutine(FlashBin(bin, new Color(0.8f, 0.95f, 0.82f, 0.98f)));

            if (m_progress >= recipe.Steps.Length)
                CompleteOrder(recipe);
            else
                RefreshTickets();
        }

        private void WrongIngredient(DinerBin bin, Ingredient expected)
        {
            m_mistakes++;
            if (m_plate != null)
            {
                Fx.Play(FxKind.Smoke, m_stack.position + m_stack.up * 0.08f, 1.2f);
                StartCoroutine(Bounce(m_plate, m_plateScale, 0.25f, 0.35f));
            }
            StartCoroutine(FlashBin(bin, new Color(1f, 0.75f, 0.7f, 0.98f)));
            foreach (var p in m_stackPieces)
                if (p != null) Destroy(p);
            m_stackPieces.Clear();
            m_stackTop = 0f;
            m_progress = 0;
            RefreshTickets();
            if (Hud != null)
                Hud.Toast("Oops! " + DinerMenu.Info(bin.Ingredient).label + " isn't next (" + DinerMenu.Info(expected).label + " was).\nPlate reset", 1.8f);
        }

        private void CompleteOrder(Recipe recipe)
        {
            int number = m_orderNumber;
            int points = recipe.Points;
            m_served++;
            m_score += points;
            RefreshScore();

            var pieces = new List<GameObject>(m_stackPieces);
            m_stackPieces.Clear();
            m_stackTop = 0f;
            m_progress = 0;
            m_busyUntil = Time.time + FlyTime + 0.12f;

            m_queue.RemoveAt(0);
            m_queue.Add(RandomRecipe(m_queue[m_queue.Count - 1]));
            m_orderNumber++;
            RefreshTickets();
            StartCoroutine(PopTicket(m_ticketAnchors[0]));
            StartCoroutine(Serve(pieces, recipe, number, points));
        }

        // Like Tween.Punch, but always settles on a known scale, so overlapping bounces
        // (quick repeated taps) can't leave the object permanently squashed.
        private static IEnumerator Bounce(Transform t, Vector3 baseScale, float amount, float time)
        {
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                if (t == null) yield break;
                float k = 1f + amount * Mathf.Sin(a * Mathf.PI) * (1f - a);
                t.localScale = new Vector3(baseScale.x * k, baseScale.y / k, baseScale.z * k);
                yield return null;
            }
            if (t != null) t.localScale = baseScale;
        }

        private static IEnumerator PopTicket(Transform t)
        {
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.3f)
            {
                if (t == null) yield break;
                t.localScale = Vector3.one * Mathf.LerpUnclamped(0.2f, 1f, Tween.EaseOutBack(a));
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one;
        }

        private IEnumerator FlashBin(DinerBin bin, Color c)
        {
            bin.Flash(c);
            yield return new WaitForSeconds(0.35f);
            bin.Flash(Ui.Paper);
        }

        // ---------------------------------------------------------------- plate visuals

        private GameObject SpawnPiece(Piece piece, Vector3 worldFrom)
        {
            GameObject prefab = m_piecePrefabs[(int)piece];
            var go = Instantiate(prefab, m_stack);
            go.name = piece.ToString();
            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Euler(0f, piece == Piece.BunTop || piece == Piece.BunBottom ? 0f : Random.Range(0f, 360f), 0f);
            t.localScale = prefab.transform.localScale;
            var fit = DinerMenu.PieceFit(piece);
            Bounds lb = DinerUtil.LocalBounds(t, m_stack);
            float k = fit.width / Mathf.Max(0.0001f, Mathf.Max(lb.size.x, lb.size.z));
            t.localScale *= k;
            lb = DinerUtil.LocalBounds(t, m_stack);

            float thickness = lb.size.y;
            float bottom = m_stackPieces.Count == 0 ? 0f : m_stackTop - fit.sink * thickness;
            Vector3 to = new Vector3(-lb.center.x, bottom - lb.min.y, -lb.center.z);
            m_stackTop = Mathf.Max(m_stackTop, bottom + thickness);

            Vector3 from = m_stack.InverseTransformPoint(worldFrom);
            t.localPosition = from;
            m_spawned.Add(go);
            StartCoroutine(Fly(t, from, to));
            return go;
        }

        private IEnumerator Fly(Transform t, Vector3 from, Vector3 to)
        {
            Quaternion r0 = t.localRotation * Quaternion.Euler(0f, -120f, 0f), r1 = t.localRotation;
            float height = 0.28f + Vector3.Distance(from, to) * 0.15f;
            for (float a = 0f; a < 1f; a += Time.deltaTime / FlyTime)
            {
                if (t == null) yield break;
                float e = Mathf.SmoothStep(0f, 1f, a);
                t.localPosition = Vector3.Lerp(from, to, e) + Vector3.up * (4f * height * a * (1f - a));
                t.localRotation = Quaternion.Slerp(r0, r1, e);
                yield return null;
            }
            if (t == null) yield break;
            t.localPosition = to;
            t.localRotation = r1;
            StartCoroutine(Tween.Punch(t, 0.22f, 0.22f));
            if (m_plate != null) StartCoroutine(Bounce(m_plate, m_plateScale, 0.06f, 0.2f));
        }

        private IEnumerator Serve(List<GameObject> pieces, Recipe recipe, int number, int points)
        {
            int round = m_round;
            yield return new WaitForSeconds(FlyTime + 0.02f);
            if (round != m_round) yield break;
            Vector3 top = m_stack.position + m_stack.up * 0.25f;
            Fx.Play(FxKind.Confetti, top, 0.9f);
            Fx.Play(FxKind.Pickup, top, 0.7f);
            ShowFloatingText("+" + points, top + m_stack.up * 0.05f);
            if (Hud != null) Hud.Toast("Order #" + number + " served! +" + points + " coins", 1.1f);

            // The finished dish gets its own plate and slides to the pick-up counter.
            var dishGo = new GameObject("Served " + recipe.Name);
            dishGo.transform.SetParent(transform, false);
            dishGo.transform.position = m_stack.position;
            dishGo.transform.localRotation = Quaternion.identity;
            m_spawned.Add(dishGo);
            if (m_platePrefab != null && m_plate != null)
            {
                var plate = Instantiate(m_platePrefab, dishGo.transform);
                plate.transform.position = m_plate.position;
                plate.transform.rotation = m_plate.rotation;
                plate.transform.localScale = m_plateScale;
            }
            foreach (var p in pieces)
                if (p != null) p.transform.SetParent(dishGo.transform, true);
            Bounds lb = DinerUtil.LocalBounds(dishGo.transform, dishGo.transform);
            var box = dishGo.AddComponent<BoxCollider>();
            box.center = lb.center;
            box.size = lb.size * 1.2f;
            var dish = dishGo.AddComponent<DinerServedDish>();
            dish.Recipe = recipe;
            dish.OrderNumber = number;
            StartCoroutine(Bounce(m_plate, m_plateScale, 0.2f, 0.3f));

            m_dishes.Insert(0, dish);
            for (int i = m_dishes.Count - 1; i >= 0; i--)
            {
                if (m_dishes[i] == null) { m_dishes.RemoveAt(i); continue; }
                if (i >= PickupSlots)
                {
                    Fx.Play(FxKind.Poof, m_dishes[i].transform.position, 0.6f);
                    Destroy(m_dishes[i].gameObject);
                    m_dishes.RemoveAt(i);
                    continue;
                }
                Vector3 slot = m_pickup.localPosition + Vector3.right * (m_pickupSpacing * i);
                if (i == 0) StartCoroutine(Slide(m_dishes[i].transform, slot));
                else StartCoroutine(Tween.MoveLocal(m_dishes[i].transform, slot, 0.3f));
            }
        }

        private IEnumerator Slide(Transform t, Vector3 to)
        {
            Vector3 from = t.localPosition;
            const float time = 0.55f;
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                if (t == null) yield break;
                float e = Mathf.SmoothStep(0f, 1f, a);
                t.localPosition = Vector3.Lerp(from, to, e) + Vector3.up * (0.35f * 4f * a * (1f - a));
                yield return null;
            }
            if (t == null) yield break;
            t.localPosition = to;
            Fx.Play(FxKind.Stars, t.position + t.up * 0.12f, 0.6f);
            StartCoroutine(Tween.Punch(t, 0.18f, 0.25f));
        }

        private void ShowFloatingText(string text, Vector3 worldPos)
        {
            var go = new GameObject("Coins Popup");
            go.transform.SetParent(transform, false);
            go.transform.position = worldPos;
            go.AddComponent<Billboard>();
            var canvas = Ui.WorldCanvas("Canvas", go.transform, new Vector2(300, 110), 0.3f);
            canvas.sortingOrder = 12;
            var label = Ui.Label(canvas.transform, text, 90, new Color(1f, 0.82f, 0.25f), TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(label.rectTransform);
            label.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = new Color(0.35f, 0.2f, 0.05f, 0.9f);
            m_spawned.Add(go);
            StartCoroutine(Rise(go.transform, label));
        }

        private IEnumerator Rise(Transform t, UnityEngine.UI.Text label)
        {
            Vector3 start = t.localPosition;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 1.0f)
            {
                if (t == null) yield break;
                t.localPosition = start + Vector3.up * (0.3f * a);
                var c = label.color; c.a = 1f - a * a; label.color = c;
                yield return null;
            }
            if (t != null) Destroy(t.gameObject);
        }

        private void ClearSpawned()
        {
            foreach (var go in m_spawned)
                if (go != null) Destroy(go);
            m_spawned.Clear();
            m_stackPieces.Clear();
            m_dishes.Clear();
        }

        // ---------------------------------------------------------------- automated test

        public override IEnumerator SelfTest()
        {
            StartRound();
            yield return new WaitForSeconds(0.5f);
            if (!m_running) Debug.LogError("[SELFTEST] round did not start");
            if (m_bins.Length != System.Enum.GetValues(typeof(Ingredient)).Length)
                Debug.LogError("[SELFTEST] expected one bin per ingredient, found " + m_bins.Length);

            // Two orders built correctly, tapping the crates in ticket order.
            for (int order = 0; order < 2; order++)
            {
                Recipe recipe = Current;
                int scoreBefore = m_score, servedBefore = m_served;
                foreach (Ingredient step in recipe.Steps)
                {
                    DinerBin bin = BinFor(step);
                    if (bin == null) { Debug.LogError("[SELFTEST] no bin for " + step); yield break; }
                    bin.OnTap(new RaycastHit());
                    yield return new WaitForSeconds(0.2f);
                }
                if (m_score <= scoreBefore) Debug.LogError("[SELFTEST] score did not go up after " + recipe.Name);
                if (m_served != servedBefore + 1) Debug.LogError("[SELFTEST] served count " + m_served + " after " + recipe.Name);
                if (Current == recipe && m_progress != 0) Debug.LogError("[SELFTEST] order did not advance");
                yield return new WaitForSeconds(0.8f);
            }
            if (m_dishes.Count != 2) Debug.LogError("[SELFTEST] expected 2 dishes on the pick-up counter, found " + m_dishes.Count);
            if (m_dishes.Count > 0) m_dishes[0].ShowInfo();
            yield return new WaitForSeconds(0.6f);
            BinFor(Ingredient.Cheese).ShowInfo();
            yield return new WaitForSeconds(0.4f);
            WorldCard.CloseOpen();

            // One right ingredient, then a wrong one: the plate must reset.
            Recipe next = Current;
            BinFor(next.Steps[0]).OnTap(new RaycastHit());
            yield return new WaitForSeconds(0.3f);
            if (m_progress != 1 || m_stackPieces.Count != 1) Debug.LogError("[SELFTEST] first ingredient not on the plate");
            Ingredient wrong = System.Enum.GetValues(typeof(Ingredient)).Cast<Ingredient>().First(i => i != next.Steps[1]);
            int scoreBeforeWrong = m_score;
            BinFor(wrong).OnTap(new RaycastHit());
            if (m_progress != 0 || m_stackPieces.Count != 0) Debug.LogError("[SELFTEST] plate not reset after a wrong ingredient");
            if (m_score != scoreBeforeWrong) Debug.LogError("[SELFTEST] score changed on a wrong ingredient");
            yield return new WaitForSeconds(0.8f);

            // Run the clock out.
            m_timeLeft = 0.4f;
            yield return new WaitForSeconds(1.0f);
            if (m_running) Debug.LogError("[SELFTEST] round still running after the timer ran out");
            if (Hud != null && !Hud.ResultVisible) Debug.LogError("[SELFTEST] result panel not shown");
            Debug.Log("[SELFTEST] diner ok: served " + m_served + ", coins " + m_score);
        }
    }
}
