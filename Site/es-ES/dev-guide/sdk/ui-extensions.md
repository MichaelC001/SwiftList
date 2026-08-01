# Extensiones de interfaz y vista previa

## Visualización de resultados

### `ISidebarFilterProvider`

Añade grupos de filtro de categorización a la barra lateral de resultados (por ejemplo, franjas por fecha o por
tamaño).

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // default 100; lower renders first
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` tiene un `Header`, un indicador `AllowMultiSelect` (`false` por defecto; permite que el grupo
seleccione más de un elemento a la vez, combinados con OR — déjalo desactivado para elementos cuyo significado
solo tiene sentido de uno en uno, por ejemplo rangos de fechas superpuestos/acumulativos), y una lista de
`SidebarFilterItem` (Id, DisplayName, icono opcional, y un `FilterPredicate` asíncrono opcional sobre la lista de
resultados actual). El host muestra un botón para limpiar en un grupo en cuanto tiene una selección, así que un
proveedor no necesita un pseudo-elemento "Todos"/"Cualquiera" propio.

### `IResultColumnProvider`

Inyecta columnas adicionales en la vista de cuadrícula de resultados (tamaño de archivo, fecha de modificación,
metadatos personalizados, ...).

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` lleva un id de columna, texto de cabecera, ancho, y delegados opcionales
`VisibilityPredicate`/`SortComparer`.

## Panel de Inicio

### `IStartupPanelTabProvider`

