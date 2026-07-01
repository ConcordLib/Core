namespace Concord;

/// <summary>
///     Marks an injection method as a reverse patch that binds to the original body of the target method.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ReversePatchAttribute : Attribute {
    /// <summary>
    ///     Initializes a new reverse patch declaration.
    /// </summary>
    /// <param name="method">The original method name to bind.</param>
    public ReversePatchAttribute(string method) {
        Method = method;
    }

    /// <summary>
    ///     Gets the original method name this reverse patch binds.
    /// </summary>
    public string Method { get; }
}
