using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.DigitalFx;
using FileFormat.DispThumbnail;
using FileFormat.SecretPhotos;
using FileFormat.SriSun;
using FileFormat.Ximage;

namespace Hawkynt.FileFormats.Images.Tests.GapClosures;

/// <summary>
/// Five formats nothing has ever published a description of, settled instead against XnView's own
/// converter: SriSun, Ximage, Digital F/X, the DISPTNL thumbnail and the SecretPhotos puzzle. Every
/// fixture below is one that converter reads at the size and depth it was built with, and the pixel
/// expectations are the ones it hands back for it.
/// </summary>
[TestFixture]
public sealed class XnViewReaderClosureTests {

  // -------- SriSun --------

  private static byte[] _SriSun(int depth, int width, int height, byte[] rows) {
    var header = new byte[SriSunFile.HeaderSize];
    SriSunFile.Magic.CopyTo(header);
    header[10] = (byte)depth;
    header[11] = 2;
    header[12] = (byte)(width >> 8);
    header[13] = (byte)width;
    header[14] = (byte)(height >> 8);
    header[15] = (byte)height;
    return header.Concat(rows).ToArray();
  }

  [Test]
  [Category("Unit")]
  public void SriSun_ReadsEightBitRowsAsTheGreysTheyAre() {
    var rows = new byte[] { 0, 17, 34, 51, 68, 85, 102, 119, 136, 153, 170, 187 };
    var image = SriSunFile.ToRawImage(SriSunReader.FromBytes(_SriSun(8, 4, 3, rows)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(4));
      Assert.That(image.Height, Is.EqualTo(3));
      for (var i = 0; i < rows.Length; ++i)
        Assert.That(image.PixelData[i * 3], Is.EqualTo(rows[i]), $"pixel {i}");
    });
  }

  [Test]
  [Category("Unit")]
  public void SriSun_ReadsTwentyFourBitRowsAsRedGreenAndBlueInThatOrder() {
    var rows = Enumerable.Range(0, 12).Select(i => (byte)(i * 17)).ToArray();
    var image = SriSunFile.ToRawImage(SriSunReader.FromBytes(_SriSun(24, 4, 1, rows)));

    Assert.That(image.PixelData[..12], Is.EqualTo(rows));
  }

  [Test]
  [Category("Unit")]
  public void SriSun_ReadsSixteenBitsAsFiveBitsAChannelInALittleEndianWord() {
    // 0x2011: red 8, green 0, blue 17, which XnView returns as 65, 0, 139.
    var image = SriSunFile.ToRawImage(SriSunReader.FromBytes(_SriSun(16, 1, 1, [0x11, 0x20])));

    Assert.Multiple(() => {
      Assert.That(image.PixelData[0], Is.EqualTo(65));
      Assert.That(image.PixelData[1], Is.EqualTo(0));
      Assert.That(image.PixelData[2], Is.EqualTo(139));
    });
  }

  [Test]
  [Category("Unit")]
  public void SriSun_ReadsASetBitAsWhite() {
    var image = SriSunFile.ToRawImage(SriSunReader.FromBytes(_SriSun(1, 8, 1, [0b10110001])));
    var greys = Enumerable.Range(0, 8).Select(x => image.PixelData[x * 3]).ToArray();

    Assert.That(greys, Is.EqualTo(new byte[] { 255, 0, 255, 255, 0, 0, 0, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void SriSun_WithoutItsEightLettersIsRefused() {
    var data = _SriSun(8, 4, 3, new byte[12]);
    data[0] = (byte)'S';
    Assert.Throws<InvalidDataException>(() => SriSunReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void SriSun_WithADataTypeItCannotReadIsRefusedRatherThanDrawn() {
    var data = _SriSun(8, 4, 3, new byte[12]);
    data[SriSunFile.DataTypeAt] = 1;
    Assert.Throws<InvalidDataException>(() => SriSunReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void SriSun_ThatIsShorterThanItsOwnSizeIsRefused()
    => Assert.Throws<InvalidDataException>(() => SriSunReader.FromBytes(_SriSun(8, 40, 30, new byte[12])));

  // -------- Ximage --------

  private static void _Text(byte[] data, int at, int length, int value) {
    var text = value.ToString();
    for (var i = 0; i < length; ++i)
      data[at + i] = i < text.Length ? (byte)text[i] : (byte)' ';
  }

  private static byte[] _Ximage(int width, int height, int colours, int planes, bool coded, byte[] body) {
    var header = new byte[XimageFile.HeaderSize];
    _Text(header, 0, 8, XimageFile.Version);
    _Text(header, 8, 8, XimageFile.HeaderSize);
    _Text(header, 16, 8, width);
    _Text(header, 24, 8, height);
    _Text(header, 32, 8, colours);
    _Text(header, 40, 3, planes);
    _Text(header, 43, 5, width);
    _Text(header, 48, 4, 1);
    _Text(header, 52, 4, 8);
    _Text(header, 56, 4, 0);
    _Text(header, 60, 4, coded ? 1 : 0);
    for (var i = 0; i < XimageFile.PaletteEntries; ++i) {
      header[XimageFile.PaletteOffset + i * 3] = (byte)i;
      header[XimageFile.PaletteOffset + i * 3 + 1] = (byte)(i * 2);
      header[XimageFile.PaletteOffset + i * 3 + 2] = (byte)(255 - i);
    }

    return header.Concat(body).ToArray();
  }

  [Test]
  [Category("Unit")]
  public void Ximage_TakesItsThreePlanesAsRedThenGreenThenBlue() {
    var body = new byte[36];
    for (var plane = 0; plane < 3; ++plane)
      for (var i = 0; i < 12; ++i)
        body[plane * 12 + i] = (byte)(plane * 80 + i * 5);

    var image = XimageFile.ToRawImage(XimageReader.FromBytes(_Ximage(4, 3, 0, 3, false, body)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(4));
      Assert.That(image.Height, Is.EqualTo(3));
      Assert.That(image.PixelData[..6], Is.EqualTo(new byte[] { 0, 80, 160, 5, 85, 165 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Ximage_PutsItsOnePlaneThroughTheColourTableWhenTheHeaderSaysItHasOne() {
    var image = XimageFile.ToRawImage(
      XimageReader.FromBytes(_Ximage(4, 3, 256, 1, false, Enumerable.Range(0, 12).Select(i => (byte)i).ToArray())));
    var rgb = image.EnsureFormat(PixelFormat.Rgb24);

    Assert.Multiple(() => {
      Assert.That(rgb.PixelData[..3], Is.EqualTo(new byte[] { 0, 0, 255 }));
      Assert.That(rgb.PixelData[3..6], Is.EqualTo(new byte[] { 1, 2, 254 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Ximage_UnpacksARunAsOneMoreThanItsCountByte() {
    var body = new byte[] { 3, 10, 3, 30, 3, 50 };
    var image = XimageFile.ToRawImage(XimageReader.FromBytes(_Ximage(4, 3, 0, 1, true, body)));

    Assert.That(
      Enumerable.Range(0, 12).Select(i => image.PixelData[i * 3]).ToArray(),
      Is.EqualTo(new byte[] { 10, 10, 10, 10, 30, 30, 30, 30, 50, 50, 50, 50 }));
  }

  [Test]
  [Category("Unit")]
  public void Ximage_ThatDoesNotStateVersionThreeAndAThousandAndTwentyFourIsRefused() {
    var data = _Ximage(4, 3, 0, 1, false, new byte[12]);
    _Text(data, 8, 8, 512);
    Assert.Throws<InvalidDataException>(() => XimageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void Ximage_UnderTheNameButHoldingSomethingElseIsRefused()
    => Assert.Throws<InvalidDataException>(() => XimageReader.FromBytes(new byte[2048]));

  [Test]
  [Category("Unit")]
  public void Ximage_WithAnAlphaChannelIsRefusedRatherThanDrawnWithoutIt() {
    var data = _Ximage(4, 3, 0, 3, false, new byte[36]);
    _Text(data, 56, 4, 1);
    Assert.Throws<InvalidDataException>(() => XimageReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void Ximage_ClaimingMorePixelsThanItsBodyCouldHoldIsRefusedBeforeAnythingIsAllocated()
    => Assert.Throws<InvalidDataException>(() => XimageReader.FromBytes(_Ximage(16000, 16000, 0, 3, false, new byte[64])));

  // -------- Digital F/X --------

  private static byte[] _DigitalFx(int width, int height, byte[] runs) {
    var header = new byte[16];
    DigitalFxFile.Magic.CopyTo(header);
    header[8] = (byte)(height >> 8);
    header[9] = (byte)height;
    header[10] = (byte)(width >> 8);
    header[11] = (byte)width;
    header[15] = 16;
    return header.Concat(runs).ToArray();
  }

  [Test]
  [Category("Unit")]
  public void DigitalFx_TakesTheSecondThirdAndFourthBytesOfAPixelAsItsColour() {
    var runs = new byte[] { 0x83, 10, 20, 30, 40, 200, 0, 0, 255, 0, 200, 0, 255, 0, 0, 200, 255, 3, 17, 34, 51, 68 };
    var image = DigitalFxFile.ToRawImage(DigitalFxReader.FromBytes(_DigitalFx(4, 2, runs)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(4));
      Assert.That(image.Height, Is.EqualTo(2));
      Assert.That(image.PixelData[..6], Is.EqualTo(new byte[] { 20, 30, 40, 0, 0, 255 }));
      Assert.That(image.PixelData[12..15], Is.EqualTo(new byte[] { 34, 51, 68 }), "the repeated pixel");
    });
  }

  [Test]
  [Category("Unit")]
  public void DigitalFx_LetsARunCarryOnAcrossTheEndOfARow() {
    var image = DigitalFxFile.ToRawImage(DigitalFxReader.FromBytes(_DigitalFx(2, 2, [7, 1, 2, 3, 4])));

    for (var i = 0; i < 4; ++i)
      Assert.That(image.PixelData[i * 3], Is.EqualTo(2), $"pixel {i}");
  }

  [Test]
  [Category("Unit")]
  public void DigitalFx_WithoutItsFourBytesIsRefused() {
    var data = _DigitalFx(2, 2, [7, 1, 2, 3, 4]);
    data[1] = 3;
    Assert.Throws<InvalidDataException>(() => DigitalFxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void DigitalFx_ThatRunsOutOfRunsBeforeItsPictureIsFullIsRefused()
    => Assert.Throws<InvalidDataException>(() => DigitalFxReader.FromBytes(_DigitalFx(8, 8, [0, 1, 2, 3, 4])));

  [Test]
  [Category("Unit")]
  public void DigitalFx_ClaimingMorePixelsThanItsRunsCouldReachIsRefusedBeforeAnythingIsAllocated()
    => Assert.Throws<InvalidDataException>(() => DigitalFxReader.FromBytes(_DigitalFx(32000, 32000, [0, 1, 2, 3, 4])));

  // -------- DISPTNL thumbnail --------

  private static byte[] _Thumbnail(byte kind, int width, int height, byte[] body) {
    var header = new byte[DispThumbnailFile.PictureOffset];
    DispThumbnailFile.Magic.CopyTo(header);
    header[7] = kind;
    header[16] = (byte)width;
    header[17] = (byte)(width >> 8);
    header[20] = (byte)height;
    header[21] = (byte)(height >> 8);
    return header.Concat(body).ToArray();
  }

  [Test]
  [Category("Unit")]
  public void Thumbnail_ReadsItsGreysOneByteAPixelFromAHundredAndSixtyEight() {
    var body = Enumerable.Range(0, 15).Select(i => (byte)(i * 13)).ToArray();
    var image = DispThumbnailFile.ToRawImage(DispThumbnailReader.FromBytes(_Thumbnail((byte)'0', 5, 3, body)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(5));
      Assert.That(image.Height, Is.EqualTo(3));
      for (var i = 0; i < body.Length; ++i)
        Assert.That(image.PixelData[i * 3], Is.EqualTo(body[i]), $"pixel {i}");
    });
  }

  [Test]
  [Category("Unit")]
  public void Thumbnail_WithoutItsSevenLettersIsRefused() {
    var data = _Thumbnail((byte)'0', 5, 3, new byte[15]);
    data[4] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => DispThumbnailReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void Thumbnail_MarkedFiveButCarryingNoJpegIsRefused()
    => Assert.Throws<InvalidDataException>(
      () => DispThumbnailReader.FromBytes(_Thumbnail(DispThumbnailFile.JpegMarker, 5, 3, new byte[15])));

  [Test]
  [Category("Unit")]
  public void Thumbnail_ThatIsShorterThanItsOwnSizeIsRefused()
    => Assert.Throws<InvalidDataException>(() => DispThumbnailReader.FromBytes(_Thumbnail((byte)'0', 50, 30, new byte[15])));

  // -------- SecretPhotos puzzle --------

  [Test]
  [Category("Unit")]
  public void SecretPhotos_WithoutItsFourBytesIsRefused() {
    var data = new byte[SecretPhotosFile.PictureOffset + 64];
    data[3] = 2;
    data[SecretPhotosFile.PictureOffset] = 0xFF;
    data[SecretPhotosFile.PictureOffset + 1] = 0xD8;
    data[SecretPhotosFile.PictureOffset + 2] = 0xFF;
    Assert.Throws<InvalidDataException>(() => SecretPhotosReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void SecretPhotos_WithNothingButPaddingWhereItsPictureBelongsIsRefused() {
    var data = new byte[SecretPhotosFile.PictureOffset + 64];
    SecretPhotosFile.Magic.CopyTo(data);
    Assert.Throws<InvalidDataException>(() => SecretPhotosReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void SecretPhotos_TooShortToReachItsPictureIsRefused()
    => Assert.Throws<InvalidDataException>(() => SecretPhotosReader.FromBytes(new byte[64]));

  // -------- the names themselves --------

  [TestCase(".ssi")]
  [TestCase(".xim")]
  [TestCase(".tdim")]
  [TestCase(".tnl")]
  [TestCase(".xp0")]
  [TestCase(".stm")]
  [TestCase(".upi")]
  [TestCase(".xif")]
  [Category("Unit")]
  public void EveryNameClosedHereIsOneTheRegistryCanReach(string extension)
    => Assert.That(FormatRegistry.DetectFromExtension(extension), Is.Not.EqualTo(ImageFormat.Unknown));
}
