using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.MgrBitmap;

namespace FileFormat.MgrBitmap.Tests;

/// <summary>
/// An MGR bitmap: "yz", then the size and depth as six-bit halves biased into printable range.
/// </summary>
/// <remarks>
/// These used to build their samples as the text "16x8\n", which is not a form the format has —
/// so they were passing against a reader that read the same invention, and every real file was
/// refused for want of an 'x'.
/// </remarks>
[TestFixture]
public sealed class MgrBitmapReaderTests {

  private static byte[] _Build(int width, int height, byte[] pixels) {
    var data = new byte[MgrBitmapFile.HeaderSize + pixels.Length];
    data[0] = (byte)'y';
    data[1] = (byte)'z';
    _Pair(data, 2, width);
    _Pair(data, 4, height);
    data[6] = MgrBitmapFile.HeaderBias + 1;
    data[7] = MgrBitmapFile.HeaderBias;
    pixels.CopyTo(data.AsSpan(MgrBitmapFile.HeaderSize));

    return data;
  }

  private static void _Pair(Span<byte> target, int at, int value) {
    target[at] = (byte)(MgrBitmapFile.HeaderBias + ((value >> 6) & 0x3F));
    target[at + 1] = (byte)(MgrBitmapFile.HeaderBias + (value & 0x3F));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MgrBitmapReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MgrBitmapReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mgr"));
    Assert.Throws<FileNotFoundException>(() => MgrBitmapReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MgrBitmapReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MgrBitmapReader.FromBytes(new byte[3]));

  [Test]
  [Category("Unit")]
  public void FromBytes_NotYz_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MgrBitmapReader.FromBytes(Encoding.ASCII.GetBytes("1234\n\0\0\0")));

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesCorrectly() {
    var pixels = new byte[2 * 8];
    pixels[0] = 0xFF;
    pixels[1] = 0xAA;

    var file = MgrBitmapReader.FromBytes(_Build(16, 8, pixels));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(16));
      Assert.That(file.Height, Is.EqualTo(8));
      Assert.That(file.PixelData.Length, Is.EqualTo(16));
      Assert.That(file.PixelData[0], Is.EqualTo(0xFF));
      Assert.That(file.PixelData[1], Is.EqualTo(0xAA));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ASizeAboveSixtyThreeUsesBothHalves() {
    // The point of the biased pair: anything wider than 63 spills into the upper half, and reading
    // only the lower one would give a width of 32 for a picture 800 across.
    var file = MgrBitmapReader.FromBytes(_Build(800, 600, new byte[100 * 600]));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(800));
      Assert.That(file.Height, Is.EqualTo(600));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnsupportedDepth_ThrowsInvalidDataException() {
    var data = _Build(8, 1, new byte[1]);
    data[6] = MgrBitmapFile.HeaderBias + 8;

    Assert.Throws<InvalidDataException>(() => MgrBitmapReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    using var ms = new MemoryStream(_Build(8, 4, [0xCD, 0, 0, 0]));
    var file = MgrBitmapReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(8));
      Assert.That(file.Height, Is.EqualTo(4));
      Assert.That(file.PixelData[0], Is.EqualTo(0xCD));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesData() {
    var pixels = new byte[] { 0b1010_1010, 0b0101_0101 };
    var file = MgrBitmapReader.FromBytes(_Build(8, 2, pixels));

    var restored = MgrBitmapReader.FromBytes(MgrBitmapWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(8));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ASetBitIsTheDarkOne() {
    // Sampled the other way round the writer produced every picture as its own negative.
    var image = new RawImage {
      Width = 8, Height = 1, Format = PixelFormat.Rgb24,
      PixelData = [0, 0, 0, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
    };

    Assert.That(MgrBitmapFile.FromRawImage(image).PixelData[0], Is.EqualTo(0b1000_0000));
  }
}
