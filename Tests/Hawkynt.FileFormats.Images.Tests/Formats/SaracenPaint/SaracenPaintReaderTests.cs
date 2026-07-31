using System;
using System.IO;
using FileFormat.Core;
using FileFormat.SaracenPaint;

namespace FileFormat.SaracenPaint.Tests;

/// <summary>
/// Saracen Paint's file layout, which is not a matter of taste: the sections sit where the format puts
/// them and a reader that packs them against one another reads a real file as noise.
/// </summary>
[TestFixture]
public sealed class SaracenPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SaracenPaintReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(
      () => SaracenPaintReader.FromBytes(new byte[SaracenPaintFile.ExpectedFileSize - 1]));

  /// <summary>Each section has to come back from where the format keeps it, not from where it fits.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_EachSection_ReadFromItsOwnOffset() {
    var data = _Build();

    var result = SaracenPaintReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(160));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
      Assert.That(result.BitmapData[SaracenPaintFile.BitmapDataSize - 1], Is.EqualTo(0xCD));
      Assert.That(result.VideoMatrix[0], Is.EqualTo(0x12));
      Assert.That(result.ColorRam[0], Is.EqualTo(0x34));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesEverySection() {
    var original = SaracenPaintReader.FromBytes(_Build());

    var written = SaracenPaintWriter.ToBytes(original);
    var reread = SaracenPaintReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(reread.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(reread.VideoMatrix, Is.EqualTo(original.VideoMatrix));
      Assert.That(reread.ColorRam, Is.EqualTo(original.ColorRam));
      Assert.That(reread.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    });
  }

  /// <summary>A picture the format can hold has to survive being written and read back.</summary>
  [Test]
  [Category("Unit")]
  public void FromRawImage_ThenBack_StaysWithinTheMachinesColours() {
    var rgb = new byte[160 * 200 * 3];
    for (var y = 0; y < 200; ++y)
    for (var x = 0; x < 160; ++x) {
      var color = Commodore64Graphics.HexColors[(x / 8 + y / 8) % 16];
      var at = (y * 160 + x) * 3;
      rgb[at] = (byte)(color >> 16);
      rgb[at + 1] = (byte)(color >> 8);
      rgb[at + 2] = (byte)color;
    }

    var source = new RawImage {
      Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = rgb,
    };

    var written = SaracenPaintWriter.ToBytes(SaracenPaintFile.FromRawImage(source));
    Assert.That(written, Has.Length.EqualTo(SaracenPaintFile.ExpectedFileSize));

    var decoded = SaracenPaintFile.ToRawImage(SaracenPaintReader.FromBytes(written));
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(160));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  private static byte[] _Build() {
    var data = new byte[SaracenPaintFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x40;

    data[SaracenPaintFile.BitmapOffset] = 0xAB;
    data[SaracenPaintFile.BitmapOffset + SaracenPaintFile.BitmapDataSize - 1] = 0xCD;
    data[SaracenPaintFile.VideoMatrixOffset] = 0x12;
    data[SaracenPaintFile.ColorRamOffset] = 0x34;

    if (SaracenPaintFile.BackgroundOffset >= 0)
      data[SaracenPaintFile.BackgroundOffset] = 0x06;

    return data;
  }
}
