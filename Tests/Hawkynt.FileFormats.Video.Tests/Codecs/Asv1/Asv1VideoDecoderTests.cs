using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs.Asv1.Tests;

/// <summary>
/// The ASV1 decoder, on streams built here bit by bit.
/// </summary>
/// <remarks>
/// Real ASV1 streams were compared with ffmpeg's decode of the same bitstream, plane by plane and
/// frame by frame, over 325 frames across five sizes (64x64 to 352x288, one of them not a whole number
/// of macroblocks in either direction), five quantisers from 1 to 31, and content from flat colour to
/// hard edges and a fractal zoom — see this codec's section of <c>README.md</c> for the measurement.
/// What these tests add is what a comparison against a real encoder cannot reach at all: ffmpeg's own
/// encoder never emits a coefficient group past the tenth (there is no eleventh to refuse), an escaped
/// eight-bit level, or a coefficient group naming the block's own DC position, so those and every
/// refusal are built by hand here instead.
/// </remarks>
[TestFixture]
public sealed class Asv1VideoDecoderTests {

  // ============================================================================================
  // The DC coefficient and dequantisation — asv1.txt 3.5, 3.6 and 4.4
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatIntraPictureIsMidGrey() {
    // c00' = 8 * c00 always, and the inverse transform's DC-only path divides that back down by
    // eight — so a DC field of 128 with no AC coefficients reconstructs to luminance 128 exactly,
    // whatever the quantiser is, because the AC dequantisation this quantiser governs never runs.
    var stream = new Asv1TestStream();
    stream.FlatMacroblock(128);

    var frame = _Decode(16, 16, 1, stream.ToPacketBytes());

    Assert.That(frame.Width, Is.EqualTo(16));
    Assert.That(frame.Height, Is.EqualTo(16));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(128) }));
  }

  [TestCase(0)]
  [TestCase(255)]
  [Category("Unit")]
  public void EveryDcFieldValueIsAValidLuminance(int dc) {
    // Unlike ITU-T H.263's INTRADC, asv1.txt leaves no DC field value unused.
    var stream = new Asv1TestStream();
    stream.FlatMacroblock(dc);

    var frame = _Decode(16, 16, 1, stream.ToPacketBytes());

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(dc) }));
  }

  [Test]
  [Category("Unit")]
  public void ASmallAcLevelIsDequantisedAgainstTheMpeg1IntraMatrixAndTransformed() {
    // Coefficient group 0, bit 1 (pattern 0b0010, code "01101") names raster position 8 — row one,
    // column zero, a purely vertical frequency. At QUANT 1 its dequantisation factor is
    // 64 * 16 / 1 = 1024, and a level of +1 (code "10") dequantises to 1024/16 = 64. Added to a flat
    // DC of 128 the inverse transform gives one value a row, constant across each row.
    var stream = new Asv1TestStream();
    stream.Bits(128, 8).Code("01101").Code("10").Code("01111");
    stream.DcOnlyBlock(128).DcOnlyBlock(128).DcOnlyBlock(128);
    stream.DcOnlyBlock(128).DcOnlyBlock(128);

    var frame = _DecodePlanes(16, 16, 1, stream.ToPacketBytes());

    int[] rows = [139, 137, 134, 130, 126, 122, 119, 117];
    for (var y = 0; y < 8; ++y)
      Assert.That(frame.Luma[y * frame.LumaWidth], Is.EqualTo(rows[y]), $"row {y}");
  }

  [Test]
  [Category("Unit")]
  public void AnEscapedLevelCarriesItsOwnSignAsTwosComplement() {
    // The same position as above, this time with a level too large for the short codes: the escape
    // "000" followed by an eight-bit two's-complement value, -8 here (0xF8), dequantising to
    // -8 * 1024 / 16 = -512.
    var stream = new Asv1TestStream();
    stream.Bits(128, 8).Code("01101").EscapedLevel(-8).Code("01111");
    stream.DcOnlyBlock(128).DcOnlyBlock(128).DcOnlyBlock(128);
    stream.DcOnlyBlock(128).DcOnlyBlock(128);

    var frame = _DecodePlanes(16, 16, 1, stream.ToPacketBytes());

    int[] rows = [39, 53, 78, 110, 146, 178, 203, 217];
    for (var y = 0; y < 8; ++y)
      Assert.That(frame.Luma[y * frame.LumaWidth], Is.EqualTo(rows[y]), $"row {y}");
  }

  // ============================================================================================
  // Picture geometry that is not a whole number of macroblocks — asv1.txt 3.1
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APartialWidthColumnAndPartialHeightRowAreCodedAfterTheMainBody() {
    // A 20x20 picture is 2x2 macroblocks: one whole one at (0,0), a partial-width column at (1,0),
    // and a partial-height row at (0,1) and (1,1) — coded in exactly that order (clause 3.1's own
    // worked example) and each given its own flat luminance so the order is what the pixels show.
    var stream = new Asv1TestStream();
    stream.FlatMacroblock(64);  // main body: (0, 0)
    stream.FlatMacroblock(96);  // right column: (1, 0)
    stream.FlatMacroblock(160); // bottom row: (0, 1)
    stream.FlatMacroblock(192); // bottom row: (1, 1)

    var frame = _Decode(20, 20, 1, stream.ToPacketBytes());

    Assert.That(_Red(frame, 20, 0, 0), Is.EqualTo(_Grey(64)));
    Assert.That(_Red(frame, 20, 16, 0), Is.EqualTo(_Grey(96)));
    Assert.That(_Red(frame, 20, 0, 16), Is.EqualTo(_Grey(160)));
    Assert.That(_Red(frame, 20, 16, 16), Is.EqualTo(_Grey(192)));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnEleventhCoefficientGroupWithoutEndOfBlockIsRefused() {
    // Ten real groups (0 to 9), every one coded with the empty pattern "10" rather than End Of
    // Block, leaves nothing but an eleventh read to answer with — and asv1.txt's own example decoder
    // treats anything but End Of Block there as an error.
    var stream = new Asv1TestStream();
    stream.Bits(128, 8);
    for (var i = 0; i < 11; ++i)
      stream.Code("10");

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(16, 16, 1, stream.ToPacketBytes()));
    Assert.That(failure.Message, Does.Contain("tenth coefficient group"));
  }

  [Test]
  [Category("Unit")]
  public void ACoefficientGroupPatternNamingTheDcPositionIsRefused() {
    // Group 0's pattern bit 0 addresses the block's own raster position 0 — the DC coefficient,
    // which asv1.txt 3.3 says "must be coded as 0" because it is read from the separate eight-bit DC
    // field instead. Pattern 1 (code "01110") sets exactly that bit.
    var stream = new Asv1TestStream();
    stream.Bits(128, 8).Code("01110");

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(16, 16, 1, stream.ToPacketBytes()));
    Assert.That(failure.Message, Does.Contain("DC position"));
  }

  [Test]
  [Category("Unit")]
  public void AQuantiserOfZeroIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => Asv1VideoDecoder.Create(_Stream(16, 16, _PrivateData(0))));
    Assert.That(failure.Message, Does.Contain("zero"));
  }

  [Test]
  [Category("Unit")]
  public void PrivateDataShorterThanTheGlobalHeaderIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(
      () => Asv1VideoDecoder.Create(_Stream(16, 16, new byte[40]))); // BITMAPINFOHEADER with nothing behind it
    Assert.That(failure.Message, Does.Contain("global header"));
  }

  [TestCase(0, 16)]
  [TestCase(16, 0)]
  [TestCase(-1, 16)]
  public void ANonPositivePictureSizeIsRefused(int width, int height) {
    var failure = Assert.Throws<InvalidDataException>(
      () => Asv1VideoDecoder.Create(_Stream(width, height, _PrivateData(1))));
    Assert.That(failure.Message, Does.Contain("picture size"));
  }

  // ============================================================================================
  // Identity
  // ============================================================================================

  [TestCase("ASV1", true)]
  [TestCase("asv1", true)]
  [TestCase("ASV2", false)]
  [TestCase("H261", false)]
  public void TheCodecTakesTheStreamsItsContainerNames(string tag, bool expected)
    => Assert.That(Asv1VideoDecoder.Accepts(_TaggedStream(tag)), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsNotTakenWhateverItsTag()
    => Assert.That(
      Asv1VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("ASV1") }),
      Is.False);

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _PrivateData(byte quantiser) {
    var data = new byte[48];
    data[40] = quantiser;
    data[44] = (byte)'A';
    data[45] = (byte)'S';
    data[46] = (byte)'U';
    data[47] = (byte)'S';
    return data;
  }

  private static MediaStreamInfo _Stream(int width, int height, byte[] privateData) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("ASV1"),
    Width = width,
    Height = height,
    CodecPrivateData = privateData,
  };

  private static MediaStreamInfo _TaggedStream(string tag)
    => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(tag) };

  private static RawImage _Decode(int width, int height, byte quantiser, byte[] packetBytes) {
    var decoder = Asv1VideoDecoder.Create(_Stream(width, height, _PrivateData(quantiser)));
    Assert.That(decoder.TryDecode(new(0, packetBytes), out var frame), Is.True);
    return frame;
  }

  private static H263Frame _DecodePlanes(int width, int height, byte quantiser, byte[] packetBytes) {
    var decoder = Asv1VideoDecoder.Create(_Stream(width, height, _PrivateData(quantiser)));
    return decoder._DecodePlanes(new(0, packetBytes));
  }

  private static byte _Red(RawImage image, int width, int x, int y) => image.PixelData[(y * width + x) * 3];

  private static byte _Grey(int luminance) => (byte)Math.Clamp((298 * (luminance - 16) + 128) >> 8, 0, 255);
}
