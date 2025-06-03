using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class VRCreationProtectionUI : MonoBehaviour
{
    [Header("UI Canvas")]
    [SerializeField] private Canvas artProtectionCanvas;

    [Header("Display Panels")]
    [SerializeField] private RawImage primaryArtworkDisplay;
    [SerializeField] private Text creationInfoText;
    [SerializeField] private Transform artViewsPanel;
    [SerializeField] private Transform creationHistoryPanel;

    [Header("Auto UI Creation")]
    [SerializeField] private bool createUIAutomatically = true;
    [SerializeField] private bool showAllArtViews = true;

    private VRCreationProtectionSystem protectionSystem;
    private List<GameObject> artViewItems = new List<GameObject>();
    private List<VRCreationProtectionSystem.ArtworkProtectionResult> protectionHistory = new List<VRCreationProtectionSystem.ArtworkProtectionResult>();

    // UI 스타일 설정
    private Color artBackgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
    private Color artPanelColor = new Color(0.12f, 0.12f, 0.18f, 0.9f);
    private Color artAccentColor = new Color(0.3f, 0.7f, 1f, 1f);
    private Color artHighlightColor = new Color(1f, 0.8f, 0.2f, 1f);

    void Start()
    {
        // VR 아트 보호 시스템 찾기
        protectionSystem = FindObjectOfType<VRCreationProtectionSystem>();

        if (protectionSystem != null)
        {
            // 이벤트 연결
            protectionSystem.OnArtworkProtected += DisplayArtworkProtectionResult;
            protectionSystem.OnCreationMilestoneReached += OnCreationMilestone;
            protectionSystem.OnToolChanged += OnToolChanged;
        }

        if (createUIAutomatically)
        {
            CreateArtProtectionUI();
        }

        // 초기 UI 설정
        if (creationInfoText != null)
        {
            creationInfoText.text = "VR 아트 창작 보호 시스템 대기 중...\n\nSpace: 수동 보호\nT: 도구 변경\nB: 브러시 스트로크";
        }
    }

    void CreateArtProtectionUI()
    {
        // 메인 Canvas 생성
        if (artProtectionCanvas == null)
        {
            GameObject canvasGO = new GameObject("VRCreationProtectionUI_Canvas");
            artProtectionCanvas = canvasGO.AddComponent<Canvas>();
            artProtectionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            artProtectionCanvas.sortingOrder = 100;

            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        CreatePrimaryArtworkDisplay();
        CreateCreationInfoPanel();
        CreateArtViewsPanel();
        CreateCreationHistoryPanel();
    }

    void CreatePrimaryArtworkDisplay()
    {
        // 주요 아트워크 디스플레이 (왼쪽 대형 패널)
        GameObject displayGO = new GameObject("PrimaryArtworkDisplay");
        displayGO.transform.SetParent(artProtectionCanvas.transform, false);

        RectTransform displayRT = displayGO.AddComponent<RectTransform>();
        displayRT.anchorMin = new Vector2(0.02f, 0.3f);
        displayRT.anchorMax = new Vector2(0.48f, 0.98f);
        displayRT.offsetMin = Vector2.zero;
        displayRT.offsetMax = Vector2.zero;

        // 아트 전시용 배경
        Image bg = displayGO.AddComponent<Image>();
        bg.color = artBackgroundColor;

        // 테두리 효과
        Outline outline = displayGO.AddComponent<Outline>();
        outline.effectColor = artAccentColor;
        outline.effectDistance = new Vector2(2, 2);

        // 아트워크 이미지
        GameObject imageGO = new GameObject("ArtworkImage");
        imageGO.transform.SetParent(displayGO.transform, false);

        RectTransform imageRT = imageGO.AddComponent<RectTransform>();
        imageRT.anchorMin = new Vector2(0.05f, 0.1f);
        imageRT.anchorMax = new Vector2(0.95f, 0.9f);
        imageRT.offsetMin = Vector2.zero;
        imageRT.offsetMax = Vector2.zero;

        primaryArtworkDisplay = imageGO.AddComponent<RawImage>();
        primaryArtworkDisplay.color = Color.white;

        // 제목
        CreateArtLabel(displayGO.transform, "보호된 VR 아트워크", new Vector2(0.5f, 0.95f), 18, artHighlightColor, FontStyle.Bold);

        // 상태 표시
        CreateArtLabel(displayGO.transform, "최적 감상 각도", new Vector2(0.5f, 0.05f), 12, artAccentColor, FontStyle.Normal);
    }

    void CreateCreationInfoPanel()
    {
        // 창작 정보 패널 (오른쪽 상단)
        GameObject infoPanelGO = new GameObject("CreationInfoPanel");
        infoPanelGO.transform.SetParent(artProtectionCanvas.transform, false);

        RectTransform infoPanelRT = infoPanelGO.AddComponent<RectTransform>();
        infoPanelRT.anchorMin = new Vector2(0.52f, 0.6f);
        infoPanelRT.anchorMax = new Vector2(0.98f, 0.98f);
        infoPanelRT.offsetMin = Vector2.zero;
        infoPanelRT.offsetMax = Vector2.zero;

        // 배경
        Image infoBg = infoPanelGO.AddComponent<Image>();
        infoBg.color = artPanelColor;

        // 테두리
        Outline infoOutline = infoPanelGO.AddComponent<Outline>();
        infoOutline.effectColor = artAccentColor;
        infoOutline.effectDistance = new Vector2(1, 1);

        // 정보 텍스트
        GameObject textGO = new GameObject("CreationInfoText");
        textGO.transform.SetParent(infoPanelGO.transform, false);

        RectTransform textRT = textGO.AddComponent<RectTransform>();
        textRT.anchorMin = new Vector2(0.05f, 0.05f);
        textRT.anchorMax = new Vector2(0.95f, 0.95f);
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        creationInfoText = textGO.AddComponent<Text>();
        creationInfoText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        creationInfoText.fontSize = 11;
        creationInfoText.color = Color.white;
        creationInfoText.alignment = TextAnchor.UpperLeft;
        creationInfoText.verticalOverflow = VerticalWrapMode.Overflow;

        // 제목 추가
        CreateArtLabel(infoPanelGO.transform, "창작 세션 정보", new Vector2(0.5f, 0.95f), 14, artHighlightColor, FontStyle.Bold);
    }

    void CreateArtViewsPanel()
    {
        // 6방향 아트뷰 패널 (오른쪽 중간)
        GameObject artViewsPanelGO = new GameObject("ArtViewsPanel");
        artViewsPanelGO.transform.SetParent(artProtectionCanvas.transform, false);

        RectTransform artViewsPanelRT = artViewsPanelGO.AddComponent<RectTransform>();
        artViewsPanelRT.anchorMin = new Vector2(0.52f, 0.3f);
        artViewsPanelRT.anchorMax = new Vector2(0.98f, 0.58f);
        artViewsPanelRT.offsetMin = Vector2.zero;
        artViewsPanelRT.offsetMax = Vector2.zero;

        // 배경
        Image artViewsBg = artViewsPanelGO.AddComponent<Image>();
        artViewsBg.color = artPanelColor;

        // 테두리
        Outline artViewsOutline = artViewsPanelGO.AddComponent<Outline>();
        artViewsOutline.effectColor = artAccentColor;
        artViewsOutline.effectDistance = new Vector2(1, 1);

        artViewsPanel = artViewsPanelGO.transform;

        // 제목
        CreateArtLabel(artViewsPanel, "6방향 아트뷰 품질", new Vector2(0.5f, 0.92f), 14, artHighlightColor, FontStyle.Bold);

        // 스크롤 영역 생성
        CreateArtViewsScrollArea();
    }

    void CreateArtViewsScrollArea()
    {
        // 그리드 레이아웃 컨테이너
        GameObject scrollAreaGO = new GameObject("ArtViewsScrollArea");
        scrollAreaGO.transform.SetParent(artViewsPanel, false);

        RectTransform scrollAreaRT = scrollAreaGO.AddComponent<RectTransform>();
        scrollAreaRT.anchorMin = new Vector2(0.05f, 0.05f);
        scrollAreaRT.anchorMax = new Vector2(0.95f, 0.85f);
        scrollAreaRT.offsetMin = Vector2.zero;
        scrollAreaRT.offsetMax = Vector2.zero;

        // Grid Layout Group (2x3 그리드)
        GridLayoutGroup glg = scrollAreaGO.AddComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(120, 60);
        glg.spacing = new Vector2(5, 5);
        glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis = GridLayoutGroup.Axis.Horizontal;
        glg.childAlignment = TextAnchor.UpperCenter;
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 2;

        // 아트뷰 아이템들이 들어갈 부모로 설정
        artViewsPanel = scrollAreaGO.transform;
    }

    void CreateCreationHistoryPanel()
    {
        // 창작 히스토리 패널 (하단)
        GameObject historyPanelGO = new GameObject("CreationHistoryPanel");
        historyPanelGO.transform.SetParent(artProtectionCanvas.transform, false);

        RectTransform historyPanelRT = historyPanelGO.AddComponent<RectTransform>();
        historyPanelRT.anchorMin = new Vector2(0.02f, 0.02f);
        historyPanelRT.anchorMax = new Vector2(0.98f, 0.28f);
        historyPanelRT.offsetMin = Vector2.zero;
        historyPanelRT.offsetMax = Vector2.zero;

        // 배경
        Image historyBg = historyPanelGO.AddComponent<Image>();
        historyBg.color = artPanelColor;

        // 테두리
        Outline historyOutline = historyPanelGO.AddComponent<Outline>();
        historyOutline.effectColor = artAccentColor;
        historyOutline.effectDistance = new Vector2(1, 1);

        creationHistoryPanel = historyPanelGO.transform;

        // 제목
        CreateArtLabel(creationHistoryPanel, "창작 보호 히스토리", new Vector2(0.5f, 0.92f), 14, artHighlightColor, FontStyle.Bold);

        // 히스토리 스크롤 영역
        CreateHistoryScrollArea();
    }

    void CreateHistoryScrollArea()
    {
        GameObject scrollAreaGO = new GameObject("HistoryScrollArea");
        scrollAreaGO.transform.SetParent(creationHistoryPanel, false);

        RectTransform scrollAreaRT = scrollAreaGO.AddComponent<RectTransform>();
        scrollAreaRT.anchorMin = new Vector2(0.02f, 0.1f);
        scrollAreaRT.anchorMax = new Vector2(0.98f, 0.85f);
        scrollAreaRT.offsetMin = Vector2.zero;
        scrollAreaRT.offsetMax = Vector2.zero;

        // Horizontal Layout Group
        HorizontalLayoutGroup hlg = scrollAreaGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.padding = new RectOffset(10, 10, 5, 5);
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        creationHistoryPanel = scrollAreaGO.transform;
    }

    GameObject CreateArtLabel(Transform parent, string text, Vector2 anchorPosition, int fontSize, Color color, FontStyle fontStyle)
    {
        GameObject labelGO = new GameObject("ArtLabel_" + text.Replace(" ", "_"));
        labelGO.transform.SetParent(parent, false);

        RectTransform labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = anchorPosition;
        labelRT.anchorMax = anchorPosition;
        labelRT.sizeDelta = new Vector2(200, 25);
        labelRT.anchoredPosition = Vector2.zero;

        Text label = labelGO.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.fontStyle = fontStyle;
        label.alignment = TextAnchor.MiddleCenter;

        // 텍스트에 그림자 효과
        Shadow shadow = labelGO.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.5f);
        shadow.effectDistance = new Vector2(1, -1);

        return labelGO;
    }

    void DisplayArtworkProtectionResult(VRCreationProtectionSystem.ArtworkProtectionResult result)
    {
        // 히스토리에 추가
        protectionHistory.Add(result);

        // 주요 아트워크 이미지 표시
        if (primaryArtworkDisplay != null && result.primaryProtectionImage != null)
        {
            primaryArtworkDisplay.texture = result.primaryProtectionImage;
        }

        // 창작 정보 텍스트 업데이트
        if (creationInfoText != null)
        {
            UpdateCreationInfoDisplay(result);
        }

        // 6방향 아트뷰 업데이트
        if (showAllArtViews)
        {
            UpdateArtViewsDisplay(result.viewQualityScores, result.primaryDirection);
        }

        // 히스토리 패널 업데이트
        UpdateCreationHistoryDisplay();

        Debug.Log($"VR 아트워크 보호 결과 UI 업데이트 완료 - 방향: {result.primaryDirection}");
    }

    void UpdateCreationInfoDisplay(VRCreationProtectionSystem.ArtworkProtectionResult result)
    {
        string info = "=== VR 아트 창작 보호 결과 ===\n\n";

        // 아티스트 정보
        info += $"🎨 아티스트: {result.metadata.artistName}\n";
        info += $"📝 프로젝트: {result.metadata.projectName}\n";
        info += $"🆔 세션 ID: {result.metadata.sessionID}\n\n";

        // 창작 현황
        info += "=== 창작 현황 ===\n";
        info += $"⏱️ 창작 시간: {result.metadata.creationDuration:F0}초\n";
        info += $"🖌️ 브러시 스트로크: {result.metadata.totalBrushStrokes}회\n";
        info += $"🛠️ 사용 도구: {result.metadata.toolsUsed.Count}개\n";
        info += $"📊 복잡도: {result.artworkComplexity:F3}\n";
        info += $"📄 버전: v{result.metadata.versionNumber}\n\n";

        // 보호 결과
        info += "=== 보호 결과 ===\n";
        info += $"📸 최적 각도: {result.primaryDirection}\n";
        info += $"⚡ 처리 시간: {result.protectionProcessingTime:F3}초\n";
        info += $"🛡️ 워터마킹 준비: {(result.readyForWatermarking ? "✅ 완료" : "⏳ 대기")}\n";
        info += $"🎯 해상도: {result.primaryProtectionImage.width}x{result.primaryProtectionImage.height}\n\n";

        // 조작법
        info += "=== 조작법 ===\n";
        info += "Space: 수동 보호\n";
        info += "T: 도구 변경\n";
        info += "B: 브러시 스트로크\n\n";

        // 보호 통계
        info += "=== 보호 통계 ===\n";
        info += $"📈 총 보호 횟수: {protectionHistory.Count}회\n";
        if (protectionHistory.Count > 1)
        {
            var avgComplexity = protectionHistory.Average(h => h.artworkComplexity);
            info += $"📊 평균 복잡도: {avgComplexity:F3}\n";
        }

        creationInfoText.text = info;
    }

    void UpdateArtViewsDisplay(Dictionary<VRCreationProtectionSystem.ArtViewDirection, float> viewScores, VRCreationProtectionSystem.ArtViewDirection primaryDirection)
    {
        // 기존 아이템들 제거
        foreach (GameObject item in artViewItems)
        {
            if (item != null) DestroyImmediate(item);
        }
        artViewItems.Clear();

        if (viewScores == null) return;

        // 품질 점수 순으로 정렬
        var sortedViews = viewScores.OrderByDescending(x => x.Value).ToList();

        foreach (var kvp in sortedViews)
        {
            CreateArtViewItem(kvp.Key, kvp.Value, kvp.Key == primaryDirection);
        }
    }

    void CreateArtViewItem(VRCreationProtectionSystem.ArtViewDirection direction, float qualityScore, bool isPrimary)
    {
        GameObject itemGO = new GameObject($"ArtViewItem_{direction}");
        itemGO.transform.SetParent(artViewsPanel, false);

        // 배경
        Image bg = itemGO.AddComponent<Image>();
        bg.color = isPrimary ? new Color(0.3f, 0.7f, 0.3f, 0.7f) : new Color(0.2f, 0.2f, 0.3f, 0.5f);

        // 테두리 (주요 뷰에만)
        if (isPrimary)
        {
            Outline outline = itemGO.AddComponent<Outline>();
            outline.effectColor = artHighlightColor;
            outline.effectDistance = new Vector2(2, 2);
        }

        // 방향 라벨
        GameObject directionLabelGO = new GameObject("DirectionLabel");
        directionLabelGO.transform.SetParent(itemGO.transform, false);

        RectTransform directionLabelRT = directionLabelGO.AddComponent<RectTransform>();
        directionLabelRT.anchorMin = new Vector2(0f, 0.6f);
        directionLabelRT.anchorMax = new Vector2(1f, 1f);
        directionLabelRT.offsetMin = Vector2.zero;
        directionLabelRT.offsetMax = Vector2.zero;

        Text directionLabel = directionLabelGO.AddComponent<Text>();
        directionLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        directionLabel.text = GetArtViewDisplayName(direction);
        directionLabel.fontSize = 10;
        directionLabel.color = isPrimary ? artHighlightColor : artAccentColor;
        directionLabel.alignment = TextAnchor.MiddleCenter;
        directionLabel.fontStyle = isPrimary ? FontStyle.Bold : FontStyle.Normal;

        // 품질 점수 바
        GameObject scoreBarBgGO = new GameObject("ScoreBarBg");
        scoreBarBgGO.transform.SetParent(itemGO.transform, false);

        RectTransform scoreBarBgRT = scoreBarBgGO.AddComponent<RectTransform>();
        scoreBarBgRT.anchorMin = new Vector2(0.1f, 0.3f);
        scoreBarBgRT.anchorMax = new Vector2(0.9f, 0.5f);
        scoreBarBgRT.offsetMin = Vector2.zero;
        scoreBarBgRT.offsetMax = Vector2.zero;

        Image scoreBarBg = scoreBarBgGO.AddComponent<Image>();
        scoreBarBg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

        // 품질 점수 채우기
        GameObject scoreBarGO = new GameObject("ScoreBar");
        scoreBarGO.transform.SetParent(scoreBarBgGO.transform, false);

        RectTransform scoreBarRT = scoreBarGO.AddComponent<RectTransform>();
        scoreBarRT.anchorMin = Vector2.zero;
        scoreBarRT.anchorMax = new Vector2(qualityScore, 1f);
        scoreBarRT.offsetMin = Vector2.zero;
        scoreBarRT.offsetMax = Vector2.zero;

        Image scoreBar = scoreBarGO.AddComponent<Image>();
        scoreBar.color = isPrimary ? artHighlightColor : artAccentColor;

        // 점수 값 텍스트
        GameObject scoreValueGO = new GameObject("ScoreValue");
        scoreValueGO.transform.SetParent(itemGO.transform, false);

        RectTransform scoreValueRT = scoreValueGO.AddComponent<RectTransform>();
        scoreValueRT.anchorMin = new Vector2(0f, 0f);
        scoreValueRT.anchorMax = new Vector2(1f, 0.3f);
        scoreValueRT.offsetMin = Vector2.zero;
        scoreValueRT.offsetMax = Vector2.zero;

        Text scoreValue = scoreValueGO.AddComponent<Text>();
        scoreValue.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        scoreValue.text = qualityScore.ToString("F2");
        scoreValue.fontSize = 9;
        scoreValue.color = isPrimary ? artHighlightColor : Color.white;
        scoreValue.alignment = TextAnchor.MiddleCenter;
        scoreValue.fontStyle = isPrimary ? FontStyle.Bold : FontStyle.Normal;

        artViewItems.Add(itemGO);
    }

    string GetArtViewDisplayName(VRCreationProtectionSystem.ArtViewDirection direction)
    {
        switch (direction)
        {
            case VRCreationProtectionSystem.ArtViewDirection.MainView: return "주요뷰";
            case VRCreationProtectionSystem.ArtViewDirection.DetailView: return "디테일";
            case VRCreationProtectionSystem.ArtViewDirection.ProfileLeft: return "좌측";
            case VRCreationProtectionSystem.ArtViewDirection.ProfileRight: return "우측";
            case VRCreationProtectionSystem.ArtViewDirection.TopView: return "상단";
            case VRCreationProtectionSystem.ArtViewDirection.BottomView: return "하단";
            default: return direction.ToString();
        }
    }

    void UpdateCreationHistoryDisplay()
    {
        // 기존 히스토리 아이템들 제거 (최근 5개만 유지)
        Transform[] children = new Transform[creationHistoryPanel.childCount];
        for (int i = 0; i < children.Length; i++)
        {
            children[i] = creationHistoryPanel.GetChild(i);
        }

        // 너무 많으면 오래된 것 제거
        if (children.Length > 5)
        {
            for (int i = 0; i < children.Length - 5; i++)
            {
                if (children[i] != null) DestroyImmediate(children[i].gameObject);
            }
        }

        // 새 히스토리 아이템 추가
        if (protectionHistory.Count > 0)
        {
            var latestResult = protectionHistory[protectionHistory.Count - 1];
            CreateHistoryItem(latestResult, protectionHistory.Count);
        }
    }

    void CreateHistoryItem(VRCreationProtectionSystem.ArtworkProtectionResult result, int index)
    {
        GameObject itemGO = new GameObject($"HistoryItem_{index}");
        itemGO.transform.SetParent(creationHistoryPanel, false);

        RectTransform itemRT = itemGO.AddComponent<RectTransform>();
        itemRT.sizeDelta = new Vector2(150, 0);

        // 배경
        Image bg = itemGO.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.2f, 0.8f);

        // 테두리
        Outline outline = itemGO.AddComponent<Outline>();
        outline.effectColor = artAccentColor;
        outline.effectDistance = new Vector2(1, 1);

        // 썸네일 (작은 이미지)
        GameObject thumbnailGO = new GameObject("Thumbnail");
        thumbnailGO.transform.SetParent(itemGO.transform, false);

        RectTransform thumbnailRT = thumbnailGO.AddComponent<RectTransform>();
        thumbnailRT.anchorMin = new Vector2(0.1f, 0.4f);
        thumbnailRT.anchorMax = new Vector2(0.9f, 0.9f);
        thumbnailRT.offsetMin = Vector2.zero;
        thumbnailRT.offsetMax = Vector2.zero;

        RawImage thumbnail = thumbnailGO.AddComponent<RawImage>();
        thumbnail.texture = result.primaryProtectionImage;
        thumbnail.color = Color.white;

        // 정보 텍스트
        GameObject infoGO = new GameObject("Info");
        infoGO.transform.SetParent(itemGO.transform, false);

        RectTransform infoRT = infoGO.AddComponent<RectTransform>();
        infoRT.anchorMin = new Vector2(0.05f, 0.05f);
        infoRT.anchorMax = new Vector2(0.95f, 0.35f);
        infoRT.offsetMin = Vector2.zero;
        infoRT.offsetMax = Vector2.zero;

        Text info = infoGO.AddComponent<Text>();
        info.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        info.text = $"v{result.metadata.versionNumber}\n{result.primaryDirection}\n복잡도: {result.artworkComplexity:F2}";
        info.fontSize = 8;
        info.color = Color.white;
        info.alignment = TextAnchor.UpperCenter;
    }

    void OnCreationMilestone(VRCreationProtectionSystem.CreationMetadata metadata)
    {
        Debug.Log($"🎯 창작 마일스톤 달성! 복잡도: {metadata.artworkComplexity:F3}, 스트로크: {metadata.totalBrushStrokes}");

        // 마일스톤 알림 효과 (간단한 색상 플래시)
        if (primaryArtworkDisplay != null)
        {
            StartCoroutine(FlashMilestoneEffect());
        }
    }

    System.Collections.IEnumerator FlashMilestoneEffect()
    {
        Color originalColor = primaryArtworkDisplay.color;

        for (int i = 0; i < 3; i++)
        {
            primaryArtworkDisplay.color = artHighlightColor;
            yield return new WaitForSeconds(0.1f);
            primaryArtworkDisplay.color = originalColor;
            yield return new WaitForSeconds(0.1f);
        }
    }

    void OnToolChanged(string newTool)
    {
        Debug.Log($"🛠️ 도구 변경: {newTool}");

        // 도구 변경 알림 (UI 업데이트는 다음 보호 시점에서)
    }

    void OnDestroy()
    {
        if (protectionSystem != null)
        {
            protectionSystem.OnArtworkProtected -= DisplayArtworkProtectionResult;
            protectionSystem.OnCreationMilestoneReached -= OnCreationMilestone;
            protectionSystem.OnToolChanged -= OnToolChanged;
        }
    }
}