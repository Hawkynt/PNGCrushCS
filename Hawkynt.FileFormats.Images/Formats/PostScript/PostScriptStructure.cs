using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.PostScript;

/// <summary>What a page is, in the units the file states it in.</summary>
/// <param name="Left">The left edge, in points.</param>
/// <param name="Bottom">The bottom edge, in points.</param>
/// <param name="Right">The right edge, in points.</param>
/// <param name="Top">The top edge, in points.</param>
/// <param name="Source">Which comment the numbers came from, so the size can be accounted for.</param>
public readonly record struct PsBoundingBox(double Left, double Bottom, double Right, double Top, string Source) {

  /// <summary>How wide the page is, in points.</summary>
  public double Width => this.Right - this.Left;

  /// <summary>How tall the page is, in points.</summary>
  public double Height => this.Top - this.Bottom;

  /// <summary>Whether the four numbers are a box something can be drawn in.</summary>
  public bool IsUsable => double.IsFinite(this.Width) && double.IsFinite(this.Height) && this.Width > 0 && this.Height > 0;

  /// <summary>The page a file that states no size gets: US Letter, which is what the reference calls the default.</summary>
  public static PsBoundingBox Letter => new(0, 0, 612, 792, "the default US Letter page, the file stating no bounding box");
}

/// <summary>
/// What the structuring comments of a PostScript file say about it.
/// </summary>
/// <remarks>
/// The comments beginning <c>%%</c> are the Document Structuring Conventions, and two things in them
/// matter to a reader that has to draw the file. The first is the bounding box, which is the only
/// statement of how big the page is. The second is the resource list: a file may declare that it
/// needs a procedure set it does not carry, and such a file is not a drawing at all until whatever
/// produced it supplies the missing definitions. Reading the declaration is what lets that be said
/// by name rather than discovered as an undefined operator half way down the page.
/// </remarks>
public readonly record struct PostScriptComments {

  /// <summary>The bounding box, from whichever comment stated one.</summary>
  public PsBoundingBox Box { get; init; }

  /// <summary>The procedure sets the file says it needs.</summary>
  public IReadOnlyList<string> NeededProcedureSets { get; init; }

  /// <summary>The procedure sets the file carries.</summary>
  public IReadOnlyList<string> SuppliedProcedureSets { get; init; }

  /// <summary>What the file calls itself.</summary>
  public string? Title { get; init; }

  /// <summary>What wrote the file.</summary>
  public string? Creator { get; init; }

  /// <summary>The procedure sets the file needs and does not carry.</summary>
  public IReadOnlyList<string> MissingProcedureSets {
    get {
      var carried = new HashSet<string>(this.SuppliedProcedureSets, StringComparer.Ordinal);
      var missing = new List<string>();
      foreach (var needed in this.NeededProcedureSets)
        if (!carried.Contains(needed))
          missing.Add(needed);

      return missing;
    }
  }
}

/// <summary>Reads the structuring comments out of a PostScript program.</summary>
public static class PostScriptStructure {

  /// <summary>How far into the file the comments are looked for, in bytes.</summary>
  /// <remarks>
  /// The header itself is over within a page or two, but the comment that announces a procedure set
  /// the file carries sits wherever that set is written, which is after it. Scanning further costs
  /// one pass over text that has to be read anyway; stopping early would call a file that carries
  /// its definitions a file that does not.
  /// </remarks>
  private const int _CommentScan = 1 << 24;

  /// <summary>The four bytes a PostScript file written for a PC opens with, before its parts.</summary>
  public static ReadOnlySpan<byte> DosEpsMagic => [0xC5, 0xD0, 0xD3, 0xC6];

  /// <summary>
  /// Where the PostScript is in the file.
  /// </summary>
  /// <remarks>
  /// Usually the whole of it. A file written for a PC wraps the program in a small binary header
  /// that also carries a preview picture, and then the program is the run of bytes that header
  /// points at — the preview being a lower-resolution copy of the same drawing, which is not what a
  /// reader that can draw the drawing wants.
  /// </remarks>
  public static (int Start, int End) Program(ReadOnlySpan<byte> data) {
    if (data.Length < 30 || !data[..4].SequenceEqual(DosEpsMagic))
      return (0, data.Length);

    var start = BitConverter.ToInt32(data[4..8]);
    var length = BitConverter.ToInt32(data[8..12]);
    if (start < 30 || length <= 0 || (long)start + length > data.Length)
      throw new InvalidDataException($"An encapsulated PostScript file says its program is {length} bytes at {start}, which is not inside a file of {data.Length}.");

    return (start, start + length);
  }

