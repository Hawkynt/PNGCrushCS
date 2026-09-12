using System;
using System.Collections.Generic;
using System.Linq;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// Holds the residual writer against the residual reader.
/// </summary>
/// <remarks>
/// The residual syntax is the one place in the standard where being a single bin out of step looks
/// exactly like working: the decoder carries on, produces coefficients, and they are not the ones
/// that were written. Nothing short of decoding what was encoded and comparing every coefficient can
/// tell those two apart, so that is what these do — over blocks chosen to reach the parts of the
/// syntax that a picture of a real scene reaches rarely: levels past the escape threshold, sub-blocks
/// with nothing in them between sub-blocks that have something, and a coefficient alone in the
/// corner furthest from the direct current one.
/// </remarks>
[TestFixture]
public sealed class H265ResidualWriterTests {

  private static H265PictureParameterSet _Pps => _ParsedPps.Value;

  private static readonly Lazy<H265PictureParameterSet> _ParsedPps = new(() => {
    var stream = new H265TestStream().PictureParameterSet(signDataHiding: false).ToArray();
    foreach (var nal in H265NalReader.SplitAnnexB(stream))
      if (nal.Type == H265NalUnitType.PictureParameterSet)
        return H265PictureParameterSet.Parse(nal.Payload);

    throw new InvalidOperationException("the fixture stream carries a picture parameter set");
  });

  [TestCase(2)]
  [TestCase(3)]
  [TestCase(4)]
  [TestCase(5)]
  [Category("RoundTrip")]
  [Category("Boundary")]
  public void ASingleCoefficientInTheCornerRoundTrips(int log2Size) {
    var size = 1 << log2Size;
    var block = new int[size * size];
    block[^1] = -3;

    _AssertRoundTrips(block, log2Size, cIdx: 0, intraPredMode: -1);
  }

  [TestCase(2)]
  [TestCase(3)]
  [TestCase(4)]
  [TestCase(5)]
  [Category("RoundTrip")]
  [Category("Boundary")]
  public void OnlyTheDirectCurrentCoefficientRoundTrips(int log2Size) {
    var size = 1 << log2Size;
    var block = new int[size * size];
    block[0] = 17;

    _AssertRoundTrips(block, log2Size, cIdx: 0, intraPredMode: -1);
  }

  [Test]
  [Category("RoundTrip")]
  [Category("Boundary")]
  public void ASubBlockWithNothingInItBetweenTwoThatHaveSomethingRoundTrips() {
    // The middle sub-block's coded_sub_block_flag is the only thing that says it is empty, and the
    // one after it has to infer its own direct current coefficient because nothing else in it is
    // significant. Both inferences are places the two directions can disagree without either
    // noticing.
    var block = new int[16 * 16];
    block[0] = 4;                    // the first sub-block
    block[(4 << 4) + 4] = 0;         // the second, left empty
    block[(8 << 4) + 8] = 1;         // the third, holding only its own corner
    block[(12 << 4) + 12] = -9;      // the last

    _AssertRoundTrips(block, log2Size: 4, cIdx: 0, intraPredMode: -1);
  }

  [Test]
  [Category("RoundTrip")]
  [Category("Boundary")]
  public void LevelsPastTheEscapeThresholdRoundTrip() {
    // Nine coefficients in one sub-block: past the eighth no greater-than-one flag is sent at all,
    // so every level above one after that is an escape code, and the Rice parameter has to adapt the
    // same way on both sides.
    var block = new int[8 * 8];
    int[] levels = [1, 2, 3, 5, 9, 33, 130, 1000, 32767];
    for (var i = 0; i < levels.Length; ++i)
      block[i] = (i & 1) == 0 ? levels[i] : -levels[i];

    _AssertRoundTrips(block, log2Size: 3, cIdx: 0, intraPredMode: -1);
  }

