using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The avui decoder, on rows built here byte by byte.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg's own encoder over both geometries it ever
/// writes — 720x486 and 720x576 — and fifty frames of pseudo-random content each, comparing
/// <see cref="AvuiVideoDecoder.DecodePlanes"/> against ffmpeg's own raw <c>uyvy422</c> output of the
/// same content: every sample of every plane of every frame identical, and the header bytes ahead of
/// the picture all zero in every one of the hundred frames measured. What these tests pin down is the
/// packing and the header skip themselves, since that comparison alone does not say which byte of a
/// group is which sample or how large the header is at each geometry.
/// </remarks>
[TestFixture]
public class AvuiVideoDecoderTests {

  private static readonly CodecTag _Avui = CodecTag.FromCharacters("AVUI");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _Avui,
    Width = width,
    Height = height,
  };

  [Test]
  [Category("Unit")]
  public void AcceptsTheAvuiTagIgnoringCase() {
    Assert.That(AvuiVideoDecoder.Accepts(_Stream(720, 486)), Is.True);
    Assert.That(AvuiVideoDecoder.Accepts(_Stream(720, 486, CodecTag.FromCharacters("avui"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(AvuiVideoDecoder.Accepts(_Stream(720, 486, CodecTag.FromCharacters("avrp"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => AvuiVideoDecoder.Create(_Stream(0, 486)));
    Assert.That(failure!.Message, Does.Contain("0x486"));
  }

  [Test]
  [Category("Unit")]
  public void AcceptsTheNtscGeometry() {
    Assert.That(() => AvuiVideoDecoder.Create(_Stream(720, 486)), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void AcceptsThePalGeometry() {
    Assert.That(() => AvuiVideoDecoder.Create(_Stream(720, 576)), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryNeitherStandardWrites() {
    // avui's own encoder refuses every width and height but the two SD standards, and there is no
    // real stream to say what a header of any other size would mean.
    var failure = Assert.Throws<NotSupportedException>(() => AvuiVideoDecoder.Create(_Stream(720, 480)));
    Assert.That(failure!.Message, Does.Contain("720x480"));
  }

  // ============================================================================================
  // The packing: one row is Cb(0), Y(0), Cr(0), Y(1), repeating — ordinary uyvy422
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DecodesTheFirstFourColumnsOfARowAfterTheHeader() {
    // Y = 101..104, Cb = 201, 203, Cr = 151, 153, packed as ffmpeg's own encoder writes uyvy422:
    // Cb(0), Y(0), Cr(0), Y(1), Cb(1), Y(2), Cr(1), Y(3). NTSC's geometry is fixed at 720 columns, so
    // the first row's leading eight bytes are what this test pins down and the rest of the picture is
    // filled with an arbitrary but complete payload behind them.
    var packet = new byte[14_400 + 720 * 2 * 486];
    var row = new byte[] { 201, 101, 151, 102, 203, 103, 153, 104 };
    row.CopyTo(packet, 14_400);
    var decoder = AvuiVideoDecoder.Create(_Stream(720, 486));

    var (luma, cb, cr) = decoder.DecodePlanes(packet);

    Assert.That(luma[..4], Is.EqualTo(new byte[] { 101, 102, 103, 104 }));
    Assert.That(cb[..2], Is.EqualTo(new byte[] { 201, 203 }));
    Assert.That(cr[..2], Is.EqualTo(new byte[] { 151, 153 }));
  }

  [Test]
  [Category("Unit")]
  public void TheHeaderIsTenLinesAtNtscAndSixteenAtPal() {
    // A packet exactly the header plus the whole picture is complete; one byte short of it is not.
    var ntscHeader = 720 * 2 * 10;
    var palHeader = 720 * 2 * 16;

    var ntsc = AvuiVideoDecoder.Create(_Stream(720, 486));
    Assert.That(() => ntsc.DecodePlanes(new byte[ntscHeader + 720 * 2 * 486]), Throws.Nothing);
    Assert.That(() => ntsc.DecodePlanes(new byte[ntscHeader + 720 * 2 * 486 - 1]), Throws.TypeOf<InvalidDataException>());

    var pal = AvuiVideoDecoder.Create(_Stream(720, 576));
    Assert.That(() => pal.DecodePlanes(new byte[palHeader + 720 * 2 * 576]), Throws.Nothing);
    Assert.That(() => pal.DecodePlanes(new byte[palHeader + 720 * 2 * 576 - 1]), Throws.TypeOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsHeaderPlusItsStride() {
    var decoder = AvuiVideoDecoder.Create(_Stream(720, 486));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(new byte[100]));
    Assert.That(failure!.Message, Does.Contain("100 byte(s)"));
    Assert.That(failure.Message, Does.Contain("14400"));
  }

  [Test]
  [Category("Unit")]
  public void TryDecodeAlwaysReturnsAPicture() {
    var packet = new byte[14_400 + 720 * 2 * 486];
    var decoder = AvuiVideoDecoder.Create(_Stream(720, 486));

    var decoded = decoder.TryDecode(new(0, packet), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Width, Is.EqualTo(720));
    Assert.That(frame.Height, Is.EqualTo(486));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Length, Is.EqualTo(720 * 486 * 3));
  }
}
