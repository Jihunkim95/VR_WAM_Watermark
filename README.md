# VR WAM Watermark Unity Project

Unity VR 환경에서 실시간 워터마킹 기술을 구현하는 프로젝트입니다. WAM(Watermark Anything) 모델과 연동하여 VR 아트워크에 24레이어 다중 워터마킹을 적용합니다

## 🎯 프로젝트 개요

### 핵심 기능
- **실시간 VR 창작 추적** - 핸드 트래킹 기반 아트워크 생성 모니터링
- **24레이어 워터마킹** - 6방향 × (3렌더맵 + 1일반이미지) = 24개 보호 레이어
- **URP 렌더맵 캡처** - Depth, Normal, SSAO 구조적 보호
- **지능형 카메라 시스템** - 아트워크 중심 기반 자동 포지셔닝
- **WAM 서버 연동** - Flask 기반 워터마크 임베딩/검출 API

### 보호 레벨
| 레벨 | 검출 레이어 | 신뢰도 | 용도 |
|------|-------------|--------|------|
| 기본 | 2/24 | 60% | 일반적 보호 |
| 표준 | 6/24 | 80% | 상업적 보호 |
| 포렌식 | 12/24 | 95% | 법적 증거 |
| 완벽 | 24/24 | 100% | 최고 보안 |

## 🛠️ 기술 스택

### Unity 요구사항
- **Unity 버전**: 2022.3.55f1 (LTS)
- **렌더 파이프라인**: Universal Render Pipeline (URP) 14.0.11
- **플랫폼**: Android/XR (Meta Quest 3 최적화)

### 필수 패키지
```json
{
  "com.unity.render-pipelines.universal": "14.0.11",  // URP 렌더링
  "com.unity.xr.interaction.toolkit": "3.1.1",        // VR 상호작용
  "com.unity.xr.openxr": "1.14.1",                   // OpenXR 지원
  "com.unity.xr.management": "4.5.0",                // XR 관리
  "com.unity.inputsystem": "1.13.1",                 // 새로운 입력 시스템
  "com.unity.nuget.newtonsoft-json": "3.2.1",        // JSON 직렬화
  "com.unity.feature.vr": "1.0.0"                    // VR Feature Set
}
```

### VR 하드웨어 지원
- **Meta Quest 3** (권장)
- **Meta Quest 2**
- **OpenXR 호환 헤드셋**
- **시뮬레이션 모드** (VR 헤드셋 없이 개발/테스트)

## 🚀 설치 및 설정

### 1. Unity 설치
```bash
# Unity Hub에서 설치
Unity 2022.3.55f1 (LTS)
- Android Build Support
- OpenXR Plugin
```

### 2. 프로젝트 설정

**Unity Hub에서 프로젝트 열기:**
1. Unity Hub 실행
2. "Open" → `VR_WAM_Watermark` 폴더 선택
3. Unity 2022.3.55f1으로 열기

**URP 설정 확인:**
1. **Project Settings** → **Graphics**
   - Scriptable Render Pipeline Settings에 URP Asset 설정 확인
2. **Project Settings** → **XR Plug-in Management**
   - OpenXR 또는 Oculus 플러그인 활성화
3. **URP Asset 설정**:
   - Depth Texture: ✅ 활성화
   - Opaque Texture: ✅ 활성화
   - **Renderer Features 추가**:
     - DepthNormals Prepass (Normal 맵용)
     - Screen Space Ambient Occlusion (SSAO 맵용)

### 3. 빌드 설정

**Meta Quest 3 빌드:**
```
File → Build Settings
- Platform: Android
- Texture Compression: ASTC
- Minimum API Level: Android 7.0 (API 24)
- Target API Level: Automatic (highest installed)
- XR Settings: OpenXR
```

## 🎮 실행 방법

### 개발 모드 (PC)

**VR 시뮬레이션 모드:**
1. **DevScene** 열기 (`Assets/Scenes/DevScene.unity`) ⭐ **현재 사용 중**
2. **VRWatermark_Realtime** 컴포넌트에서:
   ```csharp
   useVRSimulation = true;  // VR 하드웨어 없이 테스트
   ```
3. **Play 버튼** 클릭

**주요 조작키:**
- `Space` - 수동 아트워크 보호 실행
- `T` - 도구 변경 시뮬레이션
- `B` - 브러시 스트로크 추가

### VR 모드 (Meta Quest)

**사전 준비:**
1. Meta Quest 3를 Developer Mode로 설정
2. USB 디버깅 활성화
3. WAM 서버 실행 (`localhost:5000`)

**실행 순서:**
1. **Build and Run** 또는 APK 설치
2. Quest에서 앱 실행
3. 핸드 트래킹으로 VR 아트워크 생성
4. 자동 보호 트리거 또는 수동 보호 실행

## 📁 프로젝트 구조

```
Assets/
├── Scenes/
│   ├── BasicScene.unity        # 기본 VR 씬
│   ├── DevScene.unity          # 개발/테스트 씬 ⭐ (현재 사용 중)
│   └── SampleScene.unity       # 샘플 씬
├── Scripts/
│   ├── VRWatermark_Realtime.cs     # 실시간 VR 창작 추적 및 보호
│   ├── VRWatermark_PostProcess.cs  # 24레이어 후처리 시스템
│   ├── System/
│   │   └── VRWatermark_Controller.cs  # 통합 제어 시스템
│   └── UI/
│       └── VRWatermark_UI.cs        # 사용자 인터페이스
├── Settings/
│   └── Project Configuration/       # URP 및 XR 설정
├── VRTemplateAssets/               # VR 템플릿 리소스
└── ProtectedArtworks/              # 보호된 아트워크 출력 폴더
```

