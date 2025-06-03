using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Linq;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class VRCreationProtectionSystem : MonoBehaviour
{
    [Header("Artist Information")]
    [SerializeField] private string artistID = "Artist_001";
    [SerializeField] private string artistName = "Unknown Creator";
    [SerializeField] private string projectName = "VR_Artwork";

    [Header("Target Artwork")]
    [SerializeField] private Transform artworkContainer;
    [SerializeField] private LayerMask artworkLayerMask = -1;

    [Header("Creation Tracking")]
    [SerializeField] private bool enableCreationTracking = true;
    [SerializeField] private float brushChangeDetectionRadius = 0.1f;
    [SerializeField] private int minimumCreationActions = 5; // �ּ� â�� �ൿ ��

    [Header("VR Art Protection Settings")]
    [SerializeField] private int artworkResolution = 1024; // ��Ʈ��ũ�� ���ػ�
    [SerializeField] private float creationComplexityThreshold = 0.3f;
    [SerializeField] private bool enableVersionControl = true;

    [Header("Auto Protection Triggers")]
    [SerializeField] private bool protectOnBrushChange = true;
    [SerializeField] private bool protectOnCreationMilestone = true;
    [SerializeField] private float autoProtectionInterval = 180f; // 3�и���
    [SerializeField] private bool protectOnSessionEnd = true;

    [Header("VR Simulation")]
    [SerializeField] private bool useVRSimulation = true;
    [SerializeField] private float simulationRadius = 2f;

    // VR â�� ������
    [System.Serializable]
    public class CreationMetadata
    {
        public string artistID;
        public string artistName;
        public string projectName;
        public DateTime creationStartTime;
        public DateTime lastModificationTime;
        public int versionNumber;
        public int totalBrushStrokes;
        public float creationDuration; // �� â�� �ð� (��)
        public Vector3 primaryCreationArea; // �ֿ� â�� ����
        public List<string> toolsUsed;
        public float artworkComplexity;
        public string sessionID;
    }

    // 6���� ī�޶� ����
    public enum ArtViewDirection
    {
        MainView = 0,      // �ֿ� ���� ����
        DetailView = 1,    // ���� ������ ����
        ProfileLeft = 2,   // ���� ������
        ProfileRight = 3,  // ���� ������
        TopView = 4,       // ��� ��ü ��
        BottomView = 5     // �ϴ� ���� ��
    }

    // â�� ��ȣ ���
    [System.Serializable]
    public class ArtworkProtectionResult
    {
        public Texture2D primaryProtectionImage;
        public ArtViewDirection primaryDirection;
        public List<Texture2D> allViewImages;
        public CreationMetadata metadata;
        public float artworkComplexity;
        public Dictionary<ArtViewDirection, float> viewQualityScores;
        public float protectionProcessingTime;
        public bool readyForWatermarking;
    }

    // ������ �� �ý��� ������
    private Camera[] artViewCameras = new Camera[6];
    private Vector3[] optimalCameraPositions = new Vector3[6];
    private Vector3[] optimalCameraRotations = new Vector3[6];
    private Queue<RenderTexture> artRenderTexturePool = new Queue<RenderTexture>();
    private List<RenderTexture> activeArtRenderTextures = new List<RenderTexture>();

    // â�� ���� ������
    private CreationMetadata currentCreationData;
    private Vector3 lastBrushPosition = Vector3.zero;
    private string currentTool = "Default_Brush";
    private int currentBrushStrokes = 0;
    private float sessionStartTime;
    private float lastProtectionTime;
    private List<Vector3> creationHotspots = new List<Vector3>();

    // VR �ùķ��̼� ������
    private float lastSimulationUpdate = 0f;
    private float simulationUpdateInterval = 2f; // 2�ʸ��� ��ġ ����

    // ���� ���� ����
    private List<string> protectedArtworks = new List<string>();

    // �̺�Ʈ
    public System.Action<ArtworkProtectionResult> OnArtworkProtected;
    public System.Action<CreationMetadata> OnCreationMilestoneReached;
    public System.Action<string> OnToolChanged;

    // ���� �� UI
    private List<float> fpsHistory = new List<float>();

    void Start()
    {
        InitializeVRArtSystem();
        InitializeCreationTracking();

        // artworkContainer�� ������ �ݵ�� ����
        if (artworkContainer == null)
        {
            CreateSampleArtwork();
        }

        SetupArtRenderingCameras();
        SetupRenderTexturePool();

        Debug.Log($"VR ��Ʈ â�� ��ȣ �ý��� ���� - ��Ƽ��Ʈ: {artistName}");
    }

    void Update()
    {
        MonitorCreationActivity();
        TrackArtworkChanges();
        CheckProtectionTriggers();
        MonitorPerformance();

        // ���� ��ȣ ���� (VR ��Ʈ�ѷ� �Ǵ� Ű����)
        if (IsProtectionTriggerPressed())
        {
            StartCoroutine(ProtectCurrentArtwork("Manual_Protection"));
        }

        // ���� ���� �׽�Ʈ (���߿�)
        if (IsToolChangePressed()) SimulateToolChange();
        if (IsBrushStrokePressed()) SimulateBrushStroke();
    }

    #region VR Art System Initialization

    void InitializeVRArtSystem()
    {
        // ���� ��ǰ�� ����ȭ ����
        artworkResolution = Mathf.Clamp(artworkResolution, 512, 2048);

        // ��Ʈ��ũ ���� ���̾� ����
        if (artworkLayerMask == 0) artworkLayerMask = LayerMask.GetMask("Default");

        sessionStartTime = Time.time;
        lastProtectionTime = Time.time;
        lastSimulationUpdate = Time.time;
    }

    void InitializeCreationTracking()
    {
        currentCreationData = new CreationMetadata
        {
            artistID = this.artistID,
            artistName = this.artistName,
            projectName = this.projectName,
            creationStartTime = DateTime.Now,
            lastModificationTime = DateTime.Now,
            versionNumber = 1,
            totalBrushStrokes = 0,
            creationDuration = 0f,
            toolsUsed = new List<string>(),
            sessionID = System.Guid.NewGuid().ToString("N")[..8]
        };

        Debug.Log($"â�� ���� ���� - ID: {currentCreationData.sessionID}");
    }

    void SetupArtRenderingCameras()
    {
        // ���� ��ǰ ���� ����ȭ�� ī�޶� ���� ����
        float artDistance = CalculateOptimalViewingDistance();

        // �ֿ� ���� ���� (���� �ణ ����)
        optimalCameraPositions[(int)ArtViewDirection.MainView] = new Vector3(1f, 0.8f, 2f).normalized * artDistance;
        optimalCameraRotations[(int)ArtViewDirection.MainView] = new Vector3(-15f, -25f, 0f);

        // ���� ������ ���� (����� �Ÿ�)
        optimalCameraPositions[(int)ArtViewDirection.DetailView] = new Vector3(0.5f, 0f, 1f).normalized * (artDistance * 0.7f);
        optimalCameraRotations[(int)ArtViewDirection.DetailView] = new Vector3(0f, -30f, 0f);

        // ���� ������
        optimalCameraPositions[(int)ArtViewDirection.ProfileLeft] = Vector3.left * artDistance;
        optimalCameraRotations[(int)ArtViewDirection.ProfileLeft] = new Vector3(0f, 90f, 0f);

        // ���� ������
        optimalCameraPositions[(int)ArtViewDirection.ProfileRight] = Vector3.right * artDistance;
        optimalCameraRotations[(int)ArtViewDirection.ProfileRight] = new Vector3(0f, -90f, 0f);

        // ��� ��ü ��
        optimalCameraPositions[(int)ArtViewDirection.TopView] = Vector3.up * artDistance;
        optimalCameraRotations[(int)ArtViewDirection.TopView] = new Vector3(90f, 0f, 0f);

        // �ϴ� ���� ��
        optimalCameraPositions[(int)ArtViewDirection.BottomView] = Vector3.down * artDistance;
        optimalCameraRotations[(int)ArtViewDirection.BottomView] = new Vector3(-90f, 0f, 0f);

        // ī�޶� ���� �� ����
        for (int i = 0; i < 6; i++)
        {
            GameObject cameraGO = new GameObject($"ArtCamera_{(ArtViewDirection)i}");
            cameraGO.transform.SetParent(transform);

            Camera cam = cameraGO.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f); // ��Ʈ ���ÿ� ���
            cam.orthographic = false;
            cam.fieldOfView = (i == (int)ArtViewDirection.DetailView) ? 45f : 60f; // ������ ��� �� ���� �þ߰�
            cam.cullingMask = artworkLayerMask;

            artViewCameras[i] = cam;
        }

        Debug.Log("VR ��Ʈ ����� 6���� ī�޶� �ý��� �ʱ�ȭ �Ϸ�");
    }

    float CalculateOptimalViewingDistance()
    {
        if (artworkContainer == null)
        {
            Debug.LogWarning("artworkContainer�� null�Դϴ�. �⺻ �Ÿ��� ��ȯ�մϴ�.");
            return 3f;
        }

        Bounds bounds = GetArtworkBounds();
        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

        // ��Ʈ��ũ ũ�⿡ ���� ���� ���� �Ÿ�
        return Mathf.Clamp(maxDimension * 2.5f, 2f, 8f);
    }

    void SetupRenderTexturePool()
    {
        for (int i = 0; i < 8; i++) // ��Ʈ��ũ������ �� ���� �ؽ�ó �غ�
        {
            RenderTexture rt = CreateArtRenderTexture();
            artRenderTexturePool.Enqueue(rt);
        }

        Debug.Log($"��Ʈ��ũ �����ؽ�ó Ǯ �ʱ�ȭ: {artRenderTexturePool.Count}��");
    }

    RenderTexture CreateArtRenderTexture()
    {
        RenderTexture rt = new RenderTexture(artworkResolution, artworkResolution, 24);
        rt.format = RenderTextureFormat.ARGB32;
        rt.filterMode = FilterMode.Trilinear; // ��Ʈ��ũ�� ��ǰ�� ���͸�
        rt.antiAliasing = 4; // ��Ʈ��ũ�� ��Ƽ�ٸ����
        rt.Create();
        return rt;
    }

    #endregion

    #region Creation Activity Monitoring

    void MonitorCreationActivity()
    {
        if (!enableCreationTracking) return;

        // â�� �ð� ������Ʈ
        currentCreationData.creationDuration = Time.time - sessionStartTime;
        currentCreationData.lastModificationTime = DateTime.Now;

        // VR ��Ʈ�ѷ� ��ġ ����͸�
        Vector3 currentBrushPosition;

        if (useVRSimulation)
        {
            // �ùķ��̼� ���: �ֱ������� �귯�� ��ġ ����
            if (Time.time - lastSimulationUpdate > simulationUpdateInterval)
            {
                currentBrushPosition = GetCurrentBrushPosition();
                lastSimulationUpdate = Time.time;

                // �ùķ��̼ǿ����� �׻� ��ġ �������� ����
                OnBrushPositionChanged(currentBrushPosition);
                lastBrushPosition = currentBrushPosition;
            }
        }
        else
        {
            // ���� VR ���: ���� ����
            currentBrushPosition = GetCurrentBrushPosition();

            if (Vector3.Distance(currentBrushPosition, lastBrushPosition) > brushChangeDetectionRadius)
            {
                OnBrushPositionChanged(currentBrushPosition);
                lastBrushPosition = currentBrushPosition;
            }
        }
    }

    Vector3 GetCurrentBrushPosition()
    {
        // VR �ùķ��̼� ��� ���
        if (useVRSimulation)
        {
            // artworkContainer�� ������ ���� ����
            if (artworkContainer == null)
            {
                CreateSampleArtwork();
            }

            // VR ��Ʈ�ѷ� ��ġ �ùķ��̼� (��Ʈ��ũ �ֺ��� ���� ��ġ)
            Vector3 artworkCenter = artworkContainer != null ? artworkContainer.position : Vector3.zero;
            Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * simulationRadius;
            return artworkCenter + randomOffset;
        }

        // ���� VR ȯ�濡���� VR ��Ʈ�ѷ� ��ġ�� ��ȯ
        // ���콺 �Է� ��� (Input System ȣȯ)
        Vector3 mousePos;

#if ENABLE_INPUT_SYSTEM
        // Input System ���
        if (Mouse.current != null)
        {
            Vector2 screenPos = Mouse.current.position.ReadValue();
            mousePos = new Vector3(screenPos.x, screenPos.y, 2f);
        }
        else
        {
            // ���콺�� ������ ȭ�� �߾�
            mousePos = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 2f);
        }
