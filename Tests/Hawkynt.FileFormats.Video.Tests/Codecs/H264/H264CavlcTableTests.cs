using System;
using System.IO;
using System.Linq;

namespace FileFormat.Codecs.H264.Tests;

/// <summary>
/// The variable-length code tables of ITU-T H.264, clause 9.2, checked as tables.
/// </summary>
/// <remarks>
/// These are six hundred-odd codes transcribed by eye from a printed annex, and a single wrong bit in
/// one of them does not fail: it decodes a different number of coefficients, and the picture comes out
/// plausible and wrong. Three defences, and each catches something the others cannot.
/// <para/>
/// The lookup's construction catches an ambiguity — two codes where one is a prefix of the other,
/// which no valid table has. Reading every code back catches a code attached to the wrong value only
/// if the two transcriptions differ, which they do not, so what it really checks is the lookup rather
/// than the table. And the value sets below catch a whole row transcribed into the wrong place, which
/// is what a table's shape is for. What none of them catches is a code and a value both wrong
/// together in the same way; that is what the sample-by-sample comparison against a reference decoder
/// is for, and it is where these tables were actually proved.
/// </remarks>
[TestFixture]
public sealed class H264CavlcTableTests {

  [Test]
  public void EveryTableBuildsWithoutAmbiguity()
    // Construction throws when one code is a prefix of another, so reaching the count is the check.
    => Assert.That(H264CavlcTables.AllTables.Count(), Is.EqualTo(5 + 15 + 3 + 7));

  [Test]
  public void EveryCodeReadsBackAsItsOwnValueAndLength() {
    foreach (var table in H264CavlcTables.AllTables)
      foreach (var (code, value) in table.Entries) {
        var bits = code.Replace(" ", string.Empty);

        // The code followed by ones, so that a decoder reading too few bits is caught by the value
        // and one reading too many by the position.
        var padded = bits.PadRight(table.MaxLength + 8, '1');
        var reader = new H264BitReader(_Pack(padded));

        Assert.That(table.Read(ref reader), Is.EqualTo(value), $"{table.Name}: '{code}'");
        Assert.That(reader.BitPosition, Is.EqualTo(bits.Length), $"{table.Name}: '{code}' length");
      }
  }

  [Test]
  public void NoTableHasTwoCodesForOneValue() {
    foreach (var table in H264CavlcTables.AllTables) {
      var values = table.Entries.Select(entry => entry.Value).ToArray();
      Assert.That(values, Is.Unique, table.Name);
    }
  }

  [Test]
  public void CoeffTokenSpansEveryCountAndTrailingOnesCombination() {
    // Table 9-5's first four columns each hold one code per (TrailingOnes, TotalCoeff) pair that can
    // occur: no trailing ones when there are no coefficients, and never more trailing ones than
    // coefficients or more than three.
    var expected = Enumerable.Range(0, 17)
      .SelectMany(total => Enumerable.Range(0, Math.Min(total, 3) + 1).Select(ones => (total << 2) | ones))
      .OrderBy(value => value)
      .ToArray();

    foreach (var table in H264CavlcTables.AllTables.Take(4))
      Assert.That(table.Entries.Select(entry => entry.Value).OrderBy(value => value).ToArray(),
        Is.EqualTo(expected), table.Name);
  }

  [Test]
  public void ChromaDcCoeffTokenStopsAtFourCoefficients() {
    // The 2x2 chroma DC block of 4:2:0 has four coefficients, so its column of Table 9-5 is the same
    // shape cut off at four.
    var expected = Enumerable.Range(0, 5)
      .SelectMany(total => Enumerable.Range(0, Math.Min(total, 3) + 1).Select(ones => (total << 2) | ones))
      .OrderBy(value => value)
      .ToArray();

    var table = H264CavlcTables.AllTables.ElementAt(4);
    Assert.That(table.Entries.Select(entry => entry.Value).OrderBy(value => value).ToArray(), Is.EqualTo(expected));
  }

