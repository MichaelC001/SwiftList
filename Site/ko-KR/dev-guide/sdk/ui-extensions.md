# UI 및 미리보기 확장

## 결과 표시

### `ISidebarFilterProvider`

결과 사이드바에 분류용 필터 그룹을 추가합니다(예: 날짜 범위나 크기 구간).

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // default 100; lower renders first
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup`은 `Header`, `AllowMultiSelect` 플래그(기본값 `false`. 이 그룹이 OR로 결합되는 다중
선택을 허용하도록 옵트인합니다 — 겹치거나 누적되는 날짜 범위처럼 한 번에 하나만 의미가 있는 항목이라면
꺼두세요), 그리고 `SidebarFilterItem`의 목록(Id, DisplayName, 선택적 아이콘, 현재 결과 목록에 대한 선택적
비동기 `FilterPredicate`)을 가집니다. 호스트는 그룹에 선택 항목이 생기면 지우기 버튼을 표시하므로,
제공자가 자체적으로 "전체"/"임의" 의사 항목을 둘 필요는 없습니다.

### `IResultColumnProvider`

결과 그리드 뷰에 추가 열(파일 크기, 수정 날짜, 커스텀 메타데이터 등)을 삽입합니다.

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition`은 열 id, 헤더 텍스트, 너비, 선택적인 `VisibilityPredicate`/`SortComparer`
델리게이트를 가집니다.

## 시작 패널

### `IStartupPanelTabProvider`

