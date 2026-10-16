using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Concord.Host.Adapter;

/// <summary>
///     Mints the MethodSpec token a constructed generic method operand needs, encoding the signature blob
///     here instead of through the fork's own <c>DefineMethodSpecFor</c>, which opens it with the wrong byte.
/// </summary>
public static class MethodSpecMinter {
    private const byte GenericInst = 0x0A;

    private const string UpdaterName = "WaylithClr.MethodBodyUpdater, System.Private.CoreLib";

    private const string QCallName = "System.Runtime.CompilerServices.QCallModule, System.Private.CoreLib";

    private const string ModuleName = "System.Reflection.RuntimeModule, System.Private.CoreLib";

    private static readonly MethodInfo? EncodeEntry = Entry("EncodeType");

    private static readonly MethodInfo? CompressEntry = Entry("CompressUInt");

    private static readonly MethodInfo? ModuleEntry = Entry("AsRuntimeModule");

    private static readonly Define? DefineEntry = Shim();

    private delegate int Define(object module, int parent, IntPtr signature, int length);

    /// <summary>
    ///     <see langword="true" /> only when the fork's blob encoder bound and the call into its native
    ///     <c>DefineMethodSpec</c> could be built. It is <see langword="false" /> on any other runtime.
    /// </summary>
    public static bool IsAvailable =>
        EncodeEntry is not null && CompressEntry is not null && ModuleEntry is not null && DefineEntry is not null;

    /// <summary>Mints a MethodSpec token over a MemberRef or MethodDef naming the generic method definition.</summary>
    /// <param name="target">The module the token is minted into.</param>
    /// <param name="parent">The token of the generic method definition this instantiates.</param>
    /// <param name="arguments">The method's generic arguments, in order.</param>
    /// <returns>A <c>0x2B</c> MethodSpec token, valid only in <paramref name="target" />.</returns>
    /// <exception cref="ArgumentNullException">Either reference argument is <c>null</c>.</exception>
    /// <exception cref="PlatformNotSupportedException">This runtime does not carry the fork's token api.</exception>
    public static int Token(Module target, int parent, Type[] arguments) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(arguments);

        if (EncodeEntry is null || CompressEntry is null || ModuleEntry is null || DefineEntry is null) {
            throw new PlatformNotSupportedException(
                $"'{UpdaterName}' does not expose the blob encoder and native DefineMethodSpec this needs on " +
                $"'{RuntimeInformation.FrameworkDescription}', so a MethodSpec token cannot be minted here.");
        }

        object module = ModuleEntry.Invoke(null, [target])!;
        List<byte> blob = [GenericInst];
        CompressEntry.Invoke(null, [blob, (uint)arguments.Length]);
        foreach (Type argument in arguments) {
            EncodeEntry.Invoke(null, [blob, argument, module]);
        }

        byte[] signature = blob.ToArray();
        GCHandle pinned = GCHandle.Alloc(signature, GCHandleType.Pinned);
        try {
            return DefineEntry(module, parent, pinned.AddrOfPinnedObject(), signature.Length);
        } finally {
            pinned.Free();
        }
    }

    private static MethodInfo? Entry(string name) {
        Type? updater = Type.GetType(UpdaterName, throwOnError: false);
        return updater?.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
    }

    private static Define? Shim() {
        MethodInfo? define = Entry("DefineMethodSpec");
        Type? call = Type.GetType(QCallName, throwOnError: false);
        Type? module = Type.GetType(ModuleName, throwOnError: false);
        ConstructorInfo? wrap = call?.GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [module?.MakeByRefType() ?? typeof(object)]);

        if (define is null || call is null || module is null || wrap is null) {
            return null;
        }

        DynamicMethod shim = new(
            "ConcordDefineMethodSpec",
            typeof(int),
            [typeof(object), typeof(int), typeof(IntPtr), typeof(int)],
            typeof(object),
            skipVisibility: true);

        ILGenerator il = shim.GetILGenerator();
        LocalBuilder held = il.DeclareLocal(module);
        LocalBuilder wrapped = il.DeclareLocal(call);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, module);
        il.Emit(OpCodes.Stloc, held);
        il.Emit(OpCodes.Ldloca, wrapped);
        il.Emit(OpCodes.Ldloca, held);
        il.Emit(OpCodes.Call, wrap);
        il.Emit(OpCodes.Ldloc, wrapped);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, define);
        il.Emit(OpCodes.Ret);

        try {
            return shim.CreateDelegate<Define>();
        } catch (Exception) {
            return null;
        }
    }
}
