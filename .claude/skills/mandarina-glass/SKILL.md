---
name: mandarina-glass
description: Reglas de diseño "liquid glass" de Mandarina (WPF, .NET 10, Windows 11). Usar en CUALQUIER trabajo de interfaz del proyecto — la rueda, los avisos, el portapapeles, la configuración, ventanas nuevas, colores, animaciones o íconos — aunque el pedido no diga "vidrio" ni "diseño".
---

# Mandarina · Liquid Glass para Windows

Inspirada en la skill iOS Liquid Glass de zakisheriff, traducida a lo que Windows 11 y WPF realmente ofrecen.
La idea es la misma: **un solo material, siempre el mismo, nunca improvisado**, y **ninguna interacción sin respuesta visible**.

## Las dos leyes

### Ley 1 — Nunca inventes vidrio: usá el material de la casa

iOS tiene `.glassEffect()`. Windows no tiene un equivalente para formas libres (Acrylic/Mica de DWM solo funcionan en ventanas rectangulares completas). Por eso Mandarina tiene **su propio material canónico**, y ese es el "nativo" de este proyecto:

| Pieza | Dónde está | Para qué |
|---|---|---|
| `GlassCapture.Capture(...)` | `Wheel/GlassCapture.cs` | Foto única del fondo al abrir, reducida a 1/4 |
| `Glass.Vibrant(shot, dark)` | `Theme/Glass.cs` | Saturación ×1.7 + aclarado/oscurecido según tema |
| `BlurEffect` sobre esa imagen | en cada ventana | Desenfoque (16–18 px) de una imagen **estática** |
| `Glass.Tint` | `Theme/Glass.cs` | Tinte lechoso (claro) / ahumado (oscuro) |
| `Glass.Specular` | `Theme/Glass.cs` | Luz que entra desde arriba |
| `Glass.Rim` + `Glass.Outline` | `Theme/Glass.cs` | Filo de luz por dentro + línea oscura finita por fuera |
| `Glass.Chip` | `Theme/Glass.cs` | Cápsulas y botones que van encima del vidrio |
| `Glass.DiskShadow` / `Glass.RectShadow` | `Theme/Glass.cs` | Sombras baratas (capas, no `DropShadowEffect`) |
| `Glass.Text/Secondary/Tertiary` | `Theme/Glass.cs` | Las tres jerarquías de texto |
| `Palette.Current()` | `Theme/SystemTheme.cs` | Tema claro/oscuro, acento de Windows, `Danger`, `Hover` |

**Receta obligatoria de una superficie de vidrio** (en este orden, de abajo hacia arriba):

```
sombra (DiskShadow / RectShadow)
→ fondo capturado + Vibrant + BlurEffect, recortado a la forma
→ Tint
→ Specular
→ Outline (por fuera, 0.75 px)
→ Rim (por dentro, 1 px)
→ contenido
```

Si una ventana nueva necesita vidrio, **reutilizá esta receta**. Si hace falta en más de un lugar, extraé un helper en `Theme/Glass.cs` en vez de copiar y pegar.

**Prohibido:**
- Colores ARGB sueltos para "hacer vidrio" (`#80FFFFFF` a mano, `Opacity = 0.6` sobre un fondo). Todo color sale de `Glass.*` o `Palette`.
- Desenfoque en vivo o recapturar el fondo en cada cuadro / en `DragOver`. Se captura **una vez por apertura** y se libera al cerrar.
- `DropShadowEffect` o `BlurEffect` animados sobre árboles grandes (se recalculan cada cuadro). Única excepción ya existente: el "enfoque" de 150 ms al abrir la rueda.
- Imitar Mica/Acrylic con imágenes. Si la ventana es rectangular y vive mucho tiempo, usá el material real.

**Cuándo usar el material nativo de Windows en lugar del propio:**
- Ventanas rectangulares, normales, con barra de título (p. ej. Configuración) → `ThemeMode = ThemeMode.System` (Fluent de WPF) y, si se quiere material, `Backdrop.Apply(window, Win32.DWMSBT_MAINWINDOW /* Mica */, dark)`.
- Ventanas flotantes rectangulares transitorias → `DWMSBT_TRANSIENTWINDOW` (Acrylic) es válido, pero solo si se ve igual de bien que el material propio; los avisos y el portapapeles hoy usan el propio para que todo combine con la rueda.
- Formas no rectangulares (la rueda) → siempre el material propio.

### Ley 2 — Ninguna interacción sin respuesta

En iOS la regla es "cada toque, un háptico". En una PC no hay hápticos: el equivalente es **respuesta visual inmediata** (y por teclado). Toda cosa clickeable o soltable:

1. **Hover**: cambia antes de 100 ms. Sectores → `Palette.Hover` + glifo a ×1.07 con `BackEase`. Botones/links → opacidad 0.9 → 1 o chip más denso.
2. **Presión / soltar**: confirma la acción (la gota que viaja al sector, el aviso que aparece). Nunca cerrar en silencio.
3. **Cancelar** también responde: el centro de la rueda se tiñe con `Danger` al pasar por encima.
4. **Teclado**: todo lo que se hace con el mouse se puede hacer con teclado (flechas, Enter, Esc, Tab, 1–9). Un control nuevo sin camino de teclado es un bug.
5. **Errores**: tarjeta con insignia `Danger`, mensaje en castellano claro. Se cierra sola como las notificaciones de Windows, pero más tarde que un aviso de éxito (8 s contra 5 s), y no se cierra mientras el mouse está encima.

