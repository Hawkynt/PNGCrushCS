using System;
using System.IO;
using FileFormat.Core;
using FileFormat.GephardHires;

namespace FileFormat.GephardHires.Tests;

/// <summary>
/// A Gephard picture: the width as a word, the height as a byte, then one bit a pixel.
/// </summary>
/// <remarks>
/// What was tested before was a Commodore 64 screen — a load address, 8000 bytes of bitmap and 1000
/// of colour at a fixed 320 by 200 — and every one of those assertions passed against a model no
/// file has. The one real sample is 2923 bytes and states 158 by 146 in its first three.
/// </remarks>
[TestFixture]
public sealed class GephardHiresReaderTests {

  private const int Width = 158;
  private const int Height = 146;

  private static byte[] _ValidFile(int width = Width, int height = Height) {
    var data = new byte[GephardHiresFile.HeaderSize + MonochromePage.BytesPerRow(width) * height];
    data[0] = (byte)(width & 0xFF);
    data[1] = (byte)(width >> 8);
    data[2] = (byte)height;

    for (var i = GephardHiresFile.HeaderSize; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GephardHiresReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ghg"));
    Assert.Throws<FileNotFoundException>(() => GephardHiresReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GephardHiresReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => GephardHiresReader.FromBytes(new byte[GephardHiresFile.HeaderSize]));

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesTheSizeFromTheFirstThreeBytes() {
    var file = GephardHiresReader.FromBytes(_ValidFile());

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(Width));
      Assert.That(file.Height, Is.EqualTo(Height));
      Assert.That(file.PixelData.Length, Is.EqualTo(MonochromePage.BytesPerRow(Width) * Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AnyOtherLength_ThrowsInvalidDataException() {
    // There is no magic, so the size accounting for the whole file is the only thing saying these
    // three bytes are a Gephard header rather than some other format's first three.
    var data = _ValidFile();
    Array.Resize(ref data, data.Length + 1);

    Assert.Throws<InvalidDataException>(() => GephardHiresReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GephardHiresReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    using var ms = new MemoryStream(_ValidFile(64, 8));
    var file = GephardHiresReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(64));
      Assert.That(file.Height, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_DrawsTheTwoGreysAndNotBlackAndWhite() {
    var data = new byte[GephardHiresFile.HeaderSize + 1];
    data[0] = 8;
    data[2] = 1;
    data[GephardHiresFile.HeaderSize] = 0b1000_0000;

    var rgb = PixelConverter.Convert(GephardHiresFile.ToRawImage(GephardHiresReader.FromBytes(data)), PixelFormat.Rgb24).PixelData;

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(0x22), "a set bit is the darker grey, not black");
      Assert.That(rgb[3], Is.EqualTo(0xCC), "a clear bit is the lighter grey, not white");
    });
  }
}

[TestFixture]
public sealed class GephardHiresRoundTripTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      var shade = (byte)((i * 13 % 2) == 0 ? 0x00 : 0xFF);
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = shade;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheSizeAndTheBitmapComeBack() {
    var original = GephardHiresFile.FromRawImage(_Picture(158, 146));

    var restored = GephardHiresReader.FromBytes(GephardHiresWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = GephardHiresFile.FromRawImage(_Picture(320, 200));
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ghg");
    try {
      File.WriteAllBytes(path, GephardHiresWriter.ToBytes(original));
      var restored = GephardHiresReader.FromFile(new FileInfo(path));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(320));
        Assert.That(restored.Height, Is.EqualTo(200));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TooLarge_ThrowsArgumentException()
    => Assert.Throws<ArgumentException>(() => GephardHiresFile.FromRawImage(_Picture(GephardHiresFile.MaxWidth + 1, 10)));
}
