# Mandarina Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clon personal de Tangerine para Windows: tocar una tecla durante un arrastre abre una rueda de vidrio para convertir imágenes y PDF.

**Architecture:** `Mandarina.Core` (lógica pura y testeable: formatos, nombres, catálogo, backends, cola, máquina de arrastre, geometría de la rueda) y `Mandarina.App` (WPF: hooks globales, rueda, avisos, bandeja). La App solo traduce entrada/salida de Windows hacia Core.

**Tech Stack:** .NET 10 (`net10.0-windows10.0.19041.0`), WPF + WinForms (solo NotifyIcon), Magick.NET-Q8-x64, PDFsharp 6, Windows.Data.Pdf, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-18-mandarina-design.md`

## Global Constraints

- UI en español. Nombre de la app: "Mandarina".
- El original nunca se modifica; toda salida se escribe a un temporal en la misma carpeta y se renombra al final.
- Nombres: `foto.png` → `foto.jpg`; sufijos `foto (comprimida).jpg`, `foto (50%).png`, `foto (1920px).png`, `foto (rotada).png`, `foto (sin metadatos).jpg`; colisión → `foto (1).jpg`, `foto (2).jpg`.
- PDF → imágenes a 200 DPI; varias páginas van a la carpeta `nombre (páginas)`.
- Calidad de compresión: JPG 75, WebP 75, PNG optimizado sin pérdida.
- Settings en `%APPDATA%\Mandarina\settings.json`; log en `%APPDATA%\Mandarina\log.txt`.
- Teclas por defecto: Shift=Convertir, Ctrl=Herramientas, Alt=Acciones; atajo `Ctrl+Alt+Space`.
- Rueda: radio exterior 150 px, interior 48 px; abrir 160 ms (escala 0.86→1), cierre 110 ms, resaltado 90 ms.
- Reposo: sin timers ni sondeo; Magick/PDF se cargan solo al usarse.

## Review Focus

- Arrastrar texto o un enlace (no archivos) → la rueda dice "Nada para hacer con esto" y se cierra al soltar; nada explota. (Task 7 test manual + `Catalog.For` vacío en Task 2)
- Mezcla de tipos (PNG + PDF) → solo se ofrecen operaciones válidas para todos; acciones siempre. (Task 2: `For_MixedImageAndPdf_OnlyCommonOps`)
- Archivo destino ya existente → nunca se sobrescribe. (Task 1: `For_Collision_AddsCounter`)
- Imagen dañada / extensión mentirosa → trabajo en estado Failed con mensaje, sin temporales sueltos. (Task 4: `FailedJob_LeavesNoTempFiles`)
- Shift tocado sin arrastre, o arrastre corto (< umbral) → no abre rueda. (Task 5 tests)

---

### Task 1: Solución, formatos y nombres de salida

**Files:**
- Create: `Mandarina.sln`, `Directory.Build.props`, `src/Mandarina.Core/Mandarina.Core.csproj`, `src/Mandarina.Core/Formats.cs`, `src/Mandarina.Core/OutputPaths.cs`, `tests/Mandarina.Tests/Mandarina.Tests.csproj`, `tests/Mandarina.Tests/OutputPathsTests.cs`, `.gitignore`

**Interfaces:**
- Produces: `enum FileKind { Image, Pdf, Other }`; `static class Formats { FileKind KindOf(string path); IReadOnlySet<string> ImageExtensions; string Ext(string path) /* minúscula, con punto, .jpeg→.jpg */ }`
- Produces: `static class OutputPaths { string For(string source, string newExt, string? suffix = null); string UniqueFolder(string parentDir, string name); string TempFor(string finalPath) }`

- [ ] Tests: `For_ChangesExtension` (`C:\x\foto.png`,`.jpg` → `C:\x\foto.jpg`), `For_WithSuffix` (→ `foto (comprimida).jpg`), `For_Collision_AddsCounter` (existen `foto.jpg` y `foto (1).jpg` → `foto (2).jpg`), `UniqueFolder_Collision`, `KindOf` para `.JPEG`, `.heic`, `.pdf`, `.txt`.
- [ ] Run `dotnet test` → FAIL (no compila).
- [ ] Implementar. `TempFor` = `<dir>\.mandarina-<guid>.tmp<ext>`.
- [ ] Run `dotnet test` → PASS. Commit.

### Task 2: Operaciones, catálogo y settings

**Files:**
- Create: `src/Mandarina.Core/IOperation.cs`, `Catalog.cs`, `Settings.cs`, `SafeWrite.cs`; `tests/Mandarina.Tests/CatalogTests.cs`, `SettingsTests.cs`

**Interfaces:**
- Produces: `enum WheelKind { Convert, Tools, Actions }`
- Produces: `interface IOperation { string Id {get;} WheelKind Wheel {get;} string Label {get;} string Glyph {get;} bool AppliesTo(IReadOnlyList<string> files); Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<string> files, IProgress<double> progress, CancellationToken ct); }` — devuelve rutas producidas.
- Produces: `sealed class Catalog { Catalog(IEnumerable<IOperation> ops); static Catalog Default {get;} IReadOnlyList<IOperation> For(WheelKind wheel, IReadOnlyList<string> files); IOperation? ById(string id); }`
- Produces: `static class SafeWrite { Task<string> WriteAsync(string finalPath, Func<string, Task> writeTemp, CancellationToken ct); }` — escribe a `OutputPaths.TempFor`, mueve sin sobrescribir (si choca, recalcula con `For`), borra el temporal ante excepción/cancelación.
- Produces: `sealed class Settings { string ConvertKey="Shift"; string ToolsKey="Ctrl"; string ActionsKey="Alt"; string Hotkey="Ctrl+Alt+Space"; bool StartWithWindows; string? LastOperationId; static string DefaultPath; static Settings Load(string path); void Save(string path); }` — Load de archivo ausente o corrupto devuelve defaults.

- [ ] Tests con operaciones falsas: `For_FiltersByWheelAndApplies`, `For_EmptyFiles_ReturnsEmpty`, `For_MixedImageAndPdf_OnlyCommonOps` (con `Catalog.Default`: PNG+PDF en Tools no ofrece "Comprimir"; en Actions ofrece "Copiar ruta"), `Default_PngConvert_ExcludesPng`, `Settings_RoundTrip`, `Settings_CorruptFile_Defaults`, `SafeWrite_Exception_DeletesTemp`.
- [ ] FAIL → implementar (`Catalog.Default` se completa en Task 3; aquí registra las acciones de Alt: `copy-path`, `copy-to`, `move-to` — las dos últimas reciben la carpeta destino por un `Func<string?> pickFolder` estático configurable `Actions.PickFolder`) → PASS. Commit.

### Task 3: Backends de imagen y PDF + operaciones

**Files:**
- Create: `src/Mandarina.Core/Backends/ImageBackend.cs`, `Backends/PdfBackend.cs`, `Operations/ImageOperations.cs`, `Operations/PdfOperations.cs`; `tests/Mandarina.Tests/BackendTests.cs`, `tests/Mandarina.Tests/Samples.cs`

**Interfaces:**
- Produces ids: `img-to-{jpg,png,webp,avif,bmp,tiff,ico,pdf}`, `img-compress`, `img-half`, `img-max1920`, `img-rot-right`, `img-rot-left`, `img-strip`, `img-merge-pdf`, `pdf-to-png`, `pdf-to-jpg`, `pdf-merge`, `pdf-split`, `pdf-rotate`.
- Produces: `static class PdfBackend { void Merge(IReadOnlyList<string> pdfs, string dst); IReadOnlyList<string> Split(string pdf, string outDir); void Rotate(string pdf, string dst, int degrees); void ImagesToPdf(IReadOnlyList<string> images, string dst); Task<IReadOnlyList<string>> RenderAsync(string pdf, string outDirOrFile, string ext, int dpi, CancellationToken ct); }`
- `ImageBackend` envuelve Magick.NET (`MagickImage`, `AutoOrient()` antes de todo; ICO redimensiona a ≤256 px).

- [ ] `Samples` genera en temp: PNG 400×300 con alpha, JPG 3000×2000, PDF de 3 páginas (PDFsharp).
- [ ] Tests: cada `img-to-*` produce archivo legible con el formato correcto y el original intacto (hash igual); `img-half` → 200×150; `img-max1920` sobre 3000×2000 → 1920×1280; `img-rot-right` → 300×400; `img-strip` sin perfil EXIF; `img-merge-pdf` con 2 imágenes → PDF de 2 páginas; `pdf-merge` 3+3 → 6; `pdf-split` → 3 archivos; `pdf-rotate` → páginas con `Rotate==90`; `pdf-to-png` → carpeta `muestra (páginas)` con 3 PNG; PNG con alpha → JPG con fondo blanco.
- [ ] FAIL → implementar → PASS. Commit.

### Task 4: Cola de trabajos

**Files:**
- Create: `src/Mandarina.Core/JobQueue.cs`; `tests/Mandarina.Tests/JobQueueTests.cs`

**Interfaces:**
- Produces: `enum JobState { Queued, Running, Done, Failed, Canceled }`; `sealed class Job { Guid Id; string Title; double Progress; JobState State; string? Error; IReadOnlyList<string> Outputs; void Cancel(); event Action<Job>? Changed; }`; `sealed class JobQueue { JobQueue(int concurrency); Job Enqueue(IOperation op, IReadOnlyList<string> files); event Action<Job>? JobAdded; Task WhenIdle(); }`
- Title: `"{op.Label} · {nombre del primer archivo}"` (+ `" y N más"`).

- [ ] Tests: `Job_Success_Done_WithOutputs`, `Job_Throws_Failed_WithMessage`, `Cancel_SetsCanceled`, `Concurrency_Respected` (concurrencia 1, dos ops que se bloquean: nunca 2 a la vez), `FailedJob_LeavesNoTempFiles` (img-to-jpg sobre un `.png` con texto basura → Failed y la carpeta solo tiene el archivo original).
- [ ] FAIL → implementar con `SemaphoreSlim` → PASS. Commit.

### Task 5: Máquina de arrastre y geometría de la rueda

**Files:**
- Create: `src/Mandarina.Core/Input/DragSession.cs`, `src/Mandarina.Core/WheelGeometry.cs`; `tests/Mandarina.Tests/DragSessionTests.cs`, `WheelGeometryTests.cs`

**Interfaces:**
- Produces: `enum Mod { Shift, Ctrl, Alt, Other }`; `sealed class DragSession { DragSession(int dragDx, int dragDy, Func<Mod, WheelKind?> wheelFor); void MouseDown(int x,int y); void MouseMove(int x,int y); void MouseUp(); void KeyDown(Mod m); void KeyUp(Mod m); bool WheelOpen {get;} void WheelClosed(); event Action<WheelKind,int,int>? WheelRequested; event Action? RepeatRequested; }`
- Produces: `static class WheelGeometry { int? SectorAt(double dx, double dy, int count, double inner, double outer); Point SectorCenter(int index, int count, double radius); }` — `-1` = centro, `null` = fuera, sector 0 centrado arriba, sentido horario.

- [ ] Tests: `Tap_WhileDragging_RequestsWheel` (down, move 10px, KeyDown/Up Shift → Convert en la posición actual); `Tap_WithoutDrag_Nothing`; `ShortDrag_BelowThreshold_Nothing`; `ChordWithOtherKey_NotATap` (Shift down, Other down/up, Shift up → nada); `SameTapWhileOpen_Repeat`; `OtherTapWhileOpen_SwitchesWheel`; `MouseUp_Resets`. Geometría: arriba→0, derecha con 4 sectores→1, r<inner→-1, r>outer→null.
- [ ] FAIL → implementar → PASS. Commit.

### Task 6: App: arranque, hooks, bandeja

**Files:**
- Create: `src/Mandarina.App/Mandarina.App.csproj`, `App.xaml`, `App.xaml.cs`, `Native/Win32.cs`, `Input/InputHooks.cs`, `Shell/Tray.cs`, `Shell/Startup.cs`, `Assets/mandarina.ico`

**Interfaces:**
- Consumes: `DragSession`, `Settings`, `Catalog`, `JobQueue`.
- Produces: `sealed class InputHooks : IDisposable { InputHooks(DragSession session); void Start(); }` — hilo dedicado con `SetWindowsHookEx(WH_MOUSE_LL/WH_KEYBOARD_LL)` + `GetMessage`; callbacks solo llaman a `DragSession` y siempre `CallNextHookEx`. Eventos se marshalan a UI con `Dispatcher.BeginInvoke`.
- Instancia única con `Mutex("Mandarina.SingleInstance")`. Excepciones no capturadas → log.
- `Startup.Set(bool)` escribe/borra `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Mandarina`.

- [ ] `dotnet build` limpio; arrancar, ver ícono en bandeja; menú Configuración / Pausar / Salir. Commit.

### Task 7: Rueda liquid glass

**Files:**
- Create: `src/Mandarina.App/Wheel/WheelWindow.xaml(.cs)`, `Wheel/GlassCapture.cs`, `Wheel/SectorShapes.cs`, `Theme/Glass.xaml`, `Theme/SystemTheme.cs`

**Interfaces:**
- Produces: `WheelWindow { void ShowAt(WheelKind wheel, int x, int y); void ShowForFiles(WheelKind wheel, IReadOnlyList<string> files, int x, int y) /* modo teclado */; event Action<IOperation, IReadOnlyList<string>>? OperationChosen; }`
- `GlassCapture.Capture(Rect screenRect) → BitmapSource` (BitBlt de la región, reducida 1/4, blur 12 px vía `BlurEffect` renderizado una vez a `RenderTargetBitmap`).
- Ventana única reutilizada, `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW|WS_EX_TOPMOST` en modo arrastre, `AllowDrop`. DPI per-monitor v2.
- DragEnter: `DataFormats.FileDrop` → `Catalog.For`; vacío o sin archivos → texto "Nada para hacer con esto". DragOver → `WheelGeometry.SectorAt` → resaltar. Drop → `OperationChosen`. Mouse up fuera / centro → cerrar.
- Repetir: `RepeatRequested` reemplaza los sectores por uno "Repetir: {Label}" de `Settings.LastOperationId`.

- [ ] Prueba manual: arrastrar PNG del Explorador + Shift → rueda con JPG/WebP/…; soltar sobre JPG → aparece `.jpg`. Ctrl y Alt cambian rueda. Centro cancela. Tema claro/oscuro. Commit.

### Task 8: Avisos de progreso, configuración, atajo sin arrastre, verificación

**Files:**
- Create: `src/Mandarina.App/Jobs/JobToastWindow.xaml(.cs)`, `Settings/SettingsWindow.xaml(.cs)`, `Shell/HotkeyManager.cs`, `Shell/ExplorerSelection.cs`, `README.md`

**Interfaces:**
- `JobToastWindow` apila tarjetas abajo a la derecha (Mica/Acrylic vía `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)`), botones Abrir / Mostrar en carpeta (`explorer /select,`) / Cancelar; se va 4 s después de Done; Failed queda.
- `HotkeyManager.Register(string combo, Action)` con `RegisterHotKey`; si falla, avisar en Configuración.
- `ExplorerSelection.Get() → IReadOnlyList<string>` vía COM `Shell.Application` tipado (`SHDocVw.ShellWindows` por interop declarado a mano), ventana coincidente con `GetForegroundWindow`.
- `SettingsWindow`: tres combos de tecla, atajo, "Arrancar con Windows".
- `--bench`: abre/cierra la rueda 20 veces fuera de pantalla e imprime ms promedio y working set.

- [ ] `dotnet test` todo verde; `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true`; correr `--bench` (< 100 ms, < 60 MB en reposo); README con uso y build. Commit.