  /// <summary>Reads the header comments.</summary>
  public static PostScriptComments Read(ReadOnlySpan<byte> data, int start, int end) {
    var box = default(PsBoundingBox);
    var hiRes = default(PsBoundingBox);
    var needed = new List<string>();
    var supplied = new List<string>();
    string? title = null;
    string? creator = null;

    var limit = Math.Min(end, start + _CommentScan);
    var text = Encoding.Latin1.GetString(data[start..limit]);

    string? continuing = null;
    var inHeader = true;
    foreach (var raw in _Lines(text)) {
      var line = raw.TrimEnd();
      if (line.Length == 0 || !line.StartsWith('%'))
        continue;

      // What the file actually carries is announced where it carries it, which is after the header
      // and is the only statement about it that cannot be stale.
      if (_Take(line, "%%BeginProcSet:", out var beginSet)) {
        supplied.Add(_Name(beginSet));
        continue;
      }

      if (_Take(line, "%%BeginResource:", out var beginResource)) {
        _Resource("supplied", beginResource, needed, supplied);
        continue;
      }

      if (line.StartsWith("%%EndComments", StringComparison.Ordinal))
        inHeader = false;

      if (!inHeader)
        continue;

      if (line.StartsWith("%%+", StringComparison.Ordinal) && continuing != null) {
        _Resource(continuing, line[3..], needed, supplied);
        continue;
      }

      continuing = null;

      if (_Take(line, "%%BoundingBox:", out var boxText)) {
        if (_Box(boxText, "%%BoundingBox", out var parsed))
          box = parsed;

        continue;
      }

      if (_Take(line, "%%HiResBoundingBox:", out var hiResText) || _Take(line, "%%ExactBoundingBox:", out hiResText)) {
        if (_Box(hiResText, "%%HiResBoundingBox", out var parsed))
          hiRes = parsed;

        continue;
      }

      if (_Take(line, "%%Title:", out var titleText)) {
        title = _Unwrap(titleText);
        continue;
      }

      if (_Take(line, "%%Creator:", out var creatorText)) {
        creator = _Unwrap(creatorText);
        continue;
      }

      if (_Take(line, "%%DocumentNeededResources:", out var neededText)) {
        _Resource("needed", neededText, needed, supplied);
        continuing = "needed";
        continue;
      }

      if (_Take(line, "%%DocumentNeededProcSets:", out var neededSets)) {
        _Resource("needed-procset", neededSets, needed, supplied);
        continuing = "needed-procset";
        continue;
      }

      if (_Take(line, "%%DocumentSuppliedResources:", out var suppliedText)) {
        _Resource("supplied", suppliedText, needed, supplied);
        continuing = "supplied";
        continue;
      }

      if (_Take(line, "%%DocumentSuppliedProcSets:", out var suppliedSets)) {
        _Resource("supplied-procset", suppliedSets, needed, supplied);
        continuing = "supplied-procset";
      }
    }

    var chosen = box.IsUsable ? box : hiRes.IsUsable ? hiRes : PsBoundingBox.Letter;

    return new() {
      Box = chosen,
      NeededProcedureSets = needed,
      SuppliedProcedureSets = supplied,
      Title = title,
      Creator = creator
    };
  }

  private static IEnumerable<string> _Lines(string text) {
    var start = 0;
    for (var index = 0; index < text.Length; ++index) {
      var c = text[index];
      if (c != '\r' && c != '\n')
        continue;

      yield return text[start..index];
      if (c == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
        ++index;

      start = index + 1;
    }

    if (start < text.Length)
      yield return text[start..];
  }

  private static bool _Take(string line, string prefix, out string rest) {
    if (!line.StartsWith(prefix, StringComparison.Ordinal)) {
      rest = string.Empty;
      return false;
    }

    rest = line[prefix.Length..].Trim();
    return true;
  }

  /// <summary>
  /// Four numbers, or the word saying they come at the end of the file instead.
  /// </summary>
  /// <remarks>
  /// A producer that does not know the extent until it has written the page writes
  /// <c>(atend)</c> and repeats the comment in the trailer. The trailer is read the same way — the
  /// header scan covers the start of the file, and a file that only states its box at the end falls
  /// back to the default page, which is stated rather than guessed.
  /// </remarks>
  private static bool _Box(string text, string source, out PsBoundingBox box) {
    box = default;
    if (text.Contains("atend", StringComparison.OrdinalIgnoreCase))
      return false;

    var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 4)
      return false;

    var values = new double[4];
    for (var index = 0; index < 4; ++index)
      if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]) || !double.IsFinite(values[index]))
        return false;

    box = new(Math.Min(values[0], values[2]), Math.Min(values[1], values[3]), Math.Max(values[0], values[2]), Math.Max(values[1], values[3]), source);
    return box.IsUsable;
  }

  /// <summary>
  /// One entry of a resource list, which is a kind and then a name.
  /// </summary>
  /// <remarks>
  /// Only procedure sets are counted. A font that is named and not carried is a gap this reader has
  /// anyway, because it does not draw text; a procedure set that is named and not carried is every
  /// operator the drawing is made of.
  /// </remarks>
  private static void _Resource(string kind, string text, List<string> needed, List<string> supplied) {
    var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
      return;

    string name;
    if (kind.EndsWith("procset", StringComparison.Ordinal))
      name = parts[0];
    else if (parts[0].Equals("procset", StringComparison.Ordinal) && parts.Length > 1)
      name = parts[1];
    else
      return;

    var into = kind.StartsWith("needed", StringComparison.Ordinal) ? needed : supplied;
    if (!into.Contains(name))
      into.Add(name);
  }

  private static string _Name(string text) {
    var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    return parts.Length > 0 ? parts[0] : text;
  }

  private static string? _Unwrap(string text) {
    var trimmed = text.Trim();
    if (trimmed.Length >= 2 && trimmed[0] == '(' && trimmed[^1] == ')')
      trimmed = trimmed[1..^1];

    return trimmed.Length == 0 ? null : trimmed;
  }
}
