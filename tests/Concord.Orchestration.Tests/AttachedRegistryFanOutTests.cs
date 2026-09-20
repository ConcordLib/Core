using System;
using System.Collections.Generic;
using Concord;
using Concord.AttachedData;
using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class RecordingRegistry : IAttachedPropertyRegistry {
    public List<string> Names { get; } = [];

    public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
        Names.Add(name);
    }
}

public sealed class ThrowingRegistry : IAttachedPropertyRegistry {
    public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
        throw new InvalidOperationException("this consumer is broken");
    }
}

[Collection("PatcherStatics")]
public sealed class AttachedRegistryFanOutTests : IDisposable {
    private static readonly IAttachedSlot Slot =
        AttachedStorage.SlotAt(AttachedStorage.SlotFor(typeof(SomeBase), "FanOut", typeof(int)));

    public AttachedRegistryFanOutTests() {
        Patcher.ForgetAttachedPropertyRegistries();
    }

    public void Dispose() {
        Patcher.ForgetAttachedPropertyRegistries();
        Patcher.UseLog(null);
    }

    [Fact]
    public void TwoRegistries_BothKeepReceiving() {
        RecordingRegistry adapter = new RecordingRegistry();
        RecordingRegistry mod = new RecordingRegistry();
        Patcher.UseAttachedPropertyRegistry(adapter);
        Patcher.UseAttachedPropertyRegistry(mod);

        Register("after");

        Assert.Equal(["after"], adapter.Names);
        Assert.Equal(["after"], mod.Names);
    }

    [Fact]
    public void RegistryInstalledLate_GetsEverythingDeclaredBefore() {
        RecordingRegistry adapter = new RecordingRegistry();
        Patcher.UseAttachedPropertyRegistry(adapter);
        Register("before");

        RecordingRegistry mod = new RecordingRegistry();
        Patcher.UseAttachedPropertyRegistry(mod);

        Assert.Equal(["before"], mod.Names);
    }

    [Fact]
    public void SameRegistryTwice_ReceivesOnce() {
        RecordingRegistry adapter = new RecordingRegistry();
        Patcher.UseAttachedPropertyRegistry(adapter);
        Patcher.UseAttachedPropertyRegistry(adapter);

        Register("once");

        Assert.Equal(["once"], adapter.Names);
    }

    [Fact]
    public void OneRegistryThrows_TheOthersStillGetTheField() {
        List<string> logs = [];
        Patcher.UseLog(logs.Add);
        Patcher.UseAttachedPropertyRegistry(new ThrowingRegistry());
        RecordingRegistry survivor = new RecordingRegistry();
        Patcher.UseAttachedPropertyRegistry(survivor);

        Register("survives");

        Assert.Equal(["survives"], survivor.Names);
        Assert.Single(logs);
        Assert.Contains("this consumer is broken", logs[0]);
    }

    private static void Register(string name) {
        Patcher.AttachedPropertyFanOut.RegisterAttachedProperty(
            typeof(FirstDeclaration), typeof(SomeBase), name, typeof(int), Slot);
    }
}
