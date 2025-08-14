using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// VR 워터마크 통합 관리 시스템
/// 실시간 보호(VRWatermark_Realtime)와 후처리 보호(VRWatermark_PostProcess)를 통합 관리
/// </summary>
public class VRWatermark_Controller : MonoBehaviour
{
    #region Configuration

    [Header("워터마크 시스템 참조")]
    [SerializeField] private VRWatermark_Realtime realtimeSystem;
    [SerializeField] private VRWatermark_PostProcess postProcessSystem;

    [Header("통합 설정")]
    [SerializeField] private bool autoSwitchToPostProcess = true;
    [SerializeField] private float sessionTimeoutMinutes = 30f; // 30분 후 자동 세션 종료

    [Header("UI 참조 (선택사항)")]
    [SerializeField] private GameObject processingUIPanel;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Slider progressBar;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private GameObject completionEffectPrefab;

    [Header("VR UI 설정")]
    [SerializeField] private Transform vrUIContainer;
    [SerializeField] private float uiDistance = 2f;
    [SerializeField] private bool followHeadset = true;

    #endregion

    #region Private Variables

    private bool isCreationActive = false;
    private bool isPostProcessing = false;
    private float sessionStartTime;
    private string currentSessionID;
    private int quickProtectionCount = 0;
    private Coroutine sessionTimeoutCoroutine;

