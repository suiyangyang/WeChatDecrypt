using Lumine.Fairy.ContactManagement.Core.Models;
using Lumine.Fairy.ContactManagement.Core.Services;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class ScratchNoteArrangeDecisionTests
{
    [Fact]
    public void GetArrangeDecision_RealtimeEditBeforeDebounce_ReturnsPendingDelay()
    {
        var coordinator = new ScratchNoteAutoArrangeCoordinator();
        var now = new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero);

        var decision = coordinator.GetArrangeDecision(
            ScratchNoteArrangeTrigger.RealtimeEdit,
            now,
            lastEditedAtUtc: now.AddMilliseconds(-200),
            lastArrangedAtUtc: null);

        Assert.False(decision.ShouldArrange);
        Assert.Equal(ScratchNoteArrangeDepth.None, decision.Depth);
        Assert.NotNull(decision.RecommendedDelay);
        Assert.Equal("debounce-pending", decision.Reason);
    }

    [Fact]
    public void GetArrangeDecision_RealtimeEditAfterDebounce_ReturnsLightweightArrange()
    {
        var coordinator = new ScratchNoteAutoArrangeCoordinator();
        var now = new DateTimeOffset(2026, 3, 21, 10, 0, 0, TimeSpan.Zero);

        var decision = coordinator.GetArrangeDecision(
            ScratchNoteArrangeTrigger.RealtimeEdit,
            now,
            lastEditedAtUtc: now.AddMilliseconds(-800),
            lastArrangedAtUtc: null);

        Assert.True(decision.ShouldArrange);
        Assert.Equal(ScratchNoteArrangeDepth.Lightweight, decision.Depth);
        Assert.Equal("realtime-debounce-elapsed", decision.Reason);
    }

    [Fact]
    public void GetArrangeDecision_IdleTimeoutAfterOneMinuteAndNewEdits_ReturnsFullArrange()
    {
        var coordinator = new ScratchNoteAutoArrangeCoordinator();
        var now = new DateTimeOffset(2026, 3, 21, 10, 2, 0, TimeSpan.Zero);
        var lastEditedAt = now.AddMinutes(-2);

        var decision = coordinator.GetArrangeDecision(
            ScratchNoteArrangeTrigger.IdleTimeout,
            now,
            lastEditedAtUtc: lastEditedAt,
            lastArrangedAtUtc: lastEditedAt.AddSeconds(-10));

        Assert.True(decision.ShouldArrange);
        Assert.Equal(ScratchNoteArrangeDepth.Full, decision.Depth);
        Assert.Equal("idle-threshold-reached", decision.Reason);
    }

    [Fact]
    public void GetArrangeDecision_IdleTimeoutWhenAlreadyArrangedAfterEdit_SkipsRearrange()
    {
        var coordinator = new ScratchNoteAutoArrangeCoordinator();
        var now = new DateTimeOffset(2026, 3, 21, 10, 2, 0, TimeSpan.Zero);
        var lastEditedAt = now.AddMinutes(-2);

        var decision = coordinator.GetArrangeDecision(
            ScratchNoteArrangeTrigger.IdleTimeout,
            now,
            lastEditedAtUtc: lastEditedAt,
            lastArrangedAtUtc: lastEditedAt.AddSeconds(1));

        Assert.False(decision.ShouldArrange);
        Assert.Equal(ScratchNoteArrangeDepth.None, decision.Depth);
        Assert.Equal("already-arranged-after-last-edit", decision.Reason);
    }
}