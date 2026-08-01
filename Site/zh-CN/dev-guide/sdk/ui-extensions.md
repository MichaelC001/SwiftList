# 界面与预览扩展

## 结果展示

### `ISidebarFilterProvider`

给结果侧栏添加分类过滤分组(例如日期区间或文件大小档位)。

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // 默认 100;数值越小越靠前渲染
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` 有一个 `Header`、一个 `AllowMultiSelect` 开关(默认 `false`;打开后这个分组允许同时选中多项,用 OR 组合——如果分组里的选项只在单选时才有意义(比如互相重叠/累进的日期区间),就不要打开它),以及一份 `SidebarFilterItem` 列表(Id、DisplayName、可选图标，以及一个可选的、对当前结果列表做异步过滤的 `FilterPredicate`)。宿主会在分组有选中项时自动显示一个清空按钮,
所以 provider 不需要自己维护一个"全部"/"任意"伪选项。

### `IResultColumnProvider`

给结果表格视图注入额外的列(文件大小、修改日期、自定义元数据等等)。

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` 携带列 id、表头文字、宽度，以及可选的 `VisibilityPredicate`/
`SortComparer` 委托。

## 初始面板

### `IStartupPanelTabProvider`

给快速窗口的"初始面板"贡献一个标签——搜索框为空时结果列表上方显示的那个标签栏(见[初始面板](../../user-guide/settings/startup-panel))。CoreExtensions 的历史记录和收藏夹两个标签都是基于这个接口做的;参见[插件示例](../examples#coreextensions-——-动作与-shell-右键菜单)。

```csharp
interface IStartupPanelTabProvider : IPluginComponent
{
    IAsyncEnumerable<ISearchResult> GetItemsAsync(CancellationToken cancellationToken = default);
}
```

`GetItemsAsync()` 在面板每次激活时都会调用，不做缓存。它是流式的而不是返回一份完整结果：第一条到达时标签就出现，其余的边到边填，所以一个需要去慢慢找的提供器只会让自己这个标签晚一点填满，绝不会拖住面板的出现。数据本来就在内存里的提供器直接从列表里 yield 即可，不会为这个形状付出任何代价。面板关闭或重新激活时令牌会被取消——请遵守它，别为一个没人在看的面板继续枚举下去。

一条都没 yield 的提供器，其标签会被整个排除在标签栏之外，而不是显示成空的。用户可以在实时面板里用 **×** 按钮单独隐藏一个标签，这和在设置 → 插件里把该组件整个禁用是两回事，故意分开处理——宿主程序使用组件的具体类型名称（`GetType().Name`）作为稳定 Key 来持久化隐藏状态。

## 快速面板

### `IQuickPanelSourceProvider`

给[快速面板](../../user-guide/settings/quick-panel)贡献一个来源——那个停靠在前台窗口上的浮动面板。一个来源在那里就是一个分组，有自己的标题，条目由宿主用它自己的结果行渲染，所以图标、打开、动作菜单都是白送的。CoreExtensions 自带两个：Windows 历史记录和收藏夹。

```csharp
interface IQuickPanelSourceProvider : IPluginComponent
{
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

`GetEntriesAsync()` 在面板每次被呼出时调用。这里刻意没有采用 `IStartupPanelTabProvider` 那种流式形状：这个面板要把一个来源的条目当作一个**整体**来排序和截断(最新在前，或者按名称，且最多多少条)，所以它没法只显示其中一半而不在每次新条目到达时重排整个分组。这并不会带来延迟——每个工作区的每个来源都在各自的任务上加载，面板在第一个到达时就打开，所以一个需要去慢慢找的提供器只会拖慢自己那个分组。但仍然请遵守令牌：面板关闭时它会被取消。

来源知道修改时间的话，就填进 `ISearchResult.Metadata` 的 `Modified`，分组默认的「最新在前」会用它；保持默认值不填，条目就维持你返回时的顺序。返回空的来源不会产生分组，所有来源都返回空的工作区不会有标签。

来源出现在哪里由用户决定：他们在设置 → 快速面板 → 插件来源里把它加进想加的工作区，每个工作区各自记住它的位置、是否隐藏、叫什么名字、怎么显示——全部以组件 id 为键，和用户自己的文件夹放在一起。

## 预览与缩略图

### `IFilePreviewProvider`

在 QuickLook 预览面板里渲染自定义的 WPF `UIElement`(见[动作菜单与预览 → QuickLook 预览](../../user-guide/actions-and-preview#quicklook-预览))，用于你想特殊处理的文件类型。

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // 默认 0;数值越大越先运行
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
    bool RendersExternally { get; } // 默认 false
}
```

`Priority`只是*默认*的顺序——用户可以在 设置 → 通用 →
[预览与缩略图](../../user-guide/settings/general#预览与缩略图)里自由调整各个提供者的顺序(包括相对于你的这个 provider)，这个用户配置会覆盖 `Priority` 返回的值。不要假设你的 provider 声明的优先级就是它实际运行的顺序。

两个可选的配套接口可以进一步优化预览行为:

- **`IPreviewSessionAware`** —— 如果预览提供者自身持有开销较大的进程外资源(托管的原生处理程序、文件锁)，就在预览提供者本身上实现这个接口;`EndPreviewSession()` 只在整个预览会话结束时调用一次，而不是每次切换预览目标都调用。唯一的例外:如果这个 provider 的 `RendersExternally`
  为 true，宿主会在每次从它切换走的时候都调用一次，不只是会话真正结束的时候——见下文。
- **`IReusablePreview`** —— 如果 `CreatePreview` 返回的 `UIElement` 能够重新指向一个新文件，而不需要从头重建，就在它上面实现这个接口:`TrySetTarget(path, isDir)` 返回 `true` 表示已经原地处理好了变更，返回 `false` 则告诉宿主需要重新构建一个新的预览。

`RendersExternally` 适用于真正的预览内容渲染在一个独立的、由外部管理的窗口里、而不是
`CreatePreview` 返回的那个 `UIElement` 上的场景——比如把文件整个交给另一个应用程序去处理。当胜出的 provider 设置了这个属性，宿主会隐藏自己的预览面板，而不是显示 `CreatePreview` 的内容(反正也不会真的显示出来，所以可以随便返回一个占位用的空内容)。配合 **`IReceivesPreviewPanelBounds`**
使用，可以拿到宿主自己那个预览面板本该占据的屏幕矩形(物理像素)，这样外部窗口就能被摆到那个位置，而不是随便出现在别的地方:

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

内置的(实验性)QuickLook 桥接插件就是一个真实例子:它通过命名管道探测一个外部的
[QuickLook](https://github.com/QL-Win/QuickLook) 应用，如果能连上，就把它的窗口停靠到宿主面板原本的位置，覆盖所有文件/文件夹——具体的用户可见行为见[动作菜单与预览 → 通过 QuickLook 的外部预览](../../user-guide/actions-and-preview#通过-quickLook-的外部预览可选)。注意这和 SwiftList
自己内置的预览面板是两回事——本代码库和文档里也习惯把那个内置面板非正式地称为"QuickLook"。

### `IThumbnailProvider`

覆盖匹配结果显示的图标/缩略图。

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // 默认 0;数值越大越先运行
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

跟上面 `IFilePreviewProvider.Priority` 的说明一样:这只是默认顺序,用户可以在 设置 → 通用 →
[预览与缩略图](../../user-guide/settings/general#预览与缩略图)里覆盖它(这两种 provider 的排序列表在同一个标签页里)。

## 主题与本地化

### `IThemeProvider` / `ITheme`

注册一个或多个自定义 WPF 资源字典，作为可选主题(显示在**设置 → 通用 → 界面主题**里)。

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
    double WindowOpacity { get; } // 默认 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

为给定文化提供界面字符串——可以是插件自己的界面文本，也可以像 `PinyinAlias` 那样，仅仅是它自己的显示名称。参见[插件示例](../examples)了解一个把这个接口和另一个不相关接口实现在同一个类上的插件。

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // 例如 "zh-CN"、"en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations`(见[宿主服务](./services))是用内嵌在插件 DLL 里的 JSON 文件支撑这个接口的标准做法。
