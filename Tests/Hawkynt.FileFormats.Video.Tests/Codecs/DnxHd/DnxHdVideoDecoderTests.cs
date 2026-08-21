using System;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.DnxHd.Tests;

/// <summary>
/// The Avid DNxHD and DNxHR decoder, on coding units built here a codeword at a time.
/// </summary>
/// <remarks>
/// The arithmetic was checked against ffmpeg over every compression identifier its encoder will
/// write, at eight and ten bits, 4:2:2 and 4:4:4, at rasters that are and are not a whole number of
/// macroblocks. What these tests add is what that comparison cannot reach: the DC prediction and its
/// reset, the block order of Table 5, the zig-zag of Figure 48, the completeness check on the code
/// tables, and the refusals.
/// </remarks>
[TestFixture]
public class DnxHdVideoDecoderTests {

  // ============================================================================================
  // A whole coding unit, end to end
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFrameOfEmptyBlocksComesOutMidGrey() {
    // Every block carries a DC correction of zero and nothing else, so every coefficient is zero,
    // the transform gives zero, and 8.2.8.3 adds the level adjustment of 2^(b−1) — 128 at eight
    // bits, which is mid-grey and achromatic.
    var planes = _Decode(new(), _Payload(stream => {
      stream.Macroblock(1);
      for (var block = 0; block < 8; ++block)
        stream.FlatBlock();
    }));

    Assert.Multiple(() => {
      Assert.That(planes.BitDepth, Is.EqualTo(8));
      Assert.That(planes.ChromaWidth, Is.EqualTo(8), "4:2:2 chroma is half as wide");
      Assert.That(planes.Luma, Is.All.EqualTo(128));
      Assert.That(planes.Cb, Is.All.EqualTo(128));
      Assert.That(planes.Cr, Is.All.EqualTo(128));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheEightBlocksOfAMacroblockAreOrderedYYCbCrYYCbCr() {
    // SMPTE ST 2019-1:2016, Table 5. The order is not the obvious one: the top half of the
    // macroblock is complete in all three components before the bottom half begins, so the eight
    // blocks run Y0, Y1, Cb0, Cr0, Y2, Y3, Cb1, Cr1. Reading them plane by plane instead decodes the
    // right number of blocks into the wrong places and still produces a picture.
    //
    // Each block is given a DC correction of +8, which — since the corrections accumulate within a
    // component — makes each successive block of a component one step brighter than the last: a DC
    // of 8 reconstructs as 129 and one of 16 as 130.
    var planes = _Decode(new(), _Payload(stream => {
      stream.Macroblock(1);
      for (var block = 0; block < 8; ++block)
        stream.DcBlock(8);
    }));

    Assert.Multiple(() => {
      Assert.That(_At(planes.Luma, 16, 0, 0), Is.EqualTo(129), "Y0, the first luma block");
      Assert.That(_At(planes.Luma, 16, 8, 0), Is.EqualTo(130), "Y1, the second");
      Assert.That(_At(planes.Luma, 16, 0, 8), Is.EqualTo(131), "Y2, the third");
      Assert.That(_At(planes.Luma, 16, 8, 8), Is.EqualTo(132), "Y3, the fourth");
      Assert.That(_At(planes.Cb, 8, 0, 0), Is.EqualTo(129), "Cb0 is the first block of its own component");
      Assert.That(_At(planes.Cb, 8, 0, 8), Is.EqualTo(130), "Cb1 is the second");
      Assert.That(_At(planes.Cr, 8, 0, 0), Is.EqualTo(129), "Cr keeps a prediction of its own");
      Assert.That(_At(planes.Cr, 8, 0, 8), Is.EqualTo(130));
    });
  }

  [Test]
  [Category("Unit")]
  public void EachComponentKeepsItsOwnDcPrediction() {
    // 8.2.4 keeps a prediction per component type. Two luma corrections of +8 reach 16 while a
    // single chroma one reaches 8, so the two components part company — which they could not do if
    // one running value served all three.
    var planes = _Decode(new(), _Payload(stream => {
      stream.Macroblock(1);
      stream.DcBlock(8);   // Y0: luma prediction 8
      stream.DcBlock(8);   // Y1: luma prediction 16
      stream.DcBlock(8);   // Cb0: chroma prediction 8
      stream.FlatBlock();  // Cr0
      stream.FlatBlock();  // Y2
      stream.FlatBlock();  // Y3
      stream.FlatBlock();  // Cb1
      stream.FlatBlock();  // Cr1
    }));

    Assert.Multiple(() => {
      Assert.That(_At(planes.Luma, 16, 8, 0), Is.EqualTo(130), "two luma corrections of 8 reach a DC of 16");
      Assert.That(_At(planes.Cb, 8, 0, 0), Is.EqualTo(129), "one chroma correction of 8 reaches a DC of 8");
      Assert.That(_At(planes.Cr, 8, 0, 0), Is.EqualTo(128), "the other chroma component has had none at all");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheDcPredictionRestartsAtEachMacroblockScanLine() {
    // 8.2.4 resets the prediction at the start of every macroblock scan line, which is what makes a
    // scan line independently decodable and is the same property the scan index table exists for.
    // Both scan lines below code the same correction, so both must reconstruct the same value —
    // a prediction carried across would make the second brighter than the first.
    var payload = new DnxHdTestStream();
    var first = payload.Position;
    payload.Macroblock(1);
    payload.DcBlock(8);
    for (var block = 1; block < 8; ++block)
      payload.FlatBlock();

    payload.EndScanLine();
    var second = payload.Position;
    payload.Macroblock(1);
    payload.DcBlock(8);
    for (var block = 1; block < 8; ++block)
      payload.FlatBlock();

    var options = new DnxHdTestStream.Options { Height = 32, ScanIndices = [first, second] };
    var planes = _Decode(options, payload);

    Assert.Multiple(() => {
      Assert.That(_At(planes.Luma, 16, 0, 0), Is.EqualTo(129), "the first scan line");
      Assert.That(_At(planes.Luma, 16, 0, 16), Is.EqualTo(129), "the second, which starts over");
    });
  }

  [Test]
  [Category("Unit")]
  public void ANegativeDcCorrectionIsTheBottomHalfOfItsRange() {
    // 8.2.4 and Figure 46. A four-bit ρ of 7 is below 2^3, so it stands for 7 + 1 − 16 = −8 rather
    // than for +7. Reading it as unsigned makes every dark block bright.
    var planes = _Decode(new(), _Payload(stream => {
      stream.Macroblock(1);
      stream.DcBlock(7);
      for (var block = 1; block < 8; ++block)
        stream.FlatBlock();
    }));

    Assert.That(_At(planes.Luma, 16, 0, 0), Is.EqualTo(127), "a DC of −8 reconstructs one below mid-grey");
  }

  [Test]
  [Category("Unit")]
  public void TheFirstAcCoefficientIsTheFirstHorizontalFrequency() {
    // The zig-zag of Figure 48 puts bitstream index 1 at raster position (u=1, v=0), so a single AC
    // coefficient there is half a cycle of the lowest horizontal frequency: the block shades from
    // one side to the other and every row of it is the same.
    //
    // The amplitude is 16, which Table E.4 codes as 111110101 with no run and no index, followed by
    // a sign bit of 0. Inverse quantisation (8.2.7) with the weight 32 that Table D.2 gives that
    // position, a scale factor of 1 and a divisor of 32 leaves it at 16.
    var planes = _Decode(new(), _Payload(stream => {
      stream.Macroblock(1);
      stream.Code(DnxHdTestStream.DcZeroBits)
        .Code(DnxHdTestStream.AmplitudeSixteen).Bits(0, 1)
        .Code(DnxHdTestStream.EndOfBlock);
      for (var block = 1; block < 8; ++block)
        stream.FlatBlock();
    }));

    var top = Enumerable.Range(0, 16).Select(x => (int)_At(planes.Luma, 16, x, 0)).ToArray();
    var lower = Enumerable.Range(0, 8).Select(x => (int)_At(planes.Luma, 16, x, 5)).ToArray();

    Assert.Multiple(() => {
      Assert.That(top[..8], Is.EqualTo(new[] { 131, 130, 130, 129, 127, 126, 126, 125 }));
      Assert.That(top[8..], Is.All.EqualTo(128), "the second luma block has only its DC");
      Assert.That(lower, Is.EqualTo(top[..8]), "the first horizontal frequency does not vary down the block");
    });
  }

  [Test]
  [Category("Unit")]
  public void ARunOfZeroesMovesTheCoefficientAlongTheZigZag() {
    // The same amplitude, but coded with a preceding run of one zero, which puts it at bitstream
    // index 2. Figure 48 maps that to raster position (u=0, v=1) — the first *vertical* frequency —
    // so the block shades from top to bottom instead of side to side. Getting the zig-zag the wrong
    // way round transposes every block, which on most pictures looks like a decode that worked.
    var planes = _Decode(new(), _Payload(stream => {
      stream.Macroblock(1);
      stream.Code(DnxHdTestStream.DcZeroBits)
        .Code(DnxHdTestStream.AmplitudeSixteenWithRun).Bits(0, 1).Code(DnxHdTestStream.RunOne)
        .Code(DnxHdTestStream.EndOfBlock);
      for (var block = 1; block < 8; ++block)
        stream.FlatBlock();
    }));

    var column = Enumerable.Range(0, 8).Select(y => (int)_At(planes.Luma, 16, 0, y)).ToArray();
    var row = Enumerable.Range(0, 8).Select(x => (int)_At(planes.Luma, 16, x, 0)).ToArray();

    Assert.Multiple(() => {
      Assert.That(column, Is.EqualTo(new[] { 131, 130, 130, 129, 127, 126, 126, 125 }));
      Assert.That(row, Is.All.EqualTo(131), "the first vertical frequency does not vary across the block");
    });
  }

  // ============================================================================================
  // The tables
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheZigZagIsAPermutationOfTheSixtyFourFrequencies() {
    Assert.Multiple(() => {
      Assert.That(DnxHdScan.RasterPosition.OrderBy(n => n), Is.EqualTo(Enumerable.Range(0, 64)));
      Assert.That(DnxHdScan.RasterPosition[0], Is.EqualTo(0), "the DC is the first coefficient");
      Assert.That(DnxHdScan.RasterPosition[1], Is.EqualTo(1), "then the first horizontal frequency");
      Assert.That(DnxHdScan.RasterPosition[2], Is.EqualTo(8), "then the first vertical one");
      Assert.That(DnxHdScan.RasterPosition[63], Is.EqualTo(63));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryCodeTableOfAnnexEIsComplete() {
    // A complete prefix code uses every value at its longest length exactly once, so building each
    // of the eighteen tables is itself the check that none was transcribed wrongly — a missing or
    // duplicated codeword leaves the code incomplete and the constructor refuses it.
    Assert.DoesNotThrow(() => {
      for (var group = 0; group < 6; ++group) {
        DnxHdVlcTable.From(DnxHdVlcTables.AmplitudeLengths[group], DnxHdVlcTables.AmplitudeSymbols[group]);
        DnxHdVlcTable.From(DnxHdVlcTables.RunLengths[group], DnxHdVlcTables.RunSymbols[group]);
        DnxHdVlcTable.From(DnxHdVlcTables.DcLengths[group], DnxHdVlcTables.DcBitCounts[group]);
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void ACodeTableWhoseLengthsAreIncompleteIsRefused() {
    // Two codewords of one bit and one of two cannot all fit: the two of length one use the whole
    // code. This is the shape of mistake a mistranscribed table makes, and it is caught at
    // construction rather than a few thousand codewords into a picture.
    var failure = Assert.Throws<InvalidDataException>(() => new DnxHdVlcTable([1, 1, 2], [0, 1, 2]));

    Assert.That(failure!.Message, Does.Contain("complete code"));
  }

  [Test]
  [Category("Unit")]
  public void EachAmplitudeTableHoldsFourSymbolSetsOfSixtyFourAndOneEndOfBlock() {
    // Annex A.1: amplitudes 1 to 64, with and without a preceding run, in the base range and in the
    // index range — four sets — and the end-of-block codeword, so 257 in all.
    for (var group = 0; group < 6; ++group) {
      var symbols = DnxHdVlcTables.AmplitudeSymbols[group].Select(s => (int)s).ToArray();
      Assert.That(symbols, Has.Length.EqualTo(257), $"group {group}");
      Assert.That(symbols.Count(DnxHdAmplitude.IsEndOfBlock), Is.EqualTo(1), $"group {group}");

      foreach (var run in new[] { false, true })
        foreach (var index in new[] { false, true }) {
          var amplitudes = symbols
            .Where(s => !DnxHdAmplitude.IsEndOfBlock(s)
                        && DnxHdAmplitude.HasRun(s) == run
                        && DnxHdAmplitude.HasIndex(s) == index)
            .Select(DnxHdAmplitude.Value)
            .OrderBy(a => a);

          Assert.That(amplitudes, Is.EqualTo(Enumerable.Range(1, 64)), $"group {group}, run {run}, index {index}");
        }
    }
  }

  [Test]
  [Category("Unit")]
  public void TheQuantisationWeightOfTheDcCoefficientIsNeverUsed() {
    // 8.2.7: the DC is not quantised during encoding, so nothing is applied to it on the way back
    // and Annex D prints a dash where its weight would be. Zero stands in for the dash, and a zero
    // weight reaching the arithmetic would collapse a coefficient rather than scale it.
    for (var table = 0; table < 11; ++table) {
      Assert.That(DnxHdWeightTables.Luma[table][0], Is.Zero, $"table D.{table + 1} luma");
      Assert.That(DnxHdWeightTables.Chroma[table][0], Is.Zero, $"table D.{table + 1} chroma");
      Assert.That(DnxHdWeightTables.Luma[table].Skip(1), Is.All.GreaterThan(0), $"table D.{table + 1} luma");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheCompressionIdentifierChoosesTheTablesAndTheDivisor() {
    // Annex C read across. The divisor is the part with no field of its own anywhere in the
    // bitstream: 8.2.7 gives it as 8 for three identifiers and 32 for every other, and using the
    // wrong one scales every AC coefficient by four.
    Assert.Multiple(() => {
      Assert.That(DnxHdCompressionId.Find(1253)!.WeightTable, Is.EqualTo(1), "1253 uses Table D.2");
      Assert.That(DnxHdCompressionId.Find(1253)!.VlcGroup, Is.EqualTo(1), "and Tables E.4 to E.6");
      Assert.That(DnxHdCompressionId.Find(1253)!.InverseQuantisationDivisor, Is.EqualTo(32));
      Assert.That(DnxHdCompressionId.Find(1235)!.InverseQuantisationDivisor, Is.EqualTo(8));
      Assert.That(DnxHdCompressionId.Find(1241)!.InverseQuantisationDivisor, Is.EqualTo(8));
      Assert.That(DnxHdCompressionId.Find(1250)!.InverseQuantisationDivisor, Is.EqualTo(8));
      Assert.That(DnxHdCompressionId.Find(1270)!.ResolutionIndependent, Is.True);
      Assert.That(DnxHdCompressionId.Find(1253)!.ResolutionIndependent, Is.False);
      Assert.That(DnxHdCompressionId.Find(9999), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void CompressionIdentifier1271UsesTheWeightsItsBitstreamsAreCodedWith() {
    // The one row of Annex C that does not match the bitstreams. Table C.2 sends 1271 to Table D.1;
    // every DNxHR HQX frame measured is quantised with Table D.4 and decodes correctly only with
    // that one — no sample differing from the reference decode by more than 3 of 1023 with D.4,
    // against 103 with D.1. See DnxHdCompressionId for the evidence; this pins the choice so that
    // it cannot be quietly reverted to the table the standard prints.
    Assert.That(DnxHdCompressionId.Find(1271)!.WeightTable, Is.EqualTo(3), "index 3 is Table D.4");
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ACompressionIdentifierAnnexCDoesNotDefineIsRefused() {
    var options = new DnxHdTestStream.Options { CompressionId = 1299 };
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(options, _FlatPayload()));

    Assert.That(failure!.Message, Does.Contain("1299"));
  }

  [Test]
  [Category("Unit")]
  public void AHeaderVersionTheStandardDoesNotDefineIsRefused() {
    var options = new DnxHdTestStream.Options { HeaderVersion = 4 };
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(options, _FlatPayload()));

    Assert.That(failure!.Message, Does.Contain("header version 4"));
  }

  [Test]
  [Category("Unit")]
  public void AnHdCodingUnitStatingAHeaderOfTheWrongSizeIsRefused() {
    // 7.2.1 fixes the header of versions 1 and 2 at 0x280 bytes. Only the resolution-independent
    // profile has a header whose size varies, and it says so with a version of its own.
    var options = new DnxHdTestStream.Options { HeaderVersion = 1, CompressionId = 1253, StatedHeaderSize = 0x300 };
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(options, _FlatPayload(), 16, 16));

    Assert.That(failure!.Message, Does.Contain("640"));
  }

  [Test]
  [Category("Unit")]
  public void AnInterlacedFrameIsRefused() {
    var options = new DnxHdTestStream.Options { Interlaced = true };
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(options, _FlatPayload()));

    Assert.That(failure!.Message, Does.Contain("interlaced"));
  }

  [Test]
  [Category("Unit")]
  public void AFieldEncodedCodingUnitIsRefused() {
    var options = new DnxHdTestStream.Options { FrameEncoded = false };
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(options, _FlatPayload()));

    Assert.That(failure!.Message, Does.Contain("interlaced"));
  }

  [Test]
  [Category("Unit")]
  public void TheAdaptiveMacroblockModeIsRefused() {
    var options = new DnxHdTestStream.Options { AdaptiveMacroblocks = true };
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(options, _FlatPayload()));

    Assert.That(failure!.Message, Does.Contain("1260"));
  }

  [Test]
  [Category("Unit")]
  public void FourTwoZeroSamplingIsRefused() {
    var options = new DnxHdTestStream.Options { SubSampling = 1 };
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(options, _FlatPayload()));

    Assert.That(failure!.Message, Does.Contain("4:2:0"));
  }

  [Test]
  [Category("Unit")]
  public void AnAlphaChannelIsRefused() {
    var options = new DnxHdTestStream.Options { Alpha = true };
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(options, _FlatPayload()));

    Assert.That(failure!.Message, Does.Contain("alpha"));
  }

  [Test]
  [Category("Unit")]
  public void TheRgbFlagUnderAnIdentifierThatMayNotSetItIsRefused() {
    // 7.2.5 permits the colour format flag only for 1256 and 1270; anywhere else it is a bitstream
    // describing itself with syntax its own identifier does not have.
    var options = new DnxHdTestStream.Options { Rgb = true };
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(options, _FlatPayload()));

    Assert.That(failure!.Message, Does.Contain("RGB colour format flag"));
  }

  [Test]
  [Category("Unit")]
  public void AMacroblockWithNoQuantisationScaleFactorIsRefused() {
    // A scale factor of zero would make every AC coefficient of the macroblock vanish — a flat block
    // that looks like a decode rather than a refusal.
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(new(), _Payload(stream => {
      stream.Macroblock(0);
      for (var block = 0; block < 8; ++block)
        stream.FlatBlock();
    })));

    Assert.That(failure!.Message, Does.Contain("quantisation scale factor of zero"));
  }

  [Test]
  [Category("Unit")]
  public void ACodingUnitOfADifferentRasterToTheStreamItIsInIsRefused() {
    var unit = DnxHdTestStream.Unit(new(), _FlatPayload());
    var decoder = DnxHdVideoDecoder.Create(DnxHdTestStream.Stream(1920, 1080));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, unit), out _));
    Assert.That(failure!.Message, Does.Contain("16x16"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoPictureSizeIsRefused() {
    Assert.Throws<InvalidDataException>(() => DnxHdVideoDecoder.Create(DnxHdTestStream.Stream(0, 0)));
  }

  // ============================================================================================
  // Which streams this codec answers to
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("AVdn")]
  [TestCase("AVdh")]
  [TestCase("AVd1")]
  public void BothProfilesAreAccepted(string code) {
    Assert.That(DnxHdVideoDecoder.Accepts(DnxHdTestStream.Stream(codec: code)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AMatroskaTrackIsAcceptedByItsCodecIdentifier() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      CodecId = "V_DNXHD",
      Width = 16,
      Height = 16,
    };

    Assert.That(DnxHdVideoDecoder.Accepts(stream), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AStreamOfAnotherCodecIsNotAccepted() {
    Assert.That(DnxHdVideoDecoder.Accepts(DnxHdTestStream.Stream(codec: "apcn")), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AFrameComesBackAsPackedColourAtTheStatedRaster() {
    var unit = DnxHdTestStream.Unit(new(), _FlatPayload());
    var decoder = DnxHdVideoDecoder.Create(DnxHdTestStream.Stream());

    Assert.That(decoder.TryDecode(new(0, unit), out var picture), Is.True);
    Assert.Multiple(() => {
      Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(picture.Width, Is.EqualTo(16));
      Assert.That(picture.Height, Is.EqualTo(16));
      Assert.That(picture.PixelData!.Length, Is.EqualTo(16 * 16 * 3));
    });
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static DnxHdPlanes _Decode(DnxHdTestStream.Options options, DnxHdTestStream payload, int width = 0, int height = 0) {
    var unit = DnxHdTestStream.Unit(options, payload);
    var decoder = DnxHdVideoDecoder.Create(
      DnxHdTestStream.Stream(width > 0 ? width : options.Width, height > 0 ? height : options.Height));

    return decoder.DecodePlanes(unit, out _);
  }

  private static DnxHdTestStream _Payload(Action<DnxHdTestStream> write) {
    var stream = new DnxHdTestStream();
    write(stream);

    return stream;
  }

  private static DnxHdTestStream _FlatPayload() => _Payload(stream => {
    stream.Macroblock(1);
    for (var block = 0; block < 8; ++block)
      stream.FlatBlock();
  });

  private static int _At(ushort[] plane, int width, int x, int y) => plane[y * width + x];
}