    // UI 상태
    private GameObject currentUIInstance;
    private bool isUIVisible = false;

    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        ValidateSystems();
        //InitializeUI();
    }

    void Start()
    {
        StartNewSession();
    }

    void Update()
    {
        HandleInput();
        UpdateUIPosition();

        // 세션 타임아웃 체크
        if (isCreationActive && Time.time - sessionStartTime > sessionTimeoutMinutes * 60f)
        {
            Debug.LogWarning("[Manager] 세션 타임아웃 - 자동 종료");
            EndCreationSession();
        }
    }

    void OnDestroy()
    {
        if (sessionTimeoutCoroutine != null)
        {
            StopCoroutine(sessionTimeoutCoroutine);
        }
    }

    #endregion

    #region Initialization

    void ValidateSystems()
    {
        // 시스템 참조 자동 찾기
        if (realtimeSystem == null)
        {
            realtimeSystem = FindObjectOfType<VRWatermark_Realtime>();
            if (realtimeSystem == null)
            {
                Debug.LogError("[Manager] VRWatermark_Realtime을 찾을 수 없습니다!");
                Debug.Log("[Manager] VRWatermark_Realtime 컴포넌트를 GameObject에 추가하세요.");
            }
            else
            {
                Debug.Log("[Manager] VRWatermark_Realtime 자동 연결 성공");
            }
        }

        if (postProcessSystem == null)
        {
            postProcessSystem = FindObjectOfType<VRWatermark_PostProcess>();
            if (postProcessSystem == null)
            {
                Debug.LogError("[Manager] VRWatermark_PostProcess을 찾을 수 없습니다!");
                Debug.Log("[Manager] VRWatermark_PostProcess 컴포넌트를 GameObject에 추가하세요.");
            }
            else
            {
                Debug.Log("[Manager] VRWatermark_PostProcess 자동 연결 성공");
            }
        }
    }
    /// <summary>
    /// UI 초기화
    /// </summary>

    //void InitializeUI()
    //{
    //    // UI 패널이 없으면 기본 UI 생성
    //    if (processingUIPanel == null)
    //    {
    //        CreateDefaultUI();
    //    }

    //    // 초기에는 UI 숨김
    //    if (processingUIPanel != null)
    //    {
    //        processingUIPanel.SetActive(false);
    //    }
    //}

    //void CreateDefaultUI()
    //{
    //    // VR 환경에서 보일 기본 UI 생성
    //    GameObject uiGO = new GameObject("WatermarkUI");

    //    if (vrUIContainer == null)
    //    {
    //        vrUIContainer = new GameObject("VRUIContainer").transform;
    //        vrUIContainer.SetParent(Camera.main.transform);
    //        vrUIContainer.localPosition = Vector3.forward * uiDistance;
    //    }

    //    uiGO.transform.SetParent(vrUIContainer);

    //    // Canvas 설정
    //    Canvas canvas = uiGO.AddComponent<Canvas>();
    //    canvas.renderMode = RenderMode.WorldSpace;
    //    canvas.worldCamera = Camera.main;

    //    RectTransform canvasRect = canvas.GetComponent<RectTransform>();
    //    canvasRect.sizeDelta = new Vector2(400, 200);
    //    canvasRect.localScale = Vector3.one * 0.002f;

    //    // 배경 패널
    //    GameObject panel = new GameObject("Panel");
    //    panel.transform.SetParent(uiGO.transform);
    //    Image bgImage = panel.AddComponent<Image>();
    //    bgImage.color = new Color(0, 0, 0, 0.8f);

    //    RectTransform panelRect = panel.GetComponent<RectTransform>();
    //    panelRect.anchorMin = Vector2.zero;
    //    panelRect.anchorMax = Vector2.one;
    //    panelRect.offsetMin = Vector2.zero;
    //    panelRect.offsetMax = Vector2.zero;

    //    // 상태 텍스트
    //    GameObject statusGO = new GameObject("StatusText");
    //    statusGO.transform.SetParent(panel.transform);
    //    statusText = statusGO.AddComponent<TextMeshProUGUI>();
    //    statusText.text = "준비";
    //    statusText.fontSize = 24;
    //    statusText.color = Color.white;
    //    statusText.alignment = TextAlignmentOptions.Center;

    //    RectTransform statusRect = statusGO.GetComponent<RectTransform>();
    //    statusRect.anchorMin = new Vector2(0.1f, 0.6f);
    //    statusRect.anchorMax = new Vector2(0.9f, 0.9f);
    //    statusRect.offsetMin = Vector2.zero;
    //    statusRect.offsetMax = Vector2.zero;

    //    // 프로그레스 바
    //    GameObject progressGO = new GameObject("ProgressBar");
    //    progressGO.transform.SetParent(panel.transform);
    //    progressBar = progressGO.AddComponent<Slider>();
    //    progressBar.minValue = 0;
    //    progressBar.maxValue = 1;

    //    RectTransform progressRect = progressGO.GetComponent<RectTransform>();
    //    progressRect.anchorMin = new Vector2(0.1f, 0.4f);
    //    progressRect.anchorMax = new Vector2(0.9f, 0.5f);
    //    progressRect.offsetMin = Vector2.zero;
    //    progressRect.offsetMax = Vector2.zero;

    //    // 프로그레스 텍스트
    //    GameObject progressTextGO = new GameObject("ProgressText");
    //    progressTextGO.transform.SetParent(panel.transform);
    //    progressText = progressTextGO.AddComponent<TextMeshProUGUI>();
    //    progressText.text = "0%";
    //    progressText.fontSize = 18;
    //    progressText.color = Color.cyan;
    //    progressText.alignment = TextAlignmentOptions.Center;

    //    RectTransform progressTextRect = progressTextGO.GetComponent<RectTransform>();
    //    progressTextRect.anchorMin = new Vector2(0.1f, 0.1f);
    //    progressTextRect.anchorMax = new Vector2(0.9f, 0.3f);
    //    progressTextRect.offsetMin = Vector2.zero;
    //    progressTextRect.offsetMax = Vector2.zero;

    //    processingUIPanel = uiGO;
    //    Debug.Log("[Manager] 기본 UI 생성 완료");
    //}

    #endregion

    #region Session Management

    public void StartNewSession()
    {
        currentSessionID = GenerateSessionID();
        sessionStartTime = Time.time;
        isCreationActive = true;
        isPostProcessing = false;
        quickProtectionCount = 0;

        // 실시간 보호 시스템 활성화
        // VRWatermark_Realtime은 Start()에서 자동으로 초기화됨
        if (realtimeSystem != null)
        {
            realtimeSystem.enabled = true;
            // InitializeCreationTracking()은 Start()에서 자동 호출되므로
            // 여기서는 활성화만 하면 됨
        }

        // 멀티레이어 시스템은 대기 상태
        if (postProcessSystem != null)
        {
            postProcessSystem.enabled = true;
        }

        Debug.Log($"[Manager] 새 창작 세션 시작 - ID: {currentSessionID}");
        UpdateUI("창작 세션 시작", 0);

        // 세션 타임아웃 코루틴 시작
        if (sessionTimeoutCoroutine != null)
        {
            StopCoroutine(sessionTimeoutCoroutine);
        }
        sessionTimeoutCoroutine = StartCoroutine(SessionTimeoutRoutine());
    }

    public void EndCreationSession()
    {
        if (!isCreationActive || isPostProcessing)
        {
            Debug.LogWarning("[Manager] 이미 세션이 종료되었거나 처리 중입니다.");
            return;
        }

        Debug.Log($"[Manager] 창작 세션 종료 - 후처리 시작");
        isCreationActive = false;
        isPostProcessing = true;

        // 실시간 보호 중지
        if (realtimeSystem != null)
        {
            realtimeSystem.enabled = false;
        }

        // UI 표시
        ShowProcessingUI("30레이어 후처리 중...");

        // 후처리 시작
        StartCoroutine(PerformPostProcessing());
    }

    IEnumerator SessionTimeoutRoutine()
    {
        yield return new WaitForSeconds(sessionTimeoutMinutes * 60f);

        if (isCreationActive)
        {
            Debug.LogWarning("[Manager] 세션 타임아웃!");
            EndCreationSession();
        }
    }

    #endregion

    #region Input Handling

    void HandleInput()
    {
#if ENABLE_INPUT_SYSTEM
        // New Input System
        if (Keyboard.current == null) return;

        if (!isCreationActive && !isPostProcessing)
        {
            // 새 세션 시작
            if (Keyboard.current.nKey.wasPressedThisFrame)
            {
                StartNewSession();
            }
            return;
        }

        if (isCreationActive)
        {
            // 실시간 보호 트리거
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                TriggerQuickProtection("Manual");
            }

            // 세션 종료 및 후처리
            if (Keyboard.current.escapeKey.wasPressedThisFrame ||
                Keyboard.current.eKey.wasPressedThisFrame)
            {
                EndCreationSession();
            }

            // 도구 변경 시뮬레이션
            if (Keyboard.current.tKey.wasPressedThisFrame)
            {
                TriggerQuickProtection("ToolChange");
            }
        }

        // UI 토글
        if (Keyboard.current.uKey.wasPressedThisFrame)
        {
            ToggleUI();
        }
