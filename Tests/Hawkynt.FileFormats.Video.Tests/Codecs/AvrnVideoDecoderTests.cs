using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The avrn decoder, on a packet built here that names its own crop direction unambiguously.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg's own decode of four real captures at
/// samples.ffmpeg.org — 720x486, 720x576 twice and 160x120, 46, 50, 50 and 200 pictures — plane by
/// plane, every frame: 345 of the 346 real pictures agree with ffmpeg to within the same small,
/// pre-existing chroma-upsampling margin every JPEG-family decoder in this package already carries
/// (see <c>README.md</c>), and the one exception is a capture's own last packet, cut off mid-picture
/// before this decoder or ffmpeg ever saw it. What this test pins down is the one genuinely easy
/// mistake the real captures could not have caught blind: which end of a taller-than-declared coded
/// picture the padding sits on. The 720x486 capture's own frame header codes 720x496 — sixteen rows
/// more than the container states — and keeping the wrong sixteen would still have produced a picture,
/// just the wrong one; only comparing against ffmpeg settled it, and this fixture makes the same fact
/// checkable without a capture at all: a picture whose top half is one flat colour and whose bottom
/// half is another says on its face which sixteen rows a decoder kept.
/// </remarks>
[TestFixture]
public class AvrnVideoDecoderTests {

  private static readonly CodecTag _Avrn = CodecTag.FromCharacters("AVRn");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _Avrn,
    Width = width,
    Height = height,
  };

  // A real 16x32 baseline JPEG (ffmpeg's own encoder, 4:4:4): the top sixteen rows flat red
  // (0xFE, 0x00, 0x00), the bottom sixteen flat blue (0x00, 0x00, 0xFE).
  private static readonly byte[] _RedOverBlue = [
    0xFF, 0xD8, 0xFF, 0xFE, 0x00, 0x0F, 0x4C, 0x61, 0x76, 0x63, 0x36, 0x33, 0x2E, 0x31, 0x2E, 0x31, 0x30, 0x30, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x08, 0x04, 0x04, 0x04, 0x04, 0x04, 0x05, 0x05, 0x05, 0x05, 0x05, 0x05, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x07, 0x07, 0x07, 0x08, 0x08, 0x08, 0x07, 0x07, 0x07, 0x06, 0x06, 0x07, 0x07, 0x08, 0x08, 0x08, 0x08, 0x09, 0x09, 0x09, 0x08, 0x08, 0x08, 0x08, 0x09, 0x09, 0x0A, 0x0A, 0x0A, 0x0C, 0x0C, 0x0B, 0x0B, 0x0E, 0x0E, 0x0E, 0x11, 0x11, 0x14, 0xFF, 0xC4, 0x00, 0x4E, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x07, 0x06, 0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x20, 0x00, 0x10, 0x03, 0x01, 0x12, 0x00, 0x02, 0x12, 0x00, 0x03, 0x12, 0x00, 0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00, 0x8B, 0x1C, 0xA0, 0xDF, 0xC0, 0x00, 0x48, 0x0A, 0xA8, 0x4D, 0x60, 0x00, 0x3F, 0xFF, 0xD9,
  ];

  private static readonly byte[] _Red = [0xFE, 0x00, 0x00];
  private static readonly byte[] _Blue = [0x00, 0x00, 0xFE];

  [Test]
  [Category("Unit")]
  public void AcceptsTheAvrnTagIgnoringCase() {
    Assert.That(AvrnVideoDecoder.Accepts(_Stream(1, 1)), Is.True);
    Assert.That(AvrnVideoDecoder.Accepts(_Stream(1, 1, CodecTag.FromCharacters("avrN"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() => Assert.That(AvrnVideoDecoder.Accepts(_Stream(1, 1, CodecTag.FromCharacters("MJPG"))), Is.False);

  [Test]
  [Category("Unit")]
  public void LeavesThePictureAloneWhenTheContainerAgreesWithTheJpeg() {
    var decoder = AvrnVideoDecoder.Create(_Stream(16, 32));

    var ok = decoder.TryDecode(new(0, _RedOverBlue), out var frame);

    Assert.That(ok, Is.True);
    Assert.That(frame.Width, Is.EqualTo(16));
    Assert.That(frame.Height, Is.EqualTo(32));
    Assert.That(frame.PixelData[..3], Is.EqualTo(_Red));
    Assert.That(frame.PixelData[(31 * 16 * 3)..(31 * 16 * 3 + 3)], Is.EqualTo(_Blue));
  }

  [Test]
  [Category("Unit")]
  public void CropsTheTopRowsAwayRatherThanTheBottomOnes() {
    var decoder = AvrnVideoDecoder.Create(_Stream(16, 16));

    var ok = decoder.TryDecode(new(0, _RedOverBlue), out var frame);

    Assert.That(ok, Is.True);
    Assert.That(frame.Width, Is.EqualTo(16));
    Assert.That(frame.Height, Is.EqualTo(16));
    for (var y = 0; y < 16; ++y)
      for (var x = 0; x < 16; ++x)
        Assert.That(frame.PixelData[((y * 16 + x) * 3)..((y * 16 + x) * 3 + 3)], Is.EqualTo(_Blue), $"pixel at row {y} column {x}");
  }

  [Test]
  [Category("Unit")]
  public void RefusesAContainerSizeLargerThanTheJpegCodes() {
    var decoder = AvrnVideoDecoder.Create(_Stream(16, 48));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _RedOverBlue), out _));
    Assert.That(failure!.Message, Does.Contain("16x48"));
    Assert.That(failure!.Message, Does.Contain("16x32"));
  }
}
