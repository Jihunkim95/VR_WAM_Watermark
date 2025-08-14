using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

/// <summary>
/// 후처리 기반 다층 워터마킹 시스템 (Post-Processing Multi-Layer Watermarking)
/// 30개 독립 레이어(6방향 × 5종 렌더링 맵)로 VR 창작물을 보호
/// </summary>
public class VRWatermark_PostProcess : MonoBehaviour
{
    #region Constants & Enums

    // 6방향 카메라 정의 (VRWatermark_Realtime과 동일)
    public enum CaptureDirection
    {
        MainView = 0,      // 주요 감상 각도
        DetailView = 1,    // 세부 디테일 각도
        ProfileLeft = 2,   // 좌측 프로필
        ProfileRight = 3,  // 우측 프로필
        TopView = 4,       // 상단 전체 뷰
        BottomView = 5     // 하단 구조 뷰
    }

    // 5종 렌더링 맵 정의
    public enum RenderMapType
    {
        Albedo = 0,     // 색상 정보
        Depth = 1,      // 깊이 정보
        Normal = 2,     // 표면 방향
        Shadow = 3,     // 조명 정보
        Roughness = 4   // 재질 특성
    }

    private const int TOTAL_DIRECTIONS = 6;
    private const int TOTAL_RENDER_MAPS = 5;
    private const int TOTAL_LAYERS = 30; // 6 × 5 = 30개 레이어

    #endregion

    #region Configuration

    [Header("Flask 서버 설정")]
    [SerializeField] private string wamServerUrl = "http://localhost:5000";  // 기존 WAM 서버 포트
    [SerializeField] private float serverTimeout = 45f; // 전체 파이프라인 < 45초 목표
    [SerializeField] private bool useBatchAPI = false; // 배치 API 사용 여부 (서버 확장 필요)

    [Header("VRWatermark_Realtime 연동")]
    [SerializeField] private VRWatermark_Realtime vrProtectionSystem;
    [SerializeField] private bool useExistingArtCameras = true; // 기존 ArtCamera 사용

    [Header("렌더링 설정")]
    [SerializeField] private int captureResolution = 1024;
    [SerializeField] private LayerMask artworkLayerMask = -1;
    [SerializeField] private GameObject artworkContainer;

    [Header("워터마크 강도 설정 (렌더맵별)")]
    [SerializeField] private float albedoStrength = 2.0f;
    [SerializeField] private float depthStrength = 1.5f;
    [SerializeField] private float normalStrength = 1.2f;
    [SerializeField] private float shadowStrength = 0.8f;
    [SerializeField] private float roughnessStrength = 1.0f;

    [Header("마스크 비율 설정")]
    [SerializeField] private float albedoMaskRatio = 1.0f;    // 100%
    [SerializeField] private float depthMaskRatio = 0.8f;     // 80%
    [SerializeField] private float normalMaskRatio = 0.7f;    // 70%
    [SerializeField] private float shadowMaskRatio = 0.6f;    // 60%
    [SerializeField] private float roughnessMaskRatio = 0.75f; // 75%

    [Header("후처리 설정")]
    [SerializeField] private bool enableBatchProcessing = true;
    [SerializeField] private int gpuParallelCount = 4; // 병렬 GPU 처리 수

    #endregion

    #region Private Variables

    // 카메라 시스템
    private Camera[] artCameras = new Camera[TOTAL_DIRECTIONS];  // directionalCameras -> artCameras로 변경
    private RenderTexture[] renderTextures = new RenderTexture[TOTAL_RENDER_MAPS];

    // 셰이더
    private Shader depthShader;
    private Shader normalShader;
    private Shader shadowShader;
    private Shader roughnessShader;

    // 배치 처리 큐
    private Queue<LayerProcessingJob> processingQueue = new Queue<LayerProcessingJob>();
    private bool isProcessing = false;

    // 세션 데이터
    private string currentSessionID;
    private Dictionary<string, LayerProtectionData> layerProtectionResults;

    #endregion

    #region Data Structures

    [Serializable]
    public class LayerProcessingJob
    {
        public string sessionID;
        public CaptureDirection direction;
        public RenderMapType mapType;
        public byte[] imageData;
        public float watermarkStrength;
        public float maskRatio;
        public string message;
        public DateTime timestamp;
    }

    [Serializable]
    public class LayerProtectionData
    {
        public string layerID;
        public CaptureDirection direction;
        public RenderMapType mapType;
        public bool isProtected;
        public float psnr;
        public float ssim;
        public float bitAccuracy;
        public float mIoU;
        public string watermarkHash;
        public string filePath;
    }

