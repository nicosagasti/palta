# Pendientes

Cosas acordadas pero todavía no hechas.

- [x] **Acciones iguales a las de Tangerine**: Compartir, Flotar y Terminal (hecho).
- [x] **Portapapeles flotante (picture in picture)**: que el historial pueda quedar como ventanita siempre visible y movible (hecho: chinche o Ctrl+T, o arrastrarlo; recuerda dónde quedó).
- [x] **Tema Windows y Liquid glass**: tema por defecto como Windows 11 (oscuro, opaco y con las notificaciones del sistema) y Liquid glass para el vidrio de antes; se elige en Configuración o con clic derecho en el ícono de la bandeja → Tema (hecho).
- [ ] **Temas personalizables**: elegir y cambiar el aspecto desde Configuración. Ya se elige entre Windows y Liquid glass; falta el color de acento propio y más variantes.
- [ ] **Convertir documentos**: DOCX, PDF, ODT, TXT y demás formatos de oficina.
- [x] **Comprimir y descomprimir**: carpetas y archivos a ZIP, y extraer ZIP, RAR, 7Z y TAR (hecho, en Herramientas).
- [ ] **Acciones sobre texto seleccionado**: seleccionar un texto o un párrafo en cualquier app y abrir una rueda con cosas para hacer con él.
- [ ] **Muchos archivos a la vez**: definir qué ofrece la rueda cuando se sueltan muchos archivos, sean todos del mismo tipo o de tipos distintos.
- [ ] **Ruedas personalizables**: que cada usuario elija qué opciones ve en Shift, Ctrl y Alt para cada tipo de archivo, a partir de una lista predefinida.
- [x] **Límite del historial del portapapeles**: por días (30 por defecto), cantidad y espacio de imágenes, desde Configuración; lo fijado no se borra (hecho).
- [ ] Video y audio (MP4, MP3, etc.).
- [ ] Preferencias de accesibilidad de Windows (transparencia, animaciones, contraste alto).
- [ ] Detalles menores de la revisión de código: falta reinstalar los hooks si Windows los quita (cancelado, temporales ocultos y carpetas con nombre de imagen ya están).

## Acciones iguales a las de Tangerine

La rueda **Acciones** de Tangerine tiene seis opciones: AirDrop, Float, Copy, Move, Copy Path y Terminal. En Windows quedarían así:

| Tangerine | Palta | Estado |
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
- Mismo vidrio que el resto: seguir `.claude/skills/palta-glass/SKILL.md`. El fondo de vidrio se vuelve a capturar al moverla, no en cada cuadro.

**Estado: hecho.**

- Chinche arriba a la derecha del panel (o **Ctrl+T**) para dejarlo flotando; arrastrarlo desde cualquier parte vacía también lo deja flotando.
- Flotando: queda encima de todo, no se cierra al hacer clic en otra ventana, se mueve entre pantallas y aparece una ✕ para cerrarlo (o Esc).
- Hacer clic o Enter en un elemento lo pega en la última ventana que usaste, y el panel se queda.
- Imanes a 12 px de los bordes de la pantalla. La posición y si estaba flotando se guardan en `settings.json` (`ClipboardFloating`, `ClipboardFloatX`, `ClipboardFloatY`).
- Lo que no se hizo: la versión más chica de la ventanita (por ahora flota con el mismo tamaño del panel).

## Temas personalizables

Que cada usuario pueda elegir y cambiar el aspecto de la app desde **Configuración**, sin reiniciar.

- Elegir entre temas (por ejemplo: Sistema, Claro, Oscuro y algunas variantes de color) y ver el cambio al instante.
- Color de acento propio (hoy se toma el de Windows, o el verde palta si no hay).
- Que se guarde en `settings.json`, junto al resto de la configuración.

Cómo encaja con lo que ya existe:

- Hoy todos los colores salen de `Palette.Current()` (`src/Palta.App/Theme/SystemTheme.cs`) y de `Glass.*` (`src/Palta.App/Theme/Glass.cs`), y cada ventana los pide **al abrirse**. Un tema elegido por el usuario puede entrar por ahí sin tocar las ventanas: `Palette.Current()` lee primero la elección guardada y, si es "Sistema", sigue como ahora.
- La variable `PALTA_THEME=light|dark` que usan las capturas de diseño ya hace algo parecido; un tema guardado la reemplazaría.
- Respetar las reglas de `.claude/skills/palta-glass/SKILL.md`: un solo material, colores con significado, y que funcione con transparencia apagada, animaciones apagadas y contraste alto.

