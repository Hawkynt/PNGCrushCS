using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Png;

namespace FileFormat.PocketPcTheme.Tests;

/// <summary>
/// The picture inside a Pocket PC theme.
/// </summary>
/// <remarks>
/// XnView's reader for this name checks the cabinet signature and then scans the bytes for a
/// picture's opening bytes without unpacking anything. Every case below was put to its converter on
/// a fixture built the same way: it read the GIF, the PNG and the JFIF, and refused both the cabinet
/// with nothing in it and the one whose JPEG opens with an Exif segment.
/// </remarks>
[TestFixture]
public sealed class PocketPcThemeTests {

  private const int _WIDTH = 5;
  private const int _HEIGHT = 4;

  private static RawImage _Picture() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var at = (y * _WIDTH + x) * 3;
        pixels[at] = (byte)(x * 40 + 3);
        pixels[at + 1] = (byte)(y * 50 + 7);
        pixels[at + 2] = (byte)(x * y * 11 + 1);
      }

    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte[] _Png() => PngWriter.ToBytes(PngFile.FromRawImage(_Picture()));
  private static byte[] _Jpeg() => JpegWriter.ToBytes(JpegFile.FromRawImage(_Picture()));

  /// <summary>A cabinet whose header is followed by the bytes given.</summary>
  private static byte[] _Cabinet(byte[] stored, int gap = 32) {
    using var memory = new MemoryStream();
    memory.Write(PocketPcThemeFile.Signature);
    memory.Write(new byte[gap], 0, gap);
    memory.Write(stored, 0, stored.Length);
    return memory.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PocketPcThemeReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tsk"));
    Assert.Throws<FileNotFoundException>(() => PocketPcThemeReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => PocketPcThemeReader.FromBytes([0x4D, 0x53]));

  /// <summary>A picture on its own is not a theme, however readable it is.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_SomethingThatIsNotACabinetIsRefused()
    => Assert.Throws<InvalidDataException>(() => PocketPcThemeReader.FromBytes(_Png()));

  /// <summary>A cabinet whose files are all packed has nothing this can reach, and says so.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_ACabinetStoringNothingWholeIsRefused()
    => Assert.Throws<InvalidDataException>(() => PocketPcThemeReader.FromBytes(_Cabinet([])));

  /// <summary>
  /// The JPEG test is on four bytes. An Exif file opens <c>FF D8 FF E1</c> and is not one of the
  /// three signatures this looks for — XnView refuses it under this name too.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AJpegThatIsNotJfifIsNotFound() {
    var exif = _Jpeg();
    exif[3] = 0xE1;

    Assert.Throws<InvalidDataException>(() => PocketPcThemeReader.FromBytes(_Cabinet(exif)));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ThePictureIsTheFirstOneStoredWhole([Values(0, 1, 32, 512)] int gap) {
    var read = PocketPcThemeReader.FromBytes(_Cabinet(_Png(), gap));

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_AJfifIsFoundTheSameWayAPngIs() {
    var read = PocketPcThemeReader.FromBytes(_Cabinet(_Jpeg()));

    Assert.Multiple(() => {
      Assert.That(read.Width, Is.EqualTo(_WIDTH));
      Assert.That(read.Height, Is.EqualTo(_HEIGHT));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToRawImage_EveryPixelComesBackAsItWasPutIn() {
    var expected = PixelConverter.Convert(PngFile.ToRawImage(PngReader.FromBytes(_Png())), PixelFormat.Rgb24);

    var image = PocketPcThemeFile.ToRawImage(PocketPcThemeReader.FromBytes(_Cabinet(_Png())));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData, Is.EqualTo(expected.PixelData));
    });
  }
}
