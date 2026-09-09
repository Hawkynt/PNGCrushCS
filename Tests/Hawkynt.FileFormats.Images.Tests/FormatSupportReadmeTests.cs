using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests;

[TestFixture]
public sealed class FormatSupportReadmeTests {

  private const string _BEGIN_MARKER = "<!-- IMAGE-FORMATS:BEGIN generated from FormatRegistry -- do not edit this table by hand -->";
  private const string _END_MARKER = "<!-- IMAGE-FORMATS:END -->";
  private const string _SUPPORT_HEADING = "## 🧩 Format support";
  private const string _NEXT_HEADING = "## 🚀 Quick start";
  private const string _UPDATE_ENVIRONMENT_VARIABLE = "UPDATE_IMAGE_FORMAT_README";
  private const string _WRITE_COVERAGE_PREFIX = "- **Write coverage** — ";
  private const string _WRITE_COVERAGE_TEXT = "- **Write coverage** — Read support does not imply authoring support. Use the exhaustive matrix above or filter `FormatRegistry.AllFormats` by `SupportsWrite` for the exact current set of formats that can encode an arbitrary `RawImage`.";

  [Test]
  [Category("Unit")]
  public void PackageReadme_ContainsEveryRegisteredFormatWithReadWriteCapabilities() {
    var readmePath = _FindPackageReadme();
    var readme = File.ReadAllText(readmePath).ReplaceLineEndings("\n");
    var expected = _BuildMatrix();

    if (Environment.GetEnvironmentVariable(_UPDATE_ENVIRONMENT_VARIABLE) == "1") {
      readme = _RewriteSupportSection(readme, expected);
      readme = _RewriteWriteCoverage(readme);
      File.WriteAllText(readmePath, readme.ReplaceLineEndings("\r\n"));
    }

    var begin = readme.IndexOf(_BEGIN_MARKER, StringComparison.Ordinal);
    var end = readme.IndexOf(_END_MARKER, StringComparison.Ordinal);
    if (begin < 0 || end < 0 || end <= begin)
      Assert.Fail($"The image package README needs the generated format-support markers. Expected region:\n\n{expected}");

    begin += _BEGIN_MARKER.Length;
    var actual = readme[begin..end].Trim('\n', '\r');
    Assert.Multiple(() => {
      Assert.That(actual, Is.EqualTo(expected),
        $"The image package format table has drifted from FormatRegistry. Regenerate it with {_UPDATE_ENVIRONMENT_VARIABLE}=1 or replace the marked region with:\n\n{expected}");
      Assert.That(readme, Does.Contain(_WRITE_COVERAGE_TEXT),
        "The README must not carry a second hand-maintained read/write capability count.");
    });
  }

  private const string _READ_ONLY_HEADING = "### Registered but read-only";

  /// <summary>
  /// The one capability list in the README that is still written by hand must name every read-only
  /// format and no others, and count them correctly.
  /// </summary>
  /// <remarks>
  /// The matrix above it regenerates, so it cannot drift; this paragraph is where the reason each
  /// format has no writer is recorded, and a reason cannot be generated. That makes it the one place
  /// left where the README can quietly stop being true — and it had: it said 36 while the registry
  /// held a different number, so a format could lose or gain a writer and the prose would go on
  /// describing the old set.
  /// <para/>
  /// The names are checked, not the reasons. Whoever registers or unregisters a writer has to come
  /// here and say why, which is the whole point of the paragraph existing beside a generated table.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void PackageReadme_ReadOnlyParagraph_NamesEveryFormatWithoutAWriter() {
    var readme = File.ReadAllText(_FindPackageReadme()).ReplaceLineEndings("\n");

    var start = readme.IndexOf(_READ_ONLY_HEADING, StringComparison.Ordinal);
    Assert.That(start, Is.GreaterThanOrEqualTo(0), $"the README no longer has a '{_READ_ONLY_HEADING}' section");

