using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.PixelPowerCollage.Tests;

/// <summary>
/// Pixel Power Collage: a still that carries the name it is supposed to be filed under.
/// </summary>
/// <remarks>
/// Every layout below was built as a file, handed to XnView's own converter, and checked against what
/// its <c>-out pnm</c> returned — including the thirty-two-bit case, whose channel order is not the
/// one the same converter uses for a Windows bitmap of the same depth and would never have been
/// guessed right.
/// <para/>
/// The name test is the one worth having. The same bytes are read under the name they carry and
/// refused under any other, which is the whole identity of the format: there is no signature, no
/// magic and nothing else in the file that says what it is.
/// </remarks>
[TestFixture]
public sealed class PixelPowerCollageTests {

  /// <summary>Builds a picture that says it is called <paramref name="storedName"/>.</summary>
  private static byte[] _Picture(string storedName, int type, int width, int height, byte[] pixels) {
    var data = new byte[PixelPowerCollageFile.PixelOffset + pixels.Length];
    Encoding.ASCII.GetBytes(storedName).CopyTo(data, 0);
    _WriteBigEndian(data, 0x40, type);
    _WriteBigEndian(data, 0x4C, width);
    _WriteBigEndian(data, 0x50, height);
    pixels.CopyTo(data, PixelPowerCollageFile.PixelOffset);
    return data;
  }

  private static void _WriteBigEndian(byte[] data, int at, int value) {
    data[at] = (byte)(value >> 24);
    data[at + 1] = (byte)(value >> 16);
    data[at + 2] = (byte)(value >> 8);
    data[at + 3] = (byte)value;
  }

  /// <summary>Writes the picture under a name and reads it back through the path.</summary>
  private static T _Named<T>(string fileName, byte[] data, Func<FileInfo, T> read) {
    var directory = Directory.CreateTempSubdirectory("collage");
    try {
      var path = Path.Combine(directory.FullName, fileName);
      File.WriteAllBytes(path, data);
      return read(new FileInfo(path));
    } finally {
      directory.Delete(recursive: true);
    }
  }

  [Test]
  [Category("Integration")]
  public void Read_ReturnsTheColoursTheConverterReturnsForTheSameFile() {
    // Three by two: red, green, blue over white, black, yellow — stored blue first.
    byte[] pixels = [
      0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00,
      0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF,
    ];
    var image = _Named("p.i17", _Picture("p.i17", 1, 3, 2, pixels),
      file => PixelPowerCollageFile.ToRawImage(PixelPowerCollageReader.FromFile(file)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(3));
      Assert.That(image.Height, Is.EqualTo(2));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Bgr24));
      Assert.That(image.EnsureFormat(PixelFormat.Rgb24).PixelData, Is.EqualTo(new byte[] {
        0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF,
        0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00,
      }));
    });
  }

  /// <summary>
  /// The thirty-two-bit layout keeps alpha first, which the same converter's Windows bitmap does not.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void Read_TakesTheThirtyTwoBitPixelAsAlphaThenBlueGreenRed() {
    var image = _Named("q.if9", _Picture("q.if9", 0, 1, 1, [0x11, 0x22, 0x33, 0x44]),
      file => PixelPowerCollageFile.ToRawImage(PixelPowerCollageReader.FromFile(file)));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(image.PixelData, Is.EqualTo(new byte[] { 0x44, 0x33, 0x22, 0x11 }));
    });
  }

  [Test]
  [Category("Integration")]
  public void Read_TakesTheEightBitPixelAsAGreyStartingAtBlack() {
    var image = _Named("r.ib7", _Picture("r.ib7", 2, 3, 2, [0x00, 0x40, 0x80, 0xC0, 0xFF, 0x10]),
      file => PixelPowerCollageFile.ToRawImage(PixelPowerCollageReader.FromFile(file)));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(image.PixelData, Is.EqualTo(new byte[] { 0x00, 0x40, 0x80, 0xC0, 0xFF, 0x10 }));
    });
  }

  /// <summary>The same bytes under a name they do not claim are not this picture.</summary>
  [Test]
  [Category("Integration")]
  public void Read_RefusesTheVerySameBytesUnderADifferentName() {
    var data = _Picture("p.i17", 1, 1, 1, [0x00, 0x00, 0x00]);

    Assert.Multiple(() => {
      Assert.DoesNotThrow(() => _Named("p.i17", data, PixelPowerCollageReader.FromFile));
      Assert.Throws<InvalidDataException>(() => _Named("other.i17", data, PixelPowerCollageReader.FromFile));
      Assert.Throws<InvalidDataException>(() => _Named("p.i18", data, PixelPowerCollageReader.FromFile),
        "the extension is part of the name");
    });
  }

  [Test]
  [Category("Integration")]
  public void Read_DoesNotMindWhichCaseTheNameIsIn() {
    var data = _Picture("P.I17", 1, 1, 1, [0x00, 0x00, 0x00]);

    Assert.DoesNotThrow(() => _Named("p.i17", data, PixelPowerCollageReader.FromFile));
  }

  /// <summary>
  /// Bytes with no name attached cannot be checked, and this reader says so rather than guessing.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Read_RefusesBytesWithNoNameToCheckThemAgainst()
    => Assert.Throws<InvalidDataException>(
      () => PixelPowerCollageReader.FromBytes(_Picture("p.i17", 1, 1, 1, [0, 0, 0])));

  [Test]
  [Category("Unit")]
  public void Read_RefusesADepthNoPixelHas()
    => Assert.Throws<InvalidDataException>(
      () => PixelPowerCollageReader.FromNamedBytes(_Picture("p.i17", 7, 1, 1, [0, 0, 0]), "p.i17"));

  [Test]
  [Category("Unit")]
  public void Read_RefusesASizeThatIsNoPicture()
    => Assert.Throws<InvalidDataException>(
      () => PixelPowerCollageReader.FromNamedBytes(_Picture("p.i17", 1, 0, 4, []), "p.i17"));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileShortOfTheRowsItStates()
    => Assert.Throws<InvalidDataException>(
      () => PixelPowerCollageReader.FromNamedBytes(_Picture("p.i17", 1, 4, 4, [0, 0, 0]), "p.i17"));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileTooShortToHoldAHeader()
    => Assert.Throws<InvalidDataException>(() => PixelPowerCollageReader.FromNamedBytes(new byte[16], "p.i17"));
}
