# Pendientes

Cosas acordadas pero todavía no hechas.

- [x] **Acciones iguales a las de Tangerine**: Compartir, Flotar y Terminal (hecho).
- [ ] **Portapapeles flotante (picture in picture)**: que el historial pueda quedar como ventanita siempre visible y movible.
- [ ] **Temas personalizables**: elegir y cambiar el aspecto desde Configuración.
- [ ] **Convertir documentos**: DOCX, PDF, ODT, TXT y demás formatos de oficina.
- [ ] **Comprimir y descomprimir**: carpetas y archivos a ZIP, y extraer ZIP, RAR, 7Z y TAR.
- [ ] **Acciones sobre texto seleccionado**: seleccionar un texto o un párrafo en cualquier app y abrir una rueda con cosas para hacer con él.
- [ ] Video y audio (MP4, MP3, etc.).
- [ ] Preferencias de accesibilidad de Windows (transparencia, animaciones, contraste alto).
- [ ] Detalles menores de la revisión de código.

## Acciones iguales a las de Tangerine

La rueda **Acciones** de Tangerine tiene seis opciones: AirDrop, Float, Copy, Move, Copy Path y Terminal. En Windows quedarían así:

| Tangerine | Mandarina | Estado |
|---|---|---|
| AirDrop | **Compartir**: abre el panel de compartir de Windows (Compartir con dispositivos cercanos, Phone Link, mail, apps) | hecho |
| Float | **Flotar**: deja el archivo en una ventanita de vidrio siempre visible, que se puede mover y desde la que se lo vuelve a arrastrar a cualquier lado (un "estante") | hecho |
| Copy | **Copiar a…** | ya está |
| Move | **Mover a…** | ya está |
| Copy Path | **Copiar ruta** | ya está |
| Terminal | **Terminal**: abre Windows Terminal (o PowerShell si no está) en la carpeta del archivo | hecho |

Notas:

- Compartir usa la API de Windows (`DataTransferManager` con `IDataTransferManagerInterop`), así que no hace falta instalar nada.
- Flotar y el portapapeles flotante pueden ser la **misma ventanita**: un estante que guarda archivos (y, en el caso del portapapeles, también textos e imágenes).
- Todas tienen que funcionar con carpetas, con selecciones mezcladas y con archivos que no tienen conversiones, igual que en Tangerine.

## Portapapeles flotante (picture in picture)

Que el historial del portapapeles, además de abrirse con Ctrl+Alt+V y cerrarse al elegir algo, se pueda **dejar flotando** como una ventanita tipo picture in picture.

- Un botón (por ejemplo, un ícono de chinche) en el panel lo convierte en ventanita: más chica, siempre encima de las demás ventanas, y que no se cierra al hacer clic afuera.
- Se mueve arrastrándola desde cualquier parte vacía y se queda donde la dejaste; la próxima vez aparece en el mismo lugar (guardado en `settings.json`).
- Imanes en los bordes y esquinas de la pantalla, como el picture in picture de macOS.
- Lo copiado nuevo aparece en vivo arriba de la lista. Se puede arrastrar cualquier elemento hacia otra app, o hacer clic para pegarlo.
- Un botón para volver al modo normal o cerrarla.
- Mismo vidrio que el resto: seguir `.claude/skills/mandarina-glass/SKILL.md`. El fondo de vidrio se vuelve a capturar al moverla, no en cada cuadro.

## Temas personalizables

Que cada usuario pueda elegir y cambiar el aspecto de la app desde **Configuración**, sin reiniciar.

- Elegir entre temas (por ejemplo: Sistema, Claro, Oscuro y algunas variantes de color) y ver el cambio al instante.
- Color de acento propio (hoy se toma el de Windows, o el naranja mandarina si no hay).
- Que se guarde en `settings.json`, junto al resto de la configuración.

Cómo encaja con lo que ya existe:

- Hoy todos los colores salen de `Palette.Current()` (`src/Mandarina.App/Theme/SystemTheme.cs`) y de `Glass.*` (`src/Mandarina.App/Theme/Glass.cs`), y cada ventana los pide **al abrirse**. Un tema elegido por el usuario puede entrar por ahí sin tocar las ventanas: `Palette.Current()` lee primero la elección guardada y, si es "Sistema", sigue como ahora.
- La variable `MANDARINA_THEME=light|dark` que usan las capturas de diseño ya hace algo parecido; un tema guardado la reemplazaría.
- Respetar las reglas de `.claude/skills/mandarina-glass/SKILL.md`: un solo material, colores con significado, y que funcione con transparencia apagada, animaciones apagadas y contraste alto.

