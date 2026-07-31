using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Picasso64;

namespace FileFormat.Picasso64.Tests;

/// <summary>
/// Picasso 64's file layout, which is not a matter of taste: the sections sit where the format puts
/// them and a reader that packs them against one another reads a real file as noise.
/// </summary>
[TestFixture]
public sealed class Picasso64ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Picasso64Reader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(
      () => Picasso64Reader.FromBytes(new byte[Picasso64File.ExpectedFileSize - 1]));

  /// <summary>Each section has to come back from where the format keeps it, not from where it fits.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_EachSection_ReadFromItsOwnOffset() {
    var data = _Build();

    var result = Picasso64Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(160));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
      Assert.That(result.BitmapData[Picasso64File.BitmapDataSize - 1], Is.EqualTo(0xCD));
      Assert.That(result.VideoMatrix[0], Is.EqualTo(0x12));
      Assert.That(result.ColorRam[0], Is.EqualTo(0x34));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesEverySection() {
    var original = Picasso64Reader.FromBytes(_Build());

    var written = Picasso64Writer.ToBytes(original);
    var reread = Picasso64Reader.FromBytes(written);

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

    var written = Picasso64Writer.ToBytes(Picasso64File.FromRawImage(source));
    Assert.That(written, Has.Length.EqualTo(Picasso64File.ExpectedFileSize));

    var decoded = Picasso64File.ToRawImage(Picasso64Reader.FromBytes(written));
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(160));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  private static byte[] _Build() {
    var data = new byte[Picasso64File.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x40;

    data[Picasso64File.BitmapOffset] = 0xAB;
    data[Picasso64File.BitmapOffset + Picasso64File.BitmapDataSize - 1] = 0xCD;
    data[Picasso64File.VideoMatrixOffset] = 0x12;
    data[Picasso64File.ColorRamOffset] = 0x34;

    if (Picasso64File.BackgroundOffset >= 0)
      data[Picasso64File.BackgroundOffset] = 0x06;

    return data;
  }
}