Aporta una pestaña al Panel de Inicio de la ventana rápida — la franja de pestañas mostrada sobre la lista de
resultados cuando el cuadro de búsqueda está vacío (ver [Panel de Inicio](../../user-guide/settings/startup-panel)).
Las pestañas Historial y Favoritos de CoreExtensions están construidas sobre esto; ver
[Plugins de ejemplo](../examples#coreextensions-—-acciones-y-el-menu-contextual-del-shell) para un recorrido.

```csharp
interface IStartupPanelTabProvider : IPluginComponent
{
    IAsyncEnumerable<ISearchResult> GetItemsAsync(CancellationToken cancellationToken = default);
}
```

`GetItemsAsync()` se llama en cada activación del panel y no se cachea. Transmite en vez de devolver un conjunto
terminado: la pestaña aparece cuando llega el primer elemento y se va llenando conforme llegan los demás, así que un
proveedor que tenga que ir a buscar solo retrasa lo completa que esté su propia pestaña, nunca la aparición del
panel. Uno que ya lo tenga todo en memoria puede hacer yield directamente desde una lista y no paga nada por esta
forma. El token se cancela cuando el panel se cierra o se reactiva: respétalo en lugar de seguir enumerando para un
panel que nadie está mirando.

Una pestaña cuyo proveedor no hace yield de nada se omite por completo de la franja en lugar de mostrarse vacía. El
usuario puede ocultar una pestaña del panel en vivo
con su botón **×** independientemente de deshabilitar por completo el componente en Configuración → Plugins — las
dos cosas son deliberadamente independientes; el host usa el nombre de tipo de la clase concreta del componente
(`GetType().Name`) como clave estable para persistir el estado de cerrado.

## Panel Rápido

### `IQuickPanelSourceProvider`

Aporta una fuente al [Panel Rápido](../../user-guide/settings/quick-panel) — el panel flotante acoplado sobre la
ventana que esté en primer plano. Una fuente se convierte allí en un grupo, con su propio encabezado, y el host
renderiza las entradas con sus propias filas de resultado, así que los iconos, la apertura y el menú de acciones
vienen gratis. CoreExtensions trae tres: Elementos recientes de Windows, Historial y Favoritos.

```csharp
interface IQuickPanelSourceProvider : IPluginComponent
{
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

`GetEntriesAsync()` se llama cada vez que se invoca el panel. Deliberadamente no adopta la forma de transmisión de
`IStartupPanelTabProvider`: este panel ordena y recorta las entradas de una fuente **como conjunto** (lo más nuevo
primero, o por nombre, y como mucho tantas), de modo que no puede mostrar la mitad sin reordenar el grupo en cada
llegada. Eso no cuesta latencia — cada fuente de cada espacio de trabajo se carga en su propia tarea y el panel se
abre con la primera que llega, así que un proveedor que tenga que ir a buscar solo retrasa su propio grupo. Aun así,
respeta el token: se cancela cuando el panel se cierra.

Rellena `Modified` en `ISearchResult.Metadata` allí donde la fuente conozca esa fecha y el orden por defecto del
grupo (lo más nuevo primero) la usará; déjalo con su valor por defecto y las entradas conservarán el orden en que
las devolviste. Una fuente que no devuelve nada no produce grupo, y un espacio de trabajo cuyas fuentes no devuelven
nada no obtiene pestaña.

Dónde aparece la fuente lo decide el usuario: la añade a los espacios de trabajo que quiera desde Configuración →
Panel Rápido → Fuentes de complementos, y cada uno de ellos recuerda su propia posición, si está oculta, cómo se
llama y cómo se muestra — todo indexado por el id del componente, junto a sus propias carpetas.

## Vista previa y miniaturas

### `IFilePreviewProvider`

Renderiza un `UIElement` de WPF personalizado en el panel de vista previa QuickLook (ver
[Menú de acciones y vista previa](../../user-guide/actions-and-preview#vista-previa-quicklook)) para los tipos de
archivo que quieras tratar de forma especial.

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

`Priority` es solo el orden *por defecto* — el usuario puede reordenar libremente los proveedores (incluso en
relación con el tuyo) desde Configuración → General →
[Vista previa y miniaturas](../../user-guide/settings/general#vista-previa-y-miniaturas), lo cual prevalece sobre
lo que devuelva `Priority`. No des por hecho que la prioridad declarada por tu proveedor es el orden en el que
realmente se ejecuta.

Dos interfaces complementarias opcionales refinan el comportamiento de la vista previa:

- **`IPreviewSessionAware`** — implementa esto en el propio proveedor de vista previa si retiene recursos costosos
  fuera de proceso (un manejador nativo alojado, un bloqueo de archivo); `EndPreviewSession()` se llama una vez
  termina toda la sesión de vista previa, no en cada cambio individual de vista previa. La única excepción: para un
  proveedor con `RendersExternally` en `true`, el host también lo llama en cada cambio desde ese proveedor, no solo
  al final de la sesión — ver más abajo.
- **`IReusablePreview`** — implementa esto en el `UIElement` devuelto por `CreatePreview` si puede reapuntarse a un
  nuevo archivo en lugar de reconstruirse desde cero: `TrySetTarget(path, isDir)` devuelve `true` si gestionó el
  cambio in situ, `false` para indicarle al host que construya una vista previa nueva en su lugar.

`RendersExternally` es para un proveedor cuya superficie de vista previa real es una ventana separada, gestionada
externamente, en lugar del `UIElement` que devuelve `CreatePreview` — por ejemplo, entregar el archivo por completo
a otra aplicación. Cuando el proveedor ganador tiene esto activado, el host oculta su propio panel de vista previa
en lugar de mostrar el contenido de `CreatePreview` (que entonces nunca llega a mostrarse realmente, así que puede
ser un marcador de posición trivial). Combínalo con **`IReceivesPreviewPanelBounds`** para obtener el rectángulo de
pantalla exacto (en píxeles físicos) que habría ocupado el propio panel del host, de modo que la ventana externa
pueda posicionarse ahí en lugar de donde aparecería de otro modo:

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

Consulta el plugin (experimental) incluido QuickLook Bridge para ver un ejemplo real: detecta una aplicación
externa [QuickLook](https://github.com/QL-Win/QuickLook) a través de su propia named pipe y, si está accesible,
acopla la ventana de esa aplicación en el lugar del panel del host para cada archivo/carpeta — ver [Menú de
acciones y vista previa → Vista previa externa mediante
QuickLook](../../user-guide/actions-and-preview#vista-previa-externa-mediante-quicklook-opcional) para el
comportamiento de cara al usuario. Ten en cuenta que esto es algo distinto del propio panel de vista previa
integrado de SwiftList, al que también se le llama informalmente "QuickLook" en todo este código y esta
documentación.

### `IThumbnailProvider`

Sobrescribe el icono/miniatura mostrado para los resultados que coincidan.

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // default 0; higher runs first
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

La misma advertencia que con `IFilePreviewProvider.Priority` de más arriba: es solo el orden por defecto, y el
usuario puede sobrescribirlo desde Configuración → General →
[Vista previa y miniaturas](../../user-guide/settings/general#vista-previa-y-miniaturas) (la misma pestaña aloja
las listas de orden de ambos tipos de proveedor).

## Temas y localización

### `IThemeProvider` / `ITheme`

Registra uno o más diccionarios de recursos WPF personalizados como temas seleccionables (mostrados en
**Configuración → General → Tema de interfaz**).

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

Suministra cadenas de interfaz para una cultura determinada — para la interfaz propia del plugin, o (como con
`PinyinAlias`) simplemente su propio nombre visible. Ver [Plugins de ejemplo](../examples) para un plugin que
implementa esto junto a una interfaz no relacionada en la misma clase.

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // e.g. "zh-CN", "en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations` (ver [Servicios del host](./services)) es la forma estándar de
respaldar esto con archivos JSON incrustados en la DLL de tu plugin.
