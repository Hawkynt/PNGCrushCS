using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hawkynt.FileFormats.Video.Tests;

/// <summary>
/// The two support tables in <c>Hawkynt.FileFormats.Video/README.md</c> are the package's one ledger,
/// and every cell in them is a claim about what the compile-time registry holds. This fixture
/// re-derives those cells from the registry and fails on any disagreement, so a heading that says
/// "support" cannot quietly come to mean "some of it".
/// </summary>
/// <remarks>
/// This exists because the tables had already drifted, twice and in opposite directions: three
/// registered containers had no row at all, and the codec table said two codecs encoded when twenty
/// did. Neither is the kind of mistake a reader can see — a table looks complete whatever is missing
/// from it — so completeness has to be checked by something that counts.
/// <para/>
/// The registry is the authority and not a file listing. Counting decoder source files once produced
/// 72 where the registry held 82, because a glob sees files and the registry sees what the generator
/// actually registered.
/// </remarks>
[TestFixture]
public sealed class VideoReadmeStateTests {

  private const string _SUPPORTED = "✅";
  private const string _PARTIAL = "⚠️";
  private const string _ABSENT = "—";

  // ============================================================================================
  // Containers
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryRegisteredContainerHasARowAndEveryRowIsRegistered() {
    var rows = _Rows("### Container support", "### Codec support");
    var documented = rows.Select(r => r.Cell("`VideoFormat`").Trim('`')).ToList();
    var registered = VideoFormatRegistry.AllFormats.Select(f => f.Name).ToList();

    Assert.That(documented, Is.Unique, "The container table lists a format twice.");
    Assert.That(
      documented.OrderBy(x => x, StringComparer.Ordinal),
      Is.EqualTo(registered.OrderBy(x => x, StringComparer.Ordinal)).AsCollection,
      "README.md's container table and the generated registry disagree about which containers exist. "
      + $"Only in the README: {_Only(documented, registered)}. Only in the registry: {_Only(registered, documented)}.");
  }

  [Test]
  [Category("Unit")]
  public void EveryContainerRowStatesWhatTheRegistryHolds() {
    var problems = new List<string>();

    foreach (var row in _Rows("### Container support", "### Codec support")) {
      var id = row.Cell("`VideoFormat`").Trim('`');
      var entry = VideoFormatRegistry.AllFormats.FirstOrDefault(f => f.Name == id);
      if (entry == null)
        continue; // Reported by the completeness test; this one checks the cells of rows that exist.

      var extensions = Regex.Matches(row.Cell("Extensions"), "`([^`]+)`").Select(m => m.Groups[1].Value).ToArray();
      if (!extensions.SequenceEqual(entry.AllExtensions))
        problems.Add($"{id}.Extensions: documented=[{string.Join(", ", extensions)}], registered=[{string.Join(", ", entry.AllExtensions)}]");

      // Every registered entry is a reader by construction — the generator emits a registration only
      // for a container that has one.
      if (row.Cell("Demux") != _SUPPORTED)
        problems.Add($"{id}.Demux: documented={row.Cell("Demux")}, registered=a reader");

      // A writer is a separate type, and the generator gives it its own member of the format enum
      // under the reader's name plus "Writer". That member existing is the writer existing.
      var writes = Enum.IsDefined(typeof(VideoFormat), id + "Writer");
      var documentedMux = row.Cell("Mux") == _SUPPORTED;
      if (documentedMux != writes)
        problems.Add($"{id}.Mux: documented={row.Cell("Mux")}, registered={(writes ? "a writer" : "no writer")}");
    }

    Assert.That(problems, Is.Empty,
      "README.md's container table disagrees with the generated registry:\n" + string.Join("\n", problems));
  }

  // ============================================================================================
  // Codecs
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryRegisteredCodecHasARowAndEveryRowIsRegistered() {
    var documented = _CodecRows().Select(r => r.Name).ToList();
    var registered = VideoFormatRegistry.AllCodecs.Select(c => c.CodecName).ToList();

    Assert.That(documented, Is.Unique, "The codec table lists a codec twice.");
    Assert.That(
      documented.OrderBy(x => x, StringComparer.Ordinal),
      Is.EqualTo(registered.OrderBy(x => x, StringComparer.Ordinal)).AsCollection,
      "README.md's codec table and the generated registry disagree about which codecs exist. "
      + $"Only in the README: {_Only(documented, registered)}. Only in the registry: {_Only(registered, documented)}.");
  }

