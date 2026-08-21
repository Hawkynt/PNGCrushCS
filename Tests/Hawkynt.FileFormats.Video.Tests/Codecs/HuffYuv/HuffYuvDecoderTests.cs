using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.HuffYuv.Tests;

/// <summary>
/// The HuffYUV and FFVHUFF decoder, on streams built here symbol by symbol.
/// </summary>
/// <remarks>
/// The arithmetic was checked against ffmpeg over every pixel format its two encoders will write,
/// with each of the three predictors, progressive and interlaced, tables in the stream and tables in
/// every frame. What these tests add is what that comparison cannot reach: a Huffman table whose
/// lengths tell the two possible code assignments apart, the wraparound no natural picture reaches,
/// and the refusals, which by definition no valid stream contains.
/// </remarks>
[TestFixture]
public class HuffYuvDecoderTests {

  private const byte _LEFT = 0;
  private const byte _GRADIENT = 1;
  private const byte _MEDIAN = 2;
  private const byte _DECORRELATE = 0x40;
  private const byte _PROGRESSIVE = 0x20;
  private const byte _INTERLACED = 0x10;
  private const byte _TABLES_PER_FRAME = 0x40;
  private const byte _CHROMA = 0x01;
  private const byte _PLANAR_RGB = 0x02;
  private const byte _ALPHA = 0x04;

