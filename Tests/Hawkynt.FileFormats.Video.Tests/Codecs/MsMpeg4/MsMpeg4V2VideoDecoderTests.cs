using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;

namespace FileFormat.Codecs.MsMpeg4.Tests;

/// <summary>
/// What the Microsoft MPEG-4 version 2 decoder does with pictures built here, and what it refuses.
/// </summary>
/// <remarks>
/// The arithmetic was checked against ffmpeg over sixty-four encoded streams, four thousand four
/// hundred frames, plane by plane and sample by sample on every frame. What is here is the other half:
/// the refusals, the syntax an encoder never emits, and the reconstruction of pictures whose answer
/// can be stated in a sentence — a flat grey, a picture repeated by skipping every macroblock, a
/// picture moved by a vector — because a comparison against another decoder tells you the two agree
/// and not which one is right.
/// </remarks>
[TestFixture]
public sealed class MsMpeg4V2VideoDecoderTests {

  private const int _WIDTH = 32;
  private const int _HEIGHT = 32;
  private const int _MB_WIDTH = 2;
  private const int _MB_HEIGHT = 2;

  /// <summary>What a block reconstructs to when nothing has moved its DC: the middle of the range.</summary>
  private const int _GREY = 128;

  private static MediaStreamInfo _Stream(
    string codec = "MP42", int width = _WIDTH, int height = _HEIGHT) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
  };

  private static MsMpeg4V2VideoDecoder _Decoder() => MsMpeg4V2VideoDecoder.Create(_Stream());

  // ============================================================================================
  // Which streams it answers to
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ItAnswersToTheCodesVersionTwoIsNamedBy() {
    Assert.Multiple(() => {
      Assert.That(MsMpeg4V2VideoDecoder.Accepts(_Stream("MP42")), Is.True);
      Assert.That(MsMpeg4V2VideoDecoder.Accepts(_Stream("DIV2")), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void ItAnswersToTheOtherVersionsSoThatItCanRefuseThemByName() {
    Assert.Multiple(() => {
      Assert.That(MsMpeg4V2VideoDecoder.Accepts(_Stream("MPG4")), Is.True);
      Assert.That(MsMpeg4V2VideoDecoder.Accepts(_Stream("MP43")), Is.True);
      Assert.That(MsMpeg4V2VideoDecoder.Accepts(_Stream("DIV3")), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryReachesThisDecoderForAnMp42Stream() {
    // The registration is generated from the interface this type implements, so this is really a check
    // that the generator saw it: a codec that compiles and is never registered decodes nothing.
    Assert.That(Hawkynt.FileFormats.Video.VideoFormatRegistry.CreateDecoder(_Stream()),
                Is.InstanceOf<MsMpeg4V2VideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void ItDoesNotAnswerToMpeg4PartTwoItself()
    => Assert.That(MsMpeg4V2VideoDecoder.Accepts(_Stream("mp4v")), Is.False);

  [Test]
  [Category("Unit")]
  public void ItDoesNotAnswerToAnAudioStream() {
    var audio = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("MP42"),
    };

    Assert.That(MsMpeg4V2VideoDecoder.Accepts(audio), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AcceptsRefusesANullStream()
    => Assert.Throws<ArgumentNullException>(() => MsMpeg4V2VideoDecoder.Accepts(null!));

  [Test]
  [Category("Unit")]
  public void CreateRefusesANullStream()
    => Assert.Throws<ArgumentNullException>(() => MsMpeg4V2VideoDecoder.Create(null!));

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [TestCase("MPG4")]
  [TestCase("MP41")]
  [TestCase("DIV1")]
  [Category("Unit")]
  public void VersionOneIsRefusedByNameAndSaysWhy(string codec) {
    var e = Assert.Throws<NotSupportedException>(() => MsMpeg4V2VideoDecoder.Create(_Stream(codec)));

    Assert.Multiple(() => {
      Assert.That(e!.Message, Does.Contain("version 1"));
      Assert.That(e.Message, Does.Contain("no encoder"));
    });
  }

  [TestCase("MP43")]
  [TestCase("DIV3")]
  [TestCase("DIV4")]
  [TestCase("DIV5")]
  [TestCase("AP41")]
  [Category("Unit")]
  public void VersionThreeIsRefusedByNameAndSaysWhy(string codec) {
    var e = Assert.Throws<NotSupportedException>(() => MsMpeg4V2VideoDecoder.Create(_Stream(codec)));

    Assert.Multiple(() => {
      Assert.That(e!.Message, Does.Contain("version 3"));
      Assert.That(e.Message, Does.Contain("motion vector tables"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamWhoseContainerStatedNoSizeIsRefused() {
    var e = Assert.Throws<NotSupportedException>(() => MsMpeg4V2VideoDecoder.Create(_Stream(width: 0, height: 0)));

    Assert.That(e!.Message, Does.Contain("no picture size in the bitstream"));
  }

  [Test]
  [Category("Unit")]
  public void APictureTypeTheFormatDoesNotHaveIsRefused() {
    // Two bits of picture type, and only 0 and 1 are pictures. A packet stating 2 or 3 is not this
    // codec's, which is worth saying rather than decoding into noise.
    var picture = new MsMpeg4V2TestStream().Bits(2, 2).Bits(8, 5).ToArray();

    var e = Assert.Throws<InvalidDataException>(() => _Decoder().TryDecode(new(0, picture), out _));
    Assert.That(e!.Message, Does.Contain("picture type"));
  }

  [Test]
  [Category("Unit")]
  public void AQuantiserOfZeroIsRefused() {
    var picture = new MsMpeg4V2TestStream().Bits(0, 2).Bits(0, 5).Bits(0x17, 5).ToArray();

    var e = Assert.Throws<InvalidDataException>(() => _Decoder().TryDecode(new(0, picture), out _));
    Assert.That(e!.Message, Does.Contain("quantiser of zero"));
  }

  [Test]
  [Category("Unit")]
  public void ASliceCountThePictureCannotHoldIsRefused() {
    // Two macroblock rows cannot be divided into nine slices, and a field saying so means the header
    // is being read at the wrong bit rather than that the picture is unusual.
    var picture = new MsMpeg4V2TestStream().Bits(0, 2).Bits(8, 5).Bits(0x16 + 9, 5).ToArray();

    var e = Assert.Throws<InvalidDataException>(() => _Decoder().TryDecode(new(0, picture), out _));
    Assert.That(e!.Message, Does.Contain("slice"));
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureBeforeAnyIntraOneIsRefused() {
    var picture = MsMpeg4V2TestStream.SkippedPredictedPicture(_MB_WIDTH, _MB_HEIGHT, 8);

    var e = Assert.Throws<InvalidDataException>(() => _Decoder().TryDecode(new(0, picture), out _));
    Assert.That(e!.Message, Does.Contain("must begin at an intra picture"));
  }

  [Test]
  [Category("Unit")]
  public void APictureThatEndsInTheMiddleOfAMacroblockIsRefused() {
    var truncated = MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8)[..3];

    Assert.Throws<InvalidDataException>(() => _Decoder().TryDecode(new(0, truncated), out _));
  }

  // ============================================================================================
  // What comes out
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnIntraPictureIsHandedBackAtTheSizeTheContainerStated() {
    var decoder = _Decoder();

    Assert.That(decoder.TryDecode(new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8)), out var frame),
                Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(_WIDTH));
      Assert.That(frame.Height, Is.EqualTo(_HEIGHT));
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(frame.PixelData, Has.Length.EqualTo(_WIDTH * _HEIGHT * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryBlockOfAFlatIntraPictureReconstructsAtTheMiddleOfTheRange() {
    var decoder = _Decoder();
    decoder.TryDecode(new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8)), out _);

    Assert.That(_Luma(decoder), Is.All.EqualTo(_GREY));
  }

  [TestCase(0, _GREY)]
  [TestCase(20, _GREY + 20)]
  [TestCase(-40, _GREY - 40)]
  [TestCase(100, _GREY + 100)]
  [Category("Unit")]
  public void TheDcDifferentialOfTheFirstBlockMovesTheWholePicture(int differential, int expected) {
    // The first block predicts from mid-grey because everything around it is outside the picture, and
    // every block after it predicts from the one before, so one differential sets the whole picture.
    var decoder = _Decoder();
    decoder.TryDecode(
      new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8, differential)), out _);

    Assert.That(_Luma(decoder), Is.All.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void TheDcStepIsEightWhateverTheQuantiserIs() {
    // Version 2's intra DC step is a constant, unlike the standard's, so the same differential at two
    // very different quantisers has to reconstruct to the same level.
    var coarse = _Decoder();
    coarse.TryDecode(new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 31, 40)), out _);
    var fine = _Decoder();
    fine.TryDecode(new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 2, 40)), out _);

    Assert.Multiple(() => {
      Assert.That(_Luma(coarse), Is.All.EqualTo(_GREY + 40));
      Assert.That(_Luma(fine), Is.All.EqualTo(_GREY + 40));
    });
  }

  [Test]
  [Category("Unit")]
  public void SkippingEveryMacroblockRepeatsTheReferencePicture() {
    var decoder = _Decoder();
    decoder.TryDecode(new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8, 30)), out _);
    var intra = _Luma(decoder);

    Assert.That(decoder.TryDecode(
      new(0, MsMpeg4V2TestStream.SkippedPredictedPicture(_MB_WIDTH, _MB_HEIGHT, 8)), out _), Is.True);
    Assert.That(_Luma(decoder), Is.EqualTo(intra));
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureWithNoResidualAndNoMotionRepeatsItsReference() {
    var decoder = _Decoder();
    decoder.TryDecode(new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8, -25)), out _);

    decoder.TryDecode(new(0, MsMpeg4V2TestStream.MovedPredictedPicture(_MB_WIDTH, _MB_HEIGHT, 8, 0, 0)), out _);

    Assert.That(_Luma(decoder), Is.All.EqualTo(_GREY - 25));
  }

  [Test]
  [Category("Unit")]
  public void AVectorPointingOutsideThePictureReadsTheRepeatedEdge() {
    // A flat picture stays flat however far a vector reaches past its edge, because the border every
    // reference picture carries is its edge samples repeated. A decoder without one would read
    // whatever was next in memory, which on a flat picture is the one case where it shows.
    var decoder = _Decoder();
    decoder.TryDecode(new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8, 60)), out _);

    decoder.TryDecode(new(0, MsMpeg4V2TestStream.MovedPredictedPicture(_MB_WIDTH, _MB_HEIGHT, 8, -10, -10)), out _);

    Assert.That(_Luma(decoder), Is.All.EqualTo(_GREY + 60));
  }

  [Test]
  [Category("Unit")]
  public void APictureIsHandedBackForEveryPacketAndNothingIsHeldBack() {
    // The format has no bidirectionally coded pictures, so decode order is display order and there is
    // never anything waiting at the end.
    var decoder = _Decoder();

    Assert.Multiple(() => {
      Assert.That(decoder.TryDecode(new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8)), out _),
                  Is.True);
      Assert.That(decoder.TryDecode(new(0, MsMpeg4V2TestStream.SkippedPredictedPicture(_MB_WIDTH, _MB_HEIGHT, 8)), out _),
                  Is.True);
      Assert.That(decoder.Flush(), Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void AnIntraMacroblockInsideAPredictedPictureIsDecodedAsIntra() {
    // One of the four macroblock types no encoder here emits: an intra macroblock inside a predicted
    // picture, coding both chrominance blocks. Its DC differentials are read against mid-grey rather
    // than against the reference, so the picture it produces has nothing to do with the one before it.
    var decoder = _Decoder();
    decoder.TryDecode(new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8, 70)), out _);

    var s = new MsMpeg4V2TestStream().PredictedPictureHeader(8, skipBitsArePresent: false);
    for (var address = 0; address < _MB_WIDTH * _MB_HEIGHT; ++address) {
      s.Code(MsMpeg4V2TestStream.IntraInPredictedChromaNone).Bits(0, 1)
       .Code(MsMpeg4V2TestStream.LuminancePatternNone);
      for (var block = 0; block < 6; ++block)
        s.Dc(address == 0 && block == 0 ? -50 : 0, block < 4);
    }

    decoder.TryDecode(new(0, s.ToArray()), out _);

    Assert.That(_Luma(decoder), Is.All.EqualTo(_GREY - 50));
  }

  [Test]
  [Category("Unit")]
  public void APredictedMacroblockStatesItsLuminancePatternInverted() {
    // The pattern written as fifteen means "no luminance block is coded" for a predicted macroblock
    // whose chrominance bits are not both set. Reading it the right way up would have the decoder look
    // for four residuals that are not there and run off the end of the picture.
    var decoder = _Decoder();
    decoder.TryDecode(new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8, 15)), out _);

    Assert.That(decoder.TryDecode(
      new(0, MsMpeg4V2TestStream.MovedPredictedPicture(_MB_WIDTH, _MB_HEIGHT, 8, 0, 0)), out _), Is.True);
    Assert.That(_Luma(decoder), Is.All.EqualTo(_GREY + 15));
  }

  [Test]
  [Category("Unit")]
  public void ASliceBoundaryStopsTheDcPredictionDead() {
    // The same picture that comes out one flat level with a single slice comes out in two bands with
    // two, because the second slice's first row may not predict from the row above it and takes
    // mid-grey instead. That is the whole visible effect of a slice, and a decoder that ignored slices
    // would produce the flat picture and look perfectly reasonable doing it.
    var decoder = _Decoder();
    decoder.TryDecode(
      new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8, 45, slices: 2)), out _);
    var banded = _Luma(decoder);

    Assert.Multiple(() => {
      Assert.That(banded.Take(_WIDTH * 16), Is.All.EqualTo(_GREY + 45), "the slice that carries the differential");
      Assert.That(banded.Skip(_WIDTH * 16), Is.All.EqualTo(_GREY), "the slice that starts afresh");
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePicturesAfterAnIntraOneAreDividedIntoSlicesTheSameWay() {
    // Only an intra picture states how the picture is divided; the predicted ones after it say nothing
    // and are divided the same way. So a predicted picture that repeats its reference has to reproduce
    // the bands exactly — a decoder that forgot the count between pictures would have the boundary in
    // a different place and predict across the one that is really there.
    var decoder = _Decoder();
    decoder.TryDecode(
      new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8, 45, slices: 2)), out _);
    var banded = _Luma(decoder);

    Assert.That(decoder.TryDecode(
      new(0, MsMpeg4V2TestStream.SkippedPredictedPicture(_MB_WIDTH, _MB_HEIGHT, 8)), out _), Is.True);
    Assert.That(_Luma(decoder), Is.EqualTo(banded));
  }

  [Test]
  [Category("Unit")]
  public void EveryBlockOfASlicedIntraPictureStillReconstructs() {
    // With two slices the second one's first row predicts from mid-grey rather than from the row above
    // it, so a picture built as flat is still flat only if the decoder agrees about where the boundary
    // is.
    var decoder = _Decoder();
    decoder.TryDecode(
      new(0, MsMpeg4V2TestStream.FlatIntraPicture(_MB_WIDTH, _MB_HEIGHT, 8, 0, slices: 2)), out _);

    Assert.That(_Luma(decoder), Is.All.EqualTo(_GREY));
  }

  /// <summary>The luminance plane as the samples the picture is, without its padded border.</summary>
  private static int[] _Luma(MsMpeg4V2VideoDecoder decoder) {
    var frame = decoder.DecodedPlanes!;
    var result = new int[_WIDTH * _HEIGHT];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x)
        result[y * _WIDTH + x] = frame.Luma[frame.LumaOrigin + y * frame.LumaStride + x];

    return result;
  }
}
