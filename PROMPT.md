# Claude Code 지시 프롬프트 — PixelSprite CLI

## 역할
너는 C# / .NET 8 시니어 엔지니어야.
CLAUDE.md의 스펙을 읽고 PixelSprite CLI 프로젝트를 처음부터 구현해.

## 시작 전 필수 확인
1. CLAUDE.md 전체를 읽어.
2. 아래 순서대로 구현해. 순서를 바꾸지 마.

---

## 구현 순서

### Phase 1 — 솔루션 스캐폴딩
1. `PixelSprite.sln` 생성
2. `src/PixelSprite.Core` 클래스 라이브러리 프로젝트 생성 (.NET 8)
3. `src/PixelSprite.Cli` 콘솔 프로젝트 생성 (.NET 8)
4. `tests/PixelSprite.Tests` xUnit 프로젝트 생성
5. NuGet 패키지 추가:
   - Core: Silk.NET.OpenGL, Silk.NET.Windowing, Silk.NET.GLFW, Silk.NET.Assimp, SkiaSharp, nQuant.Core
   - Cli: System.CommandLine, 프로젝트 참조 → Core
   - Tests: xUnit, FluentAssertions, 프로젝트 참조 → Core
6. `build.ps1` 작성

### Phase 2 — Models (데이터 클래스)
`src/PixelSprite.Core/Models/` 아래 4개 파일:
- `RenderOptions.cs` — input, anim, directions, renderSize, fps, frames, camPitch, camZoom, ortho, upAxis
- `PixelArtOptions.cs` — spriteSize, maxColors, palettePath, alphaThreshold, edgeDilate, cleanup flags
- `OutputOptions.cs` — outputMode(enum: Sheet/Frames/Both), outDir, verbose
- `SpriteFrame.cs` — SKBitmap data, directionIndex, frameIndex, animName

### Phase 3 — DirectionScheduler
`src/PixelSprite.Core/Rendering/DirectionScheduler.cs`

```csharp
public static class DirectionScheduler
{
    // directions: 2, 4, or 8
    // returns list of yaw angles in degrees
    public static IReadOnlyList<float> GetYaws(int directions);
}
```
- 2: [0, 180]
- 4: [0, 90, 180, 270]  
- 8: [0, 45, 90, 135, 180, 225, 270, 315]

### Phase 4 — PixelArt 처리 레이어 (핵심)
아래 4개를 이 순서로 구현해.

#### 4-A. AlphaBinarizer
`src/PixelSprite.Core/PixelArt/AlphaBinarizer.cs`

```csharp
public static class AlphaBinarizer
{
    // threshold: 0-255, default 128
    // edgeDilate: copy RGB of nearest opaque neighbor to transparent border pixels
    public static SKBitmap Binarize(SKBitmap src, byte threshold = 128, bool edgeDilate = true);
}
```
- Step 1: 모든 픽셀 순회 → A >= threshold ? A=255 : A=0
- Step 2 (edgeDilate=true):
  - 투명 픽셀 중 상하좌우에 불투명 이웃이 있는 것 → 이웃 RGB 복사, A는 0 유지
  - 이 과정은 투명→불투명 전환을 하지 않음. RGB만 오염 방지용으로 채움

#### 4-B. DominantDownscaler
`src/PixelSprite.Core/PixelArt/DominantDownscaler.cs`

```csharp
public static class DominantDownscaler
{
    public static SKBitmap Downscale(SKBitmap src, int targetWidth, int targetHeight, byte alphaThreshold = 128);
}
```
블록 크기 = src.Width/targetWidth × src.Height/targetHeight (정수 나눗셈)

블록마다:
1. 블록 내 픽셀 수집
2. A >= alphaThreshold인 픽셀만 opaque로 간주
3. opaque 픽셀이 0개 → 출력 픽셀 = (0,0,0,0)
4. opaque 픽셀의 색상 빈도 집계 (RGB 기준, A 무시)
5. 최빈 색상 빈도 / opaque 픽셀 수 > 0.05 → 최빈 색상 사용 (A=255)
6. 그렇지 않으면 → opaque 픽셀 R,G,B 평균 (A=255)

#### 4-C. PaletteQuantizer
`src/PixelSprite.Core/PixelArt/PaletteQuantizer.cs`

```csharp
public static class PaletteQuantizer
{
    // fixedPalette: null이면 Wu quantization 실행
    // maxColors: nQuant.Core WuQuantizer에 전달
    public static SKBitmap Quantize(SKBitmap src, int maxColors, SKColor[]? fixedPalette = null);
}
```
- 투명 픽셀(A=0)은 양자화 대상에서 제외
- 양자화 후에도 원래 투명했던 픽셀은 (0,0,0,0) 유지
- fixedPalette가 있으면 Wu 건너뛰고 nearest-color 매핑만

#### 4-D. ArtifactCleaner
`src/PixelSprite.Core/PixelArt/ArtifactCleaner.cs`

```csharp
public static class ArtifactCleaner
{
    public static SKBitmap MorphClean(SKBitmap src);  // erosion → dilation
    public static SKBitmap JaggyClean(SKBitmap src);  // isolated pixel removal
}
```
- MorphClean: 3×3 커널 erosion 후 dilation (투명 픽셀은 배경으로 처리)
- JaggyClean: 4방향 이웃 모두와 다른 픽셀 → 최빈 이웃 색으로 교체

#### 4-E. PixelArtProcessor (오케스트레이터)
`src/PixelSprite.Core/PixelArt/PixelArtProcessor.cs`