  // ============================================================================================
  // The Huffman tables
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void CodesAreAssignedFromTheLongestLengthDownAndNotTheShortestUp() {
    // Lengths of 1, 2, 2 for the first three symbols. The format hands out codes from the longest
    // length down, which makes them 1, 00 and 01; the canonical assignment every reader reaches for
    // first would make them 0, 10 and 11, and would read this frame as another picture entirely.
    var stream = HuffYuvTestStream.PlanarStream(4, 1, _LEFT, _PROGRESSIVE, 1, table: HuffYuvTestStream.TableOfLengths(1, 2, 2));
    var frame = _Decode(stream, new HuffYuvTestStream().Code("1 00 01 1").End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 1, 3, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void ARunOfLengthsLongerThanSevenTakesItsCountFromTheNextByte() {
    // The escape the tables live on: three bits reach seven, and a real table has runs of seventy.
    // The flat table every other test here uses is two such runs of 128.
    var stream = HuffYuvTestStream.PlanarStream(4, 1, _LEFT, _PROGRESSIVE, 1);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(200, 30, 30, 1).End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 200, 230, 4, 5 }));
  }

  [Test]
  [Category("Unit")]
  public void ATableWhoseLengthsDoNotDescribeACompleteCodeIsRefused() {
    var stream = HuffYuvTestStream.PlanarStream(4, 1, _LEFT, _PROGRESSIVE, 1, table: HuffYuvTestStream.TableOfLengths(1, 2));

    var failure = Assert.Throws<InvalidDataException>(() => HuffYuvDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("complete code"));
  }

  // ============================================================================================
  // The bits
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryFourBytesOfAFrameAreReadBackToFront() {
    // The frame below is the bytes 4, 3, 2, 1 as they lie in the file. Read in file order they
    // would be the differences 4, 3, 2, 1; read as the little-endian word the coder wrote, they are
    // 1, 2, 3, 4 — which is the only reading that makes the first pixel of a 4:2:2 frame come out as
    // the fourth byte of the word.
    var stream = HuffYuvTestStream.PlanarStream(4, 1, _LEFT, _PROGRESSIVE, 1);
    var frame = _Decode(stream, [4, 3, 2, 1]);

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 3, 6, 10 }));
  }

  // ============================================================================================
  // The predictors
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void LeftPredictionCarriesTheRunningSumAcrossTheEndOfARow() {
    var stream = HuffYuvTestStream.PlanarStream(3, 2, _LEFT, _PROGRESSIVE, 1);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(10, 5, 5, 1, 1, 1).End());

    // 10, 15, 20 and then 21, 22, 23 — the second row starts from where the first left off, not
    // from nothing.
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 10, 15, 20, 21, 22, 23 }));
  }

  [Test]
  [Category("Unit")]
  public void GradientPredictionAddsTheRowAboveToTheRunningSum() {
    var stream = HuffYuvTestStream.PlanarStream(3, 2, _GRADIENT, _PROGRESSIVE, 1);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(10, 5, 5, 1, 0, 0).End());

    // First row is 10, 15, 20 as before. The second row's sums are 21, 21, 21 and the row above is
    // added to each: 31, 36, 41. That is left plus above minus above-left, written as a sum.
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 10, 15, 20, 31, 36, 41 }));
  }

  [Test]
  [Category("Unit")]
  public void MedianPredictionTakesTheMiddleOfLeftAboveAndThePlaneThroughThem() {
    var stream = HuffYuvTestStream.PlanarStream(3, 2, _MEDIAN, _PROGRESSIVE, 1);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(10, 10, 10, 0, 0, 0).End());

    // Row one is 10, 20, 30. Row two, with a difference of nothing everywhere:
    //   x=0: left 30, above 10, plane 30+10-10 = 30 -> median 30
    //   x=1: left 30, above 20, plane 30+20-10 = 40 -> median 30
    //   x=2: left 30, above 30, plane 30+30-20 = 40 -> median 30
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 30, 30, 30 }));
  }

  [Test]
  [Category("Unit")]
  public void SamplesWrapRatherThanSaturate() {
    // 200 plus 100 is 44 and not 255. Saturating would lose the losslessness at the first sample
    // either side of the range, which for a codec whose whole point is losslessness is the end of it.
    var stream = HuffYuvTestStream.PlanarStream(3, 1, _LEFT, _PROGRESSIVE, 1);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(200, 100, 212).End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 200, 44, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void AnInterlacedFrameTakesItsRowAboveFromTwoRowsUp() {
    var stream = HuffYuvTestStream.PlanarStream(2, 4, _GRADIENT, _INTERLACED, 1);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(10, 0, 20, 0, 1, 0, 1, 0).End());

    // Rows nought and one have nothing two rows above them and are read from the left alone: 10, 10
    // and 30, 30. Row two's sums are 31, 31 and it adds row nought; row three's are 32, 32 and it
    // adds row one, not row two.
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 10, 10, 30, 30, 41, 41, 62, 62 }));
  }

  // ============================================================================================
  // What the planes are
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ThePlanarFormStatesItsSubsamplingInTheLowNibbleOfItsSecondByte() {
    // 4:2:0 is a horizontal shift of one and a vertical shift of one, so a four by four picture has
    // two by two chrominance planes and the frame is sixteen luminance samples and four of each
    // chrominance.
    var stream = HuffYuvTestStream.PlanarStream(4, 4, _LEFT, (byte)(_PROGRESSIVE | _CHROMA), 3, chromaHorizontal: 1, chromaVertical: 1);
    var builder = new HuffYuvTestStream();
    for (var i = 0; i < 16; ++i)
      builder.Symbols(0);

    builder.Symbols(128, 0, 0, 0);
    builder.Symbols(128, 0, 0, 0);
    var frame = _Decode(stream, builder.End());

    Assert.That(frame.Width, Is.EqualTo(4));
    Assert.That(frame.Height, Is.EqualTo(4));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ThePlanarColourFormStoresItsPlanesGreenBlueRed() {
    var stream = HuffYuvTestStream.PlanarStream(1, 1, _LEFT, (byte)(_PROGRESSIVE | _PLANAR_RGB), 3);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(2, 3, 1).End());

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void AnAlphaPlaneComesAfterTheThreeColourPlanes() {
    var stream = HuffYuvTestStream.PlanarStream(1, 1, _LEFT, (byte)(_PROGRESSIVE | _PLANAR_RGB | _ALPHA), 4);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(2, 3, 1, 200).End());

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 200 }));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoChrominanceComesOutAsOneGreyPlane() {
    var stream = HuffYuvTestStream.PlanarStream(2, 1, _LEFT, _PROGRESSIVE, 1);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(40, 10).End());

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 40, 50 }));
  }

  // ============================================================================================
  // The interleaved form
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheFirstFourSamplesOfAnInterleavedFrameAreRawAndArriveReversed() {
    // The word swap showing through: the four bytes of the first pixel pair come out as the second
    // chrominance sample, the second luminance sample, the first chrominance sample and the first
    // luminance sample — which is Y U Y V read backwards.
    var stream = HuffYuvTestStream.InterleavedStream(2, 1, _LEFT, 16, _PROGRESSIVE);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(200, 60, 100, 50).End());

    // Luminance 50 and 60 against chrominance 100 and 200, both of which are well away from the
    // neutral 128, so a picture that had them the other way round would be a different colour.
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Length, Is.EqualTo(6));
    Assert.That(frame.PixelData[0], Is.GreaterThan(frame.PixelData[2]));
  }

  [Test]
  [Category("Unit")]
  public void ThePackedColourFormStoresItsRowsBottomUp() {
    var stream = HuffYuvTestStream.InterleavedStream(1, 2, _LEFT, 24, _PROGRESSIVE);

    // The raw pixel is red, green, blue and then a byte the twenty-four bit form does not use. It is
    // the bottom row's, so a picture that read the rows the other way would show it at the top.
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(10, 20, 30, 0).Symbols(1, 2, 3).End());

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData[3..], Is.EqualTo(new byte[] { 10, 20, 30 }));
  }

  [Test]
  [Category("Unit")]
  public void TheRawColourPixelIsTheColourItselfAndNotItsDistanceFromGreen() {
    // Every coded difference of a decorrelated stream is a distance from green; the raw pixel that
    // starts the frame is not. Reading it as though it were turns a first pixel of 0, 61, 103 into
    // 61, 61, 164, which is how this was found.
    var stream = HuffYuvTestStream.InterleavedStream(1, 1, (byte)(_LEFT | _DECORRELATE), 24, _PROGRESSIVE);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(0, 61, 103, 0).End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 61, 103 }));
  }

  [Test]
  [Category("Unit")]
  public void TheThirtyTwoBitPackedFormPutsAlphaInFrontOfTheColour() {
    var stream = HuffYuvTestStream.InterleavedStream(1, 1, _LEFT, 32, _PROGRESSIVE);
    var frame = _Decode(stream, new HuffYuvTestStream().Symbols(200, 10, 20, 30).End());

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 200 }));
  }

  // ============================================================================================
  // Tables in every frame
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFrameThatCarriesItsOwnTablesCarriesThemInsideTheWordSwap() {
    // The tables are in the frame's swapped bytes and not in front of them, and the picture starts
    // at the byte after the last of them.
    var stream = HuffYuvTestStream.PlanarStream(4, 1, _LEFT, (byte)(_PROGRESSIVE | _TABLES_PER_FRAME), 1);

    var tables = HuffYuvTestStream.FlatTable();
    var builder = new HuffYuvTestStream();
    foreach (var b in tables)
      builder.Bits(b, 8);

    var frame = _Decode(stream, builder.Symbols(10, 5, 5, 5).End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 10, 15, 20, 25 }));
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheOriginalCodecIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(
      () => HuffYuvDecoder.Create(HuffYuvTestStream.UndescribedStream(4, 4, 24)));

    Assert.That(failure!.Message, Does.Contain("carries no stream description"));
  }

  [Test]
  [Category("Unit")]
  public void SamplesDeeperThanEightBitsAreRefusedByName() {
    var description = HuffYuvTestStream.Description(_LEFT, 0x90, _PROGRESSIVE, 1, 1);
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("FFVH"),
      Width = 4,
      Height = 4,
      BitsPerPixel = 24,
      CodecPrivateData = description,
    };

    var failure = Assert.Throws<NotSupportedException>(() => HuffYuvDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("10-bit samples"));
  }

  [Test]
  [Category("Unit")]
  public void ADescriptionThatStatesNeitherInterlacedNorProgressiveIsRefused() {
    // The original codec guessed from the height. The guess is wrong for every progressive picture
    // taller than a field, and being wrong about it puts every other row in the wrong place.
    var stream = HuffYuvTestStream.PlanarStream(4, 4, _LEFT, 0x00, 1);

    var failure = Assert.Throws<NotSupportedException>(() => HuffYuvDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("neither that its frames are interlaced"));
  }

  [Test]
  [Category("Unit")]
  public void APredictionMethodThatIsNoneOfTheThreeIsRefusedByName() {
    var stream = HuffYuvTestStream.PlanarStream(4, 4, 5, _PROGRESSIVE, 1);

    var failure = Assert.Throws<NotSupportedException>(() => HuffYuvDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("prediction method 5"));
  }

  [Test]
  [Category("Unit")]
  public void InterlacedFourTwoZeroWithMedianPredictionIsRefusedByName() {
    var stream = HuffYuvTestStream.InterleavedStream(8, 8, _MEDIAN, 12, _INTERLACED);

    var failure = Assert.Throws<NotSupportedException>(() => HuffYuvDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("interlaced 4:2:0"));
  }

  [Test]
  [Category("Unit")]
  public void ABitstreamDepthTheCodecDoesNotUseIsRefusedByName() {
    var stream = HuffYuvTestStream.InterleavedStream(4, 4, _LEFT, 20, _PROGRESSIVE);

    var failure = Assert.Throws<NotSupportedException>(() => HuffYuvDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("20 bits a pixel"));
  }

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheCodecAnswersToBothOfItsCodes() {
    foreach (var code in new[] { "HFYU", "FFVH", "hfyu" }) {
      var stream = new MediaStreamInfo {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = CodecTag.FromCharacters(code),
        Width = 4,
        Height = 4,
      };

      Assert.That(HuffYuvDecoder.Accepts(stream), Is.True, code);
    }
  }

  [Test]
  [Category("Unit")]
  public void TheCodecDoesNotAnswerForAnotherCode() {
    var other = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("FFV1"),
      Width = 4,
      Height = 4,
    };

    Assert.That(HuffYuvDecoder.Accepts(other), Is.False);
  }

  // ============================================================================================

  private static RawImage _Decode(MediaStreamInfo stream, byte[] frame) {
    var decoder = HuffYuvDecoder.Create(stream);
    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True);
    return picture;
  }
}
