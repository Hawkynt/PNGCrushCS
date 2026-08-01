using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;

namespace Conformance.Recoil.Tests;

/// <summary>
/// Tom's Editor, a conversion service, used as a fourth opinion on what we write.
/// </summary>
/// <remarks>
/// It has no binary to install: it is a website, and the only way to ask it anything is to send it a
/// file. That is why it is off by default and turns on only when <c>TOMSEDITOR</c> is set — sending
/// somebody's pictures to a third party is not something a test suite should do because it happens
/// to be convenient. What goes up here is the same synthetic gradient every other oracle is asked
/// about, and nothing else.
/// <para/>
/// It is also somebody's server rather than a local program, so it is asked slowly and only about
/// the formats no installed tool can judge. Hammering it for five hundred formats would be rude and
/// would tell us little the others have not already said.
/// </remarks>
internal static class TomsEditorOracle {

  private const string _Root = "https://tomseditor.com/convert/";

  /// <summary>Whether the caller has asked for a third-party service to be used at all.</summary>
  public static bool Enabled { get; } = Environment.GetEnvironmentVariable("TOMSEDITOR") != null;

  /// <summary>How long to wait between requests, this being somebody else's machine.</summary>
  private static readonly TimeSpan _Courtesy = TimeSpan.FromSeconds(2);

  private static readonly Lazy<HttpClient?> _Client = new(() => {
    if (!Enabled)
      return null;

    var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new() };
    var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

    // The upload refuses anything without a session, which the front page hands out.
    try {
      client.GetAsync(_Root + "?l=en").GetAwaiter().GetResult().Dispose();
    } catch (Exception) {
      return null;
    }

    return client;
  });

  /// <summary>The extensions its own catalogue lists, read once from its formats page.</summary>
  public static IReadOnlySet<string> Extensions => _extensions ??= _ReadCatalogue();

  private static HashSet<string>? _extensions;

  private static HashSet<string> _ReadCatalogue() {
    var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (_Client.Value is not { } client)
      return found;

    try {
      var page = client.GetStringAsync(_Root + "supported-formats").GetAwaiter().GetResult();

      // The page names each format with a leading dot, which is what distinguishes them from prose.
      foreach (Match match in Regex.Matches(page, @"\.([A-Za-z0-9]{1,6})\b"))
        found.Add("." + match.Groups[1].Value.ToLowerInvariant());
    } catch (Exception) {
      // Unreachable is the same as knowing nothing, which the caller already handles.
    }

    return found;
  }

  /// <summary>Sends a file and asks for it back as a PNG, returning whether it could.</summary>
  public static (bool Decoded, string Output) TryDecode(string path) {
    if (_Client.Value is not { } client)
      return (false, "not enabled");

    try {
      Thread.Sleep(_Courtesy);

      using var content = new MultipartFormDataContent();
      using var file = new ByteArrayContent(File.ReadAllBytes(path));
      content.Add(file, "file", Path.GetFileName(path));

      var uploaded = client.PostAsync(_Root + "ajax/upload.php", content).GetAwaiter().GetResult();
      var uploadBody = uploaded.Content.ReadAsStringAsync().GetAwaiter().GetResult();
      if (!_Succeeded(uploadBody))
        return (false, "the upload was refused: " + _Message(uploadBody));

      var name = Regex.Match(uploadBody, "\"fname\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
      if (name.Length == 0)
        return (false, "the upload named no file");

      Thread.Sleep(_Courtesy);

      var converted = client
        .GetStringAsync($"{_Root}ajax/convert.php?fname={name}&ext=PNG")
        .GetAwaiter().GetResult();

      return (_Succeeded(converted), _Message(converted));
    } catch (Exception failure) {
      return (false, failure.Message);
    }
  }

  private static bool _Succeeded(string json) => Regex.IsMatch(json, "\"success\"\\s*:\\s*1");

  private static string _Message(string json) {
    var message = Regex.Match(json, "\"message\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
    return message.Length > 0 ? Regex.Replace(message, "<[^>]+>", string.Empty) : json[..Math.Min(120, json.Length)];
  }
}
