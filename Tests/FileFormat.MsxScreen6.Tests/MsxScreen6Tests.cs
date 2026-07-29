using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MsxScreen6;

namespace FileFormat.MsxScreen6.Tests;

[TestFixture]
public sealed class MsxScreen6Tests {

  /// <summary>Four vertical bands, one per colour the mode allows.</summary>
  private static RawImage _Bands() {
    const int width = MsxScreen6File.DisplayWidth;
    const int height = MsxScreen6File.DisplayHeight;
    byte[][] colors = [[0, 0, 0], [255, 0, 0], [0, 255, 0], [255, 255, 255]];
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      var band = colors[x / (width / 4)];
      data[o + 2] = band[0];
      data[o + 1] = band[1];
      data[o] = band[2];
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = data };
  }

  [Test]
  [Category("Unit")]
  public void TheBitmapIsFourPixelsPerByte()
    => Assert.That(MsxScreen6File.PixelDataSize, Is.EqualTo(27136));

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesTheBsaveMarkerAndBitmapEndAddress() {
    var bytes = MsxScreen6Writer.ToBytes(MsxScreen6File.FromRawImage(_Bands()));

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(MsxScreen6File.BsaveMagic));
      // Readers derive the picture height from this, so it describes the bitmap, not the file.
      Assert.That(bytes[3] | (bytes[4] << 8), Is.EqualTo(MsxScreen6File.PixelDataSize - 1));
      Assert.That(bytes, Has.Length.EqualTo(30351));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheBitmapAndPalette() {
    var file = MsxScreen6File.FromRawImage(_Bands());
    var restored = MsxScreen6Reader.FromBytes(MsxScreen6Writer.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.PixelData, Is.EqualTo(file.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(file.Palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_KeepsEachBandOneColour() {
    var decoded = MsxScreen6File.ToRawImage(MsxScreen6File.FromRawImage(_Bands()));
    const int width = MsxScreen6File.DisplayWidth;

    for (var y = 0; y < MsxScreen6File.DisplayHeight; y += 53)
    for (var x = 0; x < width; ++x)
      Assert.That(decoded.PixelData[y * width + x],
        Is.EqualTo(decoded.PixelData[y * width + x / (width / 4) * (width / 4)]), $"pixel {x},{y}");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ShowsEachStoredRowTwice() {
    var raw = MsxScreen6File.ToRawImage(MsxScreen6File.FromRawImage(_Bands()));
    const int width = MsxScreen6File.DisplayWidth;

    Assert.Multiple(() => {
      Assert.That(raw.Height, Is.EqualTo(424));
      Assert.That(raw.PixelData[width..(2 * width)], Is.EqualTo(raw.PixelData[..width]));
    });
  }

  [Test]
  [Category("Unit")]
  public void PaletteRoundTrip_SurvivesTheThreeBitChannels() {
    var palette = MsxScreen6File.PaletteFromRgb([255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255], 4);
    var rgb = MsxScreen6File.PaletteToRgb(palette);

    Assert.That(rgb, Is.EqualTo(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsATruncatedFile()
    => Assert.Throws<InvalidDataException>(() => MsxScreen6Reader.FromBytes(new byte[1024]));

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes() {
    var raw = new RawImage { Width = 256, Height = 212, Format = PixelFormat.Bgra32, PixelData = new byte[256 * 212 * 4] };

    Assert.Throws<ArgumentException>(() => MsxScreen6File.FromRawImage(raw));
  }
}
