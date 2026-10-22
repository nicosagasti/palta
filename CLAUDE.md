# Palta

Clon de Tangerine (tangerineformac.com) solo para Windows: arrastrás un archivo, tocás una tecla (Shift = Convertir, Ctrl = Herramientas, Alt = Acciones) y aparece una rueda radial de vidrio. También tiene historial del portapapeles (Ctrl+Alt+V). Ver `README.md`; lo pendiente está en `ROADMAP.md`.

Antes se llamaba **Mandarina**. La carpeta local (`Proyectos\Mandarina`) conserva el nombre viejo; el repo de GitHub ya es `palta`. `Shell/Migration.cs` muda los datos, el arranque con Windows y el acceso directo de Mandarina a Palta.

Stack: .NET 10, WPF (+ WinForms solo para el ícono de la bandeja), Magick.NET (imágenes), PDFsharp + Windows.Data.Pdf (PDF), xUnit.

- `src/Palta.Core`: lógica sin interfaz. Operaciones en `Operations/`, motores en `Backends/`, registro en `Catalog.cs`, tipos de archivo en `Formats.cs`. Las salidas nunca pisan nada (`OutputPaths` + `SafeWrite`).
- `src/Palta.App`: rueda (`Wheel/`), vidrio (`Theme/Glass.cs`, `Theme/SystemTheme.cs`), avisos (`Jobs/JobToast.cs`), estante de Flotar (`Shelf/`), portapapeles (`Clipboard/`, namespace `Palta.App.Clips`), integración con Windows (`Shell/`, `Native/Win32.cs`).
- `tests/Palta.Tests`: tests de Core.

## Compilar, testear, publicar

```
dotnet test
dotnet publish src/Palta.App -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o publish
```

Antes de publicar cerrá Palta (`Stop-Process -Name Palta` o Salir en la bandeja): si está abierta bloquea los archivos. Después volvé a abrir `publish\Palta.exe`.

## Diagnóstico

- `Palta.exe --bench`: tiempo de apertura de la rueda y memoria. Presupuesto: < 100 ms, < 60 MB, 0 % CPU en reposo.
- `Palta.exe --snapshot carpeta`: PNG de la rueda, el portapapeles, los avisos y el estante. Mirarlos con Read.
- Variables: `PALTA_LOOK=windows|glass` (tema de Palta), `PALTA_THEME=light|dark` (claro/oscuro), `PALTA_DEBUG=1`, `PALTA_CLIPBOARD_DIR`.
- `Palta.exe --toast-test`: manda dos notificaciones de Windows de prueba y lista las que registró el sistema.
- Antes de dar por buena una interfaz, sacar `--snapshot` con `PALTA_LOOK=windows` y con `PALTA_LOOK=glass`.
- **Nunca sacar capturas de pantalla completa del escritorio** (una vez salió la cámara del usuario). Usar `--snapshot`.

## Reglas

- Toda interfaz sigue `.claude/skills/palta-glass/SKILL.md`. Colores solo de `Glass.*` / `Palette`.
- **No cambiar la estética de la rueda** (vidrio oscuro): al usuario le encanta. Le gustan la sombra y el borde de luz, pero no la "segunda circunferencia" que dejaba el final de la sombra: `Glass.DiskShadow` cae con smoothstep para que no haya un corte visible.
- Dos temas (`Theme/Look.cs`): **Windows** (por defecto: oscuro, opaco, esquinas de 8 px, notificaciones del sistema en `Jobs/WindowsToasts.cs`) y **Liquid glass** (el vidrio). Se cambian en Configuración y con clic derecho en la bandeja → Tema. Las ventanas no preguntan por el tema: `Glass.*` y `GlassCapture.Backdrop` ya devuelven el material que toca.
- En Liquid glass, los avisos tienen el estilo de notificación de macOS Tahoe: no volver a cápsulas ni diseños tipo iOS viejo.
- Textos de la UI en castellano rioplatense, cortos.
- Todas las ventanas son de herramienta (`WS_EX_TOOLWINDOW`) para no aparecer en Alt+Tab, salvo Configuración mientras está abierta.
- En los drops nunca devolver `Move` como efecto (el origen borraría el archivo).
- Las operaciones `Quiet` (Compartir, Flotar, Terminal) solo avisan si fallan.
- El usuario delega las decisiones: preguntar solo si es imprescindible.

## Git

- Repo público: github.com/nicosagasti/palta. El mail tiene que quedar oculto: los commits usan `126510283+nicosagasti@users.noreply.github.com` (ya configurado en el repo).
- Terminar los mensajes de commit con la línea `Co-Authored-By`.
- Preguntar antes de cada push.

## Trampas conocidas

- Bash se come las barras invertidas en sed/perl: para escapes `\u` y similares usar Edit/Write.
- PowerShell 5 corrompe UTF-8 con `Set-Content`/`-Raw`: usar `[IO.File]` con `UTF8Encoding($false)`.
- `Geometry.Parse` y los formatos numéricos con coma decimal regional: usar `FormattableString.Invariant`.
- Los glifos de Segoe Fluent Icons (zona PUA) van como caracteres literales en el código.
- No hay Python instalado.
- WPF renderiza por software (`RenderMode.SoftwareOnly`; `--gpu` lo desactiva): evitar efectos que solo andan bien con GPU.
- Los scripts sueltos que usan `DoDragDrop` necesitan un hilo STA.
