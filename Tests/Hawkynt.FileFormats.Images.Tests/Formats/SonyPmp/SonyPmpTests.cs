using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.SonyPmp;

namespace FileFormat.SonyPmp.Tests;

/// <summary>
/// The Sony Cyber-shot DSC-F1's picture: a JPEG behind a hundred and twenty-four bytes of camera.
/// </summary>
/// <remarks>
/// The header's field map is Fred Klingebiel's, which ExifTool cites as well. What stands outside
/// this file is that a fixture built to it is read by XnView's converter at the JPEG's size — and
/// that XnView returns that same size when the header states a different one, which is why the
/// header's size is not used here either.
/// </remarks>
[TestFixture]
public sealed class SonyPmpTests {

  private const int _WIDTH = 16;
  private const int _HEIGHT = 12;

  private static byte[] _Jpeg() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var at = (y * _WIDTH + x) * 3;
        pixels[at] = (byte)(x * 16);
        pixels[at + 1] = (byte)(y * 20);
        pixels[at + 2] = 128;
      }

    return JpegWriter.ToBytes(JpegFile.FromRawImage(new() {
      Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels
    }));
  }

  private static byte[] _Build(int? statedHeaderSize = null, int? statedJpegLength = null, int headerSize = SonyPmpFile.HeaderSize) {
    var jpeg = _Jpeg();
    var data = new byte[headerSize + jpeg.Length];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(SonyPmpFile.HeaderSizeOffset), (uint)(statedHeaderSize ?? headerSize));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(SonyPmpFile.JpegLengthOffset), (uint)(statedJpegLength ?? jpeg.Length));
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(22), _WIDTH);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(24), _HEIGHT);
    jpeg.CopyTo(data, headerSize);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SonyPmpReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pmp"));
    Assert.Throws<FileNotFoundException>(() => SonyPmpReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SonyPmpReader.FromBytes(new byte[64]));

  /// <summary>A bare JPEG is not one of these, and neither is a JPEG behind a header of another size.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AJpegWithoutTheCameraHeaderIsRefused()
    => Assert.Throws<InvalidDataException>(() => SonyPmpReader.FromBytes(_Build(headerSize: 64)));

  [Test]
  [Category("Unit")]
  public void FromBytes_AHeaderStatingAnotherLengthIsRefused()
    => Assert.Throws<InvalidDataException>(() => SonyPmpReader.FromBytes(_Build(statedHeaderSize: 200)));

  [Test]
  [Category("Unit")]
  public void FromBytes_NoJpegBehindTheHeaderIsRefused() {
    var data = new byte[SonyPmpFile.HeaderSize + 64];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(SonyPmpFile.HeaderSizeOffset), SonyPmpFile.HeaderSize);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(SonyPmpFile.JpegLengthOffset), 64);

    Assert.Throws<InvalidDataException>(() => SonyPmpReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AStatedJpegLongerThanTheFileIsRefused()
    => Assert.Throws<InvalidDataException>(() => SonyPmpReader.FromBytes(_Build(statedJpegLength: 1 << 20)));

  [Test]
  [Category("Integration")]
  public void FromBytes_ThePictureIsTheJpegBehindTheHeader() {
    var read = SonyPmpReader.FromBytes(_Build());

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
      Assert.That(read.PixelData, Has.Length.EqualTo(_WIDTH * _HEIGHT * 3));
    });
  }

  /// <summary>The size in the header is not read: the JPEG's own is the picture's.</summary>
  [Test]
  [Category("Integration")]
  public void FromBytes_AHeaderStatingAnotherSizeDoesNotChangeThePicture() {
    var data = _Build();
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(22), 640);
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(24), 480);

    var read = SonyPmpReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_IsTheDecodedPicture() {
    var image = SonyPmpFile.ToRawImage(SonyPmpReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.Width, Is.EqualTo(_WIDTH));
      Assert.That(image.Height, Is.EqualTo(_HEIGHT));
    });
  }
}
