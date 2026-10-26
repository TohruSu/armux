using System.Text.Json;
using System.Text.Json.Serialization;

namespace Armux.Core;

/// <summary>
/// The armux control API (cmux's socket API, deviation D-06): one JSON request line and one JSON response line per
/// connection on the named pipe <see cref="PipeName"/>, which only the current user can open.
/// </summary>
public static class Ipc
{
    /// <summary><c>armux-&lt;user&gt;</c>; the full path is <c>\\.\pipe\armux-&lt;user&gt;</c>.</summary>
    public static string PipeName => "armux-" + new string([.. Environment.UserName.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]);

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