**Estado: primera parte hecha.**

- Dos temas, en `settings.json` como `"Theme": "windows"` (por defecto) o `"glass"`:
  - **Windows**: como Windows 11. Superficies oscuras y opacas (sin capturar ni desenfocar el fondo), bordes finitos, esquinas de 8 px y las **notificaciones del sistema** (con barra de progreso, Cancelar, Abrir y Mostrar en carpeta). Además, la rueda abre casi tres veces más rápido.
  - **Liquid glass**: el vidrio de siempre, con los avisos propios estilo macOS.
- Se cambia desde **Configuración → Tema** o con **clic derecho en el ícono de la bandeja → Tema**, y se aplica al toque (cada ventana lo toma al abrirse).
- Cómo está hecho: `Theme/Look.cs` guarda el tema; `Glass.*` y `GlassCapture.Backdrop` devuelven el material opaco cuando no es Liquid glass, así las ventanas no cambiaron. Las notificaciones de Windows están en `Jobs/WindowsToasts.cs`.
- Falta: color de acento propio, variante clara del tema Windows y las variantes de color.

## Convertir documentos

Que la rueda **Convertir** también ofrezca formatos de documento, igual que hoy con las imágenes.

- **Word ↔ PDF**: DOCX, DOC, ODT y RTF → PDF, y PDF → DOCX.
- **Entre documentos**: DOCX ↔ ODT ↔ RTF ↔ TXT ↔ Markdown ↔ HTML.
- **Hojas de cálculo y presentaciones**: XLSX/ODS/CSV → PDF, y PPTX/ODP → PDF o imágenes.

Cómo encaja con lo que ya existe:

- Es agregar un backend nuevo en `src/Palta.Core/Backends/` y sus operaciones en `src/Palta.Core/Operations/`, igual que `ImageBackend` y `PdfBackend`. La rueda, la cola y los avisos no cambian.
- Hay que agregar los tipos nuevos en `Formats.KindOf` (por ejemplo, `FileKind.Document`).
- El motor más completo para esto es **LibreOffice** en modo sin ventana (`soffice --headless --convert-to`), que habría que instalar aparte. **Pandoc** sirve para los formatos de texto (Markdown, HTML, DOCX), y PDF → DOCX necesita una librería propia o LibreOffice.
- Si falta el programa necesario, la opción aparece igual en la rueda y, al elegirla, se explica qué instalar en vez de fallar en silencio.

## Comprimir y descomprimir

Que la rueda ofrezca **Comprimir en ZIP** al soltar una carpeta (o varios archivos), y **Descomprimir** al soltar un archivo comprimido.