### 핵심 스크립트

**`VRWatermark_Realtime.cs`**
- VR 창작 활동 실시간 추적
- 6방향 카메라 시스템 관리
- 자동 보호 트리거 (도구변경, 마일스톤, 주기적)
- WAM 서버 통신 및 재시도 로직

**`VRWatermark_PostProcess.cs`**
- URP 최적화 24레이어 캡처 시스템
- 지능형 카메라 포지셔닝 (아트워크 중심 기반)
- 렌더맵(Depth/Normal/SSAO) + 일반 이미지 통합
- 배치 처리 및 성능 최적화

## ⚙️ 설정 및 커스터마이징

### VRWatermark_Realtime 설정

```csharp
[Header("Artist Information")]
public string artistID = "Artist_001";
public string artistName = "Unknown Creator";
public string projectName = "VR_Artwork";

[Header("VR Art Protection Settings")]
public int artworkResolution = 1024;           // 고해상도 캡처
public float creationComplexityThreshold = 0.1f;  // 복잡도 임계값
public bool enableVersionControl = true;       // 버전 관리

[Header("Auto Protection Triggers")]
public bool protectOnBrushChange = true;       // 도구 변경 시 보호
public bool protectOnCreationMilestone = true; // 마일스톤 달성 시 보호
public float autoProtectionInterval = 180f;    // 자동 보호 간격 (초)

[Header("Watermark Server Settings")]
public string wamServerUrl = "http://localhost:5000";  // WAM 서버 주소
public bool useWAMWatermark = true;            // WAM 워터마킹 사용
```

### VRWatermark_PostProcess 설정

```csharp
[Header("URP 렌더맵 설정")]
public int captureResolution = 1024;           // 캡처 해상도
public LayerMask artworkLayerMask = -1;        // 아트워크 레이어

[Header("워터마크 강도 (맵별)")]
public float depthStrength = 2.0f;             // Depth 맵 강도
public float normalStrength = 1.8f;            // Normal 맵 강도
public float ssaoStrength = 1.5f;              // SSAO 맵 강도

[Header("성능 설정")]
public float serverTimeout = 25f;              // 서버 타임아웃 (초)
public int maxRetryAttempts = 3;               // 최대 재시도 횟수
public bool enablePerformanceLogging = true;   // 성능 로깅
```

## 📊 성능 목표

| 항목 | 목표 | 현재 달성 |
|------|------|----------|
| VR 생성 FPS | 72+ FPS | ✅ 달성 |
| 24레이어 처리 | < 27초 | ✅ 달성 |
| WAM 개별 처리 | < 2초 | ✅ 달성 |
| 서버 재시도 | 최대 3회 | ✅ 구현 |

## 🔧 문제 해결

### 일반적인 문제

**1. VR 헤드셋이 인식되지 않는 경우**
```
1. Project Settings → XR Plug-in Management 확인
2. OpenXR 또는 Oculus 플러그인 활성화
3. 헤드셋 Developer Mode 확인
4. USB 연결 및 디버깅 권한 확인
```

**2. URP 렌더맵이 캡처되지 않는 경우**
```
1. URP Asset에서 Depth Texture 활성화
2. Renderer Features에 DepthNormals Prepass 추가
3. Renderer Features에 SSAO 추가
4. 카메라 설정에서 Depth Texture 요구 확인
```

**3. WAM 서버 연결 오류**
```
1. WAM 서버가 localhost:5000에서 실행 중인지 확인
2. 방화벽 설정 확인
3. Unity Console에서 서버 응답 로그 확인
4. 네트워크 연결 상태 확인
```

**4. 성능 저하 문제**
```
1. artworkResolution을 512로 낮춤
2. autoProtectionInterval을 300초로 증가
3. enableMemoryOptimization = true 설정
4. VR 시뮬레이션 모드로 개발/테스트
```

### 로그 분석

**Unity Console 주요 로그:**
```
[URP-WAM] 24레이어 보호 시작 - Session: URP_20250817191143_4762
[URP-WAM] 성능 통계: 현재 15.2초, 목표 대비 56.3%
[URP-WAM] ✅ 성능 목표 달성: 15.2초 < 27초
WAM Server is healthy: {"status": "healthy", "device": "cuda"}
```

## 🤝 개발 가이드

### 새로운 기능 추가

1. **새로운 워터마크 레이어 타입 추가:**
   ```csharp
   // VRWatermark_PostProcess.cs에서
   public enum CoreRenderMap {
       Depth = 0,
       Normal = 1,
       SSAO = 2,
       NewMapType = 3  // 새 맵 타입 추가
   }
   ```

2. **자동 보호 트리거 조건 추가:**
   ```csharp
   // VRWatermark_Realtime.cs에서
   bool HasReachedCreationMilestone() {
       // 새로운 조건 추가
       bool newCondition = CheckNewCondition();
       return strokeMilestone || complexityMilestone || newCondition;
   }
   ```

### 디버깅 팁

```csharp
// 디버그 맵 저장 활성화
saveDebugMaps = true;

// 성능 로깅 활성화
enablePerformanceLogging = true;

// VR 시뮬레이션으로 개발
useVRSimulation = true;
```

## 📚 참고 자료

- [Unity URP 문서](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)
- [XR Interaction Toolkit](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@latest)
- [Meta Quest 개발 가이드](https://developer.oculus.com/documentation/unity/)
- [OpenXR 사양](https://www.khronos.org/openxr/)