  [Test]
  [Category("Unit")]
  public void EveryCodecRowStatesWhetherItDecodesAndWhetherItEncodes() {
    var encoders = VideoFormatRegistry.AllEncoders.Select(e => e.CodecName).ToHashSet(StringComparer.Ordinal);
    var decoders = VideoFormatRegistry.AllCodecs.Select(c => c.CodecName).ToHashSet(StringComparer.Ordinal);
    var problems = new List<string>();

    foreach (var (name, decode, encode) in _CodecRows()) {
      // ✅ and ⚠️ both mean the registry builds a decoder; they differ over how much of the format
      // that decoder covers, which is prose and not something the registry knows.
      if (decode is not (_SUPPORTED or _PARTIAL))
        problems.Add($"{name}.Decode: '{decode}' is not one of {_SUPPORTED}, {_PARTIAL}");
      else if (!decoders.Contains(name))
        problems.Add($"{name}.Decode: documented={decode}, but no registered decoder answers to that name");

      if (encode is not (_SUPPORTED or _ABSENT))
        problems.Add($"{name}.Encode: '{encode}' is not one of {_SUPPORTED}, {_ABSENT}");
      else if ((encode == _SUPPORTED) != encoders.Contains(name))
        problems.Add($"{name}.Encode: documented={encode}, registered={(encoders.Contains(name) ? "an encoder" : "no encoder")}");
    }

    Assert.That(problems, Is.Empty,
      "README.md's codec table disagrees with the generated registry:\n" + string.Join("\n", problems));
  }

  [Test]
  [Category("Unit")]
  public void EveryRegisteredEncoderIsTickedInTheCodecTable() {
    var ticked = _CodecRows().Where(r => r.Encode == _SUPPORTED).Select(r => r.Name).ToList();
    var registered = VideoFormatRegistry.AllEncoders.Select(e => e.CodecName).ToList();

    Assert.That(
      ticked.OrderBy(x => x, StringComparer.Ordinal),
      Is.EqualTo(registered.OrderBy(x => x, StringComparer.Ordinal)).AsCollection,
      "README.md ticks Encode for a different set of codecs than the registry holds encoders for. "
      + $"Ticked without an encoder: {_Only(ticked, registered)}. Encoder without a tick: {_Only(registered, ticked)}.");
  }

  // ============================================================================================
  // Reading the tables
  // ============================================================================================

  private static IEnumerable<(string Name, string Decode, string Encode)> _CodecRows() {
    foreach (var row in _Rows("### Codec support", "### Not supported, and why"))
      yield return (_LinkText(row.Cell("Codec")), row.Cell("Decode"), row.Cell("Encode"));
  }

  /// <summary>The text of a markdown link, or the cell itself where it carries none.</summary>
  /// <remarks>
  /// Un-escapes the one pipe a codec name contains. A table cell cannot hold a bare <c>|</c>, and
  /// <c>H.264/AVC (ITU-T H.264 | ISO/IEC 14496-10)</c> is the codec's registered name.
  /// </remarks>
  private static string _LinkText(string cell) {
    var text = Regex.Match(cell, @"^\[(.*?)\]\(").Success
      ? Regex.Match(cell, @"^\[(.*?)\]\(").Groups[1].Value
      : cell;

    return text.Replace("\\|", "|");
  }

  private sealed record _Row(List<string> Columns, List<string> Cells) {

    internal string Cell(string column) {
      var index = this.Columns.IndexOf(column);
      Assert.That(index, Is.GreaterThanOrEqualTo(0), $"The table has no '{column}' column; it has {string.Join(", ", this.Columns)}.");
      return this.Cells[index];
    }
  }

  private static List<_Row> _Rows(string startHeading, string endHeading) {
    var section = _Slice(_Readme(), startHeading, endHeading);
    var rows = new List<_Row>();
    List<string>? columns = null;
    var separatorNext = false;

    foreach (var raw in section.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')) {
      var line = raw.Trim();
      if (!line.StartsWith('|')) {
        columns = null;
        continue;
      }

      // Split on pipes that are not escaped: a cell may carry an escaped one as part of its text.
      var cells = Regex.Split(line.Trim('|'), @"(?<!\\)\|").Select(c => c.Trim()).ToList();
      if (columns == null) {
        columns = cells;
        separatorNext = true;
        continue;
      }

      if (separatorNext) {
        separatorNext = false;
        continue;
      }

      if (cells.Count == columns.Count)
        rows.Add(new(columns, cells));
    }

    Assert.That(rows, Is.Not.Empty, $"No table rows found under '{startHeading}'.");
    return rows;
  }

  private static string _Slice(string text, string startHeading, string endHeading) {
    var start = text.IndexOf(startHeading, StringComparison.Ordinal);
    Assert.That(start, Is.GreaterThanOrEqualTo(0), $"README.md has no heading '{startHeading}'.");
    var end = text.IndexOf(endHeading, start + startHeading.Length, StringComparison.Ordinal);
    Assert.That(end, Is.GreaterThan(start), $"README.md has no heading '{endHeading}' after '{startHeading}'.");
    return text[start..end];
  }

  private static string _Readme() {
    for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent) {
      var path = Path.Combine(current.FullName, "Hawkynt.FileFormats.Video", "README.md");
      if (File.Exists(path))
        return File.ReadAllText(path);
    }

    throw new FileNotFoundException($"Could not find Hawkynt.FileFormats.Video/README.md walking up from '{AppContext.BaseDirectory}'.");
  }

  private static string _Only(IEnumerable<string> from, IEnumerable<string> other) {
    var missing = from.Except(other, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    return missing.Length == 0 ? "none" : string.Join("; ", missing);
  }
}
