using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Concord.Emit;
using Mono.Cecil;
using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class UnreadableDeclarationTests {
    // A [Patch(typeof(X))] whose X belongs to a mod the player does not have. Reading the attribute
    // throws, and the declaration has to be skipped with a reason rather than in silence.
    [Fact]
    public void ScanType_PatchTargetAssemblyMissing_SkipsAndSaysWhy() {
        Type declaration = BuildDeclarationTargetingAMissingAssembly();
        List<string> logs = [];
        Patcher.UseLog(logs.Add);

        try {
            PatchDeclarationScanner.ScanType(declaration, new RecordingApplier(), new FakeAttachedPropertyRegistry());
        } finally {
            Patcher.UseLog(null);
        }

        Assert.Single(logs);
        Assert.Contains(declaration.FullName!, logs[0]);
        Assert.Contains("not loaded", logs[0]);
    }

    private static Type BuildDeclarationTargetingAMissingAssembly() {
        AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("GhostedDeclarations", new Version(1, 0, 0, 0)),
            "GhostedDeclarations",
            ModuleKind.Dll);

        ModuleDefinition module = assembly.MainModule;
        AssemblyNameReference absent = new AssemblyNameReference("ModThatIsNotInstalled", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(absent);

        TypeDefinition declaration = new TypeDefinition(
            "Ghosted",
            "Declaration",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Class,
            module.ImportReference(typeof(object)));

        CustomAttribute patch = new CustomAttribute(
            module.ImportReference(typeof(PatchAttribute).GetConstructor([typeof(Type)])));
        patch.ConstructorArguments.Add(new CustomAttributeArgument(
            module.ImportReference(typeof(Type)),
            new TypeReference("NotInstalled", "Target", module, absent)));
        declaration.CustomAttributes.Add(patch);
        module.Types.Add(declaration);

        using MemoryStream image = new MemoryStream();
        assembly.Write(image);
        return Assembly.Load(image.ToArray()).GetType("Ghosted.Declaration")!;
    }

    private sealed class RecordingApplier : IPatchApplier {
        public void ApplyPatch(MethodBase target, Injection injection) {
            throw new InvalidOperationException("nothing should be applied");
        }
    }
}
