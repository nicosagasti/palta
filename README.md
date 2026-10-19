# Mandarina 🍊

Clon personal de [Tangerine](https://tangerineformac.com/) para Windows 11. Arrastrás un archivo, **tocás una tecla** y aparece una rueda de vidrio: soltás el archivo sobre una opción y listo. Todo local.

| Tecla (tocar mientras arrastrás) | Rueda | Qué ofrece |
|---|---|---|
| **Shift** | Convertir | PNG ↔ JPG, WebP, AVIF, BMP, TIFF, ICO, PDF · PDF → PNG/JPG |
| **Ctrl** | Herramientas | comprimir, 50 %, máx. 1920 px, rotar, quitar metadatos, unir en PDF · PDF: unir, separar, rotar |
| **Alt** | Acciones | compartir, flotar (estante movible), copiar a…, mover a…, copiar ruta, terminal |

- La rueda queda abierta aunque sueltes la tecla. Tocar otra tecla cambia de rueda; tocar **la misma** repite la última acción.
- Soltar en el centro (o fuera) cancela. El original nunca se modifica: el resultado aparece al lado (`foto.jpg`, `foto (comprimida).jpg`, `foto (1).jpg`…).
- **Sin arrastrar:** seleccioná archivos en el Explorador (o copialos) y apretá **Ctrl+Alt+Espacio**. Flechas eligen, Enter ejecuta, Tab cambia de rueda, 1–9 directo, Esc cierra.
- Configuración y pausa desde el ícono de la bandeja.

Formatos de entrada: PNG, JPG, WebP, HEIC, AVIF, BMP, GIF, TIFF, ICO y PDF.

## Compilar

Requiere el SDK de .NET 10 (`winget install Microsoft.DotNet.SDK.10`).

```bash
dotnet test
dotnet publish src/Mandarina.App -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o publish
```

`publish/Mandarina.exe` es la app. Para que arranque con Windows, activalo en Configuración.

## Herramientas de diagnóstico

- `Mandarina.exe --bench` — tiempo de apertura de la rueda y memoria en reposo.
- `Mandarina.exe --snapshot carpeta` — guarda cómo se ve cada rueda en PNG.
- `MANDARINA_DEBUG=1` — registra el flujo de arrastre en `%APPDATA%\Mandarina\log.txt`.
- `--gpu` — usa la GPU para dibujar (por defecto es por software: gasta ~80 MB menos y es igual de rápido).

## Límites conocidos

- Si la ventana de origen corre como administrador, Windows no deja que Mandarina vea esa entrada: la rueda no se abre.
- Alt suelta puede activar el menú de algunas apps; si molesta, cambiá la tecla en Configuración.

## Estructura

- `src/Mandarina.Core` — lógica sin interfaz: formatos, nombres de salida, catálogo de operaciones, backends (Magick.NET, PDFsharp, `Windows.Data.Pdf`), cola de trabajos, máquina de arrastre. Probada con xUnit.
- `src/Mandarina.App` — WPF: hooks globales, rueda de vidrio, avisos, bandeja, configuración.
- `docs/superpowers/` — especificación y plan.
