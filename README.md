<p align="center"><img src="src/Palta.App/Assets/palta.png" width="128" alt="Palta"></p>

# Palta

Clon personal de [Tangerine](https://tangerineformac.com/) para Windows 11. Arrastrás un archivo, **tocás una tecla** y aparece una rueda de vidrio: soltás el archivo sobre una opción y listo. Todo local.

Antes se llamaba Mandarina. Si ya la tenías instalada, al abrir Palta por primera vez se pasan solos la configuración, el historial del portapapeles, el arranque con Windows y el acceso directo.

| Tecla (tocar mientras arrastrás) | Rueda | Qué ofrece |
|---|---|---|
| **Shift** | Convertir | PNG ↔ JPG, WebP, AVIF, BMP, TIFF, ICO, PDF · PDF → PNG/JPG |
| **Ctrl** | Herramientas | comprimir, 50 %, máx. 1920 px, rotar, quitar metadatos, unir en PDF · PDF: unir, separar, rotar |
| **Alt** | Acciones | compartir, flotar (estante movible), copiar a…, mover a…, copiar ruta, terminal |

- La rueda queda abierta aunque sueltes la tecla. Tocar otra tecla cambia de rueda; tocar **la misma** repite la última acción.
- Soltar en el centro (o fuera) cancela. El original nunca se modifica: el resultado aparece al lado (`foto.jpg`, `foto (comprimida).jpg`, `foto (1).jpg`…).
- **Sin arrastrar:** seleccioná archivos en el Explorador (o copialos) y apretá **Ctrl+Alt+Espacio**. Flechas eligen, Enter ejecuta, Tab cambia de rueda, 1–9 directo, Esc cierra.
- **Portapapeles:** **Ctrl+Alt+V** abre el historial de lo que copiaste (textos, imágenes y archivos), con búsqueda y elementos fijados. Con la chinche (o **Ctrl+T**), o arrastrándolo, queda **flotando** encima de todo: lo movés a cualquier pantalla y no se cierra al cambiar de ventana.
- **Temas:** **Windows** (por defecto: oscuro, opaco y con las notificaciones del sistema) o **Liquid glass** (vidrio translúcido). Se cambia en Configuración o con clic derecho en el ícono de la bandeja → **Tema**.
- Configuración y pausa desde el ícono de la bandeja.

Formatos de entrada: PNG, JPG, WebP, HEIC, AVIF, BMP, GIF, TIFF, ICO y PDF. Para comprimir sirve cualquier archivo o carpeta, y se descomprimen ZIP, RAR, 7Z, TAR y GZ.

## Instalar

Por ahora no hay instalador: Palta se compila desde el código, en un par de minutos. Hace falta Windows 10 (versión 2004 o posterior) o Windows 11, de 64 bits.

1. **Instalá Git y el SDK de .NET 10.** En PowerShell o en la Terminal:

   ```powershell
   winget install Git.Git
   winget install Microsoft.DotNet.SDK.10
   ```

   Cerrá y volvé a abrir la terminal para que encuentre los comandos nuevos.

2. **Descargá el código:**

   ```powershell
   git clone https://github.com/nicosagasti/palta.git
   cd palta
   ```

3. **Compilá Palta en su carpeta de programas:**

   ```powershell
   dotnet publish src/Palta.App -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o "$env:LOCALAPPDATA\Programs\Palta"
   ```

4. **Abrila:**

   ```powershell
   & "$env:LOCALAPPDATA\Programs\Palta\Palta.exe"
   ```

   No se abre ninguna ventana: Palta queda en segundo plano, con su ícono en la bandeja del sistema (junto al reloj; si no lo ves, está en la flechita ^). Arrastrá un archivo y tocá Shift para probarla.

5. **Hacé que arranque sola** (opcional): clic derecho en el ícono de la bandeja → **Configuración…** → activá **Arrancar con Windows**. Ahí también podés crear un **acceso directo en el escritorio**.

### Actualizar

Desde la carpeta `palta`, cerrá Palta (clic derecho en el ícono de la bandeja → **Salir**) y repetí la compilación:

```powershell
git pull
dotnet publish src/Palta.App -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o "$env:LOCALAPPDATA\Programs\Palta"
```

La configuración y el historial del portapapeles se conservan.

### Desinstalar

1. En **Configuración**, desactivá **Arrancar con Windows** y el acceso directo del escritorio.
2. Cerrá Palta (clic derecho en el ícono de la bandeja → **Salir**).
3. Borrá la carpeta `%LOCALAPPDATA%\Programs\Palta`. Para borrar también la configuración y el historial del portapapeles, borrá `%APPDATA%\Palta` y `%LOCALAPPDATA%\Palta`.

## Compilar para desarrollar

```bash
dotnet test
dotnet publish src/Palta.App -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o publish
```

`publish/Palta.exe` es la app.

## Herramientas de diagnóstico

- `Palta.exe --bench` — tiempo de apertura de la rueda y memoria en reposo.
- `Palta.exe --snapshot carpeta` — guarda cómo se ve cada rueda en PNG.
- `PALTA_DEBUG=1` — registra el flujo de arrastre en `%APPDATA%\Palta\log.txt`.
- `--gpu` — usa la GPU para dibujar (por defecto es por software: gasta ~80 MB menos y es igual de rápido).

## Límites conocidos

- Si la ventana de origen corre como administrador, Windows no deja que Palta vea esa entrada: la rueda no se abre.
- Alt suelta puede activar el menú de algunas apps; si molesta, cambiá la tecla en Configuración.

## Estructura

- `src/Palta.Core` — lógica sin interfaz: formatos, nombres de salida, catálogo de operaciones, backends (Magick.NET, PDFsharp, `Windows.Data.Pdf`), cola de trabajos, máquina de arrastre. Probada con xUnit.
- `src/Palta.App` — WPF: hooks globales, rueda de vidrio, avisos, bandeja, configuración.
- `docs/superpowers/` — especificación y plan.
