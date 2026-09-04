using System;
using System.IO;
using System.Linq;
using System.Text;
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

  private static string _BuildMatrix() {
    var builder = new StringBuilder();
    builder.Append("| Format | Extensions | Read | Write | Info | Multi | Optimizer |\n");
    builder.Append("| --- | --- | :---: | :---: | :---: | :---: | :---: |\n");

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
        .Append(" |\n");
    }

    return builder.ToString().TrimEnd('\n');
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
