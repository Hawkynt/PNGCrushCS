using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Pixibox;

namespace FileFormat.Pixibox.Tests;

/// <summary>
/// Pixibox, a French paint package whose file nothing describes.
/// </summary>
/// <remarks>
/// The layout was recovered from XnView's own reader; no sample exists to check against. What stands
/// outside this file is that the same fixtures — the ordinary runs and the zero-count run on its
/// own — are read by XnView's converter at the size they state and come back with every pixel as it
/// was put in.
/// </remarks>
[TestFixture]
public sealed class PixiboxTests {

  private const int _WIDTH = 6;
  private const int _HEIGHT = 3;

  private static readonly byte[][][] _Rows = [
    [[255, 0, 0], [255, 0, 0], [0, 255, 0], [0, 0, 255], [0, 0, 255], [0, 0, 255]],
    [[10, 20, 30], [40, 50, 60], [40, 50, 60], [40, 50, 60], [70, 80, 90], [1, 2, 3]],
    [[9, 9, 9], [9, 9, 9], [9, 9, 9], [9, 9, 9], [9, 9, 9], [9, 9, 9]],
  ];

  private static byte[] _Header(int width = _WIDTH, int height = _HEIGHT) {
    var header = new byte[PixiboxFile.PixelDataOffset];
    PixiboxFile.Signature.CopyTo(header);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(PixiboxFile.WidthOffset), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(PixiboxFile.HeightOffset), (ushort)height);
    return header;
  }

  /// <summary>The picture coded as XnView reads it: bottom row first, one run at a time.</summary>
  private static byte[] _Build(bool useZeroRuns = false) {
    var data = new List<byte>(_Header());

    for (var row = _HEIGHT - 1; row >= 0; --row) {
      var pixels = _Rows[row];
      var x = 0;
      while (x < _WIDTH) {
        var run = 1;
        while (x + run < _WIDTH && pixels[x + run][0] == pixels[x][0] && pixels[x + run][1] == pixels[x][1] && pixels[x + run][2] == pixels[x][2])
          ++run;

        var toEnd = useZeroRuns && x + run == _WIDTH;
        data.Add(toEnd ? (byte)0 : (byte)run);
        data.AddRange(pixels[x]);
        data.Add(0);
        x += run;
      }
    }

    return data.ToArray();
  }

  private static byte[] _Expected() {
    var expected = new byte[_WIDTH * _HEIGHT * 3];
    var at = 0;
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        expected[at++] = _Rows[y][x][0];
        expected[at++] = _Rows[y][x][1];
        expected[at++] = _Rows[y][x][2];
      }

    return expected;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PixiboxReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pxb"));
    Assert.Throws<FileNotFoundException>(() => PixiboxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PixiboxReader.FromBytes(new byte[64]));

  /// <summary>A file of a thousand zero bytes has the right length and none of the signature.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheSignature_IsRefused()
    => Assert.Throws<InvalidDataException>(() => PixiboxReader.FromBytes(new byte[PixiboxFile.PixelDataOffset + 64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_CodingThatRunsOutIsRefused() {
    var data = _Build();

    Assert.Throws<InvalidDataException>(() => PixiboxReader.FromBytes(data[..^PixiboxFile.RunSize]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ARunPastTheStatedWidthIsRefused() {
    var data = _Build();
    data[PixiboxFile.PixelDataOffset] = _WIDTH + 1;

    Assert.Throws<InvalidDataException>(() => PixiboxReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_APictureIsReadAtTheStatedSize() {
    var read = PixiboxReader.FromBytes(_Build());

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_EveryPixelComesBackAsItWasPutIn() {
    var image = PixiboxFile.ToRawImage(PixiboxReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData, Is.EqualTo(_Expected()));
    });
  }

  /// <summary>A count of zero is not an empty run; it stands for the rest of the row.</summary>
  [Test]
  [Category("Integration")]
  public void FromBytes_AZeroCountRunsToTheEndOfTheRow() {
    var image = PixiboxFile.ToRawImage(PixiboxReader.FromBytes(_Build(useZeroRuns: true)));

    Assert.That(image.PixelData, Is.EqualTo(_Expected()));
  }
}