  [Test]
  [Category("RoundTrip")]
  [Category("ExceptionalCase")]
  public void TheWidestLevelTheCoderCanCarryRoundTrips() {
    var block = new int[4 * 4];
    block[0] = 32767;
    block[15] = -32768;

    _AssertRoundTrips(block, log2Size: 2, cIdx: 0, intraPredMode: -1);
  }

  [TestCase(2, 0)]
  [TestCase(3, 0)]
  [TestCase(4, 0)]
  [TestCase(5, 0)]
  [TestCase(2, 1)]
  [TestCase(3, 1)]
  [TestCase(4, 2)]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void RandomBlocksRoundTrip(int log2Size, int cIdx) {
    var random = new Random(1000 + log2Size * 10 + cIdx);
    var size = 1 << log2Size;

    for (var trial = 0; trial < 40; ++trial) {
      var block = new int[size * size];

      // Shaped like a real residual rather than uniform: energy at the low frequencies, most of the
      // block zero, and occasionally a large level.
      for (var y = 0; y < size; ++y)
        for (var x = 0; x < size; ++x) {
          if (random.Next(100) >= Math.Max(2, 60 - 6 * (x + y)))
            continue;

          var magnitude = random.Next(100) < 85 ? random.Next(1, 4) : random.Next(4, 2000);
          block[(y << log2Size) + x] = random.Next(2) == 0 ? magnitude : -magnitude;
        }

      if (block.All(static value => value == 0))
        block[0] = 1;

      _AssertRoundTrips(block, log2Size, cIdx, intraPredMode: -1);
    }
  }

  [TestCase(0)]
  [TestCase(10)]
  [TestCase(26)]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void IntraBlocksRoundTripInTheScanTheirModeChooses(int intraPredMode) {
    // Modes 6..14 read the block down its columns and 22..30 along its rows, so the writer has to
    // choose the same scan from the same mode or the coefficients come back transposed.
    var random = new Random(500 + intraPredMode);
    var block = new int[4 * 4];
    for (var i = 0; i < block.Length; ++i)
      block[i] = random.Next(4) == 0 ? random.Next(-20, 21) : 0;

    if (block.All(static value => value == 0))
      block[0] = -1;

    _AssertRoundTrips(block, log2Size: 2, cIdx: 0, intraPredMode: intraPredMode);
  }

  private static void _AssertRoundTrips(int[] block, int log2Size, int cIdx, int intraPredMode) {
    var states = new byte[H265CabacContexts.COUNT];
    H265CabacContexts.Initialize(states, 0, 26);

    var encoder = new H265CabacEncoder(states);
    H265ResidualWriter.Encode(encoder, block, log2Size, cIdx, intraPredMode, chromaArrayType: 1);
    encoder.EncodeTerminate(1);
    var payload = encoder.Finish();

    var readStates = new byte[H265CabacContexts.COUNT];
    H265CabacContexts.Initialize(readStates, 0, 26);
    var decoder = new H265CabacEngine(payload, readStates);
    decoder.Start(0);

    var decoded = new int[block.Length];
    H265Residual.Decode(
      ref decoder, decoded, log2Size, cIdx, intraPredMode, chromaArrayType: 1, _Pps, transquantBypass: false);

    Assert.Multiple(() => {
      Assert.That(decoded, Is.EqualTo(block), _Describe(block, decoded, log2Size));
      Assert.That(readStates, Is.EqualTo(states),
        "the contexts the block leaves behind are what the next block in the slice is coded against");
    });
  }

  private static string _Describe(int[] expected, int[] actual, int log2Size) {
    var differences = new List<string>();
    for (var i = 0; i < expected.Length && differences.Count < 8; ++i)
      if (expected[i] != actual[i])
        differences.Add($"({i & ((1 << log2Size) - 1)}, {i >> log2Size}): wrote {expected[i]}, read {actual[i]}");

    return differences.Count == 0 ? "the coefficients match" : string.Join("; ", differences);
  }
}
