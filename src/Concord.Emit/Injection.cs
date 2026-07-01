using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Describes one injection method that should be copied into a generated wrapper.
/// </summary>
/// <param name="InjectionMethod">The injection method whose body supplies the injected IL.</param>
/// <param name="At">The position or call site where the injection method body is inserted.</param>
/// <param name="Owner">The stable owner id used for patch grouping and unpatching.</param>
/// <param name="Priority">The ordering priority for this injection relative to other owners.</param>
public sealed record Injection(MethodBase InjectionMethod, InjectAt At, string Owner, int Priority);
