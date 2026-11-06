using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Flappy Bird for Unity 6. Everything (art, sound, UI) is generated in code.
/// Attach to any GameObject - or just press Play: the game bootstraps itself.
/// Controls: Space / Up / W / Click / Tap = flap,  R = restart,  P or Esc = pause,  M = mute.
/// </summary>
[DisallowMultipleComponent]
public class FlappyGame : MonoBehaviour
{
    [Header("Bird")]
    public float gravity = -24f;
    public float flapVelocity = 7.4f;
    public float maxFallSpeed = -13f;

    [Header("Pipes")]
    public float startSpeed = 3.1f;
    public float maxSpeed = 4.6f;
    public float pipeSpacing = 4.3f;
    public float startGap = 3.15f;
    public float minGap = 2.55f;

    [Header("Look")]
    [Tooltip("Alternate between day and night runs.")]
    public bool nightMode = true;

    // ------------------------------------------------------------------ types

    enum State { Ready, Playing, Dying, GameOver }

    sealed class Pipe
    {
        public Transform root;
        public float x, gapY, gap;
        public bool scored;
    }

    sealed class Theme
    {
        public bool night;
        public Color clouds, skyline, bushes, ground, pipes;
    }

    static readonly Theme DayTheme = new Theme
    {
        night = false,
        clouds = Color.white, skyline = Color.white, bushes = Color.white, ground = Color.white, pipes = Color.white
    };

    static readonly Theme NightTheme = new Theme
    {
        night = true,
        clouds = new Color(0.42f, 0.45f, 0.70f), skyline = new Color(0.40f, 0.42f, 0.62f),
        bushes = new Color(0.36f, 0.46f, 0.50f), ground = new Color(0.55f, 0.55f, 0.72f),
        pipes = new Color(0.62f, 0.66f, 0.85f)
    };

    // -------------------------------------------------------------- constants

    const string BestKey = "FlappyBest";
    const float GroundTop = -4f;
    const float BodyW = 26f / PixelArt.PPU;
    const float CapW = 30f / PixelArt.PPU;
    const float CapH = 13f / PixelArt.PPU;
    const float BirdR = 0.29f;
    static readonly int[] WingSeq = { 0, 1, 2, 1 };

    // ----------------------------------------------------------------- state

    State state = State.Ready;
    bool paused, muted, grounded, newBest;
    int score, best, runs;
    float scroll, speed;
    float birdX, birdY, velY, birdAngle, wingT;
    float lastGapY;
    float deathTimer, dieSfxTimer, goTimer, toastTimer, shake, flashAlpha;
    int countShown;
    float halfW = 8f;

    // ----------------------------------------------------------------- scene

    Camera cam;
    Vector3 camBase;
    AudioSource audioSrc;
    Transform world, pipeParent, birdTr;
    SpriteRenderer skySR, celestialSR, birdSR, flashSR;
    ScrollLayer starsLayer, cloudLayer, skylineLayer, windowLayer, bushLayer, groundLayer;
    Sprite skyDay, skyNight, sunSprite, moonSprite, pipeBodySprite, pipeCapSprite;
    Sprite[] birdFrames;
    readonly Dictionary<int, Sprite[]> skinCache = new Dictionary<int, Sprite[]>();
    readonly List<Pipe> pipes = new List<Pipe>();
    ParticleFX fx;
    Theme theme = DayTheme;
    Color32 featherColor = new Color32(250, 204, 48, 255);
    int skinIndex;

    // -------------------------------------------------------------------- UI

    Transform uiRoot, readyRoot, goRoot, panelTr, goTitleTr;
    SpriteRenderer scoreSR, tapSR, bestReadySR, retrySR, pausedSR, toastSR;
    SpriteRenderer scoreValueSR, bestValueSR, medalSR, newSR;

