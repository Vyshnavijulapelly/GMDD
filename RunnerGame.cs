using System.Collections.Generic;
using UnityEngine;

/// <summary>A self-contained, procedural-art endless runner for the Runner scene.</summary>
[DisallowMultipleComponent]
public sealed class RunnerGame : MonoBehaviour
{
    [Header("Runner")]
    public float startSpeed = 5.5f;
    public float maxSpeed = 10f;
    public float speedIncrease = 0.025f;
    public float jumpVelocity = 11f;
    public float gravity = -24f;
    [Range(0.8f, 1.8f)]
    [Tooltip("Medium-sized character, pickups, and hazards.")]
    public float visualScale = 1.25f;

    enum State { Ready, Running, GameOver }
    enum HazardType { Cactus, Spikes, Boulder, Vulture }

    sealed class Hazard
    {
        public Transform transform;
        public HazardType type;
        public float width;
        public float height;
        public float centerY;
    }

    sealed class Pickup
    {
        public Transform transform;
        public float phase;
        public float baseY;
    }

    const float GroundTop = -3.2f;
    const string BestKey = "RunnerBest";
    const float PlayerX = -3.2f;

    State state;
    bool paused;
    bool crouching;
    bool grounded;
    int score;
    int best;
    int bonusScore;
    int shownScore = -1;
    float distance;
    float speed;
    float playerY;
    float velocityY;
    float scroll;
    float spawnDistance;
    float gameOverTimer;
    float jumpBufferTimer;
    float halfWidth = 8f;
    float animationTimer;

    Camera gameCamera;
    Vector3 cameraBase;
    Transform world;
    Transform hazardRoot;
    Transform pickupRoot;
    Transform readyRoot;
    Transform gameOverRoot;
    Transform playerTransform;
    SpriteRenderer playerRenderer;
    SpriteRenderer skyRenderer;
    SpriteRenderer sunRenderer;
    SpriteRenderer scoreRenderer;
    SpriteRenderer bestRenderer;
    SpriteRenderer readyRootRenderer;
    SpriteRenderer readyTitleRenderer;
    SpriteRenderer readyHintRenderer;
    SpriteRenderer readyControlsRenderer;
    SpriteRenderer gameOverRootRenderer;
    SpriteRenderer gameOverTitleRenderer;
    SpriteRenderer finalScoreRenderer;
    SpriteRenderer finalBestRenderer;
    SpriteRenderer retryRenderer;
    SpriteRenderer pausedRenderer;