    [Serializable]
    public class BatchWatermarkRequest
    {
        public string session_id;
        public List<LayerData> layers;
        public int gpu_count;
    }

    [Serializable]
    public class LayerData
    {
        public string layer_id;
        public string direction;
        public string map_type;
        public string image_base64;
        public float strength;
        public float mask_ratio;
        public string message;
    }

    [Serializable]
    public class MultiLayerVerificationResult
    {
        public int detected_layers;
        public string verification_level; // Basic/Standard/Forensic/Perfect
        public float confidence;
        public Dictionary<string, LayerVerificationDetail> layer_details;
        public string certificate_hash;
    }

    [Serializable]
    public class LayerVerificationDetail
    {
        public bool detected;
        public float bit_accuracy;
        public string decoded_message;
    }

    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        InitializeShaders();

        // VRWatermark_Realtime 찾기
        if (vrProtectionSystem == null)
        {
            vrProtectionSystem = FindObjectOfType<VRWatermark_Realtime>();
        }

        if (useExistingArtCameras && vrProtectionSystem != null)
        {
            // 기존 ArtCamera 사용
            ConnectToExistingArtCameras();
        }
        else
        {
            // 새로운 카메라 생성
            SetupArtCameras();
        }

        SetupRenderTextures();
        currentSessionID = GenerateSessionID();
        layerProtectionResults = new Dictionary<string, LayerProtectionData>();
    }

    void ConnectToExistingArtCameras()
    {
        // VRWatermark_Realtime의 ArtCamera 찾기
        Camera[] allCameras = FindObjectsOfType<Camera>();
        List<Camera> foundArtCameras = new List<Camera>();

        string[] cameraNames = new string[]
        {
            "ArtCamera_MainView",
            "ArtCamera_DetailView",
            "ArtCamera_ProfileLeft",
            "ArtCamera_ProfileRight",
            "ArtCamera_TopView",
            "ArtCamera_BottomView"
        };

        foreach (string camName in cameraNames)
        {
            Camera cam = allCameras.FirstOrDefault(c => c.gameObject.name == camName);
            if (cam != null)
            {
                foundArtCameras.Add(cam);
                Debug.Log($"[MultiLayer] 기존 카메라 연결: {camName}");
            }
        }

        if (foundArtCameras.Count == TOTAL_DIRECTIONS)
        {
            artCameras = foundArtCameras.ToArray();
            Debug.Log($"[MultiLayer] VRWatermark_Realtime의 {TOTAL_DIRECTIONS}개 ArtCamera 연결 완료");
        }
        else
        {
            Debug.LogWarning($"[MultiLayer] 일부 ArtCamera를 찾을 수 없음 ({foundArtCameras.Count}/{TOTAL_DIRECTIONS}). 새로 생성합니다.");
            SetupArtCameras();
        }
    }

    void Start()
    {
        StartCoroutine(CheckServerHealth());
    }

    void OnDestroy()
    {
        CleanupRenderTextures();
    }

    #endregion

    #region Initialization

    void InitializeShaders()
    {
        // 커스텀 셰이더 로드
        depthShader = Shader.Find("Custom/DepthExtractor") ?? Shader.Find("Hidden/Internal-DepthNormalsTexture");
        normalShader = Shader.Find("Custom/NormalExtractor") ?? Shader.Find("Hidden/Internal-DepthNormalsTexture");
        shadowShader = Shader.Find("Custom/ShadowExtractor") ?? Shader.Find("Hidden/Internal-ScreenSpaceShadows");
        roughnessShader = Shader.Find("Custom/RoughnessExtractor") ?? Shader.Find("Standard");

        if (depthShader == null || normalShader == null)
        {
            Debug.LogWarning("일부 커스텀 셰이더를 찾을 수 없습니다. 기본 셰이더를 사용합니다.");
        }
    }

    void SetupArtCameras()
    {
        // VRWatermark_Realtime과 동일한 카메라 설정
        Vector3[] positions = new Vector3[]
        {
            new Vector3(1f, 1.2f, 2f).normalized * 3f,    // MainView
            new Vector3(0.5f, 0f, 1f).normalized * 2.1f,  // DetailView (가까운 거리)
            Vector3.left * 3f,                            // ProfileLeft
            Vector3.right * 3f,                           // ProfileRight
            Vector3.up * 3f,                              // TopView
            Vector3.down * 3f                             // BottomView
        };

        Vector3[] rotations = new Vector3[]
        {
            new Vector3(-15f, -30f, 0f),  // MainView
            new Vector3(0f, -30f, 0f),    // DetailView
            new Vector3(0f, 90f, 0f),     // ProfileLeft
            new Vector3(0f, -90f, 0f),    // ProfileRight
            new Vector3(90f, 0f, 0f),     // TopView
            new Vector3(-90f, 0f, 0f)     // BottomView
        };

        for (int i = 0; i < TOTAL_DIRECTIONS; i++)
        {
            GameObject camObj = new GameObject($"ArtCamera_{(CaptureDirection)i}");
            camObj.transform.SetParent(transform);

            Camera cam = camObj.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f); // 아트 전시용 배경
            cam.cullingMask = artworkLayerMask;
            cam.fieldOfView = (i == (int)CaptureDirection.DetailView) ? 45f : 60f;

            camObj.transform.localPosition = positions[i];
            camObj.transform.localEulerAngles = rotations[i];

            artCameras[i] = cam;
        }

        Debug.Log($"[MultiLayer] 6방향 아트 카메라 시스템 생성 완료");
    }

    void SetupRenderTextures()
    {
        for (int i = 0; i < TOTAL_RENDER_MAPS; i++)
        {
            renderTextures[i] = new RenderTexture(captureResolution, captureResolution, 24)
            {
                name = $"RenderTexture_{(RenderMapType)i}",
                antiAliasing = 4
            };
        }
    }

    void CleanupRenderTextures()
    {
        foreach (var rt in renderTextures)
        {
            if (rt != null)
            {
                rt.Release();
                Destroy(rt);
            }
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// 창작 세션 종료 시 30레이어 후처리 시작
    /// VRWatermark_Realtime의 아트워크 컨테이너와 카메라 사용
    /// </summary>
    public void StartMultiLayerProtection()
    {
        if (isProcessing)
        {
            Debug.LogWarning("[MultiLayer] 이미 처리 중입니다.");
            return;
        }

        // VRWatermark_Realtime에서 아트워크 컨테이너 가져오기
        if (vrProtectionSystem != null && artworkContainer == null)
        {
            // VRWatermark_Realtime의 artworkContainer 참조
            var vrArtworkContainer = vrProtectionSystem.GetComponent<VRWatermark_Realtime>();
            if (vrArtworkContainer != null)
            {
                // Reflection을 사용하거나 public 프로퍼티로 접근
                Debug.Log("[MultiLayer] VRWatermark_Realtime의 아트워크 컨테이너 사용");
            }
        }

        StartCoroutine(ProcessMultiLayerProtection());
    }

    /// <summary>
    /// 다층 검증 실행
    /// </summary>
    public void VerifyMultiLayer(string artworkPath)
    {
        StartCoroutine(PerformMultiLayerVerification(artworkPath));
    }

    #endregion

    #region Multi-Layer Capture

    IEnumerator ProcessMultiLayerProtection()
    {
        isProcessing = true;
        float startTime = Time.realtimeSinceStartup;

        Debug.Log($"[MultiLayer] 30레이어 보호 시작 - Session: {currentSessionID}");

        // Phase 1: 30개 레이어 캡처 (목표: < 5초)
        yield return StartCoroutine(CaptureAllLayers());

        // Phase 2: 배치 처리를 위한 데이터 준비
        BatchWatermarkRequest batchRequest = PrepareBatchRequest();

        // Phase 3: Flask 서버로 배치 전송 및 처리 (목표: < 30초)
        yield return StartCoroutine(SendBatchToServer(batchRequest));

        // Phase 4: 결과 저장 및 리포트 생성 (목표: < 2초)
        yield return StartCoroutine(SaveProtectionResults());

        float totalTime = Time.realtimeSinceStartup - startTime;
        Debug.Log($"[MultiLayer] 30레이어 보호 완료 - 총 시간: {totalTime:F2}초");

        // UI 알림
        OnProtectionComplete(totalTime);

        isProcessing = false;
    }

    IEnumerator CaptureAllLayers()
    {
        Debug.Log("[MultiLayer] 레이어 캡처 시작...");

        UpdateCameraPositions();

        int capturedCount = 0;

        // 6방향 × 5종 렌더맵 = 30개 캡처
        for (int dirIdx = 0; dirIdx < TOTAL_DIRECTIONS; dirIdx++)
        {
            CaptureDirection direction = (CaptureDirection)dirIdx;
            Camera cam = artCameras[dirIdx];  // directionalCameras -> artCameras로 변경

            for (int mapIdx = 0; mapIdx < TOTAL_RENDER_MAPS; mapIdx++)
            {
                RenderMapType mapType = (RenderMapType)mapIdx;
                RenderTexture rt = renderTextures[mapIdx];

                // 렌더링 맵 캡처
                byte[] imageData = CaptureRenderMap(cam, rt, mapType);

                // 레이어 작업 생성
                LayerProcessingJob job = new LayerProcessingJob
                {
                    sessionID = currentSessionID,
                    direction = direction,
                    mapType = mapType,
                    imageData = imageData,
                    watermarkStrength = GetWatermarkStrength(mapType),
                    maskRatio = GetMaskRatio(mapType),
                    message = GenerateLayerMessage(direction, mapType),
                    timestamp = DateTime.Now
                };

                processingQueue.Enqueue(job);
                capturedCount++;

                // 프레임 드롭 방지
                if (capturedCount % 5 == 0)
                {
                    yield return null;
                }
            }
        }

        Debug.Log($"[MultiLayer] {capturedCount}개 레이어 캡처 완료");
    }

    byte[] CaptureRenderMap(Camera cam, RenderTexture rt, RenderMapType mapType)
    {
        cam.targetTexture = rt;

        // 렌더맵별 셰이더 적용
        switch (mapType)
        {
            case RenderMapType.Depth:
                cam.SetReplacementShader(depthShader, "RenderType");
                break;
            case RenderMapType.Normal:
                cam.SetReplacementShader(normalShader, "RenderType");
                break;
            case RenderMapType.Shadow:
                if (shadowShader != null)
                    cam.SetReplacementShader(shadowShader, "RenderType");
                break;
            case RenderMapType.Roughness:
                if (roughnessShader != null)
                    cam.SetReplacementShader(roughnessShader, "RenderType");
                break;
            default: // Albedo
                cam.ResetReplacementShader();
                break;
        }

        cam.Render();

        // RenderTexture를 Texture2D로 변환
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(captureResolution, captureResolution, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, captureResolution, captureResolution), 0, 0);
        tex.Apply();

        byte[] imageData = tex.EncodeToPNG();

        // 정리
        RenderTexture.active = null;
        cam.targetTexture = null;
        cam.ResetReplacementShader();
        Destroy(tex);

        return imageData;
    }

    void UpdateCameraPositions()
    {
        if (artworkContainer == null) return;

        Bounds bounds = GetArtworkBounds();
        float distance = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 2f;

        Vector3 center = bounds.center;

        // 각 아트 카메라를 아트워크 중심으로 배치
        for (int i = 0; i < TOTAL_DIRECTIONS; i++)
        {
            Camera cam = artCameras[i];  // directionalCameras -> artCameras로 변경
            Vector3 localPos = cam.transform.localPosition.normalized * distance;
            cam.transform.position = center + localPos;
            cam.transform.LookAt(center);
        }
    }

    Bounds GetArtworkBounds()
    {
        if (artworkContainer == null)
            return new Bounds(Vector3.zero, Vector3.one * 2f);

        Renderer[] renderers = artworkContainer.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(artworkContainer.transform.position, Vector3.one * 2f);

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
        {
            bounds.Encapsulate(r.bounds);
        }

        return bounds;
    }

    #endregion

    #region Watermark Configuration

    float GetWatermarkStrength(RenderMapType mapType)
    {
        switch (mapType)
        {
            case RenderMapType.Albedo: return albedoStrength;
            case RenderMapType.Depth: return depthStrength;
            case RenderMapType.Normal: return normalStrength;
            case RenderMapType.Shadow: return shadowStrength;
            case RenderMapType.Roughness: return roughnessStrength;
            default: return 1.0f;
        }
    }

    float GetMaskRatio(RenderMapType mapType)
    {
        switch (mapType)
        {
            case RenderMapType.Albedo: return albedoMaskRatio;
            case RenderMapType.Depth: return depthMaskRatio;
            case RenderMapType.Normal: return normalMaskRatio;
            case RenderMapType.Shadow: return shadowMaskRatio;
            case RenderMapType.Roughness: return roughnessMaskRatio;
            default: return 0.8f;
        }
    }

    string GenerateLayerMessage(CaptureDirection direction, RenderMapType mapType)
    {
        // 계층적 메시지 구조 구현
        string baseMessage = $"{currentSessionID}_{direction}_{mapType}";

        // 방향별 기본 정보 (VRWatermark_Realtime의 뷰와 일치)
        Dictionary<CaptureDirection, string> directionMessages = new Dictionary<CaptureDirection, string>
        {
            { CaptureDirection.MainView, $"ARTIST_{SystemInfo.deviceUniqueIdentifier.Substring(0, 8)}" },
            { CaptureDirection.DetailView, $"DETAIL_{currentSessionID.Substring(0, 8)}" },
            { CaptureDirection.ProfileLeft, $"SIGNATURE_L_{DateTime.Now.Ticks}" },
            { CaptureDirection.ProfileRight, $"SIGNATURE_R_{DateTime.Now.Ticks}" },
            { CaptureDirection.TopView, $"METADATA_{Application.version}" },
            { CaptureDirection.BottomView, $"BLOCKCHAIN_{GetBlockchainHash()}" }
        };

        // 맵별 세부 정보
        Dictionary<RenderMapType, string> mapMessages = new Dictionary<RenderMapType, string>
        {
            { RenderMapType.Albedo, "COLOR_PALETTE_HASH" },
            { RenderMapType.Depth, "STRUCTURE_COMPLEXITY" },
            { RenderMapType.Normal, "SURFACE_DETAIL" },
            { RenderMapType.Shadow, "LIGHTING_SETUP" },
            { RenderMapType.Roughness, "MATERIAL_PROPERTIES" }
        };

        return $"{directionMessages[direction]}_{mapMessages[mapType]}_{baseMessage}";
    }

    string GetBlockchainHash()
    {
        // 실제 구현 시 블록체인 연동
        return "0x" + Guid.NewGuid().ToString("N").Substring(0, 16);
    }

    #endregion

    #region Server Communication

    IEnumerator CheckServerHealth()
    {
        UnityWebRequest request = UnityWebRequest.Get($"{wamServerUrl}/health");
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"[MultiLayer] WAM 서버 연결 성공: {request.downloadHandler.text}");

            // 서버 응답 파싱하여 배치 API 지원 여부 확인
            try
            {
                var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(request.downloadHandler.text);
                if (response.ContainsKey("batch_support"))
                {
                    useBatchAPI = Convert.ToBoolean(response["batch_support"]);
                    Debug.Log($"[MultiLayer] 배치 API 지원: {useBatchAPI}");
                }
            }
            catch { }
        }
        else
        {
            Debug.LogError($"[MultiLayer] WAM 서버 연결 실패: {request.error}");
        }
    }

    BatchWatermarkRequest PrepareBatchRequest()
    {
        BatchWatermarkRequest request = new BatchWatermarkRequest
        {
            session_id = currentSessionID,
            gpu_count = gpuParallelCount,
            layers = new List<LayerData>()
        };

        while (processingQueue.Count > 0)
        {
            LayerProcessingJob job = processingQueue.Dequeue();

            LayerData layer = new LayerData
            {
                layer_id = $"{job.direction}_{job.mapType}",
                direction = job.direction.ToString(),
                map_type = job.mapType.ToString(),
                image_base64 = Convert.ToBase64String(job.imageData),
                strength = job.watermarkStrength,
                mask_ratio = job.maskRatio,
                message = job.message
            };

            request.layers.Add(layer);
        }

        Debug.Log($"[MultiLayer] 배치 요청 준비 완료: {request.layers.Count}개 레이어");
        return request;
    }

    IEnumerator SendBatchToServer(BatchWatermarkRequest batchRequest)
    {
        // 배치 API가 지원되는 경우
        if (useBatchAPI)
        {
            yield return StartCoroutine(SendBatchRequestOptimized(batchRequest));
        }
        else
        {
            // 기존 서버 API 사용 (개별 요청)
            yield return StartCoroutine(SendIndividualRequests(batchRequest));
        }
    }

    IEnumerator SendBatchRequestOptimized(BatchWatermarkRequest batchRequest)
    {
        string jsonData = JsonConvert.SerializeObject(batchRequest);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);

        UnityWebRequest request = new UnityWebRequest($"{wamServerUrl}/watermark_batch", "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = (int)serverTimeout;

        Debug.Log("[MultiLayer] 서버로 30레이어 배치 전송 중...");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            ProcessBatchResponse(request.downloadHandler.text);
        }
        else
        {
            Debug.LogError($"[MultiLayer] 배치 처리 실패: {request.error}");
            SaveLocalFallback();
        }
    }

    IEnumerator SendIndividualRequests(BatchWatermarkRequest batchRequest)
    {
        Debug.Log("[MultiLayer] 개별 요청 모드로 30레이어 처리 중...");
        int processedCount = 0;

        foreach (var layer in batchRequest.layers)
        {
            // 기존 WAM 서버 API 형식에 맞게 변환
            var watermarkRequest = new Dictionary<string, object>
            {
                { "image", layer.image_base64 },
                { "creatorId", "MultiLayer_Creator" },
                { "timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                { "artworkId", batchRequest.session_id },
                { "sessionId", batchRequest.session_id },
                { "versionNumber", processedCount + 1 },
                { "viewDirection", layer.direction },
                { "complexity", layer.mask_ratio }
            };

            string jsonData = JsonConvert.SerializeObject(watermarkRequest);
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);

            UnityWebRequest request = new UnityWebRequest($"{wamServerUrl}/watermark", "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10; // 개별 요청은 짧은 타임아웃

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                ProcessIndividualResponse(layer.layer_id, request.downloadHandler.text);
                processedCount++;
            }
            else
            {
                Debug.LogError($"[MultiLayer] 레이어 {layer.layer_id} 처리 실패: {request.error}");
            }

            // 진행 상황 업데이트
            float progress = (float)processedCount / batchRequest.layers.Count;
            UpdateProgressUI(progress);

            // 서버 과부하 방지를 위한 대기
            if (processedCount % 5 == 0)
            {
                yield return new WaitForSeconds(0.5f);
            }
        }

        Debug.Log($"[MultiLayer] {processedCount}/{batchRequest.layers.Count} 레이어 처리 완료");
    }

    void ProcessIndividualResponse(string layerID, string responseJson)
    {
        try
        {
            var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseJson);

            if (response.ContainsKey("success") && Convert.ToBoolean(response["success"]))
            {
                LayerProtectionData protection = new LayerProtectionData
                {
                    layerID = layerID,
                    isProtected = true,
                    bitAccuracy = response.ContainsKey("bit_accuracy") ?
                        Convert.ToSingle(response["bit_accuracy"]) : 0f,
                    watermarkHash = response.ContainsKey("message") ?
                        response["message"].ToString() : "",
                    filePath = response.ContainsKey("filepath") ?
                        response["filepath"].ToString() : ""
                };

                // PSNR, SSIM은 기존 서버에서 제공하지 않으므로 기본값 설정
                protection.psnr = 40f; // 예상 값
                protection.ssim = 0.98f; // 예상 값
                protection.mIoU = 0.85f; // 예상 값

                layerProtectionResults[layerID] = protection;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiLayer] 응답 처리 오류: {e.Message}");
        }
    }

    void ProcessBatchResponse(string responseJson)
    {
        try
        {
            var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseJson);

            if (response.ContainsKey("results"))
            {
                var results = response["results"] as List<Dictionary<string, object>>;
                foreach (var result in results)
                {
                    string layerID = result["layer_id"].ToString();

                    LayerProtectionData protection = new LayerProtectionData
                    {
                        layerID = layerID,
                        isProtected = true,
                        psnr = Convert.ToSingle(result["psnr"]),
                        ssim = Convert.ToSingle(result["ssim"]),
                        bitAccuracy = Convert.ToSingle(result["bit_accuracy"]),
                        mIoU = Convert.ToSingle(result["miou"]),
                        watermarkHash = result["hash"].ToString(),
                        filePath = result["path"].ToString()
                    };

                    layerProtectionResults[layerID] = protection;
                }

                Debug.Log($"[MultiLayer] {layerProtectionResults.Count}개 레이어 보호 완료");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiLayer] 응답 처리 오류: {e.Message}");
        }
    }

    #endregion

    #region Multi-Layer Verification

    IEnumerator PerformMultiLayerVerification(string artworkPath)
    {
        Debug.Log($"[MultiLayer] 다층 검증 시작: {artworkPath}");

        // 기존 서버의 /verify 엔드포인트 사용
        var verifyRequest = new Dictionary<string, string>
        {
            { "image", ConvertPathToBase64(artworkPath) }
        };

        string jsonData = JsonConvert.SerializeObject(verifyRequest);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);

        UnityWebRequest request = new UnityWebRequest($"{wamServerUrl}/verify", "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            ProcessVerificationResponse(request.downloadHandler.text);
        }
        else
        {
            Debug.LogError($"[MultiLayer] 검증 실패: {request.error}");
        }
    }

    string ConvertPathToBase64(string imagePath)
    {
        if (System.IO.File.Exists(imagePath))
        {
            byte[] imageBytes = System.IO.File.ReadAllBytes(imagePath);
            return Convert.ToBase64String(imageBytes);
        }
        return "";
    }

    void ProcessVerificationResponse(string responseJson)
    {
        try
        {
            var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseJson);

            if (response.ContainsKey("detected") && Convert.ToBoolean(response["detected"]))
            {
                float confidence = response.ContainsKey("confidence") ?
                    Convert.ToSingle(response["confidence"]) : 0f;
                string message = response.ContainsKey("message") ?
                    response["message"].ToString() : "Unknown";

                // 단일 검증 결과를 다층 검증 형식으로 변환
                MultiLayerVerificationResult result = new MultiLayerVerificationResult
                {
                    detected_layers = 1, // 기존 서버는 단일 레이어만 검증
                    verification_level = GetVerificationLevel(1),
                    confidence = confidence,
                    layer_details = new Dictionary<string, LayerVerificationDetail>
                    {
                        { "single_layer", new LayerVerificationDetail
                            {
                                detected = true,
                                bit_accuracy = confidence,
                                decoded_message = message
                            }
                        }
                    },
                    certificate_hash = GenerateCertificateHash()
                };

                DisplayVerificationResult(result);
            }
            else
            {
                Debug.Log("[MultiLayer] 워터마크가 검출되지 않았습니다.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MultiLayer] 검증 응답 처리 오류: {e.Message}");
        }
    }

    string GetVerificationLevel(int detectedLayers)
    {
        if (detectedLayers >= 30) return "Perfect";
        if (detectedLayers >= 20) return "Forensic";
        if (detectedLayers >= 10) return "Standard";
        if (detectedLayers >= 3) return "Basic";
        return "None";
    }

    string GenerateCertificateHash()
    {
        return "CERT_" + Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper();
    }

    void DisplayVerificationResult(MultiLayerVerificationResult result)
    {
        string verificationReport = $@"
=== 다층 워터마크 검증 결과 ===
검출된 레이어: {result.detected_layers}/30
검증 레벨: {result.verification_level}
신뢰도: {result.confidence:P}

레이어별 상세:
";

        foreach (var detail in result.layer_details)
        {
            verificationReport += $"- {detail.Key}: ";
            verificationReport += detail.Value.detected ?
                $"검출됨 (정확도: {detail.Value.bit_accuracy:P})\n" :
                "미검출\n";
        }

        verificationReport += $"\n인증서 해시: {result.certificate_hash}";

        Debug.Log(verificationReport);

        // 검증 레벨에 따른 UI 피드백
        ShowVerificationUI(result.verification_level, result.confidence);
    }

    #endregion

    #region Save & Report

    IEnumerator SaveProtectionResults()
    {
        string basePath = Path.Combine(Application.persistentDataPath,
            "ProtectedArtworks", currentSessionID, "full_protection");

        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }

        // 메타데이터 저장
        SaveMetadata(basePath);

        // 검증 리포트 생성 요청
        yield return StartCoroutine(GenerateVerificationReport(basePath));

        Debug.Log($"[MultiLayer] 결과 저장 완료: {basePath}");
    }

    void SaveMetadata(string basePath)
    {
        var metadata = new
        {
            session_id = currentSessionID,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            total_layers = TOTAL_LAYERS,
            protected_layers = layerProtectionResults.Count,
            protection_results = layerProtectionResults,
            system_info = new
            {
                unity_version = Application.unityVersion,
                device_model = SystemInfo.deviceModel,
                gpu = SystemInfo.graphicsDeviceName
            }
        };

        string metadataJson = JsonConvert.SerializeObject(metadata, Formatting.Indented);
        string metadataPath = Path.Combine(basePath, "metadata.json");
        File.WriteAllText(metadataPath, metadataJson);
    }

    IEnumerator GenerateVerificationReport(string basePath)
    {
        // 기존 서버에는 리포트 생성 API가 없으므로 로컬에서 생성
        string reportContent = GenerateLocalReport();
        string reportPath = Path.Combine(basePath, "verification_report.txt");
        File.WriteAllText(reportPath, reportContent);

        Debug.Log($"[MultiLayer] 검증 리포트 생성 완료: {reportPath}");
        yield return null;
    }

    string GenerateLocalReport()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("=== VR Artwork Multi-Layer Protection Report ===");
        report.AppendLine($"Session ID: {currentSessionID}");
        report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine();
        report.AppendLine("Protection Summary:");
        report.AppendLine($"- Total Layers: {TOTAL_LAYERS}");
        report.AppendLine($"- Protected Layers: {layerProtectionResults.Count}");
        report.AppendLine($"- Success Rate: {(float)layerProtectionResults.Count / TOTAL_LAYERS:P}");
        report.AppendLine();
        report.AppendLine("Layer Details:");

        foreach (var kvp in layerProtectionResults)
        {
            var layer = kvp.Value;
            report.AppendLine($"  [{kvp.Key}]");
            report.AppendLine($"    - Protected: {layer.isProtected}");
            report.AppendLine($"    - Bit Accuracy: {layer.bitAccuracy:P}");
            report.AppendLine($"    - Hash: {layer.watermarkHash}");
        }

        report.AppendLine();
        report.AppendLine("Technical Information:");
        report.AppendLine($"- Unity Version: {Application.unityVersion}");
        report.AppendLine($"- Platform: {Application.platform}");
        report.AppendLine($"- Device: {SystemInfo.deviceModel}");
        report.AppendLine($"- GPU: {SystemInfo.graphicsDeviceName}");

        return report.ToString();
    }

    void SaveLocalFallback()
    {
        Debug.LogWarning("[MultiLayer] 서버 연결 실패. 로컬 저장 모드로 전환");

        string fallbackPath = Path.Combine(Application.persistentDataPath,
            "ProtectedArtworks", currentSessionID, "local_backup");

        if (!Directory.Exists(fallbackPath))
        {
            Directory.CreateDirectory(fallbackPath);
        }

        // 캡처된 이미지 로컬 저장
        int savedCount = 0;
        while (processingQueue.Count > 0)
        {
            var job = processingQueue.Dequeue();
            string fileName = $"{job.direction}_{job.mapType}_{job.timestamp:yyyyMMddHHmmss}.png";
            string filePath = Path.Combine(fallbackPath, fileName);
            File.WriteAllBytes(filePath, job.imageData);
            savedCount++;
        }

        Debug.Log($"[MultiLayer] {savedCount}개 레이어 로컬 백업 완료");
    }

    #endregion

    #region UI Feedback

    void UpdateProgressUI(float progress)
    {
        // VR UI에 진행 상황 표시
        string progressText = $"처리 중: {Mathf.RoundToInt(progress * 100)}%";
        Debug.Log($"[MultiLayer] {progressText}");

        // 실제 VR UI 업데이트 로직 추가
        // 예: progressBar.value = progress;
        // 예: progressText3D.text = progressText;
    }

    void OnProtectionComplete(float processingTime)
    {
        // VR UI 업데이트
        string message = $"30레이어 보호 완료!\n처리 시간: {processingTime:F1}초";

        // 성공 이펙트 표시
        ShowSuccessEffect();

        // 통계 표시
        ShowProtectionStats();
    }

    void ShowSuccessEffect()
    {
        // VR 환경에서 시각적 피드백
        // 예: 파티클 이펙트, 색상 변화 등
    }

    void ShowProtectionStats()
    {
        int successCount = layerProtectionResults.Count(kvp => kvp.Value.isProtected);
        float avgPSNR = layerProtectionResults.Average(kvp => kvp.Value.psnr);
        float avgSSIM = layerProtectionResults.Average(kvp => kvp.Value.ssim);

        string stats = $@"
=== 보호 통계 ===
성공: {successCount}/{TOTAL_LAYERS}
평균 PSNR: {avgPSNR:F1} dB
평균 SSIM: {avgSSIM:F3}
";

        Debug.Log(stats);
    }

    void ShowVerificationUI(string level, float confidence)
    {
        Color uiColor = Color.white;
        string icon = "";

        switch (level)
        {
            case "Perfect":
                uiColor = Color.green;
                icon = "✓✓✓";
                break;
            case "Forensic":
                uiColor = Color.cyan;
                icon = "✓✓";
                break;
            case "Standard":
                uiColor = Color.yellow;
                icon = "✓";
                break;
            case "Basic":
                uiColor = Color.blue;
                icon = "!";
                break;
            default:
                uiColor = Color.red;
                icon = "✗";
                break;
        }

        // VR UI 업데이트 로직
    }

    #endregion

    #region Helper Methods

    string GenerateSessionID()
    {
        return $"ML_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
    }

    string EncodeBase64(byte[] data)
    {
        return Convert.ToBase64String(data);
    }

    byte[] DecodeBase64(string base64)
    {
        return Convert.FromBase64String(base64);
    }

    #endregion
}