using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The avrp decoder, on words built here bit by bit.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg's own encoder over four geometries and twenty
/// frames of <c>rgbtestsrc</c> — 8x2, 64x40, 100x30 and 33x25, covering a width under one
/// sixty-four-pixel block, exactly one block, more than one block and one padding by less than half a
/// block — comparing every word this decoder produces against the <c>gbrp10le</c> planes that went
/// into the encoder: every one identical. What these tests pin down is the packing itself and the
/// padding rule, since that comparison alone does not say which bit range of the word a mistake would
/// have hidden in, or where a coded row's padding begins.
/// </remarks>
[TestFixture]
public class AvrpVideoDecoderTests {

  private static readonly CodecTag _Avrp = CodecTag.FromCharacters("AVrp");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _Avrp,
    Width = width,
    Height = height,
  };

  [Test]
  [Category("Unit")]
  public void AcceptsTheAvrpTagIgnoringCase() {
    Assert.That(AvrpVideoDecoder.Accepts(_Stream(1, 1)), Is.True);
    Assert.That(AvrpVideoDecoder.Accepts(_Stream(1, 1, CodecTag.FromCharacters("avRP"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(AvrpVideoDecoder.Accepts(_Stream(1, 1, CodecTag.FromCharacters("r10k"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => AvrpVideoDecoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void DecodesOnePixelRedHighGreenMiddleBlueNextWithTheTwoUnusedBitsAtTheBottomLittleEndian() {
    // R=500, G=300, B=700, packed as R<<22 | G<<12 | B<<2 and stored little-endian: f0 ca 12 7d. One
    // pixel in a row padded to sixty-four columns — the other sixty-three are irrelevant filler.
    var row = new byte[64 * 4];
    new byte[] { 0xf0, 0xca, 0x12, 0x7d }.CopyTo(row, 0);
    var decoder = AvrpVideoDecoder.Create(_Stream(1, 1));

    var decoded = decoder.TryDecode(new(0, row), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb30));
    var packed = BitConverter.ToUInt32(frame.PixelData, 0);
    Assert.That(packed & 0x3FF, Is.EqualTo(500u), "red, Rgb30's low ten bits");
    Assert.That((packed >> 10) & 0x3FF, Is.EqualTo(300u), "green, Rgb30's middle ten bits");
    Assert.That((packed >> 20) & 0x3FF, Is.EqualTo(700u), "blue, Rgb30's high ten bits");
    Assert.That((packed >> 30) & 0x3, Is.EqualTo(3u), "fully opaque, matching the two bits avrp leaves spare");
  }

  [Test]
  [Category("Unit")]
  public void ARowPadsUpToTheNextSixtyFourPixelBlock() {
    // A width of 33 pads to sixty-four columns, not to the next multiple of thirty-two or to no
    // padding at all — measured directly against ffmpeg's own encoder.
    var decoder = AvrpVideoDecoder.Create(_Stream(33, 1));
    var row = new byte[64 * 4];

    Assert.That(() => decoder.TryDecode(new(0, row), out _), Throws.Nothing);
    Assert.That(() => decoder.TryDecode(new(0, row[..(63 * 4)]), out _), Throws.TypeOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void AWidthThatIsAWholeNumberOfBlocksPadsToItself() {
    var decoder = AvrpVideoDecoder.Create(_Stream(64, 1));
    var row = new byte[64 * 4];

    Assert.That(() => decoder.TryDecode(new(0, row), out _), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsStride() {
    var decoder = AvrpVideoDecoder.Create(_Stream(4, 2));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[20]), out _));
    Assert.That(failure!.Message, Does.Contain("20 byte(s)"));
    Assert.That(failure.Message, Does.Contain("needs 512"));
  }
}