- **Formato: ZIP.** Es el más portable: se abre sin instalar nada en Windows, macOS, Linux, iOS y Android. RAR es un formato cerrado: solo WinRAR puede crearlo y no hay forma libre de generarlo desde la app.
- **Rapidez:** ZIP con el nivel `Fastest` (o `Optimal` como opción) de `System.IO.Compression`, que ya viene en .NET: no suma dependencias y es rapidísimo. Las fotos, videos y PDF ya vienen comprimidos, así que guardarlos sin recomprimir ahorra mucho tiempo.
- **Comprimir:** `Carpeta` → `Carpeta.zip` al lado, sin pisar nada (`OutputPaths` + `SafeWrite`). Varios archivos sueltos → un solo `Archivo.zip`. Con progreso en el aviso.
- **Descomprimir:** ZIP con .NET. RAR, 7Z, TAR y GZ con el `tar.exe` que trae Windows 11 (usa libarchive, así que también lee RAR), sin instalar nada. Se extrae a una carpeta con el nombre del archivo, sin pisar nada, y protegido contra rutas que se escapan de la carpeta (`..\`).
- Encaja igual que el resto: una `ArchiveOperations` en `src/Palta.Core/Operations/` y `FileKind.Archive` en `Formats.KindOf`. La rueda, la cola y los avisos no cambian.

## Acciones sobre texto seleccionado

Que la rueda no sirva solo para archivos: si seleccionás un texto o un párrafo en cualquier app, un atajo (o arrastrar la selección y tocar una tecla, como con los archivos) abre una rueda con acciones para ese texto.

- **Formato**: MAYÚSCULAS, minúsculas, Tipo Título, quitar saltos de línea y espacios de más, texto plano sin formato.
- **Usar**: buscar en la web, abrir si es un link, contar palabras y caracteres, copiar limpio, guardar como `.txt` en el escritorio.
- **Mandarlo a otro lado**: Compartir, Flotar en el estante y fijarlo en el historial del portapapeles.
- **Más adelante**: traducir, resumir o corregir (con IA, opcional y configurable).

Cómo encaja:

- Para leer la selección se simula Ctrl+C, se toma el texto del portapapeles y después se restaura lo que había, sin ensuciar el historial. Para arrastrar texto, la rueda tendría que aceptar también `CF_UNICODETEXT` en el drop, no solo archivos.
- Las acciones que cambian el texto lo pegan de vuelta en su lugar (Ctrl+V) o lo dejan copiado.
- Operaciones nuevas en `src/Palta.Core/Operations/` que reciben texto en vez de rutas. La rueda y la estética no cambian.

## Muchos archivos a la vez

**Cómo funciona hoy:** una opción aparece solo si sirve para *todos* los archivos soltados (`Operation.PerFile` pide que todos sean del mismo `FileKind`). Por eso:

- **Todos del mismo tipo** (por ejemplo, 40 PNG): aparecen las mismas opciones que con uno solo, y cada archivo se procesa por separado, uno detrás del otro. Con 2 o más se suman "Unir en PDF" (imágenes) y "Unir PDFs" (PDF).
- **Tipos distintos** (PNG + PDF + DOCX): Convertir y Herramientas quedan vacías ("Nada para hacer con esto"). Solo funcionan las Acciones (Compartir, Flotar, Copiar, Mover, Copiar ruta, Terminal). Ni siquiera se pueden unir imágenes y PDF en un solo PDF.

**Qué falta definir y hacer:**

- **Selección mezclada**: mostrar las opciones que sirven para *alguno* de los archivos e indicar a cuántos se aplican (por ejemplo, "JPG · 12 de 15"). Los que no se pueden convertir quedan como están y el aviso final lo dice ("12 convertidos, 3 salteados").
- **Agrupar por tipo**: si hay imágenes y PDF, que la rueda muestre las opciones de los dos grupos, cada una aplicada a su grupo.
- **Unir mezclados**: "Unir en PDF" con imágenes y PDF juntos, en el orden en que estaban seleccionados.
- **Dónde van los resultados**: con muchos archivos, guardarlos todos en una carpeta nueva ("Convertidos a JPG") en vez de desparramarlos al lado de cada original. Se puede elegir desde Configuración.
- **Rapidez**: procesar varios en paralelo (tantos como núcleos, con un límite de memoria para Magick.NET), con una sola barra de progreso total en el aviso.
- **Muchísimos archivos** (cientos o miles): que la rueda abra igual de rápido (no inspeccionar cada archivo antes de mostrarla), poder cancelar desde el aviso y no llenar la pantalla de avisos: uno solo con el total.
- **Errores**: si falla uno, seguir con el resto y al final mostrar cuáles fallaron y por qué, con una forma de reintentar solo esos.
- **Carpetas soltadas junto con archivos**: decidir si se entra a la carpeta y se procesan los archivos de adentro, o si se ofrecen solo las acciones (y Comprimir en ZIP cuando exista).
- La cápsula de abajo de la rueda debería decir el resumen: "15 archivos · 12 imágenes, 3 PDF".

## Ruedas personalizables

Que cada usuario pueda elegir, desde **Configuración**, qué opciones aparecen en cada rueda (**Shift** = Convertir, **Ctrl** = Herramientas, **Alt** = Acciones) y para cada tipo de archivo (imágenes, PDF y los que se sumen después).

- **Solo de una lista predefinida.** Para cada rueda y tipo de archivo se puede sacar, volver a poner y reordenar opciones del catálogo de Palta (`Catalog.cs`). No se pueden crear acciones propias, como comandos o scripts.
- **Pedir opciones nuevas:** si alguien quiere una opción que no está en la lista, la pide con un *issue* en GitHub (github.com/nicosagasti/palta/issues). Configuración tiene un link "¿Falta algo? Pedilo" que abre el formulario del issue.
- **Por defecto, las de ahora.** Sin tocar nada, cada rueda muestra lo mismo que hoy. Hay un botón "Restablecer" por rueda.
- **La rueda se adapta a la cantidad.** Los sectores se reparten según cuántas opciones haya (`WheelGeometry` ya divide los 360° por la cantidad). Con pocas opciones quedan sectores más anchos y los íconos se reacomodan sin superponerse.
- **Máximo: 8 opciones por rueda.** Es lo más que muestra hoy una rueda: Convertir de imágenes tiene 8 formatos, y oculta el formato propio del archivo. Más de 8 deja los sectores demasiado finos para apuntar rápido. Mínimo 1: si se sacan todas, la rueda muestra "Nada para hacer con esto", como ahora.
- **Atajos 1–9:** se renumeran según el orden elegido.
- Se guarda en `settings.json` (por ejemplo, `"wheels": { "convert.image": ["img-to-jpg", "img-to-png", ...] }`, con los ids de las operaciones). Un id que ya no exista se ignora sin romper nada.
- La pantalla de Configuración sigue la skill `palta-glass`: lista con interruptores y arrastre para reordenar, vista previa en vivo de la rueda y camino completo de teclado.

## Límite del historial del portapapeles

**Antes:** se guardaban los últimos 50 elementos. Los fijados no cuentan para el límite y no se borran nunca. Lo que copia un administrador de contraseñas ya se ignora, porque marca el contenido para que no quede en ningún historial.

**Decisión: con límite, pero configurable.** Infinito no conviene:

- **Privacidad:** cualquier cosa copiada (un número de tarjeta, una dirección, una contraseña de una app que no la marca) quedaría guardada para siempre en el disco.
- **Espacio:** los textos ocupan poco, pero las imágenes pesan. Mil capturas de pantalla son cientos de MB.
- **Rapidez:** el historial se carga entero en memoria al arrancar. Con miles de elementos, el panel tardaría más en abrir y buscar, y Palta se pasaría del presupuesto de < 60 MB en reposo.
- **Utilidad:** casi siempre se vuelve a pegar algo de hace minutos u horas. Lo que se quiere conservar se fija.

**Hecho** (en Configuración, debajo de "Guardar historial del portapapeles"):

- **Borrar después de**: 1 día, 7 días, 30 días (por defecto), 90 días, 1 año o nunca. Cuenta desde la última vez que se copió o se pegó.
- **Guardar hasta**: 50, 200, 500 (por defecto) o 1000 elementos, o sin límite.
- **Espacio de imágenes**: 100 MB, 300 MB (por defecto), 1 GB, 5 GB o sin límite. Si se pasa, se van las imágenes más viejas; la última copiada se queda siempre.
- Lo que se cumpla primero manda. Los **fijados** no cuentan para ningún límite y no vencen.
- Muestra cuántos elementos hay y cuánto ocupan las imágenes, y tiene un botón **Borrar historial…** que pide confirmación y deja lo fijado.
- Lo vencido se borra al arrancar, al copiar algo nuevo y al abrir el panel. Se guarda en `settings.json` (`ClipboardKeepDays`, `ClipboardMaxItems`, `ClipboardMaxImageMegabytes`; 0 = sin límite).

## Otras pendientes

- **Video y audio** con FFmpeg: MP4, MOV, MKV, WebM y GIF; extraer el audio a MP3; comprimir.
- **Accesibilidad**: respetar las preferencias de Windows de transparencia, animaciones y contraste alto, como pide la skill de diseño.
- **Detalles menores de la revisión de código**: reinstalar los hooks si Windows los quita. Ya están: avisar "Cancelado" al cerrar el selector de carpeta, temporales `.palta-*` ocultos y carpetas con nombre de imagen sin opciones de imagen.
