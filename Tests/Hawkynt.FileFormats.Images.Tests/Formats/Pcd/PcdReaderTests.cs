using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Pcd;

namespace FileFormat.Pcd.Tests;

/// <summary>
/// The Photo CD, which is not one picture but the same one at three fixed sizes.
/// </summary>
/// <remarks>
/// Nothing in the file states a size: the offset a plane starts at is what says which size it is.
/// These fixtures are therefore built through the writer rather than by hand, because a hand-built
/// header can agree with a reader that invented the same layout — which is exactly what the pair
/// here used to do.
/// </remarks>
[TestFixture]
public sealed class PcdReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => PcdReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_MissingFile_Throws()
    => Assert.Throws<FileNotFoundException>(() => PcdReader.FromFile(new FileInfo("nonexistent.pcd")));

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_Throws()
    => Assert.Throws<ArgumentNullException>(() => PcdReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_Throws()
    => Assert.Throws<InvalidDataException>(() => PcdReader.FromBytes(new byte[2049]));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_Throws() {
    var data = _Write();
    data[PcdFile.PreambleSize] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => PcdReader.FromBytes(data));
  }

  /// <summary>A file holding only the smaller sizes reads as the largest one it does hold.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedToAPreview_ReadsThatSize() {
    var (width, height, offset) = PcdFile.Resolutions[0];
    var data = _Write().AsSpan(0, offset + PcdFile.PlaneBytes(width, height)).ToArray();

    var result = PcdReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WholeFile_ReadsTheLargestSize() {
    var result = PcdReader.FromBytes(_Write());
    var (width, height, _) = PcdFile.Resolutions[^1];

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
      Assert.That(result.PixelData, Has.Length.EqualTo(width * height * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_Parses() {
    using var ms = new MemoryStream(_Write());

    Assert.That(PcdReader.FromStream(ms).Width, Is.EqualTo(PcdFile.Resolutions[^1].Width));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_NullStream_Throws()
    => Assert.Throws<ArgumentNullException>(() => PcdReader.FromStream(null!));

  /// <summary>
  /// A flat colour survives the trip through the colour transform, which is the part most likely to
  /// be wrong in a way that still produces a picture.
  /// </summary>
  [TestCase((byte)200, (byte)40, (byte)60)]
  [TestCase((byte)0, (byte)0, (byte)0)]
  [TestCase((byte)255, (byte)255, (byte)255)]
  [Category("Unit")]
  public void RoundTrip_FlatColor_ComesBackWithinRounding(byte red, byte green, byte blue) {
    var (width, height, _) = PcdFile.Resolutions[^1];
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = red;
      pixels[i + 1] = green;
      pixels[i + 2] = blue;
    }

    var restored = PcdReader.FromBytes(PcdWriter.ToBytes(
      new() { Width = width, Height = height, PixelData = pixels }));

    for (var i = 0; i < 3; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(pixels[i]).Within(3), $"channel {i}");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_GivesTheSizeItRead() {
    var raw = PcdFile.ToRawImage(PcdReader.FromBytes(_Write()));

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(raw.Width, Is.EqualTo(PcdFile.Resolutions[^1].Width));
    });
  }

  private static byte[] _Write() {
    var (width, height, _) = PcdFile.Resolutions[^1];
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7);

    return PcdWriter.ToBytes(new() { Width = width, Height = height, PixelData = pixels });
  }
}
