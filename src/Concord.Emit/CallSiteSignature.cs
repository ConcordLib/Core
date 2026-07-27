namespace Concord;

/// <summary>
///     An opaque carrier for a <c>calli</c> call-site signature. A transpiler can move or delete a
///     <c>calli</c> instruction, but constructing a new signature is not supported in this version.
/// </summary>
public sealed class CallSiteSignature {
    internal CallSiteSignature(object handle) {
        Handle = handle;
    }

    internal object Handle { get; }
}
