using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The v210x decoder on independently constructed big-endian words.
/// </summary>
[TestFixture]
public sealed class V210XVideoDecoderTests {

  private static MediaStreamInfo _Stream(int width, int height, string? codecId = V210XVideoDecoder.CodecId, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? CodecTag.None,
    CodecId = codecId,
    Width = width,
    Height = height,
  };

  private static readonly byte[] _SixPixelVector = [
    0x19, 0x00, 0x16, 0x40, // U0=100, Y0=1,   V0=400
    0x00, 0x8C, 0x80, 0x0C, // Y1=2,   U1=200, Y2=3
    0x7D, 0x00, 0x44, 0xB0, // V1=500, Y3=4,   U2=300
    0x01, 0x65, 0x80, 0x18, // Y4=5,   V2=600, Y5=6
  ];

  [Test]
  [Category("Unit")]
  public void AcceptsTheTextualCodecNameAndNoInventedFourCc() {
    Assert.Multiple(() => {
      Assert.That(V210XVideoDecoder.Accepts(_Stream(6, 1)), Is.True);
      Assert.That(V210XVideoDecoder.Accepts(_Stream(6, 1, "V210X")), Is.True);
      Assert.That(V210XVideoDecoder.Accepts(_Stream(6, 1, null)), Is.False);
      Assert.That(V210XVideoDecoder.Accepts(_Stream(6, 1, "v210x", CodecTag.FromCharacters("v210"))), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesEmptyOrOddGeometry() {
    Assert.Throws<InvalidDataException>(() => V210XVideoDecoder.Create(_Stream(0, 1)));
    var odd = Assert.Throws<InvalidDataException>(() => V210XVideoDecoder.Create(_Stream(3, 1)));
    Assert.That(odd!.Message, Does.Contain("odd v210x width"));
  }

  [Test]
  [Category("Unit")]
  public void DecodesTheReferencePackingWordForWord() {
    var decoder = V210XVideoDecoder.Create(_Stream(6, 1));

    var (luma, cb, cr) = decoder.DecodePlanes(_SixPixelVector);

    Assert.Multiple(() => {
      Assert.That(luma, Is.EqualTo(new ushort[] { 1, 2, 3, 4, 5, 6 }));
      Assert.That(cb, Is.EqualTo(new ushort[] { 100, 200, 300 }));
      Assert.That(cr, Is.EqualTo(new ushort[] { 400, 500, 600 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void PackingContinuesAcrossScanlinesRatherThanRestartingPerRow() {
    // The exact same four words are now three rows two pixels wide. A row restart would need each
    // row to begin on a new word and could not decode this sixteen-byte stream to the same samples.
    var decoder = V210XVideoDecoder.Create(_Stream(2, 3));

    var (luma, cb, cr) = decoder.DecodePlanes(_SixPixelVector);

    Assert.Multiple(() => {
      Assert.That(luma, Is.EqualTo(new ushort[] { 1, 2, 3, 4, 5, 6 }));
      Assert.That(cb, Is.EqualTo(new ushort[] { 100, 200, 300 }));
      Assert.That(cr, Is.EqualTo(new ushort[] { 400, 500, 600 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void AcceptsTrailingRawFramePaddingButRefusesATruncatedLastWord() {
    var decoder = V210XVideoDecoder.Create(_Stream(6, 1));
    var padded = new byte[128];
    _SixPixelVector.CopyTo(padded, 0);

    Assert.That(() => decoder.DecodePlanes(padded), Throws.Nothing);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(_SixPixelVector[..^1]));
    Assert.That(failure!.Message, Does.Contain("15 byte(s)"));
    Assert.That(failure.Message, Does.Contain("at least 16"));
  }

  [Test]
  [Category("Unit")]
  public void TwoPixelsConsumeTwoWholeWordsAndIgnoreUnusedFieldsInTheSecond() {
    // U=10, Y0=100, V=20 in word zero; Y1=200 in the high ten bits of word one. The remaining
    // fields are deliberately non-zero and must not create samples that the two-pixel picture lacks.
    byte[] data = [
      0x02, 0x86, 0x40, 0x50,
      0x32, 0x3F, 0xFF, 0xFC,
    ];
    var decoder = V210XVideoDecoder.Create(_Stream(2, 1));

    var (luma, cb, cr) = decoder.DecodePlanes(data);

    Assert.Multiple(() => {
      Assert.That(luma, Is.EqualTo(new ushort[] { 100, 200 }));
      Assert.That(cb, Is.EqualTo(new ushort[] { 10 }));
      Assert.That(cr, Is.EqualTo(new ushort[] { 20 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void TryDecodeReturnsAnIndependentRgbPicture() {
    var decoder = V210XVideoDecoder.Create(_Stream(6, 1));

    Assert.That(decoder.TryDecode(new CodedPacket(0, _SixPixelVector), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(6));
      Assert.That(frame.Height, Is.EqualTo(1));
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(frame.PixelData, Has.Length.EqualTo(18));
    });
  }
}
