# Mandarina

Clon de Tangerine (tangerineformac.com) solo para Windows: arrastrás un archivo, tocás una tecla (Shift = Convertir, Ctrl = Herramientas, Alt = Acciones) y aparece una rueda radial de vidrio. También tiene historial del portapapeles (Ctrl+Alt+V). Ver `README.md`; lo pendiente está en `ROADMAP.md`.

Stack: .NET 10, WPF (+ WinForms solo para el ícono de la bandeja), Magick.NET (imágenes), PDFsharp + Windows.Data.Pdf (PDF), xUnit.

- `src/Mandarina.Core`: lógica sin interfaz. Operaciones en `Operations/`, motores en `Backends/`, registro en `Catalog.cs`, tipos de archivo en `Formats.cs`. Las salidas nunca pisan nada (`OutputPaths` + `SafeWrite`).
- `src/Mandarina.App`: rueda (`Wheel/`), vidrio (`Theme/Glass.cs`, `Theme/SystemTheme.cs`), avisos (`Jobs/JobToast.cs`), estante de Flotar (`Shelf/`), portapapeles (`Clipboard/`, namespace `Mandarina.App.Clips`), integración con Windows (`Shell/`, `Native/Win32.cs`).
- `tests/Mandarina.Tests`: tests de Core.

## Compilar, testear, publicar

```
dotnet test
dotnet publish src/Mandarina.App -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o publish
```

Antes de publicar cerrá Mandarina (`Stop-Process -Name Mandarina` o Salir en la bandeja): si está abierta bloquea los archivos. Después volvé a abrir `publish\Mandarina.exe`.

## Diagnóstico

- `Mandarina.exe --bench`: tiempo de apertura de la rueda y memoria. Presupuesto: < 100 ms, < 60 MB, 0 % CPU en reposo.
- `Mandarina.exe --snapshot carpeta`: PNG de la rueda, el portapapeles, los avisos y el estante. Mirarlos con Read.
- Variables: `MANDARINA_THEME=light|dark`, `MANDARINA_DEBUG=1`, `MANDARINA_CLIPBOARD_DIR`.
- **Nunca sacar capturas de pantalla completa del escritorio** (una vez salió la cámara del usuario). Usar `--snapshot`.

## Reglas

- Toda interfaz sigue `.claude/skills/mandarina-glass/SKILL.md`. Colores solo de `Glass.*` / `Palette`.
- **No cambiar la estética de la rueda** (vidrio oscuro): al usuario le encanta. La rueda va **sin halo** (sin la sombra `DiskShadow` alrededor), pero conserva el borde de luz: lo eligió el usuario.
- Los avisos tienen el estilo de notificación de macOS Tahoe: no volver a cápsulas ni diseños tipo iOS viejo.
- Textos de la UI en castellano rioplatense, cortos.
- Todas las ventanas son de herramienta (`WS_EX_TOOLWINDOW`) para no aparecer en Alt+Tab, salvo Configuración mientras está abierta.
- En los drops nunca devolver `Move` como efecto (el origen borraría el archivo).
- Las operaciones `Quiet` (Compartir, Flotar, Terminal) solo avisan si fallan.
- El usuario delega las decisiones: preguntar solo si es imprescindible.

## Git

- Repo público: github.com/nicosagasti/tangerine. El mail tiene que quedar oculto: los commits usan `126510283+nicosagasti@users.noreply.github.com` (ya configurado en el repo).
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