#else
        // Legacy Input Manager ���
        mousePos = Input.mousePosition;
        mousePos.z = 2f; // ī�޶�κ����� �Ÿ�
#endif

        if (Camera.main != null)
        {
            return Camera.main.ScreenToWorldPoint(mousePos);
        }
        else
        {
            // ī�޶� ������ ���� ��ǥ�� ���� ��ȯ
            return new Vector3(
                (mousePos.x / Screen.width - 0.5f) * 10f,
                (mousePos.y / Screen.height - 0.5f) * 10f,
                0f
            );
        }
    }

    void OnBrushPositionChanged(Vector3 newPosition)
    {
        // â�� �ֽ��� ���
        creationHotspots.Add(newPosition);

        // �ʹ� ���� ���̸� ������ �� ����
        if (creationHotspots.Count > 100)
        {
            creationHotspots.RemoveAt(0);
        }

        // �ֿ� â�� ���� ������Ʈ
        UpdatePrimaryCreationArea();
    }

    void UpdatePrimaryCreationArea()
    {
        if (creationHotspots.Count == 0) return;

        Vector3 center = Vector3.zero;
        foreach (Vector3 hotspot in creationHotspots)
        {
            center += hotspot;
        }
        currentCreationData.primaryCreationArea = center / creationHotspots.Count;
    }

    void TrackArtworkChanges()
    {
        // ��Ʈ��ũ ���⵵ �ǽð� ���
        if (Time.time - lastProtectionTime > 10f) // 10�ʸ��� ���⵵ ����
        {
            currentCreationData.artworkComplexity = CalculateArtworkComplexity();
        }
    }

    float CalculateArtworkComplexity()
    {
        if (artworkContainer == null) return 0f;

        float complexity = 0f;

        // �귯�� ��Ʈ��ũ ���� ���� ���⵵
        complexity += Mathf.Clamp01(currentBrushStrokes / 100f) * 0.3f;

        // â�� �ֽ��� �л굵�� ���� ���⵵
        if (creationHotspots.Count > 1)
        {
            float spread = CalculateHotspotSpread();
            complexity += Mathf.Clamp01(spread / 5f) * 0.3f;
        }

        // ���� ���� �پ缺�� ���� ���⵵
        complexity += Mathf.Clamp01(currentCreationData.toolsUsed.Count / 5f) * 0.2f;

        // â�� �ð��� ���� ���⵵
        complexity += Mathf.Clamp01(currentCreationData.creationDuration / 1800f) * 0.2f; // 30�� ����

        return complexity;
    }

    float CalculateHotspotSpread()
    {
        if (creationHotspots.Count < 2) return 0f;

        Vector3 center = currentCreationData.primaryCreationArea;
        float maxDistance = 0f;

        foreach (Vector3 hotspot in creationHotspots)
        {
            float distance = Vector3.Distance(hotspot, center);
            if (distance > maxDistance) maxDistance = distance;
        }

        return maxDistance;
    }

    #endregion

    #region Protection Triggers

    void CheckProtectionTriggers()
    {
        // �귯�� ���� �� ��ȣ
        if (protectOnBrushChange && HasToolChanged())
        {
            StartCoroutine(ProtectCurrentArtwork("Tool_Change"));
        }

        // â�� ���Ͻ��� �޼� �� ��ȣ
        if (protectOnCreationMilestone && HasReachedCreationMilestone())
        {
            StartCoroutine(ProtectCurrentArtwork("Creation_Milestone"));
            OnCreationMilestoneReached?.Invoke(currentCreationData);
        }

        // �ڵ� �ֱ��� ��ȣ
        if (Time.time - lastProtectionTime >= autoProtectionInterval)
        {
            StartCoroutine(ProtectCurrentArtwork("Auto_Protection"));
        }
    }

    bool HasToolChanged()
    {
        // ���� VR ȯ�濡���� ��Ʈ�ѷ��� ���� ������ ����
        // ������ �ùķ��̼����� ó��
        return false; // ���� �Է����� ó��
    }

    bool HasReachedCreationMilestone()
    {
        // â�� ���Ͻ��� ���ǵ�
        bool strokeMilestone = (currentBrushStrokes > 0) && (currentBrushStrokes % 25 == 0);
        bool complexityMilestone = currentCreationData.artworkComplexity >= creationComplexityThreshold;
        bool timeMilestone = currentCreationData.creationDuration >= 300f; // 5��

        return strokeMilestone || complexityMilestone || timeMilestone;
    }

    #endregion

    #region Artwork Protection Process

    public IEnumerator ProtectCurrentArtwork(string triggerReason = "Manual")
    {
        float startTime = Time.realtimeSinceStartup;

        Debug.Log($"��Ʈ��ũ ��ȣ ���� - ����: {triggerReason}, ���⵵: {currentCreationData.artworkComplexity:F3}");

        // ī�޶� ��ġ ������Ʈ (��Ʈ��ũ �߽�����)
        UpdateArtCameraPositions();

        // 6���� ��Ʈ �� ������
        Dictionary<ArtViewDirection, ArtViewData> artViewResults = new Dictionary<ArtViewDirection, ArtViewData>();

        for (int i = 0; i < 6; i++)
        {
            ArtViewDirection direction = (ArtViewDirection)i;
            yield return StartCoroutine(RenderArtView(direction, artViewResults));
            yield return null; // ������ �л�
        }

        // ���� ��ȣ �� ����
        var bestArtView = SelectBestArtView(artViewResults);

        // ��ȣ ��� ����
        ArtworkProtectionResult result = new ArtworkProtectionResult
        {
            primaryProtectionImage = bestArtView.image,
            primaryDirection = bestArtView.direction,
            allViewImages = artViewResults.Values.Select(v => v.image).ToList(),
            metadata = currentCreationData,
            artworkComplexity = currentCreationData.artworkComplexity,
            viewQualityScores = new Dictionary<ArtViewDirection, float>(),
            protectionProcessingTime = Time.realtimeSinceStartup - startTime,
            readyForWatermarking = currentCreationData.artworkComplexity >= creationComplexityThreshold
        };

        foreach (var kvp in artViewResults)
        {
            result.viewQualityScores[kvp.Key] = kvp.Value.qualityScore;
        }

        // ���� ����
        if (enableVersionControl)
        {
            currentCreationData.versionNumber++;
            SaveArtworkProtection(result, triggerReason);
        }

        // ������� �ʴ� �̹��� ����
        CleanupUnusedArtImages(artViewResults, bestArtView.direction);

        lastProtectionTime = Time.time;

        // �̺�Ʈ �߻�
        OnArtworkProtected?.Invoke(result);

        Debug.Log($"��Ʈ��ũ ��ȣ �Ϸ� - ����: {result.primaryDirection}, " +
                 $"ǰ������: {bestArtView.qualityScore:F3}, ó���ð�: {result.protectionProcessingTime:F3}��, " +
                 $"���͸�ŷ �غ�: {(result.readyForWatermarking ? "�Ϸ�" : "���")}");
    }

    void UpdateArtCameraPositions()
    {
        // artworkContainer�� ������ �߾� ��ġ �������� ����
        if (artworkContainer == null)
        {
            Debug.LogWarning("artworkContainer�� null�Դϴ�. �⺻ ��ġ�� ī�޶� �����մϴ�.");

            // �⺻ ��ġ�� ī�޶� ����
            Vector3 centerCamera = Vector3.zero;
            float defaultDistance = 3f;

            for (int i = 0; i < 6; i++)
            {
                Camera cam = artViewCameras[i];
                Vector3 offset = optimalCameraPositions[i].normalized * defaultDistance;
                cam.transform.position = centerCamera + offset;
                cam.transform.LookAt(centerCamera);
                cam.fieldOfView = 60f;
            }
            return;
        }

        Bounds bounds = GetArtworkBounds();
        Vector3 center = bounds.center;
        float optimalDistance = CalculateOptimalViewingDistance();

        // ��Ʈ��ũ �߽��� �������� �� ī�޶� ��ġ ����
        for (int i = 0; i < 6; i++)
        {
            Camera cam = artViewCameras[i];
            ArtViewDirection direction = (ArtViewDirection)i;

            // ��ġ ����
            Vector3 offset = optimalCameraPositions[i].normalized * optimalDistance;
            cam.transform.position = center + offset;

            // ȸ�� ���� (��Ʈ��ũ �߽��� �ٶ󺸵���)
            cam.transform.LookAt(center);

            // Ư���� ���� ����
            if (direction == ArtViewDirection.MainView)
            {
                // �ֿ� ���� ������ �ణ ������
                cam.transform.position += Vector3.up * (optimalDistance * 0.2f);
                cam.transform.LookAt(center);
            }
            else if (direction == ArtViewDirection.DetailView)
            {
                // ������ ��� �� ������
                cam.transform.position = center + offset * 0.6f;
                cam.fieldOfView = 45f;
            }

            // ��Ʈ��ũ ũ�⿡ �´� �þ߰� ����
            float distance = Vector3.Distance(cam.transform.position, center);
            cam.fieldOfView = Mathf.Clamp(bounds.size.magnitude / distance * 50f, 30f, 90f);
        }
    }

    struct ArtViewData
    {
        public Texture2D image;
        public float qualityScore;
        public ArtViewDirection direction;
    }

    IEnumerator RenderArtView(ArtViewDirection direction, Dictionary<ArtViewDirection, ArtViewData> results)
    {
        Camera cam = artViewCameras[(int)direction];

        RenderTexture renderTexture = GetArtRenderTexture();
        cam.targetTexture = renderTexture;

        // ��Ʈ��ũ ���� ������ ����
        cam.enabled = true;
        cam.Render();
        cam.enabled = false;

        yield return new WaitForEndOfFrame();

        // ��ǰ�� �ؽ�ó ��ȯ
        Texture2D artImage = RenderTextureToTexture2D(renderTexture);

        // ��Ʈ��ũ ǰ�� ���� ���
        float qualityScore = CalculateArtQualityScore(artImage, direction);

        results[direction] = new ArtViewData
        {
            image = artImage,
            qualityScore = qualityScore,
            direction = direction
        };
    }

    float CalculateArtQualityScore(Texture2D image, ArtViewDirection direction)
    {
        if (image == null) return 0f;

        Color[] pixels = image.GetPixels();
        float score = 0f;

        // ��Ʈ��ũ ���� ���� ���
        int artworkPixels = 0;
        float totalSaturation = 0f;
        float totalContrast = 0f;

        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];

            // ����� �ƴ� ��Ʈ��ũ �ȼ� ����
            if (pixel.a > 0.2f && !IsBackgroundColor(pixel))
            {
                artworkPixels++;

                // ä���� ��� ���
                float saturation = Mathf.Max(pixel.r, pixel.g, pixel.b) - Mathf.Min(pixel.r, pixel.g, pixel.b);
                totalSaturation += saturation;

                float brightness = pixel.grayscale;
                totalContrast += Mathf.Abs(brightness - 0.5f);
            }
        }

        // ��Ʈ��ũ ���� ����
        float artworkRatio = (float)artworkPixels / pixels.Length;
        score += artworkRatio * 0.4f;

        // ��� ä�� ����
        if (artworkPixels > 0)
        {
            float avgSaturation = totalSaturation / artworkPixels;
            score += avgSaturation * 0.3f;

            float avgContrast = totalContrast / artworkPixels;
            score += avgContrast * 0.3f;
        }

        // ���⺰ ����ġ (�ֿ� ���� ������ �� ���� ����)
        float directionWeight = GetDirectionWeight(direction);
        score *= directionWeight;

        return Mathf.Clamp01(score);
    }

    bool IsBackgroundColor(Color color)
    {
        // ���� ���� (��ο� ȸ���迭)
        return color.r < 0.2f && color.g < 0.2f && color.b < 0.25f;
    }

    float GetDirectionWeight(ArtViewDirection direction)
    {
        switch (direction)
        {
            case ArtViewDirection.MainView: return 1.2f;      // �ֿ� ���� ����
            case ArtViewDirection.DetailView: return 1.1f;   // ������ ��
            case ArtViewDirection.ProfileLeft: return 1.0f;  // ������
            case ArtViewDirection.ProfileRight: return 1.0f; // ������
            case ArtViewDirection.TopView: return 0.8f;      // ��� ��
            case ArtViewDirection.BottomView: return 0.7f;   // �ϴ� ��
            default: return 1.0f;
        }
    }

    ArtViewData SelectBestArtView(Dictionary<ArtViewDirection, ArtViewData> artViews)
    {
        ArtViewData bestView = new ArtViewData();
        float bestScore = -1f;

        foreach (var kvp in artViews)
        {
            if (kvp.Value.qualityScore > bestScore)
            {
                bestScore = kvp.Value.qualityScore;
                bestView = kvp.Value;
            }
        }

        return bestView;
    }

    #endregion

    #region File Management

    void SaveArtworkProtection(ArtworkProtectionResult result, string triggerReason)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string sessionFolder = $"Assets/ProtectedArtworks/{currentCreationData.sessionID}/";

        EnsureDirectoryExists(sessionFolder);

        // �ֿ� ��ȣ �̹��� ����
        string primaryFileName = $"{currentCreationData.projectName}_v{currentCreationData.versionNumber:D3}_{triggerReason}_{result.primaryDirection}_{timestamp}.png";
        string primaryPath = sessionFolder + primaryFileName;
        SaveTextureToPNG(result.primaryProtectionImage, primaryPath);

        // ��Ÿ������ ����
        string metadataFileName = $"{currentCreationData.projectName}_v{currentCreationData.versionNumber:D3}_metadata_{timestamp}.json";
        string metadataPath = sessionFolder + metadataFileName;
        SaveCreationMetadata(result.metadata, metadataPath);

        protectedArtworks.Add(primaryPath);

        Debug.Log($"��Ʈ��ũ ��ȣ ���� ����: {primaryPath}");
    }

    void SaveCreationMetadata(CreationMetadata metadata, string filePath)
    {
        try
        {
            string json = JsonUtility.ToJson(metadata, true);
            System.IO.File.WriteAllText(filePath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"��Ÿ������ ���� ����: {e.Message}");
        }
    }

    void EnsureDirectoryExists(string path)
    {
        if (!System.IO.Directory.Exists(path))
        {
            System.IO.Directory.CreateDirectory(path);
        }
    }

    void SaveTextureToPNG(Texture2D texture, string filePath)
    {
        if (texture == null) return;

        byte[] pngData = texture.EncodeToPNG();
        System.IO.File.WriteAllBytes(filePath, pngData);
    }

    #endregion

    #region Utility Functions

    void CreateSampleArtwork()
    {
        // ���� ��Ʈ��ũ ���� (�׽�Ʈ��)
        GameObject artwork = GameObject.CreatePrimitive(PrimitiveType.Cube);
        artwork.name = "Sample_VR_Artwork";
        artwork.transform.position = Vector3.zero;
        artwork.transform.localScale = Vector3.one * 1.5f;

        // ��Ʈ��ũ��� ���� �߰�
        Renderer renderer = artwork.GetComponent<Renderer>();
        Material artMaterial = new Material(Shader.Find("Standard"));
        artMaterial.color = Color.HSVToRGB(UnityEngine.Random.Range(0f, 1f), 0.8f, 0.9f);

        // Standard ���̴� ������Ƽ�� ����
        artMaterial.SetFloat("_Metallic", 0.3f);
        artMaterial.SetFloat("_Glossiness", 0.7f);

        renderer.material = artMaterial;

        artworkContainer = artwork.transform;
        Debug.Log("���� VR ��Ʈ��ũ ���� �Ϸ�");
    }

    Bounds GetArtworkBounds()
    {
        // artworkContainer�� ������ �⺻ Bounds ��ȯ
        if (artworkContainer == null)
        {
            Debug.LogWarning("artworkContainer�� null�Դϴ�. �⺻ Bounds�� ��ȯ�մϴ�.");
            return new Bounds(Vector3.zero, Vector3.one);
        }

        Renderer[] renderers = artworkContainer.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(artworkContainer.position, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    RenderTexture GetArtRenderTexture()
    {
        if (artRenderTexturePool.Count > 0)
        {
            RenderTexture rt = artRenderTexturePool.Dequeue();
            activeArtRenderTextures.Add(rt);
            return rt;
        }
        else
        {
            RenderTexture rt = CreateArtRenderTexture();
            activeArtRenderTextures.Add(rt);
            return rt;
        }
    }

    void ReturnArtRenderTexture(RenderTexture rt)
    {
        if (rt != null && activeArtRenderTextures.Contains(rt))
        {
            activeArtRenderTextures.Remove(rt);
            artRenderTexturePool.Enqueue(rt);
        }
    }

    void CleanupUnusedArtImages(Dictionary<ArtViewDirection, ArtViewData> artViews, ArtViewDirection keepDirection)
    {
        foreach (var kvp in artViews)
        {
            if (kvp.Key != keepDirection && kvp.Value.image != null)
            {
                DestroyImmediate(kvp.Value.image);
            }
        }
    }

    Texture2D RenderTextureToTexture2D(RenderTexture renderTexture)
    {
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture.active = renderTexture;

        Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();

        RenderTexture.active = currentRT;
        return texture;
    }

    #endregion

    #region Input Simulation & Testing

    bool IsProtectionTriggerPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    bool IsToolChangePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.T);
#endif
    }

    bool IsBrushStrokePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.B);
