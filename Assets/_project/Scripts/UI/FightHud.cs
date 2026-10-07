using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-fight HUD: a health bar and a stamina bar for each player, P1 on the left and
/// P2 mirrored on the right. The whole HUD is built in code, so it needs no prefab or
/// scene setup: if a scene has fighters and no FightHud, one is created automatically.
/// Add this component to an object in the scene yourself to change its look in the Inspector.
/// </summary>
public class FightHud : MonoBehaviour
{
    [Header("Layout (sized for a 1920x1080 screen, scales with resolution)")]
    [SerializeField] private float barWidth = 720f;
    [SerializeField] private float healthBarHeight = 30f;
    [SerializeField] private float staminaBarHeight = 12f;
    [SerializeField] private float screenMargin = 50f;
    [Tooltip("How far the bars lean. 0 = plain rectangles.")]
    [SerializeField] private float slant = 0.45f;

    [Header("Colours")]
    [SerializeField] private Color healthColor = new Color(0.80f, 0.13f, 0.17f);
    [SerializeField] private Color lowHealthColor = new Color(1f, 0.40f, 0.30f);
    [SerializeField] private Color damageTrailColor = new Color(1f, 0.84f, 0.35f);
    [SerializeField] private Color staminaColor = new Color(0.30f, 0.78f, 0.40f);
    [SerializeField] private Color barBackColor = new Color(0.10f, 0.10f, 0.12f, 0.85f);
    [SerializeField] private Color frameColor = new Color(0f, 0f, 0f, 0.9f);
    [SerializeField] private Color exhaustedColor = new Color(0.75f, 0.10f, 0.10f, 0.9f);

    [Header("Animation")]
    [Tooltip("Seconds the yellow damage trail waits before it drains.")]
    [SerializeField] private float trailHoldTime = 0.5f;
    [Tooltip("How fast the damage trail drains, in bar-lengths per second.")]
    [SerializeField] private float trailDrainSpeed = 0.6f;

    private const float FRAME_THICKNESS = 3f;
    private const float FILL_SHARPNESS = 18f; // how quickly a bar catches up with the real value

    /// <summary>Everything the HUD tracks for one player.</summary>
    private class PlayerBars
    {
        public Fighter fighter;
        public bool innerIsRight; // the side of the bar nearest the screen centre

        public RectTransform healthFill;
        public RectTransform healthTrail;
        public RectTransform staminaFill;
        public Image healthFillImage;
        public Image staminaBackImage;
        public Text healthText;
        public Text statusText;

        public float shownHealth = 1f;
        public float trailHealth = 1f;
        public float lastHealthTarget = 1f;
        public float trailHold;
        public float shownStamina = 1f;

        public int lastHealthValue = -1;
        public int lastMaxHealthValue = -1;
        public string lastStatus;
    }

    private PlayerBars player1Bars;
    private PlayerBars player2Bars;
    private Font font;

    // ==================== AUTO-CREATE ====================

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterAutoCreate()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForFirstScene()
    {
        EnsureHudExists();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureHudExists();
    }

    private static void EnsureHudExists()
    {
        if (FindObjectsOfType<FightHud>().Length > 0) return;
        if (FindObjectsOfType<Fighter>().Length == 0) return;

        new GameObject("FightHUD").AddComponent<FightHud>();
    }

    // ==================== BUILD ====================

