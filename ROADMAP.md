# Ideas para más adelante

Cosas acordadas pero todavía no hechas.

## Temas personalizables

Que cada usuario pueda elegir y cambiar el aspecto de la app desde **Configuración**, sin reiniciar.

- Elegir entre temas (por ejemplo: Sistema, Claro, Oscuro y algunas variantes de color) y ver el cambio al instante.
- Color de acento propio (hoy se toma el de Windows, o el naranja mandarina si no hay).
- Que se guarde en `settings.json`, junto al resto de la configuración.

Cómo encaja con lo que ya existe:

- Hoy todos los colores salen de `Palette.Current()` (`src/Mandarina.App/Theme/SystemTheme.cs`) y de `Glass.*` (`src/Mandarina.App/Theme/Glass.cs`), y cada ventana los pide **al abrirse**. Un tema elegido por el usuario puede entrar por ahí sin tocar las ventanas: `Palette.Current()` lee primero la elección guardada y, si es "Sistema", sigue como ahora.
- La variable `MANDARINA_THEME=light|dark` que usan las capturas de diseño ya hace algo parecido; un tema guardado la reemplazaría.
- Respetar las reglas de `.claude/skills/mandarina-glass/SKILL.md`: un solo material, colores con significado, y que funcione con transparencia apagada, animaciones apagadas y contraste alto.

## Otras pendientes

- Video y audio (con FFmpeg) y documentos de Office (con LibreOffice o Pandoc).
- Las preferencias de accesibilidad de Windows (transparencia, animaciones, contraste alto) que pide la skill de diseño.
- Detalles menores de la revisión de código: avisar "cancelado" si se cierra el selector de carpeta, ocultar los temporales `.mandarina-*`, reinstalar los hooks si Windows los quita.
