using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4.Tests;

/// <summary>
/// The tables are the format, so what can be checked about them without a reference decoder is
/// checked here: that each is a code at all, that the limits derived from a run-level table agree
/// with the table they were derived from, and that a codeword can be written and read back.
/// </summary>
/// <remarks>
/// None of this can tell a right table from a wrong one — only decoding real files can, and that is
/// what the codec's own remarks record. What it does catch is the class of mistake that survives a
/// transcription: a table that is not a prefix code decodes something, a derived limit that is one out
/// puts every coefficient after an escape in the wrong place, and both of those look like a picture.
/// </remarks>
[TestFixture]
public sealed class MsMpeg4TableTests {

  /// <summary>How many entries a version 3 DC table holds, the last of them the escape.</summary>
  private const int _DC_ENTRIES = 120;

  /// <summary>How many entries a version 1 or 2 DC table holds: one per differential.</summary>
  private const int _V2_DC_ENTRIES = 512;

  private static IEnumerable<TestCaseData> _RunLevelTables() {
    for (var i = 0; i < MsMpeg4Tables.RunLevel.Length; ++i)
      yield return new TestCaseData(i).SetName($"RunLevelTable{i}");
  }

  [TestCaseSource(nameof(_RunLevelTables))]
  [Category("Unit")]
  public void EveryRowOfARunLevelTableIsFoundBackByItsTriple(int which) {
    var table = MsMpeg4Tables.RunLevel[which];
    var problems = new List<string>();

    for (var index = 0; index < table.EscapeIndex; ++index) {
      var found = table.IndexOf(table.IsLast(index), table.RunOf(index), table.LevelOf(index));
      if (found != index)
        problems.Add(
          $"row {index} states (last={table.IsLast(index)}, run={table.RunOf(index)}, level={table.LevelOf(index)}) "
          + $"and searching for that triple found row {found}");
    }

    Assert.That(problems, Is.Empty,
      $"{table.Name}: an encoder looks a triple up by arithmetic and a decoder reads the row back, so the two have to "
      + "agree on every row.\n" + string.Join("\n", problems));
  }

  [TestCaseSource(nameof(_RunLevelTables))]
  [Category("Unit")]
  public void TheEscapeLimitsAreTheLargestTheTableStates(int which) {
    var table = MsMpeg4Tables.RunLevel[which];
    var largestLevel = new Dictionary<(bool Last, int Run), int>();
    var largestRun = new Dictionary<(bool Last, int Level), int>();

    for (var index = 0; index < table.EscapeIndex; ++index) {
      var last = table.IsLast(index);
      var run = table.RunOf(index);
      var level = table.LevelOf(index);

      largestLevel[(last, run)] = Math.Max(largestLevel.GetValueOrDefault((last, run)), level);
      largestRun[(last, level)] = Math.Max(largestRun.GetValueOrDefault((last, level)), run);
    }

    Assert.Multiple(() => {
      foreach (var ((last, run), level) in largestLevel)
        Assert.That(table.LargestLevel(last, run), Is.EqualTo(level), $"{table.Name}: largest level for run {run}");

      foreach (var ((last, level), run) in largestRun)
        Assert.That(table.LargestRun(last, level), Is.EqualTo(run), $"{table.Name}: longest run for level {level}");
    });
  }

  [TestCaseSource(nameof(_RunLevelTables))]
  [Category("Unit")]
  public void ARunLevelTableStatesEveryRowAndNothingElse(int which) {
    var table = MsMpeg4Tables.RunLevel[which];

    Assert.Multiple(() => {
      Assert.That(table.LastIndex, Is.GreaterThan(0).And.LessThan(table.EscapeIndex),
        "the first row that ends a block sits inside the table");

      // A row past the escape would be a codeword nothing can reach, and a row short of it would be a
      // triple the escape forms are measured against and that no code states.
      Assert.That(table.LengthOf(table.EscapeIndex), Is.GreaterThan(0), "the escape has a codeword of its own");
    });
  }