No agregues sonidos: Windows no los usa para esto y molestan.

## Reglas de oro

- **Concentricidad**: radio interno = radio externo − separación. Una tarjeta de radio 16 con 10 px de margen lleva chips de radio 6; la rueda (148/40) usa sectores con el mismo centro. No mezcles esquinas rectas con redondeadas en la misma superficie.
- **El color tiene significado**: el acento (de Windows, o naranja mandarina `#FF8A3D` si no hay) solo para selección, progreso y la acción principal. `Danger` solo para cancelar/errores. La insignia roja de PDF es la única excepción (identifica tipo de archivo). Nada de tintes decorativos.
- **El texto nunca va sobre el fondo crudo**: siempre sobre `Tint` o `Chip`, con `Glass.Text/Secondary/Tertiary`. Si sobre un fondo muy chillón no se lee, subí el tinte, no le agregues sombra al texto.
- **Tipografía**: `Palette.UiFont` (Segoe UI Variable) para texto, `Palette.IconFont` (Segoe Fluent Icons) para glifos. Nada de emojis como íconos. Tamaños actuales: títulos 13 semibold, cuerpo 12–13, etiquetas de sector 11.5, detalles 9.5–11.
- **Tema**: todo color se pide a `Palette.Current()` al **abrir** la ventana, no en el constructor estático, para que siga los cambios de claro/oscuro y de acento.
- **Ventanas reutilizables**: se crean una vez y se ocultan/muestran. Sin timers ni sondeo en reposo.
- **Textos de UI en castellano rioplatense** ("Soltá acá", "Tocá otra tecla"), cortos.

## Movimiento

Tokens actuales — usalos en vez de inventar duraciones nuevas:

| Uso | Duración | Curva |
|---|---|---|
| Aparecer ventana (opacidad) | 140 ms | lineal |
| Aparecer ventana (escala 0.9/0.96 → 1) | 240–260 ms | `BackEase` amplitud 0.2–0.22 |
| Sectores escalonados | 20 ms + 14 ms × i, 160–220 ms | `CubicEase` EaseOut |
| Hover | 90–140 ms | `BackEase` amplitud 0.3 |
| Desplazar/reordenar | 180–220 ms | `CubicEase` EaseOut |
| Cerrar | 110–120 ms | `QuadraticEase` EaseIn, escala → 0.95 |

Cerrar siempre es más rápido que abrir. Nada dura más de 320 ms.

## Accesibilidad (obligatoria — hoy falta implementarla)

Al tocar cualquier ventana, verificá estas tres preferencias de Windows. Si el helper no existe todavía, **crealo en `Theme/SystemTheme.cs`** (por ejemplo, propiedades en `Palette`) y usalo desde todas las ventanas:

1. **Efectos de transparencia apagados** (Configuración → Personalización → Colores):
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency == 0`
   → no capturar fondo; usar `Tint` con alfa 0xF0 (casi opaco). Se mantienen filo, especular y sombra.
2. **Efectos de animación apagados** (Accesibilidad → Efectos visuales):
   `SystemParameters.ClientAreaAnimation == false`
   → sin escalas, rebotes, deslizamientos ni escalonado; solo fundidos de ≤ 80 ms (o aparición instantánea).
3. **Contraste alto**: `SystemParameters.HighContrast == true`
   → nada de vidrio: fondo `SystemColors.WindowBrush`, texto `SystemColors.WindowTextBrush`, bordes de 2 px con `SystemColors.WindowTextBrush`, resaltado con `SystemColors.HighlightBrush`/`HighlightTextBrush`.

Escuchá `Microsoft.Win32.SystemEvents.UserPreferenceChanged` para releer esto (y el tema) sin reiniciar la app.

## Rendimiento (presupuesto del spec)

- La rueda aparece en < 100 ms desde el toque de tecla. Medilo con `Mandarina.exe --bench` después de cambios visuales.
- En reposo ~0 % CPU y < 60 MB. No dejes animaciones infinitas ni `DispatcherTimer` corriendo con la ventana oculta.
- Render por software por defecto: evitá efectos que solo se ven bien con GPU.
- `Freeze()` en todo pincel y geometría que no cambie (`Glass.Frozen`).

## Lista de revisión (antes de dar por terminado un cambio de UI)

- [ ] ¿Todos los colores salen de `Glass.*` / `Palette`? ¿Ningún ARGB suelto?
- [ ] ¿La superficie sigue la receta en orden (sombra → fondo → tinte → especular → outline → rim)?
- [ ] ¿El fondo se captura una sola vez y se libera al cerrar?
- [ ] ¿Cada elemento interactivo tiene hover, respuesta al click/soltar y camino de teclado?
- [ ] ¿El acento/`Danger` se usan solo con significado?
- [ ] ¿Esquinas concéntricas?
- [ ] ¿Funciona con transparencia apagada, animaciones apagadas y contraste alto?
- [ ] ¿Se ve bien en claro **y** oscuro? Revisalo con `MANDARINA_THEME=light|dark` y `Mandarina.exe --snapshot carpeta`.
- [ ] ¿`--bench` sigue dentro del presupuesto?