    // ------------------------------------------------------------- bootstrap

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (FindAnyObjectByType<FlappyGame>() == null && FindAnyObjectByType<RunnerGame>() == null)
            new GameObject("FlappyGame").AddComponent<FlappyGame>();
    }

    void Start()
    {
        Application.targetFrameRate = 60;
        best = PlayerPrefs.GetInt(BestKey, 0);

        SetupCamera();
        SoundFX.Build();
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;

        BuildWorld();
        BuildUI();
        fx = new ParticleFX(world, PixelArt.Particle(), 128);
        Restart();
    }

    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
        }
        if (FindAnyObjectByType<AudioListener>() == null)
            cam.gameObject.AddComponent<AudioListener>();

        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(120, 200, 240, 255);
        camBase = cam.transform.position;
        halfW = cam.orthographicSize * cam.aspect;
    }

    // ----------------------------------------------------------------- build

    SpriteRenderer NewSR(string objName, Transform parent, Sprite sprite, int order, Vector3 localPos)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    void BuildWorld()
    {
        world = new GameObject("World").transform;
        pipeParent = new GameObject("Pipes").transform;
        pipeParent.SetParent(world, false);

        skyDay = PixelArt.Sky(PixelCanvas.C(78, 172, 236), PixelCanvas.C(198, 238, 246));
        skyNight = PixelArt.Sky(PixelCanvas.C(14, 18, 52), PixelCanvas.C(78, 70, 140));
        sunSprite = PixelArt.Sun();
        moonSprite = PixelArt.Moon();

        skySR = NewSR("Sky", world, skyDay, -100, Vector3.zero);
        starsLayer = new ScrollLayer("Stars", world, PixelArt.Stars(), 0.2f, -90, 0.015f);
        celestialSR = NewSR("Celestial", world, sunSprite, -85, new Vector3(0f, 3.4f, 0f));
        cloudLayer = new ScrollLayer("Clouds", world, PixelArt.Clouds(), 3.0f, -80, 0.08f);

        Sprite buildings, windows;
        PixelArt.Skyline(out buildings, out windows);
        skylineLayer = new ScrollLayer("Skyline", world, buildings, GroundTop - 0.3f, -70, 0.22f);
        windowLayer = new ScrollLayer("Windows", world, windows, GroundTop - 0.3f, -69, 0.22f);
        bushLayer = new ScrollLayer("Bushes", world, PixelArt.Bushes(), GroundTop - 0.2f, -60, 0.5f);
        groundLayer = new ScrollLayer("Ground", world, PixelArt.Ground(), GroundTop, 10, 1f);

        pipeBodySprite = PixelArt.PipeBody();
        pipeCapSprite = PixelArt.PipeCap();

        birdTr = new GameObject("Bird").transform;
        birdTr.SetParent(world, false);
        birdSR = birdTr.gameObject.AddComponent<SpriteRenderer>();
        birdSR.sortingOrder = 20;

        flashSR = NewSR("Flash", world, PixelArt.White(), 300, Vector3.zero);
        flashSR.transform.localScale = new Vector3(80f, 40f, 1f);
        flashSR.color = new Color(1f, 1f, 1f, 0f);
    }

    void SetText(SpriteRenderer sr, string text, float pixelSize, TextStyle style)
    {
        sr.sprite = PixelFont.Render(text, pixelSize, style);
    }

    void BuildUI()
    {
        uiRoot = new GameObject("UI").transform;

        scoreSR = NewSR("Score", uiRoot, null, 120, new Vector3(0f, 3.75f, 0f));
        scoreSR.enabled = false;

        // --- Ready screen
        readyRoot = new GameObject("Ready").transform;
        readyRoot.SetParent(uiRoot, false);
        var titleA = NewSR("TitleA", readyRoot, null, 110, new Vector3(0f, 2.6f, 0f));
        var titleB = NewSR("TitleB", readyRoot, null, 110, new Vector3(0f, 1.4f, 0f));
        SetText(titleA, "FLAPPY", 0.15f, TextStyle.Title);
        SetText(titleB, "BIRD", 0.15f, TextStyle.Title);
        tapSR = NewSR("Tap", readyRoot, null, 110, new Vector3(0f, -1.9f, 0f));
        SetText(tapSR, "TAP TO FLAP", 0.08f, TextStyle.White);
        bestReadySR = NewSR("BestReady", readyRoot, null, 110, new Vector3(0f, -2.75f, 0f));

        // --- Game over screen
        goRoot = new GameObject("GameOver").transform;
        goRoot.SetParent(uiRoot, false);

        var goTitle = NewSR("GameOverTitle", goRoot, null, 110, Vector3.zero);
        SetText(goTitle, "GAME OVER", 0.1f, TextStyle.Red);
        goTitleTr = goTitle.transform;

        var panelSR = NewSR("Panel", goRoot, PixelArt.Panel(), 110, Vector3.zero);
        panelTr = panelSR.transform;

        var scoreLabel = NewSR("ScoreLabel", panelTr, null, 111, new Vector3(1.0f, 0.95f, 0f));
        SetText(scoreLabel, "SCORE", 0.05f, TextStyle.Label);
        scoreValueSR = NewSR("ScoreValue", panelTr, null, 111, new Vector3(1.0f, 0.5f, 0f));
        var bestLabel = NewSR("BestLabel", panelTr, null, 111, new Vector3(1.0f, -0.12f, 0f));
        SetText(bestLabel, "BEST", 0.05f, TextStyle.Label);
        bestValueSR = NewSR("BestValue", panelTr, null, 111, new Vector3(1.0f, -0.57f, 0f));
        medalSR = NewSR("Medal", panelTr, null, 111, new Vector3(-1.55f, -0.05f, 0f));
        newSR = NewSR("New", panelTr, null, 112, new Vector3(-1.55f, 0.98f, 0f));
        SetText(newSR, "NEW", 0.045f, TextStyle.Badge);

        retrySR = NewSR("Retry", goRoot, null, 110, new Vector3(0f, -1.95f, 0f));
        SetText(retrySR, "TAP TO RETRY", 0.07f, TextStyle.White);
        goRoot.gameObject.SetActive(false);

        // --- overlays
        pausedSR = NewSR("Paused", uiRoot, null, 150, new Vector3(0f, 0.2f, 0f));
        SetText(pausedSR, "PAUSED", 0.12f, TextStyle.White);
        pausedSR.enabled = false;

        toastSR = NewSR("Toast", uiRoot, null, 150, new Vector3(0f, -3.0f, 0f));
        toastSR.enabled = false;
    }

    // ------------------------------------------------------------ game flow

    Sprite[] FramesForSkin(int index)
    {
        Sprite[] frames;
        if (!skinCache.TryGetValue(index, out frames))
        {
            frames = PixelArt.MakeBird(PixelArt.Skins[index]);
            skinCache[index] = frames;
        }
        return frames;
    }

    void ApplyTheme(Theme t)
    {
        theme = t;
        skySR.sprite = t.night ? skyNight : skyDay;
        celestialSR.sprite = t.night ? moonSprite : sunSprite;
        cam.backgroundColor = t.night ? (Color)new Color32(40, 40, 96, 255) : (Color)new Color32(120, 200, 240, 255);

        cloudLayer.SetColor(t.clouds);
        skylineLayer.SetColor(t.skyline);
        bushLayer.SetColor(t.bushes);
        groundLayer.SetColor(t.ground);
        starsLayer.SetColor(new Color(1f, 1f, 1f, t.night ? 1f : 0f));
        windowLayer.SetColor(new Color(1f, 1f, 1f, t.night ? 1f : 0f));
    }

    void Restart()
    {
        for (int i = 0; i < pipes.Count; i++) Destroy(pipes[i].root.gameObject);
        pipes.Clear();
        fx.Clear();

        runs++;
        score = 0;
        velY = 0f;
        birdAngle = 0f;
        grounded = false;
        paused = false;
        pausedSR.enabled = false;
        shake = 0f;
        flashAlpha = 0f;
        speed = startSpeed;
        lastGapY = 0.4f;

        ApplyTheme(nightMode && runs > 1 && Random.value < 0.5f ? NightTheme : DayTheme);

        skinIndex = Random.Range(0, PixelArt.Skins.Length);
        birdFrames = FramesForSkin(skinIndex);
        featherColor = PixelArt.Skins[skinIndex].Main;
        birdSR.sprite = birdFrames[1];

        birdX = Mathf.Clamp(-halfW * 0.38f, -3.2f, -1.1f);
        birdY = 0f;
        birdTr.position = new Vector3(birdX, birdY, 0f);
        birdTr.rotation = Quaternion.identity;

        scoreSR.enabled = false;
        goRoot.gameObject.SetActive(false);
        readyRoot.gameObject.SetActive(true);
        if (best > 0)
        {
            bestReadySR.enabled = true;
            SetText(bestReadySR, "BEST " + best, 0.06f, TextStyle.White);
        }
        else bestReadySR.enabled = false;

        state = State.Ready;
    }

    void StartGame()
    {
        state = State.Playing;
        readyRoot.gameObject.SetActive(false);
        scoreSR.enabled = true;
        RefreshScore();
        Flap();
    }

    void Flap()
    {
        velY = flapVelocity;
        Play(SoundFX.Flap, 0.6f);
        for (int i = 0; i < 3; i++)
        {
            fx.Emit(new Vector2(birdX - 0.35f, birdY - 0.1f),
                    new Vector2(Random.Range(-1.8f, -0.6f), Random.Range(-1.2f, 0.2f)),
                    new Color(1f, 1f, 1f, 0.9f), 0.35f, 0.2f, 0.05f, 0f, 0f);
        }
    }

    void Die(bool hitPipe)
    {
        if (state != State.Playing) return;
        state = State.Dying;
        deathTimer = 0f;
        shake = 0.28f;
        flashAlpha = 0.85f;
        Play(SoundFX.Hit, 0.9f);

        if (hitPipe)
        {
            grounded = false;
            velY = Mathf.Min(velY, 0f);
            dieSfxTimer = 0.12f;
        }
        else
        {
            grounded = true;
            velY = 0f;
            dieSfxTimer = -1f;
        }

        for (int i = 0; i < 14; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float s = Random.Range(2f, 5.5f);
            Color c = (i % 3 == 0) ? Color.white : (Color)featherColor;
            fx.Emit(new Vector2(birdX, birdY), new Vector2(Mathf.Cos(a) * s, Mathf.Sin(a) * s + 1.5f),
                    c, Random.Range(0.6f, 1.0f), 0.2f, 0.06f, -9f, Random.Range(-360f, 360f));
        }
    }

    void ShowGameOver()
    {
        state = State.GameOver;
        goTimer = 0f;
        countShown = -1;

        newBest = score > best;
        if (newBest)
        {
            best = score;
            PlayerPrefs.SetInt(BestKey, best);
            PlayerPrefs.Save();
        }

        scoreSR.enabled = false;
        goRoot.gameObject.SetActive(true);
        retrySR.enabled = false;

        int tier = score >= 40 ? 3 : score >= 30 ? 2 : score >= 20 ? 1 : score >= 10 ? 0 : -1;
        medalSR.enabled = tier >= 0;
        if (tier >= 0) medalSR.sprite = PixelArt.Medal(tier);
        newSR.enabled = newBest;
        SetText(bestValueSR, best.ToString(), 0.09f, TextStyle.White);

        panelTr.localPosition = new Vector3(0f, -6f, 0f);
        goTitleTr.localPosition = new Vector3(0f, 6f, 0f);
        Play(SoundFX.Swoosh, 0.7f);
    }

    // ---------------------------------------------------------------- update

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        halfW = cam.orthographicSize * cam.aspect;

        if (GameInput.MuteDown()) ToggleMute();
        if (GameInput.PauseDown() && (state == State.Playing || paused)) SetPaused(!paused);

        bool flap = GameInput.FlapDown();
        bool restartKey = GameInput.RestartDown();

        if (paused && flap)
        {
            SetPaused(false);
            flap = false;
        }

        if (!paused)
        {
            switch (state)
            {
                case State.Ready: TickReady(dt, flap); break;
                case State.Playing: TickPlaying(dt, flap); break;
                case State.Dying: TickDying(dt); break;
                case State.GameOver: TickGameOver(dt, flap || restartKey); break;
            }
            fx.Update(dt);
        }

        UpdateVisuals(dt);
    }

    void TickReady(float dt, bool flap)
    {
        birdX = Mathf.Clamp(-halfW * 0.38f, -3.2f, -1.1f);
        birdY = Mathf.Sin(Time.time * 5f) * 0.18f;
        scroll += startSpeed * 0.8f * dt;
        AnimateWing(dt, 8f);
        tapSR.enabled = Mathf.Repeat(Time.time, 1.1f) < 0.75f;
        PlaceBird(dt, 0f);
        if (flap) StartGame();
    }

    void TickPlaying(float dt, bool flap)
    {
        speed = Mathf.Min(maxSpeed, startSpeed + score * 0.03f);
        scroll += speed * dt;

        if (flap) Flap();
        velY = Mathf.Max(maxFallSpeed, velY + gravity * dt);
        birdY += velY * dt;

        float ceiling = cam.orthographicSize - BirdR;
        if (birdY > ceiling)
        {
            birdY = ceiling;
            if (velY > 0f) velY = 0f;
        }

        AnimateWing(dt, 12f);
        PlaceBird(dt, Mathf.Clamp(velY * 5f, -70f, 30f));

        // spawn
        float lastX = pipes.Count > 0 ? pipes[pipes.Count - 1].x : halfW + 2.5f - pipeSpacing;
        while (lastX < halfW + 1f)
        {
            lastX += pipeSpacing;
            SpawnPipe(lastX);
        }

        // move / score / collide / cull
        for (int i = pipes.Count - 1; i >= 0; i--)
        {
            Pipe p = pipes[i];
            p.x -= speed * dt;
            p.root.position = new Vector3(p.x, 0f, 0f);

            if (!p.scored && p.x < birdX)
            {
                p.scored = true;
                score++;
                RefreshScore();
                Play(SoundFX.Point, 0.7f);
                Sparkle();
            }

            if (HitsPipe(p))
            {
                Die(true);
                return;
            }

            if (p.x < -halfW - 2f)
            {
                Destroy(p.root.gameObject);
                pipes.RemoveAt(i);
            }
        }

        if (birdY - BirdR <= GroundTop)
        {
            birdY = GroundTop + BirdR;
            Die(false);
        }
    }

    void TickDying(float dt)
    {
        deathTimer += dt;
        if (dieSfxTimer > 0f)
        {
            dieSfxTimer -= dt;
            if (dieSfxTimer <= 0f) Play(SoundFX.Die, 0.8f);
        }

        if (!grounded)
        {
            velY = Mathf.Max(maxFallSpeed, velY + gravity * dt);
            birdY += velY * dt;
            if (birdY - BirdR <= GroundTop)
            {
                birdY = GroundTop + BirdR;
                grounded = true;
                velY = 0f;
                for (int i = 0; i < 6; i++)
                    fx.Emit(new Vector2(birdX + Random.Range(-0.3f, 0.3f), GroundTop + 0.1f),
                            new Vector2(Random.Range(-1.5f, 1.5f), Random.Range(0.5f, 1.8f)),
                            new Color(0.93f, 0.85f, 0.6f, 0.9f), 0.5f, 0.22f, 0.05f, -4f, 0f);
            }
        }

        birdSR.sprite = birdFrames[1];
        PlaceBird(dt, -90f);
        if (deathTimer >= 0.75f && grounded) ShowGameOver();
    }

    void TickGameOver(float dt, bool restartPressed)
    {
        goTimer += dt;
        birdSR.sprite = birdFrames[1];
        PlaceBird(dt, -90f);

        goTitleTr.localPosition = new Vector3(0f, Mathf.LerpUnclamped(6f, 2.7f, EaseOutBack(Mathf.Clamp01(goTimer / 0.5f))), 0f);
        panelTr.localPosition = new Vector3(0f, Mathf.LerpUnclamped(-6f, 0.45f, EaseOutBack(Mathf.Clamp01((goTimer - 0.25f) / 0.5f))), 0f);

        // count the score up
        float ct = Mathf.Clamp01((goTimer - 0.6f) / 0.5f);
        int shown = score == 0 ? 0 : Mathf.Min(score, Mathf.CeilToInt(ct * score));
        if (shown != countShown)
        {
            countShown = shown;
            SetText(scoreValueSR, shown.ToString(), 0.09f, TextStyle.White);
        }

        if (goTimer > 1.0f)
        {
            retrySR.enabled = Mathf.Repeat(Time.time, 1.1f) < 0.75f;
            if (restartPressed) Restart();
        }
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    // ---------------------------------------------------------------- helpers

    void AnimateWing(float dt, float fps)
    {
        wingT += dt * fps;
        birdSR.sprite = birdFrames[WingSeq[(int)wingT % WingSeq.Length]];
    }

    void PlaceBird(float dt, float targetAngle)
    {
        birdAngle = Mathf.Lerp(birdAngle, targetAngle, 1f - Mathf.Exp(-12f * dt));
        birdTr.position = new Vector3(birdX, birdY, 0f);
        birdTr.rotation = Quaternion.Euler(0f, 0f, birdAngle);
    }

    void RefreshScore()
    {
        SetText(scoreSR, score.ToString(), 0.14f, TextStyle.White);
    }

    void Sparkle()
    {
        for (int i = 0; i < 6; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float s = Random.Range(1f, 2.6f);
            fx.Emit(new Vector2(birdX + 0.2f, birdY), new Vector2(Mathf.Cos(a) * s, Mathf.Sin(a) * s),
                    new Color(1f, 0.9f, 0.3f, 1f), 0.45f, 0.16f, 0.04f, 0f, 0f);
        }
    }

    void SpawnPipe(float x)
    {
        float gap = Mathf.Max(minGap, startGap - score * 0.012f);
        float lo = GroundTop + gap * 0.5f + 0.5f;
        float hi = 5f - gap * 0.5f - 0.6f;
        float center = Mathf.Clamp(lastGapY + Random.Range(-2.2f, 2.2f), lo, hi);
        lastGapY = center;

        var p = new Pipe { x = x, gapY = center, gap = gap };
        var root = new GameObject("Pipe").transform;
        root.SetParent(pipeParent, false);
        root.position = new Vector3(x, 0f, 0f);
        p.root = root;

        float eTop = center + gap * 0.5f;
        float eBot = center - gap * 0.5f;

        // ceiling pipe: cap (flipped) + shaft up to y=7
        PipePart(root, pipeCapSprite, new Vector3(0f, eTop + CapH * 0.5f, 0f), Vector3.one, true, -9);
        float topH = 7f - (eTop + CapH);
        PipePart(root, pipeBodySprite, new Vector3(0f, eTop + CapH + topH * 0.5f, 0f), new Vector3(1f, topH, 1f), false, -10);

        // floor pipe: cap + shaft down to y=-7
        PipePart(root, pipeCapSprite, new Vector3(0f, eBot - CapH * 0.5f, 0f), Vector3.one, false, -9);
        float botH = (eBot - CapH) + 7f;
        PipePart(root, pipeBodySprite, new Vector3(0f, -7f + botH * 0.5f, 0f), new Vector3(1f, botH, 1f), false, -10);

        pipes.Add(p);
    }

    void PipePart(Transform parent, Sprite sprite, Vector3 localPos, Vector3 scale, bool flipY, int order)
    {
        var go = new GameObject("part");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.flipY = flipY;
        sr.sortingOrder = order;
        sr.color = theme.pipes;
    }

    bool HitsPipe(Pipe p)
    {
        float eTop = p.gapY + p.gap * 0.5f;
        float eBot = p.gapY - p.gap * 0.5f;
        float hw = BodyW * 0.5f, cw = CapW * 0.5f;
        return CircleRect(p.x - cw, eTop, p.x + cw, eTop + CapH)
            || CircleRect(p.x - hw, eTop + CapH, p.x + hw, 20f)
            || CircleRect(p.x - cw, eBot - CapH, p.x + cw, eBot)
            || CircleRect(p.x - hw, -20f, p.x + hw, eBot - CapH);
    }

    bool CircleRect(float x0, float y0, float x1, float y1)
    {
        float dx = birdX - Mathf.Clamp(birdX, x0, x1);
        float dy = birdY - Mathf.Clamp(birdY, y0, y1);
        return dx * dx + dy * dy < BirdR * BirdR;
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip != null && audioSrc != null) audioSrc.PlayOneShot(clip, volume);
    }

    void SetPaused(bool value)
    {
        paused = value;
        pausedSR.enabled = value;
    }

    void ToggleMute()
    {
        muted = !muted;
        AudioListener.volume = muted ? 0f : 1f;
        SetText(toastSR, muted ? "SOUND OFF" : "SOUND ON", 0.06f, TextStyle.White);
        toastSR.enabled = true;
        toastTimer = 1.2f;
    }

    void OnApplicationFocus(bool focus)
    {
        if (!focus && state == State.Playing && !paused) SetPaused(true);
    }

    // ---------------------------------------------------------------- visuals

    void UpdateVisuals(float dt)
    {
        float w = halfW;

        skySR.transform.localScale = new Vector3((2f * w + 2f) * 64f, 10.6f, 1f);
        celestialSR.transform.position = new Vector3(Mathf.Min(w * 0.55f, 6f), 3.4f, 0f);

        starsLayer.Update(scroll, w);
        cloudLayer.Update(scroll, w);
        skylineLayer.Update(scroll, w);
        windowLayer.Update(scroll, w);
        bushLayer.Update(scroll, w);
        groundLayer.Update(scroll, w);

        if (theme.night)
            starsLayer.SetColor(new Color(1f, 1f, 1f, 0.75f + 0.25f * Mathf.Sin(Time.time * 2f)));

        // UI scales down on narrow (portrait) windows
        float ui = Mathf.Min(1f, 2f * w * 0.94f / 7.2f);
        uiRoot.localScale = new Vector3(ui, ui, 1f);

        // screen shake + flash
        shake = Mathf.MoveTowards(shake, 0f, 0.9f * dt);
        Vector2 o = shake > 0f ? Random.insideUnitCircle * shake : Vector2.zero;
        cam.transform.position = camBase + new Vector3(o.x, o.y, 0f);

        flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, 3.5f * dt);
        flashSR.color = new Color(1f, 1f, 1f, flashAlpha);

        if (toastTimer > 0f)
        {
            toastTimer -= dt;
            if (toastTimer <= 0f) toastSR.enabled = false;
        }
    }
}