  [Test]
  [Category("Unit")]
  public void BothMotionVectorTablesAreCompleteCodes() {
    // Built from lengths alone, which only reproduces the codewords while the code is complete —
    // Kraft's sum exactly one. The construction refuses an overrun; this states the other half, that
    // nothing is left over, by checking the two escapes came out where the reference encoder writes
    // them: eight zero bits in the first table and the four bits 1011 in the second.
    Assert.Multiple(() => {
      Assert.That(MsMpeg4Tables.MotionVectorCodes[0].EscapeLength, Is.EqualTo(8));
      Assert.That(MsMpeg4Tables.MotionVectorCodes[0].EscapeCode, Is.EqualTo(0x00));
      Assert.That(MsMpeg4Tables.MotionVectorCodes[1].EscapeLength, Is.EqualTo(4));
      Assert.That(MsMpeg4Tables.MotionVectorCodes[1].EscapeCode, Is.EqualTo(0x0B));
    });
  }

  [Test]
  [Category("Unit")]
  public void AMotionVectorTableReadsBackEveryVectorAnEncoderWrites() {
    var problems = new List<string>();

    for (var table = 0; table < 2; ++table)
      for (var x = 0; x < 64; ++x)
        for (var y = 0; y < 64; ++y) {
          // The escape's own pair cannot be written: it is what says "the pair follows".
          if (x == 0 && y == 0)
            continue;

          var writer = new MsMpeg4BitWriter();
          MsMpeg4Tables.MotionVectorCodes[table].Write(writer, x, y);
          var reader = new Mpeg4BitReader(writer.ToArray());

          var code = MsMpeg4Tables.MotionVector[table].Read(ref reader);
          var (readX, readY) = code != 0 ? (code >> 8, code & 0xFF) : (reader.ReadBits(6), reader.ReadBits(6));

          if (readX != x || readY != y)
            problems.Add($"table {table}: wrote ({x}, {y}) and read back ({readX}, {readY})");
        }

    Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(10)));
  }

  [Test]
  [Category("Unit")]
  public void TheVersionTwoDcTableIsTheStandardsWithEveryBitInverted() {
    // ISO/IEC 14496-2 Table B-13 states a luminance differential of nought as "011" in three bits;
    // Microsoft's is the complement of that and carries no magnitude bits after it.
    var (codes, lengths) = MsMpeg4Tables.V2DcCodes[0];

    Assert.Multiple(() => {
      Assert.That(codes, Has.Length.EqualTo(_V2_DC_ENTRIES));
      Assert.That(lengths[MsMpeg4Tables.V2DcBias], Is.EqualTo(3));
      Assert.That(codes[MsMpeg4Tables.V2DcBias], Is.EqualTo(0b100));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryVersionTwoDcDifferentialReadsBackAsItself() {
    var problems = new List<string>();

    for (var plane = 0; plane < 2; ++plane) {
      var (codes, lengths) = MsMpeg4Tables.V2DcCodes[plane];

      for (var differential = -MsMpeg4Tables.V2DcBias; differential < MsMpeg4Tables.V2DcBias; ++differential) {
        var at = differential + MsMpeg4Tables.V2DcBias;
        var writer = new MsMpeg4BitWriter();
        writer.Write(codes[at], lengths[at]);

        var reader = new Mpeg4BitReader(writer.ToArray());
        var read = MsMpeg4Tables.V2Dc[plane].Read(ref reader) - MsMpeg4Tables.V2DcBias;
        if (read != differential)
          problems.Add($"plane {plane}: wrote {differential} and read back {read}");
      }
    }

    Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(10)));
  }

  [Test]
  [Category("Unit")]
  public void TheVersionThreeDcTablesHoldOneEntryPerMagnitudeAndAnEscape() {
    Assert.Multiple(() => {
      Assert.That(MsMpeg4Data.Dc0LuminanceCodes, Has.Length.EqualTo(_DC_ENTRIES));
      Assert.That(MsMpeg4Data.Dc0ChrominanceCodes, Has.Length.EqualTo(_DC_ENTRIES));
      Assert.That(MsMpeg4Data.Dc1LuminanceCodes, Has.Length.EqualTo(_DC_ENTRIES));
      Assert.That(MsMpeg4Data.Dc1ChrominanceCodes, Has.Length.EqualTo(_DC_ENTRIES));
      Assert.That(MsMpeg4Tables.V3DcEscape, Is.EqualTo(_DC_ENTRIES - 1));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheVersionThreeDcStepsAreTheOnesEveryPlayerUsesAndNotTheStandardsAboveQuantiserTwentyFour() {
    // The two agree up to twenty-four and part company above it. Which of them a real Microsoft
    // decoder used is written down nowhere; what is known is what the decoder these files are played
    // with does, and this states that this library followed it rather than the standard.
    Assert.Multiple(() => {
      for (var quantiser = 1; quantiser <= 24; ++quantiser) {
        Assert.That(MsMpeg4Data.Version3LuminanceDcStep[quantiser],
          Is.EqualTo(Mpeg4Quantisation.DcScaler(quantiser, isLuminance: true)), $"luminance at {quantiser}");
        Assert.That(MsMpeg4Data.Version3ChrominanceDcStep[quantiser],
          Is.EqualTo(Mpeg4Quantisation.DcScaler(quantiser, isLuminance: false)), $"chrominance at {quantiser}");
      }

      Assert.That(MsMpeg4Data.Version3LuminanceDcStep[25], Is.EqualTo(33));
      Assert.That(Mpeg4Quantisation.DcScaler(25, isLuminance: true), Is.EqualTo(34));
      Assert.That(MsMpeg4Data.Version3LuminanceDcStep[31], Is.EqualTo(39));
      Assert.That(MsMpeg4Data.Version3ChrominanceDcStep[31], Is.EqualTo(22));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryMacroblockTableReadsBackEveryValueItStates() {
    Assert.Multiple(() => {
      _AssertRoundTrips(MsMpeg4Tables.IntraMacroblockPattern,
        MsMpeg4Data.IntraMacroblockPatternCodes, MsMpeg4Data.IntraMacroblockPatternLengths);
      _AssertRoundTrips(MsMpeg4Tables.MacroblockNonIntra,
        MsMpeg4Data.MacroblockNonIntraCodes, MsMpeg4Data.MacroblockNonIntraLengths);
      _AssertRoundTrips(MsMpeg4Tables.V2MacroblockType,
        MsMpeg4Data.V2MacroblockTypeCodes, MsMpeg4Data.V2MacroblockTypeLengths);
      _AssertRoundTrips(MsMpeg4Tables.V2IntraChromaPattern,
        MsMpeg4Data.V2IntraChromaPatternCodes, MsMpeg4Data.V2IntraChromaPatternLengths);
      _AssertRoundTrips(MsMpeg4Tables.CodedBlockPatternY,
        MsMpeg4Data.CodedBlockPatternYCodes, MsMpeg4Data.CodedBlockPatternYLengths);
      _AssertRoundTrips(MsMpeg4Tables.MotionVectorMagnitude,
        MsMpeg4Data.MotionVectorMagnitudeCodes, MsMpeg4Data.MotionVectorMagnitudeLengths);
      _AssertRoundTrips(MsMpeg4Tables.IntraMacroblock,
        MsMpeg4Data.IntraMacroblockCodes, MsMpeg4Data.IntraMacroblockLengths);
      _AssertRoundTrips(MsMpeg4Tables.InterMacroblock,
        MsMpeg4Data.InterMacroblockCodes, MsMpeg4Data.InterMacroblockLengths);
    });
  }

  private static void _AssertRoundTrips(MsMpeg4VlcTable table, int[] codes, int[] lengths) {
    for (var value = 0; value < codes.Length; ++value) {
      if (lengths[value] == 0)
        continue;

      var writer = new MsMpeg4BitWriter();
      writer.Write(codes[value], lengths[value]);

      // A codeword shorter than the table's longest is padded past its end, which is what the reader
      // has to tolerate on the last codeword of every picture.
      var reader = new Mpeg4BitReader(writer.ToArray());
      Assert.That(table.Read(ref reader), Is.EqualTo(value));
      Assert.That(reader.BitPosition, Is.EqualTo(lengths[value]));
    }
  }
}