#endif
    }

    void SimulateToolChange()
    {
        string[] tools = { "Brush", "Pencil", "Marker", "Spray", "Eraser", "Sculpt" };
        string newTool = tools[UnityEngine.Random.Range(0, tools.Length)];

        if (newTool != currentTool)
        {
            currentTool = newTool;
            if (!currentCreationData.toolsUsed.Contains(newTool))
            {
                currentCreationData.toolsUsed.Add(newTool);
            }

            OnToolChanged?.Invoke(newTool);
            Debug.Log($"���� ����: {newTool}");

            if (protectOnBrushChange)
            {
                StartCoroutine(ProtectCurrentArtwork("Tool_Change_" + newTool));
            }
        }
    }

    void SimulateBrushStroke()
    {
        currentBrushStrokes++;
        currentCreationData.totalBrushStrokes = currentBrushStrokes;

        // ���� ��ġ�� �귯�� ��Ʈ��ũ �߰�
        Vector3 randomPos = UnityEngine.Random.insideUnitSphere * 2f;
        OnBrushPositionChanged(randomPos);

        Debug.Log($"�귯�� ��Ʈ��ũ #{currentBrushStrokes} - ���⵵: {currentCreationData.artworkComplexity:F3}");
    }

    #endregion

    #region Performance Monitoring

    void MonitorPerformance()
    {
        fpsHistory.Add(1f / Time.unscaledDeltaTime);

        if (fpsHistory.Count > 60)
        {
            fpsHistory.RemoveAt(0);
        }
    }

    #endregion

    #region UI Display

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 400, 500));

        GUILayout.Label("=== VR ��Ʈ â�� ��ȣ �ý��� ===", GUI.skin.box);

        // ��Ƽ��Ʈ ����
        GUILayout.Label($"��Ƽ��Ʈ: {artistName} (ID: {artistID})");
        GUILayout.Label($"������Ʈ: {projectName}");
        GUILayout.Label($"���� ID: {currentCreationData.sessionID}");

        GUILayout.Space(10);

        // â�� ��Ȳ
        GUILayout.Label("=== â�� ��Ȳ ===", GUI.skin.box);
        GUILayout.Label($"â�� �ð�: {currentCreationData.creationDuration:F1}��");
        GUILayout.Label($"�귯�� ��Ʈ��ũ: {currentBrushStrokes}ȸ");
        GUILayout.Label($"��� ����: {currentCreationData.toolsUsed.Count}��");
        GUILayout.Label($"���� ����: {currentTool}");
        GUILayout.Label($"��Ʈ��ũ ���⵵: {currentCreationData.artworkComplexity:F3}");
        GUILayout.Label($"����: v{currentCreationData.versionNumber}");

        GUILayout.Space(10);

        // ��ȣ �ý��� ����
        GUILayout.Label("=== ��ȣ �ý��� ===", GUI.skin.box);
        GUILayout.Label($"VR �ùķ��̼�: {(useVRSimulation ? "Ȱ��" : "��Ȱ��")}");
        GUILayout.Label($"�ػ�: {artworkResolution}x{artworkResolution}");
        GUILayout.Label($"��� FPS: {(fpsHistory.Count > 0 ? fpsHistory[fpsHistory.Count - 1] : 0):F1}");
        GUILayout.Label($"��ȣ�� ��ǰ: {protectedArtworks.Count}��");
        GUILayout.Label($"���� �ڵ� ��ȣ: {(autoProtectionInterval - (Time.time - lastProtectionTime)):F0}�� ��");

        GUILayout.Space(10);

        // ���� ��ư��
        GUILayout.Label("=== ���� ===", GUI.skin.box);

        if (GUILayout.Button("��Ʈ��ũ ��ȣ ���� (Space)"))
        {
            StartCoroutine(ProtectCurrentArtwork("Manual_GUI"));
        }

        if (GUILayout.Button("���� ���� �ùķ��̼� (T)"))
        {
            SimulateToolChange();
        }

        if (GUILayout.Button("�귯�� ��Ʈ��ũ �߰� (B)"))
        {
            SimulateBrushStroke();
        }

        GUILayout.Space(5);
        GUILayout.Label("Space: ��ȣ ����, T: ��������, B: �귯�ý�Ʈ��ũ");

        GUILayout.EndArea();
    }

    #endregion

    void OnDestroy()
    {
        // �����ؽ�ó ����
        while (artRenderTexturePool.Count > 0)
        {
            RenderTexture rt = artRenderTexturePool.Dequeue();
            if (rt != null) rt.Release();
        }

        foreach (var rt in activeArtRenderTextures)
        {
            if (rt != null) rt.Release();
        }

        Debug.Log($"VR ��Ʈ â�� ���� ���� - �� ��ȣ�� ��ǰ: {protectedArtworks.Count}��");
    }
}