    private void Start()
    {
        Fighter player1 = null;
        Fighter player2 = null;
        foreach (Fighter fighter in FindObjectsOfType<Fighter>())
        {
            if (player1 == null && fighter.PlayerNumber == 1) player1 = fighter;
            else if (player2 == null && fighter.PlayerNumber == 2) player2 = fighter;
        }

        if (player1 == null && player2 == null)
        {
            Debug.LogWarning($"FightHud on {name}: no fighters found, nothing to show.");
            return;
        }

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject canvasObject = new GameObject("FightHUD Canvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRect = (RectTransform)canvasObject.transform;
        if (player1 != null) player1Bars = BuildPlayerBars(canvasRect, player1, mirrored: false);
        if (player2 != null) player2Bars = BuildPlayerBars(canvasRect, player2, mirrored: true);
    }

    /// <summary>
    /// Builds one player's block: name and HP number on top, the health bar under them,
    /// then the shorter stamina bar with a status line beside it. P2's block is mirrored.
    /// </summary>
    private PlayerBars BuildPlayerBars(RectTransform canvasRect, Fighter fighter, bool mirrored)
    {
        PlayerBars bars = new PlayerBars();
        bars.fighter = fighter;
        bars.innerIsRight = !mirrored;

        float textRowHeight = 32f;
        float healthTop = textRowHeight + 6f;
        float staminaTop = healthTop + healthBarHeight + 10f;
        float staminaWidth = barWidth * 0.62f;
        float skew = mirrored ? -slant : slant;

        // Block anchored to the top-left (P1) or top-right (P2) corner of the screen.
        float cornerX = mirrored ? 1f : 0f;
        RectTransform root = CreateRect($"P{fighter.PlayerNumber} Bars", canvasRect);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(cornerX, 1f);
        root.anchoredPosition = new Vector2(mirrored ? -screenMargin : screenMargin, -screenMargin);
        root.sizeDelta = new Vector2(barWidth, staminaTop + staminaBarHeight);

        // ----- Text row -----
        string fighterName = fighter.fighterData != null ? fighter.fighterData.FighterName : "";
        Text nameText = CreateText("Name", root, 26, FontStyle.Bold, outerSide: true, mirrored);
        Place((RectTransform)nameText.transform, 0f, textRowHeight, barWidth * 0.7f, alignInner: false, mirrored);
        nameText.text = $"P{fighter.PlayerNumber}  {fighterName}";

        bars.healthText = CreateText("Health Number", root, 22, FontStyle.Normal, outerSide: false, mirrored);
        Place((RectTransform)bars.healthText.transform, 0f, textRowHeight, barWidth * 0.3f, alignInner: true, mirrored);

        // ----- Health bar -----
        RectTransform healthBar = CreateRect("Health Bar", root);
        Place(healthBar, healthTop, healthBarHeight, barWidth, alignInner: true, mirrored);

        CreateBarLayer("Frame", healthBar, frameColor, skew, FRAME_THICKNESS);
        CreateBarLayer("Back", healthBar, barBackColor, skew, 0f);
        bars.healthTrail = CreateBarLayer("Damage Trail", healthBar, damageTrailColor, skew, 0f).rectTransform;
        bars.healthFillImage = CreateBarLayer("Fill", healthBar, healthColor, skew, 0f);
        bars.healthFill = bars.healthFillImage.rectTransform;

        // Soft highlight across the top half so the bar doesn't look flat.
        // Pivot sits on the bar's centre line so it leans exactly like the layers under it.
        Image gloss = CreateBarLayer("Gloss", healthBar, new Color(1f, 1f, 1f, 0.10f), skew, 0f);
        gloss.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        gloss.rectTransform.pivot = new Vector2(0.5f, 0f);
        gloss.rectTransform.offsetMin = Vector2.zero;
        gloss.rectTransform.offsetMax = Vector2.zero;

        // ----- Stamina bar -----
        RectTransform staminaBar = CreateRect("Stamina Bar", root);
        Place(staminaBar, staminaTop, staminaBarHeight, staminaWidth, alignInner: true, mirrored);

        CreateBarLayer("Frame", staminaBar, frameColor, skew, FRAME_THICKNESS);
        bars.staminaBackImage = CreateBarLayer("Back", staminaBar, barBackColor, skew, 0f);
        bars.staminaFill = CreateBarLayer("Fill", staminaBar, staminaColor, skew, 0f).rectTransform;

        // ----- Status line (blocking / counter ready / out of stamina), beside the stamina bar -----
        bars.statusText = CreateText("Status", root, 18, FontStyle.Bold, outerSide: true, mirrored);
        Place((RectTransform)bars.statusText.transform, staminaTop - 6f, staminaBarHeight + 12f,
            barWidth - staminaWidth - 20f, alignInner: false, mirrored);

        return bars;
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>
    /// Positions a rect inside a player's block: <paramref name="top"/> pixels down from the
    /// top, against either the inner edge (toward screen centre) or the outer edge.
    /// </summary>
    private static void Place(RectTransform rect, float top, float height, float width, bool alignInner, bool mirrored)
    {
        // P1's inner edge is its right side; P2 is mirrored, so its inner edge is the left.
        bool alignRight = alignInner ? !mirrored : mirrored;
        float x = alignRight ? 1f : 0f;

        rect.anchorMin = rect.anchorMax = new Vector2(x, 1f);
        rect.pivot = new Vector2(x, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -(top + height * 0.5f));
        rect.sizeDelta = new Vector2(width, height);
    }

    /// <summary>A solid slanted rectangle filling its parent bar, grown outward by <paramref name="padding"/>.</summary>
    private static Image CreateBarLayer(string objectName, RectTransform bar, Color color, float skew, float padding)
    {
        RectTransform rect = CreateRect(objectName, bar);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-padding, -padding);
        rect.offsetMax = new Vector2(padding, padding);

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        rect.gameObject.AddComponent<UISkew>().amount = skew;
        return image;
    }

    private Text CreateText(string objectName, RectTransform parent, int size, FontStyle style, bool outerSide, bool mirrored)
    {
        RectTransform rect = CreateRect(objectName, parent);

        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        // Outer-side text hugs the screen edge, inner-side text hugs the centre.
        bool alignRight = outerSide ? mirrored : !mirrored;
        text.alignment = alignRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        return text;
    }

    // ==================== UPDATE ====================

    // LateUpdate so this frame's damage, blocks and recovery are already applied.
    private void LateUpdate()
    {
        if (player1Bars != null) UpdatePlayerBars(player1Bars);
        if (player2Bars != null) UpdatePlayerBars(player2Bars);
    }

    private void UpdatePlayerBars(PlayerBars bars)
    {
        if (bars.fighter == null || bars.fighter.Stats == null) return;

        FighterStats stats = bars.fighter.Stats;
        float catchUp = 1f - Mathf.Exp(-FILL_SHARPNESS * Time.deltaTime);

        // ----- Health -----
        float healthTarget = Fraction(stats.currentHealth, stats.maxHealth);

        // Fresh damage: leave the yellow trail where it was for a moment, then drain it.
        if (healthTarget < bars.lastHealthTarget - 0.0001f)
        {
            bars.trailHold = trailHoldTime;
        }
        bars.lastHealthTarget = healthTarget;

        bars.shownHealth = Mathf.Lerp(bars.shownHealth, healthTarget, catchUp);

        if (bars.trailHealth <= healthTarget)
        {
            bars.trailHealth = healthTarget; // healing: nothing to trail
        }
        else if (bars.trailHold > 0f)
        {
            bars.trailHold -= Time.deltaTime;
        }
        else
        {
            bars.trailHealth = Mathf.MoveTowards(bars.trailHealth, healthTarget, trailDrainSpeed * Time.deltaTime);
        }

        SetFill(bars.healthFill, bars.shownHealth, bars.innerIsRight);
        SetFill(bars.healthTrail, Mathf.Max(bars.trailHealth, bars.shownHealth), bars.innerIsRight);

        // Pulse the bar when the fighter is in desperation range.
        bool lowHealth = stats.IsAlive() && healthTarget <= BalanceConfig.DESPERATION_HP_THRESHOLD;
        bars.healthFillImage.color = lowHealth
            ? Color.Lerp(healthColor, lowHealthColor, Mathf.PingPong(Time.time * 3f, 1f))
            : healthColor;

        if (stats.currentHealth != bars.lastHealthValue || stats.maxHealth != bars.lastMaxHealthValue)
        {
            bars.lastHealthValue = stats.currentHealth;
            bars.lastMaxHealthValue = stats.maxHealth;
            bars.healthText.text = $"{stats.currentHealth} / {stats.maxHealth}";
        }

        // ----- Stamina -----
        float staminaTarget = Fraction(stats.currentStamina, stats.maxStamina);
        bars.shownStamina = Mathf.Lerp(bars.shownStamina, staminaTarget, catchUp);
        SetFill(bars.staminaFill, bars.shownStamina, bars.innerIsRight);

        // Out of stamina (can't block): the empty bar flashes red.
        bool exhausted = stats.currentStamina <= 0;
        bars.staminaBackImage.color = exhausted
            ? Color.Lerp(barBackColor, exhaustedColor, Mathf.PingPong(Time.time * 4f, 1f))
            : barBackColor;

        // ----- Status line -----
        string status = "";
        Color statusColor = Color.white;
        if (exhausted)
        {
            status = "OUT OF STAMINA";
            statusColor = new Color(1f, 0.35f, 0.30f);
        }
        else if (bars.fighter.DashCounterTimeLeft > 0f)
        {
            status = "COUNTER READY";
            statusColor = new Color(1f, 0.84f, 0.35f);
        }
        else if (bars.fighter.IsBlocking)
        {
            status = "BLOCKING";
            statusColor = new Color(0.55f, 0.80f, 1f);
        }

        if (status != bars.lastStatus)
        {
            bars.lastStatus = status;
            bars.statusText.text = status;
            bars.statusText.color = statusColor;
        }
    }

    private static float Fraction(int current, int max)
    {
        return max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
    }

    /// <summary>
    /// Sizes a fill layer to <paramref name="fraction"/> of its bar. The filled part stays
    /// against the inner edge, so both players' bars empty toward the centre of the screen.
    /// </summary>
    private static void SetFill(RectTransform fill, float fraction, bool innerIsRight)
    {
        fraction = Mathf.Clamp01(fraction);
        if (innerIsRight)
        {
            fill.anchorMin = new Vector2(1f - fraction, 0f);
            fill.anchorMax = new Vector2(1f, 1f);
        }
        else
        {
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(fraction, 1f);
        }
    }
}
