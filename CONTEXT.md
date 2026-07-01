# Concord

Concord is a runtime patching library for .NET mods. This context captures the project language for mod-authored declarations, runtime patching behavior, and attached data.

## Language

**Patch**:
The casual umbrella word for changing target runtime behavior, and the normal verb for applying that change.
_Avoid_: Patch when a precise term like patch declaration, injection, or applied patch is needed

**Patch Declaration**:
An author-written type marked with `[Patch]` that Concord scans for injections, injected member declarations, shadow fields, and attached properties.
_Avoid_: Patch when the declaration itself is meant; registration class

**Patch Declaration Scanner**:
The orchestration component that scans assemblies and types for patch declarations, then forwards discovered injections and attached properties to runtime-adapter appliers and registries.
_Avoid_: Runtime adapter, patch scanner

**Injection**:
A single `[Inject]` method plus its target and position.
_Avoid_: Hook when the Concord declaration is meant

**Injection Position**:
The selected declaration/API value for an injection, such as Head, Tail, Return, Around, or Invoke.
_Avoid_: Injection point when referring to the category or value; At as the concept name

**Injection Point**:
The runtime place in a target method's behavior where injected code runs.
_Avoid_: Injection position when referring to the runtime location

**Around Injection**:
An injection that controls the whole target method by using an injection-method call to the target method as the splice point for the original body.
_Avoid_: Invoke injection, operation injection

**Invoke Injection**:
An injection that controls a call site inside the target method.
_Avoid_: Around injection, method wrapper

**Operation**:
The handle an invoke injection uses to run, skip, or replace its matched call-site operation.
_Avoid_: ControlHandle, original body

**Control Handle**:
The `ControlHandle` or `ControlHandle<T>` value an injection uses to cancel target execution or inspect and replace a target method return value.
_Avoid_: Operation, callback

**Applied Patch**:
The runtime modification produced after Concord applies one or more injections to a target method.
_Avoid_: Patch when runtime state or runtime effect is meant

**Patch Handle**:
The handle returned when Concord applies patches, used to undo the applied patches it owns.
_Avoid_: Apply handle

**Apply**:
The lifecycle operation that makes patch declarations, injections, or detours live.
_Avoid_: Setup

**Undo**:
The lifecycle operation that removes the applied patches owned by a patch handle.
_Avoid_: Unpatch

**Patch Applier**:
The runtime-adapter component that applies an injection to a target method.
_Avoid_: Patch sink

**Wrapper**:
The generated method that contains the target method behavior plus the injections Concord applies to that target.
_Avoid_: Generated method, replacement method, composed method

**Detour Backend**:
The runtime mechanism Concord uses to route calls from a target method to its wrapper.
_Avoid_: Detour when explaining beginner-facing behavior

**Runtime Adapter**:
The integration package that connects Concord to a specific target runtime.
_Avoid_: Integration layer

**Concord Assembly**:
The merged `Concord.dll` artifact that provides Concord's runtime implementation when a target runtime or runtime adapter loads it.
_Avoid_: Runtime assembly, runtime `Concord.dll`, real runtime

**Core Library**:
One of Concord's implementation projects that compiles separately and merges into the Concord Assembly.
_Avoid_: Core when the Concord Assembly, target runtime, or a target runtime's own core module is meant

**Reference Assembly**:
The compile-time-only assembly that exposes Concord's API surface without runtime implementation. `Concord.Ref` is the package that contains Concord's reference assembly.
_Avoid_: Ref package when the assembly artifact is meant; runtime assembly

**Bootstrap Assembly**:
An assembly that can execute before the Concord Assembly or runtime adapter is loadable. It avoids hard references to Concord implementation types and uses reflection or handoff code until loading is ready.
_Avoid_: Startup assembly when the early-load constraint matters; runtime adapter

**Assembly Resolver**:
The .NET assembly-load hook a bootstrap assembly or runtime adapter registers so the target runtime can locate the Concord Assembly or adapter assemblies.
_Avoid_: Runtime resolver

**Diagnostic Code**:
A stable identifier Concord reports for a rejected patch or declaration. `CONCxxx` codes come from runtime composition; `CONCORDxxx` IDs come from analyzer diagnostics.
_Avoid_: Diagnostic ID except when specifically referring to Roslyn analyzer APIs; error code

**Target Runtime**:
The external game, engine, application, or runtime environment whose behavior Concord patches.
_Avoid_: Runtime when the external system could be confused with the .NET runtime or Concord Assembly

**Target Assembly**:
An assembly that contains target types or target members a patch declaration is written against.
_Avoid_: Target runtime when the compile-time assembly artifact is meant; target when the precise concept is target assembly

**Data Adapter**:
The runtime-adapter component that maps Concord attached properties into a runtime's data files, save/load hooks, or object model.
_Avoid_: Persistence when the component also handles object construction or data-file integration; persistence layer

**Target Method**:
The method in a target runtime whose behavior Concord changes.
_Avoid_: Target when the precise concept is target method, target type, target instance, or target member; original method when referring to the patched entry point; base method unless literally referring to reflection on a base type

**Target Type**:
The type in a target runtime that owns the target method, target member, or target instance a patch declaration is written against.
_Avoid_: Target when the precise concept is target type

**Target Instance**:
The live object from a target runtime whose method call, state, or attached data a patch is acting on.
_Avoid_: Target when the precise concept is target instance

**Target Member**:
A field, property, method, or constructor on a target type that Concord resolves for an injection or injected member declaration.
_Avoid_: Target when the precise concept is target member

**Injection Method**:
The author-written method marked with `[Inject]`.
_Avoid_: Body method

**Original Body**:
The target method's unmodified behavior as Concord preserves it for wrapper composition and reverse patches. In task-style docs, "unpatched original" is acceptable when explaining the callable behavior reached through a reverse patch.
_Avoid_: Original method, original source, clean clone, vanilla method

**Reverse Patch**:
A Concord capability that gives mod code a callable path to a target method's original body.
_Avoid_: Original body, clone

**Injected Member Declaration**:
A formal grouping term for `[InjectField]`, `[InjectProperty]`, `[InjectMethod]`, or `[InjectInstance]` declarations that stand in for real target members or the target instance. Use the specific declaration type when it is known.
_Avoid_: Shadow field, attached property

**Shadow Field**:
An inherited declaration field that Concord maps to a matching target field by name.
_Avoid_: Private target field as a formal term, injected member, attached field

**Attached Data**:
Runtime side storage and values Concord associates with target instances without adding real members to the target type.
_Avoid_: Shadow field, injected field

**Attached Property**:
A field declaration on a patch declaration that Concord registers as attached data. The runtime stored value is attached data, not an attached property.
_Avoid_: Attached data when the declaration itself is meant
