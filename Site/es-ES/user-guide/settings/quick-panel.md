# Panel Rápido

Un panel flotante que se invoca con una tecla y se acopla en la esquina inferior derecha de la ventana que esté en
primer plano, con la mitad de su alto y la mitad de su ancho. Muestra las carpetas que le indiques — como miniaturas
o como lista — para llegar a archivos, arrastrarlos fuera o soltarlos dentro sin salir de la ventana en la que estás
trabajando. Arrastra su borde superior para moverlo a otro sitio durante la invocación actual.

- **Activar el panel rápido** — interruptor general; apagado, la tecla no hace nada.

La tecla que lo invoca está en la página de [Atajos](./hotkeys-page) (`Ctrl+F2` por defecto).

## Espacios de trabajo

El panel muestra un **espacio de trabajo** cada vez, y cada espacio de trabajo es una pestaña de su franja. Un
espacio de trabajo es un conjunto de fuentes reunido para un tipo de trabajo: las carpetas de un proyecto, el sitio
donde guardas material de referencia, una bandeja de entrada donde vas dejando cosas.

La lista de la izquierda son los espacios de trabajo, con los botones **Nuevo espacio de trabajo**, **Duplicar
espacio de trabajo** y **Eliminar espacio de trabajo**, y la misma lista con flechas arriba/abajo (o arrastrar para
reordenar) que se usa en el resto de la Configuración (ver [Favoritos](./favorites)). De arriba abajo aquí es de
izquierda a derecha en la franja de pestañas del panel.

- **Nombre** — lo que se lee en su pestaña. Si se deja vacío, recurre a un nombre por defecto traducido, así que un
  espacio de trabajo al que nunca se le cambió el nombre sigue el idioma de la interfaz.
- **Activado** — la casilla junto a cada espacio de trabajo. Apagada, mantiene la configuración pero le quita la
  pestaña; pensado para uno preparado para un trabajo que este mes no estás haciendo, donde borrarlo significaría
  rehacer la lista de fuentes. La **×** de una pestaña del panel en vivo hace exactamente esto, y por eso volver a
  activarlo se hace aquí.

El espacio de trabajo seleccionado se edita en tres subpestañas: **Fuentes**, **Fuentes de complementos** y
**Aplicaciones**.

## Fuentes

Cada fuente es un grupo del panel, mostrado en el orden de esta lista. **Añadir carpeta** elige una; la casilla de
cada fila oculta ese grupo sin quitarlo; el cuadro de nombre sustituye el encabezado del grupo (déjalo vacío para el
nombre propio de la carpeta); y **Más opciones** abre el resto:

- **Mostrar** — de qué tira el grupo dentro de la carpeta:
  - **Archivos cambiados recientemente** — solo lo que ha cambiado hace poco, lo más nuevo primero, respondido
    desde el índice y no recorriendo la carpeta.
  - **Todo, lo más nuevo primero** — nunca oculta un archivo por antigüedad, solo decide qué va antes.
  - **Todo, por nombre** — una carpeta usada como barra de accesos directos.
- **Carpeta** — la carpeta en sí, con un botón **…** para buscarla.
- **Incluir subcarpetas** — desactivado por defecto.
- **Aceptar archivos soltados** — los archivos y carpetas que arrastres sobre este grupo se copian dentro de su
  carpeta, usando la propia copia de archivos de Windows (su diálogo de progreso, sus avisos de conflicto, su
  deshacer). Siempre una copia, nunca un movimiento. Desactivado por defecto y preguntado por fuente: una carpeta
  que usas como bandeja de entrada lo quiere, y una de la que solo lees, no.
- **Archivos** — uno o más patrones separados por `;` o `,` (p. ej. `*.mp4;*.mkv`). Las carpetas se muestran siempre.
- **Como máximo** — cuántas entradas muestra el grupo. 0 significa todo lo que tenga la fuente.
- **Cambiado en (minutos)** — solo cuentan las entradas cambiadas dentro de ese tiempo. 0 significa sin límite de
  antigüedad.
- **Mostrar como lista** — el grupo se abre como lista de detalles en vez de miniaturas. Cuál conviene es una
  propiedad de la carpeta: las imágenes quieren miniaturas, los documentos quieren nombres y fechas.

