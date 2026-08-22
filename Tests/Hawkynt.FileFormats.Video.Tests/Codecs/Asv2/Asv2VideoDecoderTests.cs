using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs.Asv2.Tests;

/// <summary>
/// The ASV2 decoder, on streams built here bit by bit.
/// </summary>
/// <remarks>
/// Real ASV2 streams were compared with ffmpeg's decode of the same bitstream, plane by plane and
/// frame by frame, over 302 frames across six streams and a quantiser range of 1 to 69 wide enough to
/// exercise every magnitude a coded level carries, including the ones asv1.txt leaves unstated between
/// its printed examples — see this codec's section of <c>README.md</c> for the measurement and how that
/// unstated range was recovered. What these tests add is the sharpest single case of each: a block
/// whose DC field alone has to survive the fixed-width fields' own extra bit reversal, one coefficient
/// from the extrapolated part of the level table and one from its escape, and the picture-edge ordering
/// a real encoder rarely if ever needs to reach in full.
/// </remarks>
[TestFixture]
public sealed class Asv2VideoDecoderTests {

  // ============================================================================================
  // The DC coefficient, and the fixed-width fields' own bit reversal — asv1.txt 3.5, 4.5
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatIntraPictureIsMidGrey() {
    var stream = new Asv2TestStream();
    stream.FlatMacroblock(128);

    var frame = _Decode(16, 16, 1, stream.ToPacketBytes());

    Assert.That(frame.Width, Is.EqualTo(16));
    Assert.That(frame.Height, Is.EqualTo(16));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(128) }));
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(255)]
  [Category("Unit")]
  public void EveryDcFieldValueIsAValidLuminance(int dc) {
    // 1 and 128 are each other's bit-reversal at eight bits, which is exactly the pair a decoder that
    // forgot the fixed-width fields' own second reversal confuses.
    var stream = new Asv2TestStream();
    stream.FlatMacroblock(dc);

    var frame = _Decode(16, 16, 1, stream.ToPacketBytes());

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(dc) }));
  }

  // ============================================================================================
  // AC coefficients — asv1.txt 3.6, 5.2.1 and 5.2.3
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASmallAcLevelIsDequantisedAgainstTheMpeg1IntraMatrixAndTransformed() {
    // Coefficient group 0's first pattern 0b0100 (code "101") sets its second position, raster
    // position 8 — row one, column zero. At QUANT 4 its dequantisation factor is 128 * 16 / 4 = 512,
    // and a level of -12 (code "00010011", the extrapolated part of the level table) dequantises to
    // -12 * 512 / 16 = -384.
    var stream = new Asv2TestStream();
    stream.ReversedField(0, 4).ReversedField(128, 8).Code("101").Code("00010011");
    stream.DcOnlyBlock(128).DcOnlyBlock(128).DcOnlyBlock(128);
    stream.DcOnlyBlock(128).DcOnlyBlock(128);

    var frame = _DecodePlanes(16, 16, 4, stream.ToPacketBytes());

    int[] rows = [61, 72, 90, 115, 141, 166, 184, 195];
    for (var y = 0; y < 8; ++y)
      Assert.That(frame.Luma[y * frame.LumaWidth], Is.EqualTo(rows[y]), $"row {y}");
  }

  [Test]
  [Category("Unit")]
  public void AnEscapedLevelCarriesItsOwnSignAsTwosComplement() {
    // The same position, this time with a level too large even for the extrapolated table: the escape
    // "00000" followed by an eight-bit two's-complement value, -33 here (0xDF), dequantising to
    // -33 * 512 / 16 = -1056 at the same QUANT 4.
    var stream = new Asv2TestStream();
    stream.ReversedField(0, 4).ReversedField(128, 8).Code("101").EscapedLevel(-33);
    stream.DcOnlyBlock(128).DcOnlyBlock(128).DcOnlyBlock(128);
    stream.DcOnlyBlock(128).DcOnlyBlock(128);

    var frame = _DecodePlanes(16, 16, 4, stream.ToPacketBytes());

    int[] rows = [0, 0, 24, 92, 164, 232, 255, 255];
    for (var y = 0; y < 8; ++y)
      Assert.That(frame.Luma[y * frame.LumaWidth], Is.EqualTo(rows[y]), $"row {y}");
  }

  // ============================================================================================
  // Picture geometry that is not a whole number of macroblocks — asv1.txt 3.1
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APartialWidthColumnAndPartialHeightRowAreCodedAfterTheMainBody() {
    var stream = new Asv2TestStream();
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
  public void AQuantiserOfZeroIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => Asv2VideoDecoder.Create(_Stream(16, 16, _PrivateData(0))));
    Assert.That(failure.Message, Does.Contain("zero"));
  }

  [Test]
  [Category("Unit")]
  public void PrivateDataShorterThanTheGlobalHeaderIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(
      () => Asv2VideoDecoder.Create(_Stream(16, 16, new byte[40])));
    Assert.That(failure.Message, Does.Contain("global header"));
  }

  [TestCase(0, 16)]
  [TestCase(16, 0)]
  [TestCase(-1, 16)]
  public void ANonPositivePictureSizeIsRefused(int width, int height) {
    var failure = Assert.Throws<InvalidDataException>(
      () => Asv2VideoDecoder.Create(_Stream(width, height, _PrivateData(1))));
    Assert.That(failure.Message, Does.Contain("picture size"));
  }

  // ============================================================================================
  // Identity
  // ============================================================================================

  [TestCase("ASV2", true)]
  [TestCase("asv2", true)]
  [TestCase("ASV1", false)]
  [TestCase("H261", false)]
  public void TheCodecTakesTheStreamsItsContainerNames(string tag, bool expected)
    => Assert.That(Asv2VideoDecoder.Accepts(_TaggedStream(tag)), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsNotTakenWhateverItsTag()
    => Assert.That(
      Asv2VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("ASV2") }),
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
    Codec = CodecTag.FromCharacters("ASV2"),
    Width = width,
    Height = height,
    CodecPrivateData = privateData,
  };

  private static MediaStreamInfo _TaggedStream(string tag)
    => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(tag) };

  private static RawImage _Decode(int width, int height, byte quantiser, byte[] packetBytes) {
    var decoder = Asv2VideoDecoder.Create(_Stream(width, height, _PrivateData(quantiser)));
    Assert.That(decoder.TryDecode(new(0, packetBytes), out var frame), Is.True);
    return frame;
  }

  private static H263Frame _DecodePlanes(int width, int height, byte quantiser, byte[] packetBytes) {
    var decoder = Asv2VideoDecoder.Create(_Stream(width, height, _PrivateData(quantiser)));
    return decoder._DecodePlanes(new(0, packetBytes));
  }

  private static byte _Red(RawImage image, int width, int x, int y) => image.PixelData[(y * width + x) * 3];

  private static byte _Grey(int luminance) => (byte)Math.Clamp((298 * (luminance - 16) + 128) >> 8, 0, 255);
}