    var end = readme.IndexOf(_NEXT_HEADING, start, StringComparison.Ordinal);
    Assert.That(end, Is.GreaterThan(start), "the read-only section is not followed by the quick-start heading");
    var paragraph = readme[start..end];

    var readOnly = FormatRegistry.AllFormats
      .Where(static entry => !entry.SupportsWrite)
      .Select(static entry => entry.Name)
      .OrderBy(static name => name, StringComparer.Ordinal)
      .ToList();

    // Whole-word so that a format named inside a longer one does not count as mentioned.
    var mentioned = new HashSet<string>(
      Regex.Matches(paragraph, @"\b[A-Za-z][A-Za-z0-9]*\b").Select(static match => match.Value),
      StringComparer.Ordinal);

    var unnamed = readOnly.Where(name => !mentioned.Contains(name)).ToList();
    var stillNamed = FormatRegistry.AllFormats
      .Where(entry => entry.SupportsWrite && mentioned.Contains(entry.Name))
      .Select(static entry => entry.Name)
      .OrderBy(static name => name, StringComparer.Ordinal)
      .ToList();

    Assert.Multiple(() => {
      Assert.That(unnamed, Is.Empty,
        "these formats have no writer and the read-only section does not say why: " + string.Join(", ", unnamed));
      Assert.That(stillNamed, Is.Empty,
        "these formats gained a writer and the read-only section still lists them: " + string.Join(", ", stillNamed));
      Assert.That(paragraph, Does.Contain($"These {readOnly.Count} entries"),
        $"the read-only section must open with the count the registry holds, which is {readOnly.Count}");
    });
  }

  private static string _BuildMatrix() {
    var builder = new StringBuilder();
    builder.Append("| Format | Extensions | Read | Write | Info | Multi | Optimizer | Oracle |\n");
    builder.Append("| --- | --- | :---: | :---: | :---: | :---: | :---: | --- |\n");

    foreach (var entry in FormatRegistry.AllFormats
               .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
               .ThenBy(static entry => entry.Format)) {
      var extensions = entry.AllExtensions.Length == 0
        ? "—"
        : string.Join(", ", entry.AllExtensions.Select(static extension => $"`{_Escape(extension)}`"));

      builder.Append("| ")
        .Append(_Escape(entry.Name))
        .Append(" | ")
        .Append(extensions)
        .Append(" | ")
        .Append(entry.SupportsRead ? "✅" : "—")
        .Append(" | ")
        .Append(entry.SupportsWrite ? "✅" : "—")
        .Append(" | ")
        .Append(entry.ReadImageInfo != null ? "✅" : "—")
        .Append(" | ")
        .Append(entry.SupportsMultiImage ? "✅" : "—")
        .Append(" | ")
        .Append((entry.Capabilities & FormatCapability.HasDedicatedOptimizer) != 0 ? "✅" : "—")
        .Append(" | ")
        .Append(_OracleCell(entry))
        .Append(" |\n");
    }

    builder.Append('\n').Append(_BuildLegend());

    return builder.ToString().TrimEnd('\n');
  }

  /// <summary>What the Oracle column says about one format.</summary>
  /// <remarks>
  /// Three answers, and they mean three different things. A tool's name is a claim that that program
  /// has read what this writer produced. <c>none</c> is a writer nothing outside this repository has
  /// ever looked at, which is the answer that matters most and the one a blank cell would hide. An
  /// em dash is no writer at all, so there is nothing for an oracle to have read.
  /// </remarks>
  private static string _OracleCell(FormatEntry entry) {
    if (!entry.SupportsWrite)
      return "—";

    return entry.VerifiedBy.Length == 0
      ? "none"
      : string.Join(", ", entry.VerifiedBy.Select(static oracle => oracle.DisplayName()));
  }

  /// <summary>
  /// The one place each oracle is linked, generated from the oracles the table actually names.
  /// </summary>
  /// <remarks>
  /// A link in every cell would put several hundred copies of the same URL into a table of nearly a
  /// thousand rows and make the column unreadable in the raw file, which is where most of this
  /// README is read. So the cells carry the tool's name and the names are resolved once, here.
  /// </remarks>
  private static string _BuildLegend() {
    var used = FormatRegistry.AllFormats
      .SelectMany(static entry => entry.VerifiedBy)
      .Distinct()
      .OrderBy(static oracle => oracle.DisplayName(), StringComparer.OrdinalIgnoreCase)
      .ToList();

    var builder = new StringBuilder();
    builder.Append(
      "**Oracle** — the tool outside this repository that has read what the writer produces. "
      + "`none` means nothing but this package's own reader ever has, and a reader agreeing with the "
      + "writer beside it proves only that the two share one reading of the format; `—` means there "
      + "is no writer, so there is nothing for anything to have read. What a name in this column "
      + "states exactly: handed a file this writer produced, under one of the format's own "
      + "extensions and at one of the sizes the format declares, that tool decoded it back to a "
      + "picture of the same size which is not a blank canvas. That is weaker than the pixel-for-pixel "
      + "comparisons in `Tests/Conformance.Recoil.Tests` and in the codec evidence below, and it is "
      + "the one thing that could be asked of every writer here rather than of a chosen few. It does "
      + "not prove the pixels agree, and where two unrelated formats share an extension and a "
      + "geometry it can be a tool reading the other one.");

    if (used.Count == 0)
      return builder.Append('\n').ToString();

    builder.Append(" The tools: ");
    builder.Append(string.Join(" · ", used.Select(static oracle => $"[{oracle.DisplayName()}]({oracle.HomePage()})")));
    builder.Append(".\n");

    return builder.ToString();
  }

  private static string _RewriteSupportSection(string readme, string matrix) {
    var generated = $"{_BEGIN_MARKER}\n{matrix}\n{_END_MARKER}";
    var begin = readme.IndexOf(_BEGIN_MARKER, StringComparison.Ordinal);
    var end = readme.IndexOf(_END_MARKER, StringComparison.Ordinal);
    if (begin >= 0 && end > begin)
      return readme[..begin] + generated + readme[(end + _END_MARKER.Length)..];

    var support = readme.IndexOf(_SUPPORT_HEADING, StringComparison.Ordinal);
    var next = readme.IndexOf(_NEXT_HEADING, StringComparison.Ordinal);
    if (support < 0 || next <= support)
      throw new InvalidDataException("Could not locate the image package format-support section.");

    var section = $"""
      {_SUPPORT_HEADING}

      This table is generated from `FormatRegistry.AllFormats`, which is the authoritative package inventory. Every registered image format has one row; extensions and capabilities come directly from its `FormatEntry`.

      `✅` means the corresponding registry operation is available. A registered operation can still have format-specific subset limitations described later in this README; the matrix records capability presence, not a claim that every producer-specific variant is implemented.

      {generated}

      """;

    return readme[..support] + section + readme[next..];
  }

  private static string _RewriteWriteCoverage(string readme) {
    var start = readme.IndexOf(_WRITE_COVERAGE_PREFIX, StringComparison.Ordinal);
    if (start < 0)
      throw new InvalidDataException("Could not locate the README write-coverage limitation.");

    var end = readme.IndexOf('\n', start);
    if (end < 0)
      end = readme.Length;

    return readme[..start] + _WRITE_COVERAGE_TEXT + readme[end..];
  }

  private static string _Escape(string value)
    => value.Replace("|", "\\|", StringComparison.Ordinal);

  private static string _FindPackageReadme() {
    for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
         directory is not null;
         directory = directory.Parent) {
      var candidate = Path.Combine(directory.FullName, "Hawkynt.FileFormats.Images", "README.md");
      if (File.Exists(candidate))
        return candidate;
    }

    throw new FileNotFoundException("Could not locate Hawkynt.FileFormats.Images/README.md from the test output directory.");
  }
}
