# Panel de Inicio

Una franja de pestañas mostrada sobre la lista de resultados en la ventana rápida siempre que el cuadro de
búsqueda está vacío, dando acceso rápido a archivos recientes, favoritos e historial sin escribir ninguna consulta.

- **Habilitar el panel de inicio** — interruptor maestro; desactivado significa que el panel nunca se activa en
  absoluto, sin importar los ajustes por pestaña de abajo.

Cuatro subpestañas: **Archivos recientes**, **Última carpeta**, **Pestañas de plugin**, y **Orden de pestañas**.

## Archivos recientes

- **Habilitar panel** — casilla; muestra la pestaña cuando el cuadro de búsqueda está vacío.
- **Directorios** — carpetas a vigilar, una por línea (la misma edición de añadir/editar/eliminar fila y edición
  de texto masiva que [Reglas de exclusión](./index-drives#reglas-de-exclusion)). Puede incluir unidades locales,
  unidades de red asignadas, y rutas WSL (`\\wsl$\...` o `\\wsl.localhost\...`) — comparadas con cualquiera de las
  que ya hayas configurado e indexado en [Índice](./index-drives).
- **Número máximo de archivos a mostrar** — rango 1–100, por defecto 10.
- **Rango de tiempo (minutos)** — solo son elegibles los archivos modificados dentro de este número de minutos
  desde ahora, además del límite de recuento de arriba. Rango 1–43200 (30 días), por defecto 60.

Solo archivos, no carpetas: la fecha de modificación de una carpeta cambia cada vez que algo entra o sale de
ella, así que en cualquier directorio en el que se esté trabajando las carpetas quedaban entre lo más reciente y
desplazaban justo aquello que esta lista existe para mostrar.

La segunda línea de cada entrada lleva como prefijo un tiempo relativo — "hace X segundos/minutos/horas/días" —
construido a partir de la marca de tiempo de última modificación de ese archivo, ya almacenada en el índice, así
que no cuesta ningún acceso adicional a disco. Las horas y los días omiten la unidad menor cuando es cero (por
ejemplo, "hace 2 horas" en lugar de "hace 2 horas 0 minutos").

## Última carpeta

- **Habilitar panel** — casilla, activada por defecto.

Muestra el contenido de cualquier carpeta a la que se navegó por última vez en un diálogo nativo de abrir/guardar
archivo, mientras la propia función de interceptación de diálogos de SwiftList estaba activa — una forma rápida de
volver a donde estabas explorando en un selector de archivos, sin necesidad de recordar la ruta. El mismo filtrado
de archivos ocultos/de sistema y las [Reglas de exclusión](./index-drives#reglas-de-exclusion) que aplica el
índice real también se aplican aquí, así que cosas como `$RECYCLE.BIN` no aparecen. Esta pestaña no aparece en
absoluto si eso todavía no ha ocurrido en esta sesión, la carpeta ya no existe, o es tu Escritorio (con demasiada
frecuencia es solo el punto de aterrizaje predeterminado, no un destino de navegación real).

## Pestañas de plugin

Las pestañas aportadas por plugins (por ejemplo, Historial, Favoritos) muestran cada una un botón **×** en el
panel en vivo para ocultarlas por ahora. Esto es una decisión de "ocultarla" local al panel — independiente de
deshabilitar el propio componente de plugin en [Plugins](./plugins), lo cual impide que se use en absoluto. Una
pestaña cerrada de esta forma se lista aquí, agrupada por el plugin que la aporta, sin marcar; márcala para que
vuelva.

Solo las pestañas cuyo componente de plugin esté actualmente habilitado aparecen en esta lista — una deshabilitada
por completo en [Plugins](./plugins) nunca llega a ser candidata a pestaña en primer lugar.

## Orden de pestañas

La misma lista con flechas arriba/abajo (o arrastrar para reordenar) usada en otras partes de Configuración (ver
[Favoritos](./favorites)), pero que cubre la franja de pestañas del panel en su conjunto — tanto las pestañas
integradas (Archivos recientes, Última carpeta) como cada pestaña de plugin actualmente visible, todas en una
única lista plana, a diferencia de Pestañas de plugin de arriba, que agrupa por plugin. Mueve una pestaña arriba o
abajo para cambiar dónde cae respecto a las demás; de izquierda a derecha en esta lista es de izquierda a derecha
en el panel en vivo.

Solo se listan las pestañas que realmente se mostrarían ahora mismo — una pestaña de Archivos recientes/Última
carpeta que hayas deshabilitado arriba, o una pestaña de plugin cerrada con su botón **×**, no tiene nada que
ordenar hasta que se vuelva a habilitar o reabrir.
