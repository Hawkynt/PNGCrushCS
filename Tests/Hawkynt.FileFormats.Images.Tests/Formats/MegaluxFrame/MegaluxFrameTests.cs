using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.MegaluxFrame;

namespace FileFormat.MegaluxFrame.Tests;

/// <summary>
/// Megalux Frame, a captured video frame written out whole.
/// </summary>
/// <remarks>
/// No sample of this format could be found, so the fixtures are built here. What stands outside this
/// file is that the same fixture, handed to XnView's own converter, comes back at the size it states
/// with every one of its pixels unchanged — and that a file with the picture at offset eight, where
/// FFmpeg's demuxer puts it, comes back shifted.
/// </remarks>
[TestFixture]
public sealed class MegaluxFrameTests {

  private const int _WIDTH = 4;
  private const int _HEIGHT = 3;

  private static byte[] _Rgb(int x, int y) => [(byte)(x * 60 + 7), (byte)(y * 80 + 13), (byte)(x * y * 30 + 21)];

  private static byte[] _Build(
    byte code = MegaluxFrameFile.SupportedFormatCode,
    int width = _WIDTH,
    int height = _HEIGHT,
    int trailing = 0) {

    var data = new byte[MegaluxFrameFile.PixelDataOffset + width * height * MegaluxFrameFile.BytesPerPixel + trailing];
    MegaluxFrameFile.Signature.CopyTo(data);
    data[3] = code;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)height);

    var at = MegaluxFrameFile.PixelDataOffset;
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var rgb = _Rgb(x, y);
        data[at++] = rgb[2];
        data[at++] = rgb[1];
        data[at++] = rgb[0];
        data[at++] = 0;
      }

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MegaluxFrameReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".frm"));
    Assert.Throws<FileNotFoundException>(() => MegaluxFrameReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MegaluxFrameReader.FromBytes(new byte[8]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheSignature_IsRefused() {
    var data = _Build();
    data[0] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => MegaluxFrameReader.FromBytes(data));
  }

  /// <summary>
  /// FFmpeg's demuxer names five pixel layouts. XnView reads one of them and refuses the rest, so
  /// the rest are refused here too rather than decoded at a width that has never been checked.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ALayoutOtherThanTheFourthIsRefused([Values(0, 1, 2, 3, 5, 255)] int code)
    => Assert.Throws<InvalidDataException>(() => MegaluxFrameReader.FromBytes(_Build((byte)code)));

  [Test]
  [Category("Unit")]
  public void FromBytes_ShorterThanTheStatedPictureIsRefused() {
    var data = _Build();

    Assert.Throws<InvalidDataException>(() => MegaluxFrameReader.FromBytes(data[..^4]));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_APictureIsReadAtTheStatedSize() {
    var read = MegaluxFrameReader.FromBytes(_Build());

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_EveryPixelComesBackAsItWasPutIn() {
    var image = MegaluxFrameFile.ToRawImage(MegaluxFrameReader.FromBytes(_Build()));

    var expected = new byte[_WIDTH * _HEIGHT * 3];
    var at = 0;
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var rgb = _Rgb(x, y);
        expected[at++] = rgb[0];
        expected[at++] = rgb[1];
        expected[at++] = rgb[2];
      }

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData, Is.EqualTo(expected));
    });
  }

  /// <summary>Bytes eight to twenty-three are not read: XnView returns the same picture whatever
  /// stands in them, and so does this.</summary>
  [Test]
  [Category("Integration")]
  public void FromBytes_TheSixteenBytesBehindTheSizeAreNotRead() {
    var plain = _Build();
    var filled = _Build();
    for (var i = MegaluxFrameFile.DeclaredHeaderSize; i < MegaluxFrameFile.PixelDataOffset; ++i)
      filled[i] = 0xFF;

    Assert.That(MegaluxFrameReader.FromBytes(filled).PixelData,
      Is.EqualTo(MegaluxFrameReader.FromBytes(plain).PixelData));
  }
}
