namespace Palta.Core.Input;

/// <summary>
/// Windows quita en silencio los hooks de bajo nivel si alguna vez tardan en responder (por ejemplo,
/// con la PC muy cargada), y no avisa. La señal es indirecta: hubo actividad de mouse o teclado
/// (<c>GetLastInputInfo</c>) y los hooks no la vieron.
/// </summary>
public static class HookWatchdog
{
    /// <summary>Margen para no confundir un evento que todavía no llegó al hook con un hook muerto.</summary>
    public const uint GraceMs = 3000;

    /// <param name="lastInputTick">Última entrada del usuario según Windows (ms, GetTickCount, 32 bits).</param>
    /// <param name="lastHookTick">Último evento que recibieron los hooks (misma base).</param>
    /// <param name="nowTick">Ahora (misma base).</param>
    /// <returns>True si hay que reinstalar los hooks.</returns>
    public static bool ShouldReinstall(uint lastInputTick, uint lastHookTick, uint nowTick)
    {
        // Con aritmética sin signo la vuelta a cero del contador (cada 49,7 días) no molesta.
        var sinceInput = unchecked(nowTick - lastInputTick);
        var sinceHook = unchecked(nowTick - lastHookTick);
        // Hubo entrada más nueva que lo último que vio el hook, y ya pasó el margen.
        return sinceInput < sinceHook && sinceHook - sinceInput > GraceMs && sinceInput < uint.MaxValue / 2;
    }
}
