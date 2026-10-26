using System.Text.Json;
using System.Text.Json.Serialization;

namespace Armux.Core;

/// <summary>
/// The armux control API (cmux's socket API, deviation D-06): one JSON request line and one JSON response line per
/// connection on a named pipe that only the current user can open. Each armux window process has its own pipe,
/// <c>armux-&lt;user&gt;-&lt;pid&gt;</c>, so two windows never answer for each other.
/// </summary>
public static class Ipc
{
    static string Prefix => "armux-" + new string([.. Environment.UserName.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]) + "-";

    /// <summary>The pipe served by this process: <c>\\.\pipe\armux-&lt;user&gt;-&lt;pid&gt;</c>.</summary>
    public static string PipeNameFor(int processId) => Prefix + processId;

    /// <summary>
    /// The pipe a client should use: <c>ARMUX_PIPE</c> (set in every armux terminal, naming the window that owns
    /// it), else the first armux pipe of this user that exists, else null.
    /// </summary>
    public static string? FindPipe()
    {
        if (Environment.GetEnvironmentVariable("ARMUX_PIPE") is { Length: > 0 } fromEnv) return fromEnv;
        try
        {
            return Directory.GetFiles(@"\\.\pipe\", Prefix + "*").Select(Path.GetFileName).Order(StringComparer.Ordinal).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public static string Encode(Request r) => JsonSerializer.Serialize(r, IpcJson.Default.Request);
    public static string Encode(Response r) => JsonSerializer.Serialize(r, IpcJson.Default.Response);

    public static Request? DecodeRequest(string line)
    {
        try { return JsonSerializer.Deserialize(line, IpcJson.Default.Request); }
        catch (JsonException) { return null; }
    }

    public static Response? DecodeResponse(string line)
    {
        try { return JsonSerializer.Deserialize(line, IpcJson.Default.Response); }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// <c>Command</c> is "ping", "notify", "send" (types <c>Body</c> into a terminal), "open" (<c>Body</c> in a browser split) or "list". <c>Surface</c> targets a terminal (the CLI fills it from
/// ARMUX_SURFACE_ID); 0 means the focused surface.
/// </summary>
public sealed record Request
{
    public string Command { get; set; } = "";
    public int Surface { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
}

public sealed record Response
{
    public bool Ok { get; set; }
    public string Error { get; set; } = "";
    /// <summary>Text for the CLI to print (version for ping, the workspace list for list).</summary>
    public string Output { get; set; } = "";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Request))]
[JsonSerializable(typeof(Response))]
internal sealed partial class IpcJson : JsonSerializerContext;