#else
        // Legacy Input System
        if (!isCreationActive && !isPostProcessing)
        {
            // 새 세션 시작
            if (Input.GetKeyDown(KeyCode.N))
            {
                StartNewSession();
            }
            return;
        }
        
        if (isCreationActive)
        {
            // 실시간 보호 트리거
            if (Input.GetKeyDown(KeyCode.Space))
            {
                TriggerQuickProtection("Manual");
            }
            
            // 세션 종료 및 후처리
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.E))
            {
                EndCreationSession();
            }
            
            // 도구 변경 시뮬레이션
            if (Input.GetKeyDown(KeyCode.T))
            {
                TriggerQuickProtection("ToolChange");
            }
        }
        
        // UI 토글
        if (Input.GetKeyDown(KeyCode.U))
        {
            ToggleUI();
        }
#endif
    }

    void TriggerQuickProtection(string reason)
    {
        if (realtimeSystem != null && isCreationActive)
        {
            quickProtectionCount++;
            Debug.Log($"[Manager] 실시간 보호 #{quickProtectionCount} - 사유: {reason}");

            // VRWatermark_Realtime의 공개 메서드 사용
            // ProtectCurrentArtwork는 public 코루틴이므로 StartCoroutine 필요
            StartCoroutine(realtimeSystem.ProtectCurrentArtwork(reason));

            // UI 피드백
            StartCoroutine(ShowQuickProtectionFeedback());
        }
    }

    #endregion

    #region Post Processing

    IEnumerator PerformPostProcessing()
    {
        float startTime = Time.time;

        // 30레이어 처리 시작
        if (postProcessSystem != null)
        {
            postProcessSystem.StartMultiLayerProtection();

            // 처리 진행 상황 모니터링
            float targetDuration = 45f; // 목표 처리 시간
            float elapsed = 0;

            while (elapsed < targetDuration && isPostProcessing)
            {
                elapsed = Time.time - startTime;
                float progress = Mathf.Clamp01(elapsed / targetDuration);

                UpdateUI($"처리 중... {Mathf.RoundToInt(progress * 30)}/30 레이어", progress);

                yield return new WaitForSeconds(0.5f);
            }
        }

        // 처리 완료
        float totalTime = Time.time - startTime;
        OnPostProcessingComplete(totalTime);
    }

    void OnPostProcessingComplete(float processingTime)
    {
        isPostProcessing = false;

        Debug.Log($"[Manager] 후처리 완료 - 시간: {processingTime:F1}초");

        // 완료 UI
        UpdateUI($"✅ 보호 완료!\n실시간: {quickProtectionCount}개\n후처리: 30개 레이어", 1f);

        // 완료 이펙트
        if (completionEffectPrefab != null)
        {
            Instantiate(completionEffectPrefab, vrUIContainer.position, Quaternion.identity);
        }

        // 3초 후 UI 숨기기
        StartCoroutine(HideUIAfterDelay(3f));

        // 통계 로그
        PrintSessionStatistics();
    }

    #endregion

    #region UI Management

    public void ShowProcessingUI(string message)
    {
        if (processingUIPanel != null)
        {
            processingUIPanel.SetActive(true);
            isUIVisible = true;
            UpdateUI(message, 0);
        }
    }

    public void HideProcessingUI()
    {
        if (processingUIPanel != null)
        {
            processingUIPanel.SetActive(false);
            isUIVisible = false;
        }
    }

    void UpdateUI(string status, float progress)
    {
        if (statusText != null)
        {
            statusText.text = status;
        }

        if (progressBar != null)
        {
            progressBar.value = progress;
        }

        if (progressText != null)
        {
            progressText.text = $"{Mathf.RoundToInt(progress * 100)}%";
        }
    }

    void UpdateUIPosition()
    {
        if (!followHeadset || vrUIContainer == null || !isUIVisible)
            return;

        // VR 헤드셋 방향으로 UI 회전
        if (Camera.main != null)
        {
            Vector3 lookDirection = Camera.main.transform.forward;
            lookDirection.y = 0;
            if (lookDirection != Vector3.zero)
            {
                vrUIContainer.rotation = Quaternion.LookRotation(lookDirection);
            }
        }
    }

    void ToggleUI()
    {
        if (isUIVisible)
        {
            HideProcessingUI();
        }
        else
        {
            ShowProcessingUI("상태 확인");
        }
    }

    IEnumerator ShowQuickProtectionFeedback()
    {
        string originalText = statusText != null ? statusText.text : "";

        UpdateUI($"✓ 실시간 보호 #{quickProtectionCount}", 1f);

        yield return new WaitForSeconds(1.5f);

        if (!isPostProcessing)
        {
            UpdateUI(originalText, 0);
        }
    }

    IEnumerator HideUIAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideProcessingUI();
    }

    #endregion

    #region Statistics

    void PrintSessionStatistics()
    {
        float sessionDuration = Time.time - sessionStartTime;

        string stats = $@"
=== 세션 통계 ===
세션 ID: {currentSessionID}
총 시간: {sessionDuration / 60f:F1}분
실시간 보호: {quickProtectionCount}회
후처리 레이어: 30개
보호 수준: 포렌식 등급

폴더 구조:
- quick_protection/ : {quickProtectionCount}개 파일
- full_protection/ : 30개 레이어
";

        Debug.Log(stats);
    }

    public SessionStatistics GetSessionStatistics()
    {
        return new SessionStatistics
        {
            sessionID = currentSessionID,
            duration = Time.time - sessionStartTime,
            quickProtectionCount = quickProtectionCount,
            multiLayerCount = 30,
            isActive = isCreationActive,
            isProcessing = isPostProcessing
        };
    }

    #endregion

    #region Helper Methods

    string GenerateSessionID()
    {
        return $"VR_{DateTime.Now:yyyyMMddHHmmss}_{UnityEngine.Random.Range(1000, 9999)}";
    }

    #endregion

    #region Public API

    /// <summary>
    /// 외부에서 실시간 보호 트리거
    /// </summary>
    public void RequestQuickProtection(string reason = "External")
    {
        TriggerQuickProtection(reason);
    }

    /// <summary>
    /// 외부에서 세션 종료 요청
    /// </summary>
    public void RequestSessionEnd()
    {
        EndCreationSession();
    }

    /// <summary>
    /// 시스템 상태 확인
    /// </summary>
    public bool IsReady()
    {
        return realtimeSystem != null && postProcessSystem != null;
    }

    public bool IsCreating()
    {
        return isCreationActive;
    }

    public bool IsProcessing()
    {
        return isPostProcessing;
    }

    #endregion

    #region Data Classes

    [Serializable]
    public class SessionStatistics
    {
        public string sessionID;
        public float duration;
        public int quickProtectionCount;
        public int multiLayerCount;
        public bool isActive;
        public bool isProcessing;
    }

    #endregion
}