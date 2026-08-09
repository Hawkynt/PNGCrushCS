using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.MawWareTexture;

namespace FileFormat.MawWareTexture.Tests;

/// <summary>
/// The fixtures are built to the header XnView's own converter was shown to expect, and the cases
/// below are the ones that probing established: the constant decides, the byte count decides the
/// depth, the fifth word is ignored, and two bytes a pixel is refused.
/// </summary>
[TestFixture]
public sealed class MawWareTextureTests {

  private static byte[] _Build(int width, int height, int bytesPerPixel, uint magic = MawWareTextureFile.Magic, uint reserved = 0, int extra = 0) {
    var pixels = width * height * bytesPerPixel + extra;
    var output = new byte[MawWareTextureFile.HeaderSize + pixels];
    _Write(output, 0, magic);
    _Write(output, 4, (uint)width);
    _Write(output, 8, (uint)height);
    _Write(output, 12, (uint)bytesPerPixel);
    _Write(output, 16, reserved);
    for (var i = 0; i < pixels; ++i)
      output[MawWareTextureFile.HeaderSize + i] = (byte)(i * 13 % 251);

    return output;
  }

  private static void _Write(byte[] data, int at, uint value) {
    data[at] = (byte)value;
    data[at + 1] = (byte)(value >> 8);
    data[at + 2] = (byte)(value >> 16);
    data[at + 3] = (byte)(value >> 24);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MawWareTextureReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheOpeningWordIsRefused()
    => Assert.Throws<InvalidDataException>(() => MawWareTextureReader.FromBytes(_Build(4, 3, 3, magic: 0x68)));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheSizeAndTheWidthOfAPixel() {
    var file = MawWareTextureReader.FromBytes(_Build(7, 5, 3));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(7));
      Assert.That(file.Height, Is.EqualTo(5));
      Assert.That(file.BytesPerPixel, Is.EqualTo(3));
      Assert.That(MawWareTextureFile.ToRawImage(file).Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OneByteAPixelIsAGrey()
    => Assert.That(MawWareTextureFile.ToRawImage(MawWareTextureReader.FromBytes(_Build(4, 4, 1))).Format, Is.EqualTo(PixelFormat.Gray8));

  [Test]
  [Category("Unit")]
  public void FromBytes_TheFifthWordChangesNothing() {
    var plain = MawWareTextureReader.FromBytes(_Build(4, 4, 3));
    var marked = MawWareTextureReader.FromBytes(_Build(4, 4, 3, reserved: 0x12345678));

    Assert.That(marked.PixelData, Is.EqualTo(plain.PixelData));
  }

  /// <summary>Four bytes of constant is not a signature, so the length has to account for itself.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AFileLongerOrShorterThanItsOwnArithmeticIsRefused() {
    Assert.Throws<InvalidDataException>(() => MawWareTextureReader.FromBytes(_Build(4, 4, 3, extra: 5)));
    Assert.Throws<InvalidDataException>(() => MawWareTextureReader.FromBytes(_Build(4, 4, 3)[..^5]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TwoBytesAPixelIsRefused()
    => Assert.Throws<InvalidDataException>(() => MawWareTextureReader.FromBytes(_Build(4, 4, 2)));

  [Test]
  [Category("Unit")]
  public void ToBytes_RoundTrips() {
    var pixels = Enumerable.Range(0, 4 * 3 * 3).Select(i => (byte)(i * 5)).ToArray();
    var written = MawWareTextureWriter.ToBytes(new() { Width = 4, Height = 3, BytesPerPixel = 3, PixelData = pixels });
    var read = MawWareTextureReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(4));
      Assert.That(read.Height, Is.EqualTo(3));
      Assert.That(read.PixelData, Is.EqualTo(pixels));
    });
  }
}