```csharp
public class PixelArtProcessor
{
    public SKBitmap Process(SKBitmap highResFrame, PixelArtOptions opts);
}
```
실행 순서:
1. ArtifactCleaner.MorphClean (opts.cleanup.morph == true)
2. DominantDownscaler.Downscale
3. AlphaBinarizer.Binarize
4. PaletteQuantizer.Quantize
5. ArtifactCleaner.JaggyClean (opts.cleanup.jaggy == true)

### Phase 5 — OffscreenRenderer
`src/PixelSprite.Core/Rendering/OffscreenRenderer.cs`

이미 구현된 Bo-sung/ComfyUI-FBX-ControlNet-Converter의 렌더러 패턴을 따라.
차이점:
- FBO는 반드시 RGBA8 (알파 채널 포함)
- glClearColor(0, 0, 0, 0) — 배경 완전 투명
- GL_BLEND: glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA)
- GLFW 창 숨김: GLFW_VISIBLE = false (headless)
- 렌더 후 glReadPixels → SKBitmap(RGBA8888) 변환
- Dispose 패턴 적용 (IDisposable)

```csharp
public class OffscreenRenderer : IDisposable
{
    public OffscreenRenderer(int width, int height);
    
    // scene: Assimp로 로드된 씬, animIndex: 애니메이션 인덱스
    // yaw: 카메라 수평 회전(도), pitch: 수직각(도)
    // time: 애니메이션 타임스탬프(초)
    public SKBitmap RenderFrame(Scene scene, int animIndex, float yaw, float pitch, float time, RenderOptions opts);
    
    public void Dispose();
}
```

### Phase 6 — RenderJob
`src/PixelSprite.Core/Rendering/RenderJob.cs`

```csharp
public class RenderJob
{
    // 전체 파이프라인 실행: 방향 × 프레임 루프
    public IEnumerable<SpriteFrame> Execute(RenderOptions renderOpts, PixelArtOptions pixelOpts, Action<string>? progress = null);
}
```
루프: foreach direction → foreach frame → Render → PixelArtProcessor.Process → yield SpriteFrame

### Phase 7 — Packing
`src/PixelSprite.Core/Packing/SpriteSheetPacker.cs`

```csharp
public static class SpriteSheetPacker
{
    // frames: direction × frame 순서 정렬 가정
    // 결과: rows=directions, cols=frames
    public static SKBitmap Pack(IReadOnlyList<SpriteFrame> frames, int spriteW, int spriteH, int dirCount, int frameCount);
}
```
- Pack 결과 SKBitmap의 배경: 완전 투명 (SKColors.Empty)
- SkiaSharp SKCanvas 사용

`src/PixelSprite.Core/Packing/MetadataWriter.cs`

```csharp
public static class MetadataWriter
{
    public static void Write(string path, OutputMetadata metadata);
}
```
CLAUDE.md의 JSON 스키마 그대로 출력. `System.Text.Json` 사용.

### Phase 8 — CLI 진입점
`src/PixelSprite.Cli/Program.cs`

System.CommandLine으로 CLAUDE.md의 CLI 계약 전체 구현.
- `--input` 필수, 나머지 선택적
- 파싱 후 RenderOptions / PixelArtOptions / OutputOptions 조립
- RenderJob.Execute 호출
- OutputMode에 따라 SpriteSheetPacker 또는 프레임 시퀀스 저장
- verbose 시 각 프레임 진행상황 출력
- 오류 시 stderr에 출력, exit code 1

### Phase 9 — 테스트
`tests/PixelSprite.Tests/`

#### DominantDownscalerTests.cs
- 단일 색 블록 → 그 색 반환
- 빈 블록(전투명) → A=0 반환
- 50/50 혼합 → mean 반환
- 70/30 혼합 → dominant 반환

#### AlphaBinarizerTests.cs
- A=200 → A=255
- A=100 → A=0 (threshold=128)
- edgeDilate: 투명 경계 픽셀 RGB가 이웃 불투명 픽셀 색으로 채워지는지
- edgeDilate: 채워진 픽셀의 A가 여전히 0인지

#### PaletteQuantizerTests.cs
- 투명 픽셀이 양자화 후에도 A=0인지
- maxColors=2로 줄였을 때 팔레트가 2색 이하인지

---

## 구현 규칙

1. **알파 최우선**: 모든 SKBitmap 생성은 `SKColorType.Rgba8888`, `SKAlphaType.Premul`. 배경을 색으로 채우는 코드는 절대 작성하지 마.

2. **SkiaSharp만**: 이미지 I/O는 SkiaSharp만. System.Drawing이나 SixLabors.ImageSharp 사용 금지.

3. **nQuant.Core**: Wu quantization은 nQuant.Core의 `WuQuantizer` 사용. 직접 구현하지 마.

4. **Dispose**: SKBitmap, SKCanvas, SKSurface는 모두 using으로 처리. 렌더러는 IDisposable.

5. **에러 처리**: 파일 없음, 지원하지 않는 포맷 등은 의미있는 메시지와 함께 InvalidOperationException 또는 FileNotFoundException.

6. **GUI 없음**: OffscreenRenderer는 창을 띄우지 않음. GLFW_VISIBLE=false 필수.

7. **주석**: public API는 XML doc comment. 알고리즘 핵심 로직은 인라인 주석.

8. **테스트**: 실제 FBX 파일 없이 순수 SKBitmap 합성으로 테스트. IO 의존성 없음.

---

## 완료 기준 체크리스트
- [ ] `dotnet build` 경고 없이 성공
- [ ] `dotnet test` 전체 통과
- [ ] `build.ps1` 실행 시 `./bin/pixelsprite.exe` 생성
- [ ] `pixelsprite.exe --help` 전체 옵션 출력
- [ ] 투명 배경 규칙 (알파 0) 코드 레벨에서 보장

