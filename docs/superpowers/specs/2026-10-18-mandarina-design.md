# Mandarina — diseño

Fecha: 2026-10-18
Estado: borrador para revisión

## 1. Objetivo

Un clon personal de [Tangerine](https://tangerineformac.com/) para Windows 11: arrastrás un archivo, tocás una tecla y aparece una rueda flotante; soltás el archivo sobre una opción y se convierte. Uso personal, todo local, sin red.

**Criterios de éxito**

- Arrastrar un PNG desde el Explorador, tocar Shift, soltar sobre "JPG" → aparece `foto.jpg` al lado del original en menos de 1 s para una imagen típica.
- La rueda se ve como "liquid glass": translúcida, desenfoca lo que hay detrás, brillo en los bordes, animaciones suaves.
- Eficiente: en reposo ~0 % CPU y < 60 MB de RAM; la rueda aparece en < 100 ms desde el toque de tecla.
- El original nunca se modifica.

**Fuera de alcance (por ahora)**: audio, video, documentos de Office, Linux/macOS, instalador, actualizaciones automáticas, multi-idioma (la UI es en español).

## 2. Interacción (igual que Tangerine)

| Tecla (toque durante un arrastre) | Rueda | Contenido |
|---|---|---|
| Shift | Convertir | formatos destino según el tipo de archivo |
| Ctrl | Herramientas | comprimir, redimensionar, rotar, unir, separar… |
| Alt | Acciones | copiar a…, mover a…, copiar ruta |

- "Toque" = la tecla se aprieta y se suelta sin otra tecla en el medio, mientras el botón izquierdo del mouse está apretado y el cursor se movió más que el umbral de arrastre de Windows (`SM_CXDRAG`/`SM_CYDRAG`).
- La rueda aparece centrada en el cursor y queda abierta aunque sueltes la tecla. Tocar otra tecla cambia de rueda; tocar la misma otra vez muestra un único sector "Repetir: <última acción>".
- Soltar sobre un sector ejecuta esa operación. Soltar en el centro o fuera de la rueda la cierra sin hacer nada.
- **Sin arrastre**: con archivos seleccionados en el Explorador, `Ctrl+Alt+Espacio` abre la rueda Convertir en el cursor. Flechas/ratón eligen, Enter ejecuta, Tab cambia de rueda, Esc cierra, 1–9 ejecutan directo.
- Las teclas se pueden cambiar en Configuración.
- Varios archivos a la vez: cada uno se convierte por separado; las operaciones "unir" producen un solo resultado.

## 3. Operaciones (v1)

**Entrada de imágenes**: PNG, JPG/JPEG, WebP, HEIC/HEIF, AVIF, BMP, GIF, TIFF, ICO.

**Convertir (Shift)**
- Imagen → JPG, PNG, WebP, AVIF, BMP, TIFF, ICO, PDF (no se ofrece el formato actual).
- PDF → PNG, JPG (todas las páginas, 200 DPI, en una carpeta `nombre (páginas)` si son varias).

**Herramientas (Ctrl)**
- Imagen: comprimir (JPG calidad 75 / PNG optimizado / WebP 75), reducir a 50 %, máximo 1920 px, rotar 90° derecha, rotar 90° izquierda, quitar metadatos (EXIF/GPS), unir en PDF (varias imágenes → un PDF).
- PDF: unir (varios → uno), separar en páginas, rotar 90°.

**Acciones (Alt)**: copiar a… (selector de carpeta), mover a…, copiar ruta al portapapeles. Funcionan con cualquier archivo o carpeta.

**Nombres de salida**: `foto.png` → `foto.jpg`; herramientas agregan sufijo (`foto (comprimida).jpg`, `foto (1920px).png`); si existe, `foto (1).jpg`, `foto (2).jpg`…

## 4. Arquitectura

Una solución .NET 10 (LTS) con tres proyectos:

```
Mandarina.sln
src/Mandarina.Core/     lógica pura: catálogo, backends, nombres, cola (sin WPF)
src/Mandarina.App/      WPF: hooks, rueda, avisos, bandeja, configuración
tests/Mandarina.Tests/  xUnit
```

### Componentes

1. **`InputHooks`** (App) — `WH_MOUSE_LL` + `WH_KEYBOARD_LL` en un hilo propio con su bucle de mensajes. El callback solo actualiza un estado pequeño (botón apretado, posición inicial, tecla en curso) y devuelve enseguida; cuando detecta "toque durante arrastre" publica un evento `WheelRequested(tipo, x, y)` al hilo de UI. Nunca bloquea ni come eventos (`CallNextHookEx` siempre).
2. **`DragSession`** (App) — máquina de estados: `Inactivo → BotónApretado → Arrastrando → RuedaAbierta → Inactivo`. Se resetea al soltar el botón.
3. **`WheelWindow`** (App) — una única ventana creada al iniciar y reutilizada (oculta/mostrada). Sin bordes, transparente, siempre encima, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` en modo arrastre (no roba foco); `AllowDrop=true`: recibe el drop OLE del Explorador. Calcula el sector por ángulo/radio del cursor en `DragOver`.
4. **`Catalog`** (Core) — lista de `IOperation { Id, Rueda, Etiqueta, Glifo, bool AplicaA(archivos), Task EjecutarAsync(archivos, progreso, ct) }`. La rueda pide `catalog.Para(rueda, archivos)`. Agregar video/documentos en el futuro = agregar operaciones y un backend.
5. **Backends** (Core)
   - `ImageBackend` — **Magick.NET-Q8-x64** (Apache-2.0): lee/escribe todos los formatos de imagen incluidos HEIC, WebP y AVIF. Se carga perezosamente (la primera vez que se usa).
   - `PdfBackend` — **PDFsharp 6** (MIT) para unir/separar/rotar/imágenes→PDF; **`Windows.Data.Pdf`** (incluido en Windows) para renderizar páginas a imagen. TFM `net10.0-windows10.0.19041.0`.
6. **`JobQueue`** (Core) — ejecuta operaciones en segundo plano con concurrencia = nº de núcleos / 2 (mín. 1). Cada trabajo escribe a un archivo temporal en la misma carpeta y lo renombra al final; si falla o se cancela, borra el temporal.
7. **`JobToast`** (App) — tarjetas de vidrio en la esquina inferior derecha: nombre, barra de progreso, botones "Abrir", "Mostrar en carpeta", "Cancelar". Se desvanecen 4 s después de terminar. Errores quedan hasta cerrarlos.
8. **`Settings`** (Core) — JSON en `%APPDATA%\Mandarina\settings.json`: teclas, atajo, arrancar con Windows (clave `HKCU\...\Run`), última acción.
9. **Bandeja** (App) — ícono con menú: Configuración, Pausar, Salir. La ventana de configuración es simple (mismo estilo vidrio).
10. **`ExplorerSelection`** (App) — para el atajo sin arrastre: obtiene los archivos seleccionados de la ventana del Explorador en primer plano vía `Shell.Application` (COM, tipado con interop, sin `dynamic`).

### Flujo

```
toque Shift durante arrastre → InputHooks → DragSession → WheelWindow.Mostrar(Convertir, x, y)
Explorador → DragEnter(archivos) → Catalog.Para(...) → sectores
DragOver → resaltar sector    Drop → JobQueue.Encolar(op, archivos) → JobToast
```

Si el contenido arrastrado no son archivos (texto, etc.), la rueda muestra "Nada para hacer con esto" y se cierra al soltar.

## 5. Estética: liquid glass

- **Forma**: anillo (radio exterior ~150 px, interior ~48 px) dividido en sectores con separaciones finas; centro con ícono "×". Cada sector: glifo (Segoe Fluent Icons) + etiqueta corta (`JPG`, `Comprimir`).
- **Vidrio**: al abrir la rueda se captura una vez la región de pantalla detrás (BitBlt, ~300×300 px), se reduce a 1/4, se desenfoca (blur gaussiano ~12 px) y se muestra recortada a la forma del anillo. Encima: tinte blanco/negro al 18–30 % según el tema, borde de 1 px con gradiente (brillo arriba-izquierda, tenue abajo), resplandor interno suave y una leve "refracción" (el fondo escalado 1.04× cerca del borde). Como la captura es única por apertura, el costo es mínimo.
- **Etéreo**: aparición con escala 0.86→1 y opacidad 0→1 en 160 ms con curva tipo resorte; sectores aparecen escalonados 12 ms; el sector bajo el cursor se ilumina (tinte más claro + glifo 1.08×) con transición de 90 ms; cierre 110 ms.
- **Tema**: sigue el modo claro/oscuro de Windows; acento = color de acento del sistema en el resaltado.
- Las tarjetas de progreso y la configuración usan el mismo material (en ventanas rectangulares se puede usar el backdrop Acrylic/Mica nativo de Windows 11 vía `DwmSetWindowAttribute`).

## 6. Eficiencia

- Framework-dependent + ReadyToRun: el `.exe` pesa poco; requiere el runtime de .NET 10 Desktop (se instala con el SDK).
- En reposo: solo los hooks (callbacks de microsegundos), sin timers ni sondeo.
- Ventanas creadas una vez y reutilizadas; Magick.NET y PDF se cargan solo al usarse; `GC.Collect` no se fuerza, pero se libera la captura del fondo al cerrar la rueda.
- Rendimiento verificado con un modo `--bench` que mide tiempo de apertura de la rueda y uso de memoria en reposo.

## 7. Errores y límites

- Errores de conversión → tarjeta roja con mensaje claro ("No se pudo leer foto.heic: formato dañado"). Nunca se cierra la app por un error de un trabajo; excepciones no capturadas se registran en `%APPDATA%\Mandarina\log.txt`.
- **Límite conocido**: si la ventana en primer plano corre como administrador, Windows no deja que los hooks de una app normal vean esa entrada (UIPI). Arrastrar desde ventanas elevadas no abrirá la rueda.
- Si la tecla elegida ya la usa otra app como atajo global, se avisa en Configuración.

## 8. Pruebas

- **Unitarias (xUnit, Core)**: nombres de salida y colisiones, `Catalog.Para` por tipo de archivo, máquina de estados `DragSession` (lógica separada de los hooks), ángulo → sector.
- **Integración (Core)**: cada conversión/herramienta sobre archivos de muestra generados en el test (PNG/JPG/WebP/PDF): verifica que el resultado existe, se abre y tiene el formato/tamaño esperado; el original no cambió.
- **Manual (lista de chequeo)**: arrastre desde Explorador y escritorio con cada tecla, cancelar en el centro, varios archivos, atajo sin arrastre, tema claro/oscuro, memoria en reposo.
