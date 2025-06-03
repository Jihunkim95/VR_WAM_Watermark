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
    [SerializeField] private int minimumCreationActions = 5; // 최소 창작 행동 수

    [Header("VR Art Protection Settings")]
    [SerializeField] private int artworkResolution = 1024; // 아트워크는 고해상도
    [SerializeField] private float creationComplexityThreshold = 0.3f;
    [SerializeField] private bool enableVersionControl = true;

    [Header("Auto Protection Triggers")]
    [SerializeField] private bool protectOnBrushChange = true;
    [SerializeField] private bool protectOnCreationMilestone = true;
    [SerializeField] private float autoProtectionInterval = 180f; // 3분마다
    [SerializeField] private bool protectOnSessionEnd = true;

    [Header("VR Simulation")]
    [SerializeField] private bool useVRSimulation = true;
    [SerializeField] private float simulationRadius = 2f;

    // VR 창작 데이터
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
        public float creationDuration; // 총 창작 시간 (초)
        public Vector3 primaryCreationArea; // 주요 창작 영역
        public List<string> toolsUsed;
        public float artworkComplexity;
        public string sessionID;
    }

    // 6방향 카메라 정의
    public enum ArtViewDirection
    {
        MainView = 0,      // 주요 감상 각도
        DetailView = 1,    // 세부 디테일 각도
        ProfileLeft = 2,   // 좌측 프로필
        ProfileRight = 3,  // 우측 프로필
        TopView = 4,       // 상단 전체 뷰
        BottomView = 5     // 하단 구조 뷰
    }

    // 창작 보호 결과
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

    // 렌더링 및 시스템 변수들
    private Camera[] artViewCameras = new Camera[6];
    private Vector3[] optimalCameraPositions = new Vector3[6];
    private Vector3[] optimalCameraRotations = new Vector3[6];
    private Queue<RenderTexture> artRenderTexturePool = new Queue<RenderTexture>();
    private List<RenderTexture> activeArtRenderTextures = new List<RenderTexture>();

    // 창작 추적 변수들
    private CreationMetadata currentCreationData;
    private Vector3 lastBrushPosition = Vector3.zero;
    private string currentTool = "Default_Brush";
    private int currentBrushStrokes = 0;
    private float sessionStartTime;
    private float lastProtectionTime;
    private List<Vector3> creationHotspots = new List<Vector3>();

    // VR 시뮬레이션 변수들
    private float lastSimulationUpdate = 0f;
    private float simulationUpdateInterval = 2f; // 2초마다 위치 변경

    // 파일 저장 관련
    private List<string> protectedArtworks = new List<string>();

    // 이벤트
    public System.Action<ArtworkProtectionResult> OnArtworkProtected;
    public System.Action<CreationMetadata> OnCreationMilestoneReached;
    public System.Action<string> OnToolChanged;

    // 성능 및 UI
    private List<float> fpsHistory = new List<float>();

    void Start()
    {
        InitializeVRArtSystem();
        InitializeCreationTracking();

        // artworkContainer가 없으면 반드시 생성
        if (artworkContainer == null)
        {
            CreateSampleArtwork();
        }

        SetupArtRenderingCameras();
        SetupRenderTexturePool();

        Debug.Log($"VR 아트 창작 보호 시스템 시작 - 아티스트: {artistName}");
    }

    void Update()
    {
        MonitorCreationActivity();
        TrackArtworkChanges();
        CheckProtectionTriggers();
        MonitorPerformance();

        // 수동 보호 실행 (VR 컨트롤러 또는 키보드)
        if (IsProtectionTriggerPressed())
        {
            StartCoroutine(ProtectCurrentArtwork("Manual_Protection"));
        }

        // 도구 변경 테스트 (개발용)
        if (IsToolChangePressed()) SimulateToolChange();
        if (IsBrushStrokePressed()) SimulateBrushStroke();
    }

    #region VR Art System Initialization

    void InitializeVRArtSystem()
    {
        // 예술 작품용 최적화 설정 (WAM은 256x256 실험했지만, 고해상도 가능 및 VR처리 한계 고려 512,2048)
        artworkResolution = Mathf.Clamp(artworkResolution, 512, 2048);

        // 아트워크 전용 레이어 설정
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

        Debug.Log($"창작 세션 시작 - ID: {currentCreationData.sessionID}");
    }

    void SetupArtRenderingCameras()
    {
        // 예술 작품 감상에 최적화된 카메라 각도 설정
        float artDistance = CalculateOptimalViewingDistance();

        // 주요 감상 각도 (정면 약간 우상단)
        optimalCameraPositions[(int)ArtViewDirection.MainView] = new Vector3(1f, 0.8f, 2f).normalized * artDistance;
        optimalCameraRotations[(int)ArtViewDirection.MainView] = new Vector3(-15f, -25f, 0f);

        // 세부 디테일 각도 (가까운 거리)
        optimalCameraPositions[(int)ArtViewDirection.DetailView] = new Vector3(0.5f, 0f, 1f).normalized * (artDistance * 0.7f);
        optimalCameraRotations[(int)ArtViewDirection.DetailView] = new Vector3(0f, -30f, 0f);

        // 좌측 프로필
        optimalCameraPositions[(int)ArtViewDirection.ProfileLeft] = Vector3.left * artDistance;
        optimalCameraRotations[(int)ArtViewDirection.ProfileLeft] = new Vector3(0f, 90f, 0f);

        // 우측 프로필
        optimalCameraPositions[(int)ArtViewDirection.ProfileRight] = Vector3.right * artDistance;
        optimalCameraRotations[(int)ArtViewDirection.ProfileRight] = new Vector3(0f, -90f, 0f);

        // 상단 전체 뷰
        optimalCameraPositions[(int)ArtViewDirection.TopView] = Vector3.up * artDistance;
        optimalCameraRotations[(int)ArtViewDirection.TopView] = new Vector3(90f, 0f, 0f);

        // 하단 구조 뷰
        optimalCameraPositions[(int)ArtViewDirection.BottomView] = Vector3.down * artDistance;
        optimalCameraRotations[(int)ArtViewDirection.BottomView] = new Vector3(-90f, 0f, 0f);

        // 카메라 생성 및 설정
        for (int i = 0; i < 6; i++)
        {
            GameObject cameraGO = new GameObject($"ArtCamera_{(ArtViewDirection)i}");
            cameraGO.transform.SetParent(transform);

            Camera cam = cameraGO.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f); // 아트 전시용 배경
            cam.orthographic = false;
            cam.fieldOfView = (i == (int)ArtViewDirection.DetailView) ? 45f : 60f; // 디테일 뷰는 더 좁은 시야각
            cam.cullingMask = artworkLayerMask;

            artViewCameras[i] = cam;
        }

        Debug.Log("VR 아트 감상용 6방향 카메라 시스템 초기화 완료");
    }

    float CalculateOptimalViewingDistance()
    {
        if (artworkContainer == null)
        {
            Debug.LogWarning("artworkContainer가 null입니다. 기본 거리를 반환합니다.");
            return 3f;
        }

        Bounds bounds = GetArtworkBounds();
        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

        // 아트워크 크기에 따른 최적 감상 거리
        return Mathf.Clamp(maxDimension * 2.5f, 2f, 8f);
    }

    // Pool 렌더텍스처 초기화 (Queue)
    void SetupRenderTexturePool()
    {
        for (int i = 0; i < 8; i++) // 아트워크용으로 더 많은 텍스처 준비
        {
            RenderTexture rt = CreateArtRenderTexture();
            artRenderTexturePool.Enqueue(rt);
        }

        Debug.Log($"아트워크 렌더텍스처 풀 초기화: {artRenderTexturePool.Count}개");
    }

    RenderTexture CreateArtRenderTexture()
    {
        RenderTexture rt = new RenderTexture(artworkResolution, artworkResolution, 24);
        rt.format = RenderTextureFormat.ARGB32;
        rt.filterMode = FilterMode.Trilinear; // 아트워크용 고품질 필터링
        rt.antiAliasing = 4; // 아트워크용 안티앨리어싱
        rt.Create();
        return rt;
    }

    #endregion

    #region Creation Activity Monitoring

    void MonitorCreationActivity()
    {
        if (!enableCreationTracking) return;

        // 창작 시간 업데이트
        currentCreationData.creationDuration = Time.time - sessionStartTime;
        currentCreationData.lastModificationTime = DateTime.Now;

        // VR 컨트롤러 위치 모니터링
        Vector3 currentBrushPosition;

        if (useVRSimulation)
        {
            // 시뮬레이션 모드: 주기적으로 브러시 위치 변경
            if (Time.time - lastSimulationUpdate > simulationUpdateInterval)
            {
                currentBrushPosition = GetCurrentBrushPosition();
                lastSimulationUpdate = Time.time;

                // 시뮬레이션에서는 항상 위치 변경으로 간주
                OnBrushPositionChanged(currentBrushPosition);
                lastBrushPosition = currentBrushPosition;
            }
        }
        else
        {
            // 실제 VR 모드: 기존 로직
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
        // VR 시뮬레이션 모드 사용
        if (useVRSimulation)
        {
            // artworkContainer가 없으면 먼저 생성
            if (artworkContainer == null)
            {
                CreateSampleArtwork();
            }

            // VR 컨트롤러 위치 시뮬레이션 (아트워크 주변의 랜덤 위치)
            Vector3 artworkCenter = artworkContainer != null ? artworkContainer.position : Vector3.zero;
            Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * simulationRadius;
            return artworkCenter + randomOffset;
        }

        // 실제 VR 환경에서는 VR 컨트롤러 위치를 반환
        // 마우스 입력 백업 (Input System 호환)
        Vector3 mousePos;

#if ENABLE_INPUT_SYSTEM
        // Input System 사용
        if (Mouse.current != null)
        {
            Vector2 screenPos = Mouse.current.position.ReadValue();
            mousePos = new Vector3(screenPos.x, screenPos.y, 2f);
        }
        else
        {
            // 마우스가 없으면 화면 중앙
            mousePos = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 2f);
        }
#else
        // Legacy Input Manager 사용
        mousePos = Input.mousePosition;
        mousePos.z = 2f; // 카메라로부터의 거리
#endif

        if (Camera.main != null)
        {
            return Camera.main.ScreenToWorldPoint(mousePos);
        }
        else
        {
            // 카메라가 없으면 월드 좌표로 간단 변환
            return new Vector3(
                (mousePos.x / Screen.width - 0.5f) * 10f,
                (mousePos.y / Screen.height - 0.5f) * 10f,
                0f
            );
        }
    }

    void OnBrushPositionChanged(Vector3 newPosition)
    {
        // 창작 핫스팟 기록
        creationHotspots.Add(newPosition);

        // 너무 많이 쌓이면 오래된 것 제거
        if (creationHotspots.Count > 100)
        {
            creationHotspots.RemoveAt(0);
        }

        // 주요 창작 영역 업데이트
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
        // 아트워크 복잡도 실시간 계산
        if (Time.time - lastProtectionTime > 10f) // 10초마다 복잡도 재계산
        {
            currentCreationData.artworkComplexity = CalculateArtworkComplexity();
        }
    }

    float CalculateArtworkComplexity()
    {
        if (artworkContainer == null) return 0f;

        float complexity = 0f;

        // 브러시 스트로크 수에 따른 복잡도
        complexity += Mathf.Clamp01(currentBrushStrokes / 100f) * 0.3f;

        // 창작 핫스팟 분산도에 따른 복잡도
        if (creationHotspots.Count > 1)
        {
            float spread = CalculateHotspotSpread();
            complexity += Mathf.Clamp01(spread / 5f) * 0.3f;
        }

        // 사용된 도구 다양성에 따른 복잡도
        complexity += Mathf.Clamp01(currentCreationData.toolsUsed.Count / 5f) * 0.2f;

        // 창작 시간에 따른 복잡도
        complexity += Mathf.Clamp01(currentCreationData.creationDuration / 1800f) * 0.2f; // 30분 기준

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
        // 브러시 변경 시 보호
        if (protectOnBrushChange && HasToolChanged())
        {
            StartCoroutine(ProtectCurrentArtwork("Tool_Change"));
        }

        // 창작 마일스톤 달성 시 보호
        if (protectOnCreationMilestone && HasReachedCreationMilestone())
        {
            StartCoroutine(ProtectCurrentArtwork("Creation_Milestone"));
            OnCreationMilestoneReached?.Invoke(currentCreationData);
        }

        // 자동 주기적 보호
        if (Time.time - lastProtectionTime >= autoProtectionInterval)
        {
            StartCoroutine(ProtectCurrentArtwork("Auto_Protection"));
        }
    }

    bool HasToolChanged()
    {
        // 실제 VR 환경에서는 컨트롤러의 도구 변경을 감지
        // 지금은 시뮬레이션으로 처리
        return false; // 별도 입력으로 처리
    }

    bool HasReachedCreationMilestone()
    {
        // 창작 마일스톤 조건들
        bool strokeMilestone = (currentBrushStrokes > 0) && (currentBrushStrokes % 25 == 0);
        bool complexityMilestone = currentCreationData.artworkComplexity >= creationComplexityThreshold;
        bool timeMilestone = currentCreationData.creationDuration >= 300f; // 5분

        return strokeMilestone || complexityMilestone || timeMilestone;
    }

    #endregion

    #region Artwork Protection Process

    public IEnumerator ProtectCurrentArtwork(string triggerReason = "Manual")
    {
        float startTime = Time.realtimeSinceStartup;

        Debug.Log($"아트워크 보호 시작 - 사유: {triggerReason}, 복잡도: {currentCreationData.artworkComplexity:F3}");

        // 카메라 위치 업데이트 (아트워크 중심으로)
        UpdateArtCameraPositions();

        // 6방향 아트 뷰 렌더링
        Dictionary<ArtViewDirection, ArtViewData> artViewResults = new Dictionary<ArtViewDirection, ArtViewData>();

        for (int i = 0; i < 6; i++)
        {
            ArtViewDirection direction = (ArtViewDirection)i;
            yield return StartCoroutine(RenderArtView(direction, artViewResults));
            yield return null; // 프레임 분산
        }

        // 최적 보호 뷰 선택
        var bestArtView = SelectBestArtView(artViewResults);

        // 보호 결과 생성
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

        // 버전 관리
        if (enableVersionControl)
        {
            currentCreationData.versionNumber++;
            SaveArtworkProtection(result, triggerReason);
        }

        // 사용하지 않는 이미지 정리
        CleanupUnusedArtImages(artViewResults, bestArtView.direction);

        lastProtectionTime = Time.time;

        // 이벤트 발생
        OnArtworkProtected?.Invoke(result);

        Debug.Log($"아트워크 보호 완료 - 방향: {result.primaryDirection}, " +
                 $"품질점수: {bestArtView.qualityScore:F3}, 처리시간: {result.protectionProcessingTime:F3}초, " +
                 $"워터마킹 준비: {(result.readyForWatermarking ? "완료" : "대기")}");
    }

    void UpdateArtCameraPositions()
    {
        // artworkContainer가 없으면 중앙 위치 기준으로 설정
        if (artworkContainer == null)
        {
            Debug.LogWarning("artworkContainer가 null입니다. 기본 위치로 카메라를 설정합니다.");

            // 기본 위치로 카메라 설정
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

        // 아트워크 중심을 기준으로 각 카메라 위치 조정
        for (int i = 0; i < 6; i++)
        {
            Camera cam = artViewCameras[i];
            ArtViewDirection direction = (ArtViewDirection)i;

            // 위치 설정
            Vector3 offset = optimalCameraPositions[i].normalized * optimalDistance;
            cam.transform.position = center + offset;

            // 회전 설정 (아트워크 중심을 바라보도록)
            cam.transform.LookAt(center);

            // 특별한 각도 조정
            if (direction == ArtViewDirection.MainView)
            {
                // 주요 감상 각도는 약간 위에서
                cam.transform.position += Vector3.up * (optimalDistance * 0.2f);
                cam.transform.LookAt(center);
            }
            else if (direction == ArtViewDirection.DetailView)
            {
                // 디테일 뷰는 더 가까이
                cam.transform.position = center + offset * 0.6f;
                cam.fieldOfView = 45f;
            }

            // 아트워크 크기에 맞는 시야각 조정
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

        // 아트워크 전용 렌더링 설정
        cam.enabled = true;
        cam.Render();
        cam.enabled = false;

        yield return new WaitForEndOfFrame();

        // 고품질 텍스처 변환
        Texture2D artImage = RenderTextureToTexture2D(renderTexture);

        // 아트워크 품질 점수 계산
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

        // 아트워크 내용 비율 계산
        int artworkPixels = 0;
        float totalSaturation = 0f;
        float totalContrast = 0f;

        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];

            // 배경이 아닌 아트워크 픽셀 감지
            if (pixel.a > 0.2f && !IsBackgroundColor(pixel))
            {
                artworkPixels++;

                // 채도와 대비 계산
                float saturation = Mathf.Max(pixel.r, pixel.g, pixel.b) - Mathf.Min(pixel.r, pixel.g, pixel.b);
                totalSaturation += saturation;

                float brightness = pixel.grayscale;
                totalContrast += Mathf.Abs(brightness - 0.5f);
            }
        }

        // 아트워크 비율 점수
        float artworkRatio = (float)artworkPixels / pixels.Length;
        score += artworkRatio * 0.4f;

        // 평균 채도 점수
        if (artworkPixels > 0)
        {
            float avgSaturation = totalSaturation / artworkPixels;
            score += avgSaturation * 0.3f;

            float avgContrast = totalContrast / artworkPixels;
            score += avgContrast * 0.3f;
        }

        // 방향별 가중치 (주요 감상 각도에 더 높은 점수)
        float directionWeight = GetDirectionWeight(direction);
        score *= directionWeight;

        return Mathf.Clamp01(score);
    }

    bool IsBackgroundColor(Color color)
    {
        // 배경색 범위 (어두운 회색계열)
        return color.r < 0.2f && color.g < 0.2f && color.b < 0.25f;
    }

    float GetDirectionWeight(ArtViewDirection direction)
    {
        switch (direction)
        {
            case ArtViewDirection.MainView: return 1.2f;      // 주요 감상 각도
            case ArtViewDirection.DetailView: return 1.1f;   // 디테일 뷰
            case ArtViewDirection.ProfileLeft: return 1.0f;  // 프로필
            case ArtViewDirection.ProfileRight: return 1.0f; // 프로필
            case ArtViewDirection.TopView: return 0.8f;      // 상단 뷰
            case ArtViewDirection.BottomView: return 0.7f;   // 하단 뷰
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

        // 주요 보호 이미지 저장
        string primaryFileName = $"{currentCreationData.projectName}_v{currentCreationData.versionNumber:D3}_{triggerReason}_{result.primaryDirection}_{timestamp}.png";
        string primaryPath = sessionFolder + primaryFileName;
        SaveTextureToPNG(result.primaryProtectionImage, primaryPath);

        // 메타데이터 저장
        string metadataFileName = $"{currentCreationData.projectName}_v{currentCreationData.versionNumber:D3}_metadata_{timestamp}.json";
        string metadataPath = sessionFolder + metadataFileName;
        SaveCreationMetadata(result.metadata, metadataPath);

        protectedArtworks.Add(primaryPath);

        Debug.Log($"아트워크 보호 파일 저장: {primaryPath}");
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
            Debug.LogError($"메타데이터 저장 실패: {e.Message}");
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
        // 샘플 아트워크 생성 (테스트용)
        GameObject artwork = GameObject.CreatePrimitive(PrimitiveType.Cube);
        artwork.name = "Sample_VR_Artwork";
        artwork.transform.position = Vector3.zero;
        artwork.transform.localScale = Vector3.one * 1.5f;

        // 아트워크답게 색상 추가
        Renderer renderer = artwork.GetComponent<Renderer>();
        Material artMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        artMaterial.color = Color.HSVToRGB(UnityEngine.Random.Range(0f, 1f), 0.8f, 0.9f);

        // Standard 셰이더 프로퍼티로 설정
        artMaterial.SetFloat("_Metallic", 0.3f);
        artMaterial.SetFloat("_Glossiness", 0.7f);

        renderer.material = artMaterial;

        artworkContainer = artwork.transform;
        Debug.Log("샘플 VR 아트워크 생성 완료");
    }

    Bounds GetArtworkBounds()
    {
        // artworkContainer가 없으면 기본 Bounds 반환
        if (artworkContainer == null)
        {
            Debug.LogWarning("artworkContainer가 null입니다. 기본 Bounds를 반환합니다.");
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
            Debug.Log($"도구 변경: {newTool}");

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

        // 랜덤 위치에 브러시 스트로크 추가
        Vector3 randomPos = UnityEngine.Random.insideUnitSphere * 2f;
        OnBrushPositionChanged(randomPos);

        Debug.Log($"브러시 스트로크 #{currentBrushStrokes} - 복잡도: {currentCreationData.artworkComplexity:F3}");
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

        GUILayout.Label("=== VR 아트 창작 보호 시스템 ===", GUI.skin.box);

        // 아티스트 정보
        GUILayout.Label($"아티스트: {artistName} (ID: {artistID})");
        GUILayout.Label($"프로젝트: {projectName}");
        GUILayout.Label($"세션 ID: {currentCreationData.sessionID}");

        GUILayout.Space(10);

        // 창작 현황
        GUILayout.Label("=== 창작 현황 ===", GUI.skin.box);
        GUILayout.Label($"창작 시간: {currentCreationData.creationDuration:F1}초");
        GUILayout.Label($"브러시 스트로크: {currentBrushStrokes}회");
        GUILayout.Label($"사용 도구: {currentCreationData.toolsUsed.Count}개");
        GUILayout.Label($"현재 도구: {currentTool}");
        GUILayout.Label($"아트워크 복잡도: {currentCreationData.artworkComplexity:F3}");
        GUILayout.Label($"버전: v{currentCreationData.versionNumber}");

        GUILayout.Space(10);

        // 보호 시스템 상태
        GUILayout.Label("=== 보호 시스템 ===", GUI.skin.box);
        GUILayout.Label($"VR 시뮬레이션: {(useVRSimulation ? "활성" : "비활성")}");
        GUILayout.Label($"해상도: {artworkResolution}x{artworkResolution}");
        GUILayout.Label($"평균 FPS: {(fpsHistory.Count > 0 ? fpsHistory[fpsHistory.Count - 1] : 0):F1}");
        GUILayout.Label($"보호된 작품: {protectedArtworks.Count}개");
        GUILayout.Label($"다음 자동 보호: {(autoProtectionInterval - (Time.time - lastProtectionTime)):F0}초 후");

        GUILayout.Space(10);

        // 조작 버튼들
        GUILayout.Label("=== 조작 ===", GUI.skin.box);

        if (GUILayout.Button("아트워크 보호 실행 (Space)"))
        {
            StartCoroutine(ProtectCurrentArtwork("Manual_GUI"));
        }

        if (GUILayout.Button("도구 변경 시뮬레이션 (T)"))
        {
            SimulateToolChange();
        }

        if (GUILayout.Button("브러시 스트로크 추가 (B)"))
        {
            SimulateBrushStroke();
        }

        GUILayout.Space(5);
        GUILayout.Label("Space: 보호 실행, T: 도구변경, B: 브러시스트로크");

        GUILayout.EndArea();
    }

    #endregion

    void OnDestroy()
    {
        // 렌더텍스처 정리
        while (artRenderTexturePool.Count > 0)
        {
            RenderTexture rt = artRenderTexturePool.Dequeue();
            if (rt != null) rt.Release();
        }

        foreach (var rt in activeArtRenderTextures)
        {
            if (rt != null) rt.Release();
        }

        Debug.Log($"VR 아트 창작 세션 종료 - 총 보호된 작품: {protectedArtworks.Count}개");
    }
}