using System;
using System.IO;
using FileFormat.Vivid;
using FileFormat.Core;

namespace FileFormat.Vivid.Tests;

/// <summary>
/// What a QRT / Vivid picture is.
/// </summary>
/// <remarks>
/// These used to build a buffer of interleaved red, green and blue and assert it came back unchanged,
/// which passed because the reader handed its input through. A real file states its size in four bytes
/// and then gives each row its own number followed by all of its red, all of its green and all of its
/// blue in turn.
/// </remarks>
[TestFixture]
public class VividReaderTests {

  /// <summary>Builds a picture where each row is numbered and its colours are a plane at a time.</summary>
  private static byte[] _BuildValidFile(int width, int height) {
    var stride = VividFile.RowNumberSize + width * 3;
    var data = new byte[VividFile.HeaderSize + stride * height];

    data[0] = (byte)width;
    data[1] = (byte)(width >> 8);
    data[2] = (byte)height;
    data[3] = (byte)(height >> 8);

    for (var y = 0; y < height; ++y) {
      var row = VividFile.HeaderSize + y * stride;
      data[row] = (byte)y;
      data[row + 1] = (byte)(y >> 8);

      for (var x = 0; x < width; ++x) {
        data[row + VividFile.RowNumberSize + x] = (byte)(x + 1);
        data[row + VividFile.RowNumberSize + width + x] = (byte)(y + 100);
        data[row + VividFile.RowNumberSize + width * 2 + x] = 200;
      }
    }

    return data;
  }

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => VividReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => VividReader.FromFile(new FileInfo("nonexistent.bin")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => VividReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => VividReader.FromBytes(new byte[3]));

  [Test]
  public void FromBytes_ShorterThanItsOwnSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => VividReader.FromBytes(_BuildValidFile(64, 8)[..100]));

  [Test]
  public void FromBytes_TakesTheSizeFromTheFirstFourBytes() {
    var result = VividReader.FromBytes(_BuildValidFile(320, 200));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.PixelData.Length, Is.EqualTo(320 * 200 * 3));
    });
  }

  [Test]
  public void FromBytes_ReadsEachRowAPlaneAtATime() {
    var result = VividReader.FromBytes(_BuildValidFile(4, 3));

    // Row two: red counts up from one across the row, green is the row number plus a hundred, blue is
    // flat. Read as interleaved triples none of that would hold.
    Assert.Multiple(() => {
      Assert.That(result.PixelData[(2 * 4 + 0) * 3], Is.EqualTo(1));
      Assert.That(result.PixelData[(2 * 4 + 3) * 3], Is.EqualTo(4));
      Assert.That(result.PixelData[(2 * 4 + 0) * 3 + 1], Is.EqualTo(102));
      Assert.That(result.PixelData[(2 * 4 + 0) * 3 + 2], Is.EqualTo(200));
    });
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => VividReader.FromStream(null!));

  [Test]
  public void ToRawImage_IsFullColour() {
    var raw = VividFile.ToRawImage(VividReader.FromBytes(_BuildValidFile(8, 4)));

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }
}
