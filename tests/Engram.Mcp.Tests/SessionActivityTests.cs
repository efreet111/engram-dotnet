using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Engram.Mcp;
using Xunit;

namespace Engram.Mcp.Tests;

public class SessionActivityTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Go-style test clock: shared mutable clock so state CAN be shared across
    // instances. This sacrifices cross-instance thread-safety for testability,
    // but SessionActivity is scoped-per-MCP-call (singleton DI) so that's fine.
    // ─────────────────────────────────────────────────────────────────────────
    private sealed class TestClock
    {
        public DateTimeOffset Value;
        public TestClock(DateTimeOffset start) => Value = start;
        public DateTimeOffset Now() => Value;
        public void Advance(TimeSpan span) => Value = Value.Add(span);
    }

    private static Engram.Mcp.SessionActivity Create(
        TimeSpan nudgeAfter = default,
        TestClock? clock = null)
    {
        nudgeAfter = nudgeAfter == default ? TimeSpan.FromMinutes(10) : nudgeAfter;
        clock ??= new TestClock(DateTimeOffset.UtcNow);
        return new Engram.Mcp.SessionActivity(nudgeAfter, () => clock.Value);
    }

    // ─── RecordAndNudge ───────────────────────────────────────────────────────

    [Fact]
    public void RecordAndNudge_FiresAfterThreshold()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var a     = Create(TimeSpan.FromMinutes(10), clock);
        var sid   = "test-session";

        // Record 6 tool calls
        for (int i = 0; i < 6; i++) a.RecordToolCall(sid);

        // Session just started → no nudge
        Assert.Empty(a.NudgeIfNeeded(sid));

        // Advance clock past 10 min threshold
        clock.Advance(TimeSpan.FromMinutes(15));

        var nudge = a.NudgeIfNeeded(sid);
        Assert.False(string.IsNullOrEmpty(nudge), $"Expected nudge, got: {nudge}");
        Assert.Contains("15 minutes", nudge);
        Assert.Contains("No mem_save calls", nudge);
    }

    // ─── RecordSave_ResetsNudge ─────────────────────────────────────────────

    [Fact]
    public void RecordSave_ResetsNudgeTimer()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var a     = Create(TimeSpan.FromMinutes(10), clock);
        var sid   = "test-session";

        // Record 6 tool calls
        for (int i = 0; i < 6; i++) a.RecordToolCall(sid);

        // Advance past threshold → nudge fires
        clock.Advance(TimeSpan.FromMinutes(15));
        Assert.False(string.IsNullOrEmpty(a.NudgeIfNeeded(sid)));

        // Record save → resets nudge timer
        a.RecordSave(sid);

        // Nudge gone immediately after save
        Assert.Empty(a.NudgeIfNeeded(sid));

        // Advance clock past threshold again
        clock.Advance(TimeSpan.FromMinutes(12));

        var nudge = a.NudgeIfNeeded(sid);
        Assert.False(string.IsNullOrEmpty(nudge), $"Expected nudge after reset, got: {nudge}");
        Assert.Contains("12 minutes", nudge);
    }

    // ─── ActivityScore ────────────────────────────────────────────────────────

    [Fact]
    public void ActivityScore_ReflectsToolCallsAndSaves()
    {
        var a = Create();

        // Unknown session → empty
        Assert.Empty(a.ActivityScore("unknown-session"));

        var sid = "test-session";
        for (int i = 0; i < 8; i++) a.RecordToolCall(sid);

        var score = a.ActivityScore(sid);
        Assert.Contains("8 tool calls", score);
        Assert.Contains("0 saves", score);
        Assert.Contains("high activity with no saves", score);

        // After a save, warning disappears
        a.RecordSave(sid);
        score = a.ActivityScore(sid);
        Assert.Contains("1 save", score);
        Assert.DoesNotContain("high activity", score);
    }

    [Fact]
    public void ActivityScore_SingularAndPluralLabels()
    {
        var a   = Create();
        var sid = "test-session";

        a.RecordToolCall(sid);
        a.RecordSave(sid);

        var score = a.ActivityScore(sid);
        Assert.Contains("1 tool call,", score);
        Assert.Contains("1 save", score);

        a.RecordToolCall(sid);
        a.RecordSave(sid);

        score = a.ActivityScore(sid);
        Assert.Contains("2 tool calls,", score);
        Assert.Contains("2 saves", score);
    }

    // ─── Idle Detection ───────────────────────────────────────────────────────

    [Fact]
    public void NoNudgeForIdleSessions()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var a     = Create(TimeSpan.FromMinutes(10), clock);
        var sid   = "idle-session";

        // Only 3 tool calls, no saves
        for (int i = 0; i < 3; i++) a.RecordToolCall(sid);

        // Advance past threshold
        clock.Advance(TimeSpan.FromMinutes(20));

        // ≤5 tool calls and 0 saves → no nudge
        Assert.Empty(a.NudgeIfNeeded(sid));
    }

    // ─── ClearSession ─────────────────────────────────────────────────────────

    [Fact]
    public void ClearSession_RemovesStatsAndIsIdempotent()
    {
        var a   = Create();
        var sid = "test-session";

        a.RecordToolCall(sid);
        a.RecordSave(sid);

        Assert.NotEmpty(a.ActivityScore(sid));

        a.ClearSession(sid);
        Assert.Empty(a.ActivityScore(sid));

        // Idempotent: no panic
        a.ClearSession("non-existent-session");
    }

    // ─── ConcurrentAccess ─────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentAccess_RemainsThreadSafe()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var a     = Create(TimeSpan.FromMinutes(10), clock);

        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => a.RecordToolCall("concurrent")));
            tasks.Add(Task.Run(() => a.RecordSave("concurrent")));
            tasks.Add(Task.Run(() => a.NudgeIfNeeded("concurrent")));
            tasks.Add(Task.Run(() => a.ActivityScore("concurrent")));
        }
        await Task.WhenAll(tasks.ToArray());

        // Reached without exception = thread-safe
        Assert.True(true);
    }
}