## Convertir documentos

Que la rueda **Convertir** también ofrezca formatos de documento, igual que hoy con las imágenes.

- **Word ↔ PDF**: DOCX, DOC, ODT y RTF → PDF, y PDF → DOCX.
- **Entre documentos**: DOCX ↔ ODT ↔ RTF ↔ TXT ↔ Markdown ↔ HTML.
- **Hojas de cálculo y presentaciones**: XLSX/ODS/CSV → PDF, y PPTX/ODP → PDF o imágenes.

Cómo encaja con lo que ya existe:

- Es agregar un backend nuevo en `src/Mandarina.Core/Backends/` y sus operaciones en `src/Mandarina.Core/Operations/`, igual que `ImageBackend` y `PdfBackend`. La rueda, la cola y los avisos no cambian.
- Hay que agregar los tipos nuevos en `Formats.KindOf` (por ejemplo, `FileKind.Document`).
- El motor más completo para esto es **LibreOffice** en modo sin ventana (`soffice --headless --convert-to`), que habría que instalar aparte. **Pandoc** sirve para los formatos de texto (Markdown, HTML, DOCX), y PDF → DOCX necesita una librería propia o LibreOffice.
- Si falta el programa necesario, la opción aparece igual en la rueda y, al elegirla, se explica qué instalar en vez de fallar en silencio.

## Comprimir y descomprimir

Que la rueda ofrezca **Comprimir en ZIP** al soltar una carpeta (o varios archivos), y **Descomprimir** al soltar un archivo comprimido.

- **Formato: ZIP.** Es el más portable: se abre sin instalar nada en Windows, macOS, Linux, iOS y Android. RAR es un formato cerrado: solo WinRAR puede crearlo y no hay forma libre de generarlo desde la app.
- **Rapidez:** ZIP con el nivel `Fastest` (o `Optimal` como opción) de `System.IO.Compression`, que ya viene en .NET: no suma dependencias y es rapidísimo. Las fotos, videos y PDF ya vienen comprimidos, así que guardarlos sin recomprimir ahorra mucho tiempo.
- **Comprimir:** `Carpeta` → `Carpeta.zip` al lado, sin pisar nada (`OutputPaths` + `SafeWrite`). Varios archivos sueltos → un solo `Archivo.zip`. Con progreso en el aviso.
- **Descomprimir:** ZIP con .NET. RAR, 7Z, TAR y GZ con el `tar.exe` que trae Windows 11 (usa libarchive, así que también lee RAR), sin instalar nada. Se extrae a una carpeta con el nombre del archivo, sin pisar nada, y protegido contra rutas que se escapan de la carpeta (`..\`).
- Encaja igual que el resto: una `ArchiveOperations` en `src/Mandarina.Core/Operations/` y `FileKind.Archive` en `Formats.KindOf`. La rueda, la cola y los avisos no cambian.

## Acciones sobre texto seleccionado

Que la rueda no sirva solo para archivos: si seleccionás un texto o un párrafo en cualquier app, un atajo (o arrastrar la selección y tocar una tecla, como con los archivos) abre una rueda con acciones para ese texto.

- **Formato**: MAYÚSCULAS, minúsculas, Tipo Título, quitar saltos de línea y espacios de más, texto plano sin formato.
- **Usar**: buscar en la web, abrir si es un link, contar palabras y caracteres, copiar limpio, guardar como `.txt` en el escritorio.
- **Mandarlo a otro lado**: Compartir, Flotar en el estante y fijarlo en el historial del portapapeles.
- **Más adelante**: traducir, resumir o corregir (con IA, opcional y configurable).

Cómo encaja:

- Para leer la selección se simula Ctrl+C, se toma el texto del portapapeles y después se restaura lo que había, sin ensuciar el historial. Para arrastrar texto, la rueda tendría que aceptar también `CF_UNICODETEXT` en el drop, no solo archivos.
- Las acciones que cambian el texto lo pegan de vuelta en su lugar (Ctrl+V) o lo dejan copiado.
- Operaciones nuevas en `src/Mandarina.Core/Operations/` que reciben texto en vez de rutas. La rueda y la estética no cambian.

## Otras pendientes

- **Video y audio** con FFmpeg: MP4, MOV, MKV, WebM y GIF; extraer el audio a MP3; comprimir.
- **Accesibilidad**: respetar las preferencias de Windows de transparencia, animaciones y contraste alto, como pide la skill de diseño.
- **Detalles menores de la revisión de código**: avisar "cancelado" si se cierra el selector de carpeta, ocultar los temporales `.mandarina-*` y reinstalar los hooks si Windows los quita.