  [Test]
  public void EachTotalZerosTableCoversExactlyThePositionsItsBlockHasLeft() {
    // Tables 9-7 and 9-8 are indexed by how many coefficients were found: with n of a block's sixteen
    // positions non-zero, between zero and 16 − n of the rest are zeroes, and each of those has a code.
    var tables = H264CavlcTables.AllTables.Skip(5).Take(15).ToArray();

    for (var coefficients = 1; coefficients <= 15; ++coefficients)
      Assert.That(tables[coefficients - 1].Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
        Is.EqualTo(Enumerable.Range(0, 16 - coefficients + 1).ToArray()),
        $"tzVlcIndex {coefficients}");
  }

  [Test]
  public void EachRunBeforeTableCoversTheRunsItsRemainingZeroesAllow() {
    // Table 9-10: with n zeroes left to place, a run is between none and all of them — except in the
    // last column, which serves every n above six and runs to fourteen.
    var tables = H264CavlcTables.AllTables.Skip(23).ToArray();

    for (var zerosLeft = 1; zerosLeft <= 6; ++zerosLeft)
      Assert.That(tables[zerosLeft - 1].Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
        Is.EqualTo(Enumerable.Range(0, zerosLeft + 1).ToArray()), $"zerosLeft {zerosLeft}");

    Assert.That(tables[6].Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 15).ToArray()));
  }

  [Test]
  public void CodedBlockPatternIsABijectionOverThePatternsFourTwoZeroAllows() {
    // Table 9-4 (a) maps 48 code numbers onto the 48 patterns a 4:2:0 macroblock can have: four bits
    // of luma and one of three chroma states. Each reading must be a permutation of those, or some
    // pattern is unreachable and another is reachable twice.
    var intra = new int[48];
    var inter = new int[48];

    for (var codeNum = 0; codeNum < 48; ++codeNum) {
      intra[codeNum] = _ReadCodedBlockPattern(codeNum, isIntra: true);
      inter[codeNum] = _ReadCodedBlockPattern(codeNum, isIntra: false);
    }

    Assert.That(intra.OrderBy(v => v).ToArray(), Is.EqualTo(Enumerable.Range(0, 48).ToArray()));
    Assert.That(inter.OrderBy(v => v).ToArray(), Is.EqualTo(Enumerable.Range(0, 48).ToArray()));
  }

  [Test]
  public void ACodeNumberBeyondTheCodedBlockPatternTableIsRefused()
    => Assert.That(() => _ReadCodedBlockPattern(48, isIntra: true),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("Table 9-4"));

  [Test]
  public void BitsThatAreNoCodeAreRefusedWithTheTablesName() {
    // Sixteen zeroes are a code in no column of Table 9-5, and the refusal has to say which table was
    // being read: a decoder that fell through to the wrong column reports exactly this.
    Assert.That(() => _ReadCoeffToken(0),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("Table 9-5"));
  }

  [Test]
  public void TheChromaDcColumnOfFourTwoTwoIsRefusedRatherThanMisread() {
    Assert.That(() => _ReadCoeffToken(-2),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("4:2:2"));
  }

  /// <summary>Reads a coeff_token out of sixteen zero bits, which no column of Table 9-5 defines.</summary>
  private static int _ReadCoeffToken(int nC) {
    var reader = new H264BitReader(new byte[4]);
    return H264CavlcTables.ReadCoeffToken(ref reader, nC);
  }

  private static int _ReadCodedBlockPattern(int codeNum, bool isIntra) {
    var builder = new H264TestStream().Unsigned(codeNum);
    var reader = new H264BitReader(builder.RawPayload());
    return H264CavlcTables.ReadCodedBlockPattern(ref reader, isIntra);
  }

  private static byte[] _Pack(string bits) {
    var bytes = new byte[(bits.Length + 7) / 8];
    for (var i = 0; i < bits.Length; ++i)
      if (bits[i] == '1')
        bytes[i >> 3] |= (byte)(0x80 >> (i & 7));

    return bytes;
  }
}
