namespace Armux.Core;

/// <summary>What a terminal tab is doing, for the taskbar indicator. Ordered from least to most urgent.</summary>
public enum AgentState { Idle, Working, Error, NeedsInput }

/// <summary>
/// Reads a terminal's state from what programs already emit:
/// <list type="bullet">
/// <item>OSC 9;4 progress (any program): 1 or 3 working, 2 error, 4 paused (waiting).</item>
/// <item>Claude Code's tab title: a spinner glyph while it works (<c>◐◓◑◒</c>, braille dots), <c>✳</c> when idle.
/// Claude Code emits OSC 9;4 only for ConEmu, Ghostty and iTerm, so the title is the signal in armux.</item>
/// </list>
/// "Needs input" comes from notifications (OSC 9 / 99 / 777, the bell), not from here.
/// </summary>
public static class AgentStatus
{
    public static AgentState FromProgress(int oscProgressState) => oscProgressState switch
    {
        1 or 3 => AgentState.Working,
        2 => AgentState.Error,
        4 => AgentState.NeedsInput,
        _ => AgentState.Idle,
    };

    public static bool TitleShowsWork(string title)
    {
        if (title.Length == 0) return false;
        char c = title[0];
        // Only the glyphs Claude Code puts in the title while working; its idle title starts with '✳'.
        return c is '◐' or '◓' or '◑' or '◒' || c is >= '⠀' and <= '⣿';
    }

    /// <summary>The most urgent of the states.</summary>
    public static AgentState Max(IEnumerable<AgentState> states) => states.DefaultIfEmpty(AgentState.Idle).Max();
}