퀵 창의 시작 패널에 탭을 기여합니다 — 검색 상자가 비어 있을 때 결과 목록 위에 표시되는 탭 스트립입니다
([시작 패널](../../user-guide/settings/startup-panel) 참고). CoreExtensions의 기록과 즐겨찾기 탭은
둘 다 이를 기반으로 만들어졌습니다. 둘러본 내용은
[예제 플러그인](../examples#coreextensions-—-동작과-셸-컨텍스트-메뉴)을 참고하세요.

```csharp
interface IStartupPanelTabProvider : IPluginComponent
{
    IAsyncEnumerable<ISearchResult> GetItemsAsync(CancellationToken cancellationToken = default);
}
```

`GetItemsAsync()`는 패널이 활성화될 때마다 호출되며 캐시되지 않습니다. 완성된 집합을 반환하는 대신
스트리밍합니다: 첫 항목이 도착하면 탭이 나타나고 나머지는 도착하는 대로 채워지므로, 찾아봐야 하는
제공자는 자기 탭이 채워지는 시점만 늦출 뿐 패널이 나타나는 것 자체를 붙잡지 않습니다. 이미 메모리에 다
있는 제공자는 목록에서 그대로 yield하면 되며 이 형태 때문에 치르는 비용은 없습니다. 패널이 닫히거나 다시
활성화되면 토큰이 취소됩니다 — 아무도 보지 않는 패널을 위해 계속 열거하지 말고 이를 존중하세요.

하나도 yield하지 않는 제공자의 탭은 빈 상태로 표시되는 대신 탭 스트립에서 아예 빠집니다. 사용자는 설정 →
플러그인에서 컴포넌트 자체를 완전히 비활성화하는 것과는 별개로, **×** 버튼으로 현재 패널에서 탭을 숨길 수
있습니다 — 이 둘은 의도적으로 분리되어 있으며, 호스트는 닫힌 상태를 영속화하기 위한 안정적인 키로
컴포넌트의 구체적인 클래스 타입 이름(`GetType().Name`)을 사용합니다.

## 빠른 패널

### `IQuickPanelSourceProvider`

[빠른 패널](../../user-guide/settings/quick-panel)에 소스를 제공합니다 — 앞에 있는 창 위에 도킹되는 그
떠 있는 패널입니다. 소스 하나는 그곳에서 자체 머리글을 가진 그룹 하나가 되며, 항목은 호스트 자신의 결과
행으로 렌더링되므로 아이콘, 열기, 동작 메뉴가 모두 공짜로 따라옵니다. CoreExtensions에는 세 개가
들어 있습니다: Windows 최근 항목, 기록, 즐겨찾기.

```csharp
interface IQuickPanelSourceProvider : IPluginComponent
{
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

`GetEntriesAsync()`는 패널을 호출할 때마다 실행됩니다. `IStartupPanelTabProvider`의 스트리밍 형태를
의도적으로 따르지 않았습니다: 이 패널은 한 소스의 항목을 **하나의 집합**으로 정렬하고 잘라내므로(최신순
또는 이름순, 최대 몇 개까지), 항목이 도착할 때마다 그룹 전체를 다시 정렬하지 않고서는 그 절반만 보여줄 수
없습니다. 그렇다고 지연이 생기지는 않습니다 — 모든 워크스페이스의 모든 소스가 각자의 작업에서 로드되고
패널은 가장 먼저 도착한 것으로 열리므로, 찾아봐야 하는 제공자는 자기 그룹만 늦춥니다. 그래도 토큰은
존중하세요: 패널이 닫히면 취소됩니다.

소스가 수정 시각을 알고 있다면 `ISearchResult.Metadata`의 `Modified`를 채우세요. 그룹의 기본값인 최신순이
그 값을 사용합니다. 기본값 그대로 두면 항목은 반환한 순서를 유지합니다. 아무것도 반환하지 않는 소스는
그룹을 만들지 않으며, 모든 소스가 빈 워크스페이스에는 탭이 생기지 않습니다.

소스가 어디에 나타날지는 사용자가 정합니다: 설정 → 빠른 패널 → 플러그인 소스에서 원하는 워크스페이스에
추가하며, 각 워크스페이스가 그 위치, 숨김 여부, 이름, 표시 방식을 각각 기억합니다 — 모두 컴포넌트 id를
키로, 사용자 자신의 폴더와 나란히 저장됩니다.

## 미리보기와 썸네일

### `IFilePreviewProvider`

특별히 처리하고 싶은 파일 형식에 대해 QuickLook 미리보기 창(자세한 내용은
[동작 메뉴 및 미리보기](../../user-guide/actions-and-preview#quicklook-미리보기) 참고)에 커스텀 WPF
`UIElement`를 렌더링합니다.

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // default 0; higher runs first
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
    bool RendersExternally { get; } // default false
}
```

`Priority`는 어디까지나 *기본* 순서일 뿐입니다 — 사용자는 설정 → 일반 →
[미리보기 및 썸네일](../../user-guide/settings/general#미리보기-및-썸네일)에서 (여러분의 제공자를
포함해) 제공자 순서를 자유롭게 재정렬할 수 있으며, 이것이 `Priority`가 반환하는 값보다 우선합니다.
여러분의 제공자가 선언한 우선순위가 실제로 실행되는 순서라고 가정하지 마세요.

두 개의 선택적인 동반 인터페이스가 미리보기 동작을 세밀하게 조정합니다.

- **`IPreviewSessionAware`** — 미리보기 제공자 자체가 비용이 큰 프로세스 외부 리소스(호스팅되는 네이티브
  핸들러, 파일 잠금 등)를 붙들고 있다면 이를 구현하세요. `EndPreviewSession()`은 개별 미리보기가 전환될
  때마다가 아니라 전체 미리보기 세션이 끝날 때 한 번 호출됩니다. 한 가지 예외로, `RendersExternally`가
  true인 제공자의 경우 세션이 끝날 때뿐 아니라 그 제공자에서 벗어나는 전환마다 호스트가 이를 호출합니다
  (아래 참고).
- **`IReusablePreview`** — `CreatePreview`가 반환한 `UIElement`가 완전히 새로 만들어지는 대신 새로운
  파일을 다시 가리킬 수 있다면 이를 구현하세요. `TrySetTarget(path, isDir)`는 변경 사항을 제자리에서
  처리했다면 `true`를, 호스트에게 새 미리보기를 만들도록 알리려면 `false`를 반환합니다.

`RendersExternally`는 실제 미리보기 표면이 `CreatePreview`가 반환하는 `UIElement`가 아니라 별도의,
외부에서 관리되는 창인 제공자를 위한 것입니다 — 예를 들어 파일을 완전히 다른 애플리케이션에 넘기는
경우입니다. 이 값이 설정된 제공자가 선정되면, 호스트는 `CreatePreview`의 콘텐츠를 표시하는 대신(그 콘텐츠는
실제로 보이지 않으므로 사소한 자리표시자여도 됩니다) 자체 미리보기 패널을 숨깁니다. 이를
**`IReceivesPreviewPanelBounds`** 와 함께 사용하면 호스트 자체 패널이 차지했을 정확한 화면 사각형(물리
픽셀)을 받아, 외부 창을 원래 나타날 위치 대신 그 자리에 배치할 수 있습니다.

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

실제 예시는 번들로 제공되는 (실험적인) QuickLook Bridge 플러그인을 참고하세요. 이 플러그인은 외부
[QuickLook](https://github.com/QL-Win/QuickLook) 앱을 자체 이름 있는 파이프를 통해 감지하고, 연결
가능하다면 모든 파일/폴더에 대해 그 앱의 창을 호스트 패널의 위치에 도킹시킵니다 — 사용자 대상 동작은
[동작 메뉴 및 미리보기 → QuickLook을 통한 외부 미리보기](../../user-guide/actions-and-preview#quicklook을-통한-외부-미리보기-선택-사항)를
참고하세요. 이는 이 코드베이스와 문서 전반에서 마찬가지로 비공식적으로 "QuickLook"이라고 불리는
SwiftList 자체의 내장 미리보기 패널과는 다른 것이라는 점에 유의하세요.

### `IThumbnailProvider`

일치하는 결과에 표시되는 아이콘/썸네일을 재정의합니다.

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // default 0; higher runs first
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

위의 `IFilePreviewProvider.Priority`와 동일한 주의 사항이 적용됩니다. 이는 어디까지나 기본 순서일
뿐이며, 사용자는 설정 → 일반 →
[미리보기 및 썸네일](../../user-guide/settings/general#미리보기-및-썸네일)에서 이를 재정의할 수
있습니다(같은 탭에서 두 제공자의 순서 목록을 모두 다룹니다).

## 테마와 지역화

### `IThemeProvider` / `ITheme`

하나 이상의 커스텀 WPF 리소스 딕셔너리를 선택 가능한 테마로 등록합니다(**설정 → 일반 → 인터페이스 테마**에
표시됨).

```csharp
interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}

interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    double WindowOpacity { get; } // default 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

특정 문화권에 대한 UI 문자열을 제공합니다 — 플러그인 자체의 UI를 위한 것일 수도 있고, `PinyinAlias`처럼
단순히 자신의 표시 이름만을 위한 것일 수도 있습니다. 이를 무관한 다른 인터페이스와 같은 클래스에 함께
구현한 플러그인 예시는 [예제 플러그인](../examples)을 참고하세요.

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // e.g. "zh-CN", "en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations`([호스트 서비스](./services) 참고)는 이를 플러그인 DLL에
내장된 JSON 파일로 뒷받침하는 표준적인 방법입니다.
