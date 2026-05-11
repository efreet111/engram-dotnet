using System;
using System.Collections.Generic;

namespace Engram.Mcp;

/// <summary>
/// Tracks tool call activity for save reminders and activity scores.
/// Ported faithfully from Go internal/mcp/activity.go.
/// </summary>
public sealed class SessionActivity
{
    private readonly object _lock = new();
    private readonly Dictionary<string, SessionStats> _sessions = new();
    private readonly TimeSpan _nudgeAfter;
    private readonly Func<DateTimeOffset> _nowFunc;

    /// <summary>
    /// Creates a SessionActivity with the default 10-minute nudge threshold.
    /// </summary>
    public SessionActivity() : this(TimeSpan.FromMinutes(10), () => DateTimeOffset.UtcNow) { }

    /// <summary>
    /// Creates a SessionActivity with a custom nudge threshold and clock.
    /// </summary>
    public SessionActivity(TimeSpan nudgeAfter, Func<DateTimeOffset>? nowFunc = null)
    {
        _nudgeAfter = nudgeAfter;
        _nowFunc    = nowFunc ?? (() => DateTimeOffset.UtcNow);
    }

    private SessionStats GetOrCreate(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var stats))
        {
            stats = new SessionStats { StartedAt = _nowFunc() };
            _sessions[sessionId] = stats;
        }
        return stats;
    }

    /// <summary>
    /// Increments the tool call counter for a session.
    /// </summary>
    public void RecordToolCall(string sessionId)
    {
        lock (_lock)
        {
            var s = GetOrCreate(sessionId);
            s.ToolCallCount++;
        }
    }

    /// <summary>
    /// Removes the session entry, freeing memory.
    /// </summary>
    public void ClearSession(string sessionId)
    {
        lock (_lock)
        {
            _sessions.Remove(sessionId);
        }
    }

    /// <summary>
    /// Increments the save counter and updates the last-save timestamp.
    /// </summary>
    public void RecordSave(string sessionId)
    {
        lock (_lock)
        {
            var s = GetOrCreate(sessionId);
            s.SaveCount++;
            s.LastSaveAt = _nowFunc();
        }
    }

    /// <summary>
    /// Returns a reminder string if too much time has passed since the last save.
    /// Returns empty string if no nudge is needed.
    /// </summary>
    public string NudgeIfNeeded(string sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var s))
                return "";

            var now = _nowFunc();

            // Don't nudge if session is too young
            if (now - s.StartedAt < _nudgeAfter)
                return "";

            // Don't nudge idle/new sessions (no saves and few tool calls)
            if (s.SaveCount == 0 && s.ToolCallCount <= 5)
                return "";

            // Check time since last save (or session start if no saves yet)
            var refTime = s.LastSaveAt;
            if (refTime == default)
                refTime = s.StartedAt;

            var elapsed = now - refTime;
            if (elapsed < _nudgeAfter)
                return "";

            var minutes = (int)elapsed.TotalMinutes;
            return $"\n\n⚠️ No mem_save calls for this project in {minutes} minutes. Did you make any decisions, fix bugs, or discover something worth persisting?";
        }
    }

    /// <summary>
    /// Returns a formatted activity score string for the session.
    /// </summary>
    public string ActivityScore(string sessionId)
    {
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out var s))
                return "";

            var callLabel = s.ToolCallCount == 1 ? "tool call" : "tool calls";
            var saveLabel = s.SaveCount == 1 ? "save" : "saves";
            var score     = $"Session activity: {s.ToolCallCount} {callLabel}, {s.SaveCount} {saveLabel}";

            if (s.SaveCount == 0 && s.ToolCallCount > 5)
                score += " — high activity with no saves, consider persisting important decisions";

            return score;
        }
    }
}

/// <summary>
/// Ephemeral per-session statistics stored in-memory.
/// </summary>
internal sealed class SessionStats
{
    public DateTimeOffset LastSaveAt    { get; set; }
    public int            ToolCallCount { get; set; }
    public int            SaveCount     { get; set; }
    public DateTimeOffset StartedAt     { get; set; }
}