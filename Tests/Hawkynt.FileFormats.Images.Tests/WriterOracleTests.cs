using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Holds the Oracle column of the format table to the thing it claims.
/// </summary>
/// <remarks>
/// A column naming the tool that checked a writer is only worth having if the naming can be wrong
/// and be caught. Left unchecked it becomes decoration, and a decorative column is worse than an
/// empty one: it reads like evidence. So every claim <c>[VerifiedBy]</c> makes is executable here —
/// the format is written, the named tool is run over what came out, and the picture it reconstructs
/// has to be the picture that went in.
/// <para/>
/// Two of the tests below need no tool at all and so run everywhere: a format cannot name an oracle
/// nothing here knows how to run, and a format with no writer cannot name one at all. Those are the
/// checks that keep the column honest on a machine with none of the oracles installed; the third
/// one, which actually runs them, reports inconclusive there instead of failing.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class WriterOracleTests {

  /// <summary>Where <c>ORACLE_SURVEY</c> points when the whole registry is to be re-measured.</summary>
  private const string _SURVEY_VARIABLE = "ORACLE_SURVEY";

  /// <summary>Where <c>ORACLE_SURVEY_ONLY</c> narrows the survey to a few named formats.</summary>
  private const string _SURVEY_ONLY_VARIABLE = "ORACLE_SURVEY_ONLY";

  /// <summary>Where <c>ORACLE_SURVEY_TOOLS</c> narrows the survey to a few of the installed tools.</summary>
  /// <remarks>
  /// A sweep costs one process per format per tool, and a tool newly taught to this fixture has to
  /// be swept over the whole registry before anything can be claimed from it. Asking the seven that
  /// have already been swept all over again multiplies that wait by seven for no new answer, so the
  /// sweep can be pointed at the tools whose answers are not yet known.
  /// </remarks>
  private const string _SURVEY_TOOLS_VARIABLE = "ORACLE_SURVEY_TOOLS";

  // ============================================================================================
  // Checks that need no tool
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void NoFormatClaimsAnOracleThisFixtureCannotRun() {
    var runnable = WriterOracleTool.Runnable.ToHashSet();

    var unrunnable = FormatRegistry.AllFormats
      .SelectMany(entry => entry.VerifiedBy.Select(oracle => (entry.Name, Oracle: oracle)))
      .Where(claim => !runnable.Contains(claim.Oracle))
      .Select(claim => $"{claim.Name} claims {claim.Oracle}")
      .OrderBy(static text => text, StringComparer.Ordinal)
      .ToList();

    Assert.That(unrunnable, Is.Empty,
      "A claim nothing can run is a claim nothing can check, which is what makes a column decoration. "
      + "Either give WriterOracleTool a runner for the tool or take the claim off the format:\n"
      + string.Join("\n", unrunnable));
  }

  [Test]
  [Category("Unit")]
  public void NoFormatWithoutAWriterClaimsAnOracle() {
    var claimed = FormatRegistry.AllFormats
      .Where(static entry => !entry.SupportsWrite && entry.VerifiedBy.Length != 0)
      .Select(static entry => entry.Name)
      .OrderBy(static name => name, StringComparer.Ordinal)
      .ToList();

    Assert.That(claimed, Is.Empty,
      "The claim is about a writer's output, and these formats have no writer: " + string.Join(", ", claimed));
  }

  [Test]
  [Category("Unit")]
  public void EveryOracleTheColumnCanPrintHasANameAndAPlaceToGetIt() {
    var incomplete = Enum.GetValues<ConformanceOracle>()
      .Where(static oracle => oracle != ConformanceOracle.None)
      .Where(static oracle => string.IsNullOrWhiteSpace(oracle.DisplayName()) || string.IsNullOrWhiteSpace(oracle.HomePage()))
      .Select(static oracle => oracle.ToString())
      .ToList();

    Assert.That(incomplete, Is.Empty,
      "The legend under the table is generated from these, so an oracle without a name or a link "
      + "prints as an unlinked mystery: " + string.Join(", ", incomplete));
  }

  // ============================================================================================
  // The check that runs the tools
  // ============================================================================================

  private static IEnumerable<TestCaseData> Claims() {
    var claims = FormatRegistry.AllFormats
      .Where(static entry => entry.VerifiedBy.Length != 0)
      .OrderBy(static entry => entry.Name, StringComparer.Ordinal)
      .SelectMany(static entry => entry.VerifiedBy.Select(oracle => (entry.Format, Oracle: oracle)))
      .ToList();

    // NUnit needs at least one case, and a registry with no claim at all is a legitimate state —
    // it is the state this started in.
    if (claims.Count == 0) {
      yield return new TestCaseData(ImageFormat.Unknown, ConformanceOracle.None).SetName("{m}(nothing is claimed)");
      yield break;
    }

    foreach (var (format, oracle) in claims)
      yield return new TestCaseData(format, oracle).SetName($"{{m}}({format}, {oracle})");
  }

  [TestCaseSource(nameof(Claims))]
  public void TheOracleAFormatClaims_ReadsBackWhatThatFormatWrites(ImageFormat format, ConformanceOracle oracle) {
    if (format == ImageFormat.Unknown)
      Assert.Pass("No format claims an oracle, so there is nothing to hold to one.");

    if (!WriterOracleTool.IsAvailable(oracle))
      Assert.Inconclusive(
        $"{oracle.DisplayName()} is not on this machine, so it has not been asked. "
        + $"Point the fixture at it or put it on PATH — see {nameof(WriterOracleTool)}.");

    var entry = FormatRegistry.GetEntry(format)!;
    var (verdict, detail) = _AskAbout(entry, oracle);

    // A tool that is installed but has no reader for this format cannot confirm the claim and cannot
    // refute it either, so it is inconclusive rather than a failure. The distinction matters because
    // the claims were placed from a survey on one machine: ImageMagick is built with different
    // delegates on different platforms, and its Windows build reads neither Aseprite nor EPS, which
    // says nothing about what this writer produces. A rejection is still a failure — that is the tool
    // reading the file and disagreeing, which is the whole point of the column.
    if (verdict == WriterOracleTool.Verdict.NoOpinion)
      Assert.Inconclusive(
        $"{oracle.DisplayName()} on this machine has no reader for {entry.Name}, so the claim is "
        + $"unconfirmed here rather than wrong: {detail}");

    Assert.That(verdict, Is.EqualTo(WriterOracleTool.Verdict.Accepted),
      $"{entry.Name} says {oracle.DisplayName()} has read what it writes, and it has not: {detail}");
  }

  /// <summary>Writes the format every way it says it can be written, until the tool accepts one.</summary>
  private static (WriterOracleTool.Verdict Verdict, string Detail) _AskAbout(FormatEntry entry, ConformanceOracle oracle) {
    var directory = Directory.CreateTempSubdirectory("oracleclaim");
    var best = WriterOracleTool.Verdict.NoOpinion;
    var detail = "the tool has no reader for any name this format goes by";

    try {
      foreach (var (width, height, mode) in OracleProbePicture.CasesFor(entry))
      foreach (var extension in _ExtensionsOf(entry)) {
        var path = Path.Combine(directory.FullName, "sample" + extension);

        try {
          if (!FormatRegistry.Write(OracleProbePicture.Sample(width, height, mode), entry.Format, new FileInfo(path)))
            continue;
        } catch (Exception) {
          // A size this format will not take. Another one on the list may be.
          continue;
        }

        var (verdict, why) = WriterOracleTool.Ask(oracle, path, width, height);
        if (verdict == WriterOracleTool.Verdict.Accepted)
          return (verdict, string.Empty);

        if (verdict != WriterOracleTool.Verdict.Rejected)
          continue;

        best = WriterOracleTool.Verdict.Rejected;
        detail = $"handed the {width}x{height} picture written as {extension}, {why}";
      }

      return (best, detail);
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  private static IEnumerable<string> _ExtensionsOf(FormatEntry entry) {
    yield return entry.PrimaryExtension;

    foreach (var extension in entry.AllExtensions)
      if (!string.Equals(extension, entry.PrimaryExtension, StringComparison.OrdinalIgnoreCase))
        yield return extension;
  }

  // ============================================================================================
  // Re-measuring the whole registry
  // ============================================================================================

  /// <summary>
  /// Asks every installed oracle about every writer and writes down what each said.
  /// </summary>
  /// <remarks>
  /// This is where the claims come from. It is opt-in because it starts a process per format per
  /// tool and takes minutes rather than seconds, and because its answer is only as complete as the
  /// machine running it: a build with none of the tools would survey nothing and conclude nothing,
  /// which is not the same as concluding no. So it never edits an attribute itself — it prints what
  /// it found, and the claims are moved onto the format types deliberately.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  [Explicit("Starts a process per format per tool; run it when re-measuring the Oracle column.")]
  public void SurveyEveryWriterAgainstEveryInstalledOracle() {
    var destination = Environment.GetEnvironmentVariable(_SURVEY_VARIABLE);
    if (string.IsNullOrWhiteSpace(destination))
      Assert.Inconclusive($"Set {_SURVEY_VARIABLE} to the file the survey should be written to.");

    var wanted = (Environment.GetEnvironmentVariable(_SURVEY_TOOLS_VARIABLE) ?? string.Empty)
      .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var available = WriterOracleTool.Runnable
      .Where(oracle => wanted.Count == 0 || wanted.Contains(oracle.ToString()))
      .Where(WriterOracleTool.IsAvailable)
      .ToArray();

    if (available.Length == 0)
      Assert.Inconclusive("None of the oracles is installed here, so the survey would find nothing and mean nothing.");

    var only = (Environment.GetEnvironmentVariable(_SURVEY_ONLY_VARIABLE) ?? string.Empty)
      .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var report = new StringBuilder();
    report.Append("# oracles asked: ").AppendLine(string.Join(", ", available.Select(o => o.DisplayName())));

    foreach (var entry in FormatRegistry.SupportedWriteFormats
               .Where(entry => only.Count == 0 || only.Contains(entry.Name))
               .OrderBy(static e => e.Name, StringComparer.Ordinal)) {
      var accepted = new List<ConformanceOracle>();
      var refusals = new List<string>();

      foreach (var oracle in available) {
        var (verdict, why) = _AskAbout(entry, oracle);
        if (verdict == WriterOracleTool.Verdict.Accepted)
          accepted.Add(oracle);
        else if (verdict == WriterOracleTool.Verdict.Rejected)
          refusals.Add($"{oracle}: {why}");
      }

      report.Append(entry.Format).Append('\t')
        .Append(accepted.Count == 0 ? "none" : string.Join(",", accepted.Select(static oracle => oracle.ToString())))
        .Append('\t')
        .AppendLine(string.Join(" | ", refusals));
    }

    File.WriteAllText(destination!, report.ToString());
    TestContext.Out.WriteLine($"Survey written to {destination}");
  }
}
