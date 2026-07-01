namespace Concord.Detour;

/// <summary>
///     Stores the process-wide detour backend used by Concord when applying native method redirects.
/// </summary>
public static class DetourBackend {
    /// <summary>
    ///     Gets or sets the active detour backend.
    /// </summary>
    /// <remarks>
    ///     The default backend uses MonoMod.Core. Tests and runtime adapters can replace this value to provide
    ///     a test backend or runtime-specific lifecycle behavior.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when the assigned backend is <see langword="null" />.</exception>
    public static IDetourBackend Current {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    } = new MonoModDetourBackend();
}