    Sprite[] runFrames;
    Sprite crouchSprite;
    Sprite coinSprite;
    Sprite cactusSprite;
    Sprite spikesSprite;
    Sprite boulderSprite;
    Sprite vultureSprite;
    ScrollLayer cloudLayer;
    ScrollLayer duneLayer;
    ScrollLayer platformLayer;
    ScrollLayer groundLayer;
    readonly List<Hazard> hazards = new List<Hazard>();
    readonly List<Pickup> pickups = new List<Pickup>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Runner"
            && FindAnyObjectByType<RunnerGame>() == null)
            new GameObject("RunnerGame").AddComponent<RunnerGame>();
    }

    void Start()
    {
        Application.targetFrameRate = 60;
        best = PlayerPrefs.GetInt(BestKey, 0);
        SetupCamera();
        BuildWorld();
        BuildUI();
        Restart();
    }

    void SetupCamera()
    {
        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            gameCamera = cameraObject.AddComponent<Camera>();
        }

        if (FindAnyObjectByType<AudioListener>() == null)
            gameCamera.gameObject.AddComponent<AudioListener>();

        gameCamera.orthographic = true;
        gameCamera.orthographicSize = 4.7f;
        gameCamera.transform.position = new Vector3(0f, 0f, -10f);
        gameCamera.transform.rotation = Quaternion.identity;
        gameCamera.clearFlags = CameraClearFlags.SolidColor;
        gameCamera.backgroundColor = new Color32(255, 229, 155, 255);
        cameraBase = gameCamera.transform.position;
        halfWidth = gameCamera.orthographicSize * gameCamera.aspect;
    }

    SpriteRenderer NewRenderer(string objectName, Transform parent, Sprite sprite, int order, Vector3 position)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        return renderer;
    }

    void BuildWorld()
    {
        world = new GameObject("Runner World").transform;
        hazardRoot = new GameObject("Hazards").transform;
        hazardRoot.SetParent(world, false);
        pickupRoot = new GameObject("Pickups").transform;
        pickupRoot.SetParent(world, false);

        Sprite sky = PixelArt.Sky(PixelCanvas.C(255, 221, 130), PixelCanvas.C(255, 245, 190));
        skyRenderer = NewRenderer("Sky", world, sky, -100, Vector3.zero);
        sunRenderer = NewRenderer("Sun", world, MakeDesertSun(), -85, new Vector3(5.8f, 3.3f, 0f));
        cloudLayer = new ScrollLayer("Desert Clouds", world, PixelArt.Clouds(), 3.1f, -80, 0.02f);
        duneLayer = new ScrollLayer("Distant Dunes", world, MakeDesertDunes(), GroundTop, -70, 0.055f);
        platformLayer = new ScrollLayer("Desert Platforms and Ladders", world, MakeDesertPlatforms(),
            GroundTop - 0.1f, -50, 0.18f);
        groundLayer = new ScrollLayer("Sandy Ground", world, MakeDesertGround(), GroundTop, 10, 1f);

        playerTransform = new GameObject("Runner").transform;
        playerTransform.SetParent(world, false);
        playerRenderer = playerTransform.gameObject.AddComponent<SpriteRenderer>();
        playerRenderer.sortingOrder = 20;
        playerTransform.localScale = Vector3.one * visualScale;
        runFrames = new[] { MakeRunner(false), MakeRunner(true) };
        crouchSprite = MakeRunnerCrouch();
        coinSprite = MakeCoin();
        cactusSprite = MakeCactus();
        spikesSprite = MakeSpikes();
        boulderSprite = MakeBoulder();
        vultureSprite = MakeVulture();
    }

    void BuildUI()
    {
        Transform ui = new GameObject("Runner UI").transform;
        scoreRenderer = NewRenderer("Score", ui, null, 120, new Vector3(-halfWidth + 2.1f, 4.05f, 0f));
        bestRenderer = NewRenderer("Best Score", ui, null, 120, new Vector3(-halfWidth + 2.1f, 3.58f, 0f));

        readyRoot = new GameObject("Ready Screen").transform;
        readyRoot.SetParent(ui, false);
        readyRootRenderer = NewRenderer("Ready Background", readyRoot, PixelArt.Panel(), 110, new Vector3(0f, 0.25f, 0f));
        readyRootRenderer.transform.localScale = new Vector3(1.05f, 1.1f, 1f);
        readyTitleRenderer = NewRenderer("Ready Title", readyRoot, null, 112, new Vector3(0f, 2.15f, 0f));
        readyHintRenderer = NewRenderer("Ready Hint", readyRoot, null, 112, new Vector3(0f, -0.75f, 0f));
        readyControlsRenderer = NewRenderer("Ready Controls", readyRoot, null, 112, new Vector3(0f, -1.42f, 0f));

        gameOverRoot = new GameObject("Game Over Screen").transform;
        gameOverRoot.SetParent(ui, false);
        gameOverRootRenderer = NewRenderer("Game Over Background", gameOverRoot, PixelArt.Panel(), 110, new Vector3(0f, 0.15f, 0f));
        gameOverRootRenderer.transform.localScale = new Vector3(1.05f, 1.1f, 1f);
        gameOverTitleRenderer = NewRenderer("Game Over Title", gameOverRoot, null, 112, new Vector3(0f, 2.12f, 0f));
        finalScoreRenderer = NewRenderer("Final Score", gameOverRoot, null, 112, new Vector3(0f, 0.45f, 0f));
        finalBestRenderer = NewRenderer("Final Best", gameOverRoot, null, 112, new Vector3(0f, -0.45f, 0f));
        retryRenderer = NewRenderer("Retry Hint", gameOverRoot, null, 112, new Vector3(0f, -1.78f, 0f));

        pausedRenderer = NewRenderer("Paused", ui, null, 130, new Vector3(0f, 0.2f, 0f));
        pausedRenderer.enabled = false;

        SetText(readyTitleRenderer, "PIXEL RUNNER", 0.055f, TextStyle.Title);
        SetText(readyHintRenderer, "SPACE TO START", 0.04f, TextStyle.White);
        SetText(readyControlsRenderer, "SPACE JUMP DOWN DUCK P PAUSE", 0.023f, TextStyle.Label);
        SetText(gameOverTitleRenderer, "GAME OVER", 0.06f, TextStyle.Red);
        SetText(retryRenderer, "SPACE OR R TO RETRY", 0.03f, TextStyle.White);
        SetText(pausedRenderer, "PAUSED P ESC RESUME", 0.03f, TextStyle.White);
        gameOverRoot.gameObject.SetActive(false);

        UpdateHud();
    }

    void SetText(SpriteRenderer renderer, string text, float pixelSize, TextStyle style)
    {
        renderer.sprite = PixelFont.Render(text, pixelSize, style);
    }

    void Restart()
    {
        for (int i = 0; i < hazards.Count; i++)
            if (hazards[i].transform != null) Destroy(hazards[i].transform.gameObject);
        for (int i = 0; i < pickups.Count; i++)
            if (pickups[i].transform != null) Destroy(pickups[i].transform.gameObject);
        hazards.Clear();
        pickups.Clear();

        state = State.Ready;
        paused = false;
        crouching = false;
        grounded = true;
        score = 0;
        bonusScore = 0;
        distance = 0f;
        speed = startSpeed;
        playerY = GroundTop;
        velocityY = 0f;
        scroll = 0f;
        spawnDistance = 5f;
        gameOverTimer = 0f;
        jumpBufferTimer = 0f;
        animationTimer = 0f;
        shownScore = -1;

        playerRenderer.sprite = runFrames[0];
        playerTransform.position = new Vector3(PlayerX, GroundTop, 0f);
        readyRoot.gameObject.SetActive(true);
        readyHintRenderer.enabled = true;
        gameOverRoot.gameObject.SetActive(false);
        pausedRenderer.enabled = false;
        UpdateHud();
    }

    void BeginRun()
    {
        state = State.Running;
        readyRoot.gameObject.SetActive(false);
        Jump();
    }

    void Jump()
    {
        if (!grounded || crouching) return;
        grounded = false;
        velocityY = jumpVelocity;
    }

    void EndRun()
    {
        if (state != State.Running) return;
        state = State.GameOver;
        paused = false;
        pausedRenderer.enabled = false;
        gameOverTimer = 0f;
        crouching = false;

        if (score > best)
        {
            best = score;
            PlayerPrefs.SetInt(BestKey, best);
            PlayerPrefs.Save();
        }

        SetText(finalScoreRenderer, "SCORE " + score, 0.05f, TextStyle.White);
        SetText(finalBestRenderer, "BEST " + best, 0.05f, TextStyle.White);
        gameOverRoot.gameObject.SetActive(true);
        UpdateHud();
    }

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        halfWidth = gameCamera.orthographicSize * gameCamera.aspect;

        bool jumpPressed = GameInput.FlapDown();
        bool restartPressed = GameInput.RestartDown();
        if (GameInput.PauseDown() && (state == State.Running || paused))
        {
            paused = !paused;
            pausedRenderer.enabled = paused;
        }

        if (paused)
        {
            UpdateVisuals(dt);
            return;
        }

        switch (state)
        {
            case State.Ready:
                scroll += startSpeed * 0.35f * dt;
                animationTimer += dt;
                playerY = GroundTop + Mathf.Sin(Time.time * 4f) * 0.04f;
                playerTransform.position = new Vector3(PlayerX, playerY, 0f);
                playerRenderer.sprite = runFrames[(int)(animationTimer * 8f) % runFrames.Length];
                readyHintRenderer.enabled = Mathf.Repeat(Time.time, 1f) < 0.72f;
                if (jumpPressed) BeginRun();
                break;

            case State.Running:
                TickRun(dt, jumpPressed);
                break;

            case State.GameOver:
                gameOverTimer += dt;
                if (gameOverTimer > 0.6f && (jumpPressed || restartPressed))
                    Restart();
                break;
        }

        UpdateVisuals(dt);
    }

    void TickRun(float dt, bool jumpPressed)
    {
        speed = Mathf.Min(maxSpeed, startSpeed + distance * speedIncrease);
        distance += speed * dt;
        scroll += speed * dt;
        score = Mathf.FloorToInt(distance * 2f) + bonusScore;

        if (jumpPressed) jumpBufferTimer = 0.18f;
        else jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - dt);

        crouching = grounded && GameInput.CrouchHeld();
        if (grounded && !crouching && jumpBufferTimer > 0f)
        {
            Jump();
            jumpBufferTimer = 0f;
        }

        if (!grounded)
        {
            velocityY += gravity * dt;
            playerY += velocityY * dt;
            if (playerY <= GroundTop)
            {
                playerY = GroundTop;
                velocityY = 0f;
                grounded = true;
            }
        }

        animationTimer += dt;
        if (crouching) playerRenderer.sprite = crouchSprite;
        else playerRenderer.sprite = runFrames[(int)(animationTimer * Mathf.Lerp(8f, 13f, (speed - startSpeed) / Mathf.Max(0.01f, maxSpeed - startSpeed))) % runFrames.Length];
        playerTransform.position = new Vector3(PlayerX, playerY, 0f);

        spawnDistance -= speed * dt;
        if (spawnDistance <= 0f)
        {
            SpawnHazard(halfWidth + 1.5f);
            spawnDistance = Random.Range(8.5f, 10f);
        }

        float playerHeight = (crouching ? 0.56f : 0.96f) * visualScale;
        float playerCenterY = playerY + playerHeight * 0.5f;
        for (int i = hazards.Count - 1; i >= 0; i--)
        {
            Hazard hazard = hazards[i];
            hazard.transform.position += Vector3.left * speed * dt;
            float hazardX = hazard.transform.position.x;

            if (Overlaps(PlayerX, playerCenterY, 0.62f * visualScale, playerHeight,
                    hazardX, hazard.centerY, hazard.width, hazard.height))
            {
                EndRun();
                return;
            }

            if (hazardX < -halfWidth - 2f)
            {
                Destroy(hazard.transform.gameObject);
                hazards.RemoveAt(i);
            }
        }

        for (int i = pickups.Count - 1; i >= 0; i--)
        {
            Pickup pickup = pickups[i];
            pickup.transform.position += Vector3.left * speed * dt;
            pickup.phase += dt * 5f;
            pickup.transform.position = new Vector3(pickup.transform.position.x,
                pickup.baseY + Mathf.Sin(pickup.phase) * 0.08f, 0f);

            if (Overlaps(PlayerX, playerCenterY, 0.7f * visualScale, playerHeight, pickup.transform.position.x,
                    pickup.transform.position.y, 0.65f * visualScale, 0.65f * visualScale))
            {
                bonusScore += 5;
                score += 5;
                Destroy(pickup.transform.gameObject);
                pickups.RemoveAt(i);
                continue;
            }

            if (pickup.transform.position.x < -halfWidth - 1f)
            {
                Destroy(pickup.transform.gameObject);
                pickups.RemoveAt(i);
            }
        }

        UpdateHud();
    }

    static bool Overlaps(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh)
    {
        return Mathf.Abs(ax - bx) * 2f < aw + bw && Mathf.Abs(ay - by) * 2f < ah + bh;
    }

    void SpawnHazard(float x)
    {
        float roll = Random.value;
        HazardType type;
        if (roll < 0.36f) type = HazardType.Cactus;
        else if (roll < 0.61f) type = HazardType.Spikes;
        else if (roll < 0.82f) type = HazardType.Boulder;
        else type = HazardType.Vulture;

        Sprite sprite;
        float width;
        float height;
        float y;
        switch (type)
        {
            case HazardType.Cactus:
                sprite = cactusSprite;
                width = 0.95f * visualScale;
                height = 1.05f * visualScale;
                y = GroundTop + height * 0.5f;
                break;
            case HazardType.Spikes:
                sprite = spikesSprite;
                width = 1.25f * visualScale;
                height = 0.58f * visualScale;
                y = GroundTop + height * 0.5f;
                break;
            case HazardType.Boulder:
                sprite = boulderSprite;
                width = 0.95f * visualScale;
                height = 0.78f * visualScale;
                y = GroundTop + height * 0.5f;
                break;
            default:
                sprite = vultureSprite;
                width = 1.12f * visualScale;
                height = 0.58f * visualScale;
                y = GroundTop + 1.05f * visualScale;
                break;
        }

        GameObject go = new GameObject(type.ToString());
        go.transform.SetParent(hazardRoot, false);
        go.transform.position = new Vector3(x, y, 0f);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 15;
        renderer.transform.localScale = new Vector3(width / sprite.bounds.size.x, height / sprite.bounds.size.y, 1f);
        hazards.Add(new Hazard { transform = go.transform, type = type, width = width, height = height, centerY = y });

        if (Random.value < 0.45f)
            SpawnCoin(x + Random.Range(1.7f, 3f), GroundTop + Random.Range(1.25f, 2.3f));
    }

    void SpawnCoin(float x, float y)
    {
        GameObject go = new GameObject("Bonus Coin");
        go.transform.SetParent(pickupRoot, false);
        go.transform.position = new Vector3(x, y, 0f);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = coinSprite;
        renderer.sortingOrder = 18;
        go.transform.localScale = Vector3.one * visualScale * 0.75f;
        pickups.Add(new Pickup { transform = go.transform, phase = Random.Range(0f, 6.28f), baseY = y });
    }

    void UpdateHud()
    {
        if (score != shownScore)
        {
            shownScore = score;
            SetText(scoreRenderer, "SCORE " + score, 0.05f, TextStyle.White);
        }

        SetText(bestRenderer, "BEST " + best, 0.05f, TextStyle.White);
        scoreRenderer.transform.localPosition = new Vector3(-halfWidth + 2.1f, 4.05f, 0f);
        bestRenderer.transform.localPosition = new Vector3(-halfWidth + 2.1f, 3.58f, 0f);
    }

    void UpdateVisuals(float dt)
    {
        float w = halfWidth;
        skyRenderer.transform.localScale = new Vector3((2f * w + 2f) * 64f, 10.6f, 1f);
        sunRenderer.transform.position = new Vector3(Mathf.Min(w * 0.58f, 6f), 3.35f, 0f);
        cloudLayer.Update(scroll, w);
        duneLayer.Update(scroll, w);
        platformLayer.Update(scroll, w);
        groundLayer.Update(scroll, w);
        if (gameOverTimer > 0.6f && state == State.GameOver)
            retryRenderer.enabled = Mathf.Repeat(Time.time, 1f) < 0.72f;
        else
            retryRenderer.enabled = state == State.GameOver;
    }

    static Sprite MakeDesertSun()
    {
        PixelCanvas canvas = new PixelCanvas(48, 48);
        canvas.FillEllipse(24, 24, 20, 20, PixelCanvas.C(247, 177, 64));
        canvas.FillEllipse(23, 25, 16, 16, PixelCanvas.C(255, 218, 112));
        canvas.FillEllipse(19, 30, 7, 6, PixelCanvas.C(255, 235, 160));
        canvas.Outline(PixelCanvas.C(225, 148, 55), true);
        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 0.5f));
    }

    static void FillTriangle(PixelCanvas canvas, int x0, int y0, int x1, int y1, int x2, int y2, Color32 color)
    {
        int minY = Mathf.Min(y0, Mathf.Min(y1, y2));
        int maxY = Mathf.Max(y0, Mathf.Max(y1, y2));
        for (int y = minY; y <= maxY; y++)
        {
            float t0 = y1 == y0 ? 0f : Mathf.Clamp01((y - y0) / (float)(y1 - y0));
            float t1 = y2 == y1 ? 0f : Mathf.Clamp01((y - y1) / (float)(y2 - y1));
            float t2 = y2 == y0 ? 0f : Mathf.Clamp01((y - y0) / (float)(y2 - y0));
            float[] intersections = new float[3];
            int count = 0;
            if ((y >= Mathf.Min(y0, y1)) && (y <= Mathf.Max(y0, y1))) intersections[count++] = Mathf.Lerp(x0, x1, t0);
            if ((y >= Mathf.Min(y1, y2)) && (y <= Mathf.Max(y1, y2))) intersections[count++] = Mathf.Lerp(x1, x2, t1);
            if ((y >= Mathf.Min(y0, y2)) && (y <= Mathf.Max(y0, y2))) intersections[count++] = Mathf.Lerp(x0, x2, t2);

            if (count < 2) continue;
            float left = intersections[0];
            float right = intersections[0];
            for (int i = 1; i < count; i++)
            {
                left = Mathf.Min(left, intersections[i]);
                right = Mathf.Max(right, intersections[i]);
            }
            canvas.FillRect(Mathf.CeilToInt(left), y, Mathf.FloorToInt(right), y, color);
        }
    }

    static Sprite MakeDesertDunes()
    {
        const int width = 480;
        const int height = 160;
        PixelCanvas canvas = new PixelCanvas(width, height);
        Color32 far = PixelCanvas.C(239, 183, 109);
        Color32 mid = PixelCanvas.C(246, 198, 119);
        Color32 light = PixelCanvas.C(255, 213, 139);

        canvas.FillEllipse(84, 5, 120, 74, far);
        canvas.FillEllipse(258, 3, 154, 82, far);
        canvas.FillEllipse(445, 8, 118, 68, far);
        FillTriangle(canvas, 0, 0, 80, 120, 182, 0, mid);
        FillTriangle(canvas, 130, 0, 278, 145, 420, 0, mid);
        FillTriangle(canvas, 352, 0, 420, 94, 480, 0, mid);
        FillTriangle(canvas, 194, 0, 278, 104, 363, 0, light);
        canvas.FillEllipse(20, 6, 90, 38, light);
        canvas.FillEllipse(365, 4, 110, 40, light);

        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 0f));
    }

    static void DrawDesertCoin(PixelCanvas canvas, int x, int y)
    {
        canvas.FillEllipse(x, y, 5, 8, PixelCanvas.C(185, 112, 31));
        canvas.FillEllipse(x, y, 3, 6, PixelCanvas.C(255, 183, 42));
        canvas.FillRect(x - 1, y + 2, x, y + 4, PixelCanvas.C(255, 232, 116));
    }

    static void DrawDesertCactus(PixelCanvas canvas, int x, int y, int scale)
    {
        Color32 dark = PixelCanvas.C(42, 133, 73);
        Color32 green = PixelCanvas.C(54, 171, 82);
        Color32 highlight = PixelCanvas.C(126, 212, 101);
        canvas.FillRoundRect(x - 4 * scale, y, x + 4 * scale, y + 23 * scale, 2 * scale, green);
        canvas.FillRect(x - scale, y + 12 * scale, x, y + 21 * scale, highlight);
        canvas.FillRect(x - 10 * scale, y + 9 * scale, x - 5 * scale, y + 13 * scale, dark);
        canvas.FillRect(x - 10 * scale, y + 13 * scale, x - 7 * scale, y + 19 * scale, green);
        canvas.FillRect(x + 5 * scale, y + 15 * scale, x + 10 * scale, y + 19 * scale, dark);
        canvas.FillRect(x + 7 * scale, y + 19 * scale, x + 10 * scale, y + 24 * scale, green);
    }

    static void DrawLadder(PixelCanvas canvas, int centerX, int bottom, int top)
    {
        Color32 wood = PixelCanvas.C(153, 85, 43);
        Color32 light = PixelCanvas.C(203, 130, 68);
        canvas.FillRect(centerX - 9, bottom, centerX - 5, top, wood);
        canvas.FillRect(centerX + 5, bottom, centerX + 9, top, wood);
        canvas.FillRect(centerX - 8, bottom, centerX - 7, top, light);
        canvas.FillRect(centerX + 6, bottom, centerX + 7, top, light);
        for (int y = bottom + 5; y < top; y += 9)
        {
            canvas.FillRect(centerX - 6, y, centerX + 6, y + 2, wood);
            canvas.FillRect(centerX - 5, y + 1, centerX + 5, y + 1, light);
        }
    }

    static void DrawFloatingPlatform(PixelCanvas canvas, int left, int right, int top)
    {
        Color32 soil = PixelCanvas.C(183, 101, 48);
        Color32 soilLight = PixelCanvas.C(216, 138, 69);
        Color32 soilShadow = PixelCanvas.C(143, 75, 43);
        Color32 topColor = PixelCanvas.C(255, 216, 135);
        Color32 topLight = PixelCanvas.C(255, 235, 174);

        canvas.FillRect(left + 8, top - 15, right - 7, top - 5, soil);
        FillTriangle(canvas, left + 10, top - 15, (left + right) / 2, top - 34, right - 8, top - 15, soilShadow);
        canvas.FillRect(left + 10, top - 12, right - 10, top - 8, soilLight);
        for (int x = left + 16; x < right - 12; x += 17)
        {
            int y = top - 11 - (x % 3);
            canvas.FillEllipse(x, y, 2, 2, soilShadow);
            canvas.FillRect(x + 6, y + 4, x + 8, y + 5, PixelCanvas.C(238, 165, 89));
        }

        float center = (left + right) * 0.5f;
        float radius = (right - left) * 0.5f;
        canvas.FillEllipse(center, top, radius, 8, PixelCanvas.C(190, 118, 57));
        canvas.FillEllipse(center, top + 2, radius - 4, 5, topColor);
        canvas.FillEllipse(center - radius * 0.22f, top + 4, radius * 0.48f, 2,
            PixelCanvas.C(255, 242, 190));
    }

    static Sprite MakeDesertPlatforms()
    {
        const int width = 480;
        const int height = 168;
        PixelCanvas canvas = new PixelCanvas(width, height);

        DrawFloatingPlatform(canvas, 16, 112, 76);
        DrawLadder(canvas, 42, 5, 71);
        DrawDesertCactus(canvas, 85, 82, 2);
        DrawDesertCactus(canvas, 101, 82, 1);
        DrawDesertCoin(canvas, 69, 102);
        DrawDesertCoin(canvas, 82, 104);
        DrawDesertCoin(canvas, 95, 102);

        DrawFloatingPlatform(canvas, 164, 292, 119);
        DrawLadder(canvas, 196, 43, 114);
        DrawDesertCactus(canvas, 251, 125, 1);
        DrawDesertCactus(canvas, 267, 125, 2);
        DrawDesertCoin(canvas, 219, 148);
        DrawDesertCoin(canvas, 234, 153);
        DrawDesertCoin(canvas, 249, 156);
        DrawDesertCoin(canvas, 264, 153);

        DrawFloatingPlatform(canvas, 337, 456, 84);
        DrawLadder(canvas, 420, 7, 79);
        DrawDesertCactus(canvas, 367, 90, 2);
        DrawDesertCactus(canvas, 389, 90, 1);
        DrawDesertCoin(canvas, 360, 108);
        DrawDesertCoin(canvas, 375, 111);
        DrawDesertCoin(canvas, 390, 108);
        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 0f));
    }

    static Sprite MakeDesertGround()
    {
        const int width = 192;
        const int height = 48;
        PixelCanvas canvas = new PixelCanvas(width, height);
        canvas.FillRect(0, 0, width - 1, height - 1, PixelCanvas.C(189, 105, 50));
        canvas.FillRect(0, 0, width - 1, 35, PixelCanvas.C(183, 96, 48));
        canvas.FillRect(0, 35, width - 1, 37, PixelCanvas.C(215, 132, 64));

        for (int x = 0; x < width; x += 2)
        {
            canvas.Set(x, 47, PixelCanvas.C(255, 235, 174));
            canvas.Set(x + 1, 46, PixelCanvas.C(255, 222, 141));
        }
        canvas.FillRect(0, 38, width - 1, 45, PixelCanvas.C(244, 194, 109));
        canvas.FillRect(0, 40, width - 1, 43, PixelCanvas.C(255, 215, 131));

        System.Random random = new System.Random(57);
        for (int i = 0; i < 145; i++)
        {
            int x = random.Next(width);
            int y = random.Next(3, 33);
            Color32 speckle = i % 3 == 0 ? PixelCanvas.C(238, 151, 70) : PixelCanvas.C(151, 76, 43);
            canvas.FillRect(x, y, x + random.Next(1, 4), y + random.Next(1, 3), speckle);
        }

        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 1f));
    }

    Sprite MakeRunner(bool alternate)
    {
        PixelCanvas canvas = new PixelCanvas(24, 30);
        Color32 outline = PixelCanvas.C(50, 42, 48);
        Color32 body = PixelCanvas.C(232, 92, 48);
        Color32 bodyLight = PixelCanvas.C(255, 164, 72);
        Color32 pants = PixelCanvas.C(50, 94, 146);
        Color32 shoe = PixelCanvas.C(72, 48, 36);
        canvas.FillRoundRect(6, 8, 17, 22, 3, body);
        canvas.FillRect(7, 18, 16, 21, bodyLight);
        canvas.FillEllipse(12, 24, 6, 5, PixelCanvas.C(246, 198, 142));
        canvas.FillRect(14, 24, 15, 26, outline);
        canvas.FillRect(14, 25, 14, 26, PixelCanvas.C(255, 255, 255));
        canvas.FillRect(6, 13, 3, 17, body);
        canvas.FillRect(4, 15, 3, 18, bodyLight);

        if (alternate)
        {
            canvas.FillRect(8, 3, 11, 9, pants);
            canvas.FillRect(14, 5, 17, 9, pants);
            canvas.FillRect(6, 2, 12, 2, shoe);
            canvas.FillRect(14, 4, 19, 4, shoe);
        }
        else
        {
            canvas.FillRect(8, 5, 11, 9, pants);
            canvas.FillRect(13, 3, 16, 9, pants);
            canvas.FillRect(6, 4, 12, 4, shoe);
            canvas.FillRect(12, 2, 18, 2, shoe);
        }

        canvas.Outline(outline, true);
        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 0f));
    }

    Sprite MakeRunnerCrouch()
    {
        PixelCanvas canvas = new PixelCanvas(30, 20);
        Color32 outline = PixelCanvas.C(50, 42, 48);
        canvas.FillRoundRect(7, 5, 23, 12, 3, PixelCanvas.C(232, 92, 48));
        canvas.FillEllipse(21, 14, 5, 4, PixelCanvas.C(246, 198, 142));
        canvas.FillRect(23, 14, 24, 16, outline);
        canvas.FillRect(23, 15, 23, 16, PixelCanvas.C(255, 255, 255));
        canvas.FillRect(9, 2, 14, 5, PixelCanvas.C(50, 94, 146));
        canvas.FillRect(18, 2, 23, 5, PixelCanvas.C(50, 94, 146));
        canvas.FillRect(6, 1, 15, 1, PixelCanvas.C(72, 48, 36));
        canvas.FillRect(16, 1, 25, 1, PixelCanvas.C(72, 48, 36));
        canvas.Outline(outline, true);
        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 0f));
    }

    Sprite MakeCactus()
    {
        PixelCanvas canvas = new PixelCanvas(26, 34);
        Color32 green = PixelCanvas.C(52, 156, 80);
        Color32 light = PixelCanvas.C(122, 214, 106);
        canvas.FillRoundRect(9, 0, 17, 33, 3, green);
        canvas.FillRect(11, 24, 12, 30, light);
        canvas.FillRect(3, 12, 9, 16, green);
        canvas.FillRect(3, 16, 6, 23, green);
        canvas.FillRect(18, 19, 23, 23, green);
        canvas.FillRect(20, 23, 23, 29, green);
        canvas.Outline(PixelCanvas.C(38, 104, 64), true);
        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 0.5f));
    }

    Sprite MakeSpikes()
    {
        PixelCanvas canvas = new PixelCanvas(30, 16);
        Color32 spike = PixelCanvas.C(230, 82, 62);
        canvas.FillRect(0, 0, 29, 3, PixelCanvas.C(98, 56, 48));
        for (int x = 1; x < 29; x += 7)
        {
            canvas.FillRect(x + 2, 4, x + 4, 8, spike);
            canvas.Set(x + 3, 9, PixelCanvas.C(255, 194, 110));
        }
        canvas.Outline(PixelCanvas.C(88, 48, 42), true);
        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 0.5f));
    }

    Sprite MakeBoulder()
    {
        PixelCanvas canvas = new PixelCanvas(30, 24);
        canvas.FillEllipse(9, 8, 8, 7, PixelCanvas.C(111, 92, 78));
        canvas.FillEllipse(18, 9, 9, 8, PixelCanvas.C(133, 109, 88));
        canvas.FillEllipse(14, 14, 8, 7, PixelCanvas.C(160, 135, 105));
        canvas.FillEllipse(11, 16, 3, 2, PixelCanvas.C(196, 165, 126));
        canvas.FillRect(6, 5, 11, 7, PixelCanvas.C(88, 75, 67));
        canvas.FillRect(17, 7, 22, 9, PixelCanvas.C(103, 86, 73));
        canvas.Outline(PixelCanvas.C(83, 67, 57), true);
        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 0.5f));
    }

    Sprite MakeVulture()
    {
        PixelCanvas canvas = new PixelCanvas(32, 24);
        canvas.FillEllipse(15, 10, 10, 6, PixelCanvas.C(133, 94, 64));
        canvas.FillEllipse(14, 13, 6, 3, PixelCanvas.C(191, 148, 99));
        canvas.FillEllipse(8, 15, 8, 4, PixelCanvas.C(91, 73, 61));
        canvas.FillEllipse(18, 15, 8, 4, PixelCanvas.C(91, 73, 61));
        canvas.FillEllipse(24, 13, 4, 4, PixelCanvas.C(191, 148, 99));
        canvas.FillRect(25, 10, 31, 12, PixelCanvas.C(232, 169, 57));
        canvas.FillRect(25, 15, 27, 16, PixelCanvas.C(255, 255, 228));
        canvas.Set(26, 15, PixelCanvas.C(50, 42, 48));
        canvas.FillRect(12, 5, 13, 8, PixelCanvas.C(111, 85, 67));
        canvas.FillRect(18, 5, 19, 8, PixelCanvas.C(111, 85, 67));
        canvas.Outline(PixelCanvas.C(73, 58, 48), true);
        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 0.5f));
    }

    Sprite MakeCoin()
    {
        PixelCanvas canvas = new PixelCanvas(16, 16);
        canvas.FillEllipse(8, 8, 7, 7, PixelCanvas.C(180, 112, 24));
        canvas.FillEllipse(8, 8, 5, 5, PixelCanvas.C(255, 210, 54));
        canvas.FillRect(7, 4, 8, 11, PixelCanvas.C(255, 244, 154));
        canvas.Outline(PixelCanvas.C(120, 74, 24), true);
        return canvas.ToSprite(PixelArt.PPU, new Vector2(0.5f, 0.5f));
    }
}
