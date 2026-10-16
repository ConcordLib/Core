using System;
using System.Reflection;
using Xunit;

namespace Concord.Detour.Tests;

public sealed class ExceptionReasonTests {
    [Fact]
    public void Of_TargetInvocationException_ReportsTheInnerFailure() {
        Exception wrapped = new TargetInvocationException(new InvalidOperationException("the real cause"));

        Assert.Equal("InvalidOperationException: the real cause", ExceptionReason.Of(wrapped));
    }

    [Fact]
    public void Of_NestedTargetInvocationExceptions_UnwrapsToTheInnermostFailure() {
        Exception wrapped = new TargetInvocationException(
            new TargetInvocationException(new DivideByZeroException("inner most")));

        Assert.Equal("DivideByZeroException: inner most", ExceptionReason.Of(wrapped));
    }

    [Fact]
    public void Of_PlainException_KeepsItsOwnMessage() {
        Assert.Equal("ArgumentException: plain", ExceptionReason.Of(new ArgumentException("plain")));
    }
}
