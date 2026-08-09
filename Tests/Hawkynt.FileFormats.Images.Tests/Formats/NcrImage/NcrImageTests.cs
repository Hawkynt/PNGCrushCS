using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ccitt;
using FileFormat.Core;
using FileFormat.NcrImage;

namespace FileFormat.NcrImage.Tests;

/// <summary>
/// The NCR Image: a Group 4 raster under a fixed header.
/// </summary>
/// <remarks>
/// Nothing describing this format has been published; the header was recovered from XnView's own
/// reader. What stands outside this file is that a fixture built this way — the same coded bytes
/// under the same header — is read by XnView's converter at the size it states and comes back as the
/// page that was coded, pixel for pixel.
/// </remarks>
[TestFixture]
public sealed class NcrImageTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 32;

  /// <summary>A checkerboard, packed one bit a pixel with a set bit for ink.</summary>
  private static byte[] _Bitmap() {
    var stride = BilevelRows.Stride(_WIDTH);
    var bits = new byte[stride * _HEIGHT];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x)
        if ((x / 5 + y / 3) % 2 == 0)
          bits[y * stride + x / 8] |= (byte)(0x80 >> (x % 8));

    return bits;
  }

  private static byte[] _Build(byte coding = 1, int width = _WIDTH, int height = _HEIGHT, byte[]? coded = null) {
    coded ??= CcittG4Encoder.Encode(_Bitmap(), _WIDTH, _HEIGHT);
    var data = new byte[NcrImageFile.CodedDataOffset + coded.Length];
    NcrImageFile.Signature.CopyTo(data);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(NcrImageFile.WidthOffset), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(NcrImageFile.HeightOffset), (ushort)height);
    data[NcrImageFile.CodingOffset] = coding;
    coded.CopyTo(data, NcrImageFile.CodedDataOffset);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NcrImageReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ncr"));
    Assert.Throws<FileNotFoundException>(() => NcrImageReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NcrImageReader.FromBytes(new byte[64]));

  /// <summary>A file of the right length and none of the signature is not one of these.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheSignature_IsRefused()
    => Assert.Throws<InvalidDataException>(() => NcrImageReader.FromBytes(new byte[NcrImageFile.CodedDataOffset + 256]));

  /// <summary>Coding zero selects something else in XnView and no file exists to check it against.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ACodingOtherThanGroupFourIsRefused()
    => Assert.Throws<InvalidDataException>(() => NcrImageReader.FromBytes(_Build(coding: 0)));

  [Test]
  [Category("Unit")]
  public void FromBytes_CodingShorterThanTheStatedHeightIsRefused()
    => Assert.Throws<InvalidDataException>(() => NcrImageReader.FromBytes(_Build(height: _HEIGHT * 2)));

  [Test]
  [Category("Unit")]
  public void FromBytes_AStatedSizeOfNothingIsRefused()
    => Assert.Throws<InvalidDataException>(() => NcrImageReader.FromBytes(_Build(width: 0)));

  [Test]
  [Category("Integration")]
  public void FromBytes_APageIsReadAtTheStatedSize() {
    var read = NcrImageReader.FromBytes(_Build());

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
      Assert.That(read.PixelData, Is.EqualTo(_Bitmap()));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_ASetBitIsInk() {
    var image = NcrImageFile.ToRawImage(NcrImageReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(image.PaletteCount, Is.EqualTo(2));
      Assert.That(image.PixelData[0], Is.EqualTo(1));
      Assert.That(image.PixelData[5], Is.EqualTo(0));
    });
  }

  /// <summary>Every coding byte from one upwards selects Group 4, as XnView reads them.</summary>
  [Test]
  [Category("Integration")]
  public void FromBytes_EveryCodingFromOneUpwardsIsGroupFour([Values(1, 2, 255)] int coding) {
    var read = NcrImageReader.FromBytes(_Build((byte)coding));

    Assert.That(read.PixelData, Is.EqualTo(_Bitmap()));
  }
}