## Fuentes de complementos

Fuentes aportadas por plugins. Marca una para añadirla a este espacio de trabajo; a partir de ahí aparece en la
lista **Fuentes** junto a las carpetas y se ordena, renombra y oculta exactamente igual que una. CoreExtensions trae
dos: **Elementos recientes de Windows** (la propia lista de documentos recientes del shell, resuelta a los archivos
a los que apunta, lo más nuevo primero) y **Favoritos** (tus [Favoritos](./favorites), en el orden en que los
colocaste).

Añadir una es una decisión que se toma una vez, y por eso es una pestaña aparte de la lista donde se ordena y se
renombra. Solo se listan las fuentes cuyo componente de plugin está habilitado en [Plugins](./plugins). Un id cuyo
plugin ha desaparecido conserva su sitio en lugar de podarse, así que un plugin apagado una semana vuelve donde lo
pusiste.

## Aplicaciones

Aplicaciones a las que pertenece este espacio de trabajo, un nombre de proceso por línea (`chrome` o `chrome.exe`,
da igual). Invoca el panel sobre una de ellas y se abrirá en este espacio de trabajo en lugar de donde se quedó — la
aplicación en la que ya estás dice de qué conjunto de carpetas hablas. Vacío, al espacio de trabajo solo se llega a
mano.

## Solo panel rápido

Aplicaciones de las que el panel se mantiene alejado, un nombre de proceso por línea. Se **suma** a la
[lista negra de procesos](./hotkeys-page#process-blacklist) global en vez de sustituirla: lo bloqueado globalmente
también lo está aquí. Esta lista es para las aplicaciones que solo este panel tiene motivos para evitar — se acopla
sobre la ventana en primer plano, así que arruina un reproductor a pantalla completa o un juego sin que estos
merezcan un bloqueo global.

## Usar el panel

- **Cuadro de filtro** — a la derecha de la franja de pestañas, con el foco puesto en cuanto se abre el panel.
  Empareja de forma difusa (la misma coincidencia estilo fzf que usa la ventana de búsqueda, alias de pinyin
  incluidos) y solo dentro del espacio de trabajo actual. Un grupo sin nada que coincida se oculta mientras el
  filtro esté puesto.
- **Enter** abre lo que esté seleccionado, que es lo mismo que hace un doble clic. La primera entrada está
  seleccionada desde el principio, así que una invocación se resuelve escribiendo y pulsando Enter sin salir nunca
  del cuadro de filtro.
- **Cambiar de espacio de trabajo** — mantén el modificador de "saltar al resultado N" (`Ctrl` por defecto, ver
  [Atajos](../hotkeys)) y pulsa 1–9, o haz clic en una pestaña. Las pestañas se arrastran para reordenarlas, y cada
  una tiene una **×** que apaga ese espacio de trabajo. Cerrar la última cierra el panel.
- **Los encabezados de grupo** llevan un conmutador de orden (por nombre / por fecha de modificación), uno de vista
  (miniaturas / lista) y una flecha para plegar. Lo que hagas aquí dura mientras el panel esté abierto; el estado
  inicial es el que digan los ajustes de arriba.
- **Seleccionar varios** — en vista de miniaturas, arrastra un recuadro por el espacio vacío para hacer una
  selección. La selección pertenece a un solo grupo, ya que cada grupo dibuja su propia lista; hacer clic en el
  espacio vacío la limpia.
- **Soltar archivos dentro** — arrastra archivos, carpetas o una imagen directamente desde una página web sobre un
  grupo que acepte archivos soltados. Se acepta cualquier cosa que el arrastre pueda ofrecer como archivo, no solo
  imágenes. El grupo se recarga solo cuando termina la copia.
- **Que no se cierre** — el panel se cierra al perder el foco. El botón de anclaje, o el atajo de "mantener la
  ventana abierta" (`Ctrl+T` por defecto, el mismo que usa la ventana rápida), lo suspende durante la invocación
  actual.
- **Esc** vacía el cuadro de filtro si tiene texto, y cierra el panel si no.
- El panel se mantiene al día solo: una carpeta que esté mostrando y cambie en disco se recarga, a través de la
  misma vigilancia basada en el índice que usa el resto de la aplicación, no de un escaneo propio.
