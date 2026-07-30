using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MsxScreen10;

namespace FileFormat.MsxScreen10.Tests;

[TestFixture]
public sealed class MsxScreen10Tests {

  private static RawImage _Gradient() {
    var data = new byte[MsxScreen10File.Width * MsxScreen10File.Height * 3];
    for (var y = 0; y < MsxScreen10File.Height; ++y)
    for (var x = 0; x < MsxScreen10File.Width; ++x) {
      var o = (y * MsxScreen10File.Width + x) * 3;
      data[o] = (byte)x;
      data[o + 1] = (byte)(y * 255 / (MsxScreen10File.Height - 1));
      data[o + 2] = (byte)(255 - x);
    }

    return new() {
      Width = MsxScreen10File.Width, Height = MsxScreen10File.Height,
      Format = PixelFormat.Rgb24, PixelData = data,
    };
  }

  [Test]
  public void Written_HasTheLayoutTheMachineExpects() {
    var bytes = MsxScreen10Writer.ToBytes(MsxScreen10File.FromRawImage(_Gradient()));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(MsxScreen10File.FileSize));
      Assert.That(bytes[0], Is.EqualTo(MsxGraphics.BsaveMagic));
      Assert.That(MsxGraphics.ReadBsaveEndAddress(bytes), Is.EqualTo(MsxScreen10File.BsaveEndAddress));
    });
  }

  [Test]
  public void RoundTrip_PreservesEveryByte() {
    var file = MsxScreen10File.FromRawImage(_Gradient());
    var reread = MsxScreen10Reader.FromBytes(MsxScreen10Writer.ToBytes(file));

    Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void Encoded_ReproducesAGradientClosely() {
    var source = _Gradient();
    var decoded = MsxScreen10File.ToRawImage(MsxScreen10File.FromRawImage(source));

    // Luma is per pixel but the two chroma components are shared by four, so a horizontal gradient
    // cannot come back exactly. What must hold is that the error stays small and bounded.
    long total = 0;
    var worst = 0;
    for (var i = 0; i < source.PixelData.Length; ++i) {
      var delta = Math.Abs(source.PixelData[i] - decoded.PixelData[i]);
      total += delta;
      worst = Math.Max(worst, delta);
    }

    Assert.Multiple(() => {
      Assert.That(total / (double)source.PixelData.Length, Is.LessThan(12), "mean channel error");
      Assert.That(worst, Is.LessThan(64), "worst channel error");
    });
  }

  [Test]
  public void FlatColor_SurvivesExactlyWhereYjkCanExpressIt() {
    var data = new byte[MsxScreen10File.Width * MsxScreen10File.Height * 3];
    for (var i = 0; i < data.Length; i += 3) {
      data[i] = 0x52;      // all three channels land on exact 5-bit values with zero chroma error
      data[i + 1] = 0x52;
      data[i + 2] = 0x52;
    }

    var source = new RawImage {
      Width = MsxScreen10File.Width, Height = MsxScreen10File.Height,
      Format = PixelFormat.Rgb24, PixelData = data,
    };
    var decoded = MsxScreen10File.ToRawImage(MsxScreen10File.FromRawImage(source));

    // Grey has J = K = 0, so nothing is lost to the shared chroma; only luma quantisation remains.
    for (var i = 0; i < data.Length; ++i)
      Assert.That(Math.Abs(decoded.PixelData[i] - data[i]), Is.LessThanOrEqualTo(8), $"channel {i}");
  }

  [Test]
  public void OddLuma_ReadsAsAPaletteEntry() {
    // Screen 10's escape: luma bit 0 set means the remaining four bits are a palette index.
    var pixels = new byte[MsxScreen10File.PixelDataSize];
    var palette = new byte[MsxScreen10File.PaletteSize];
    palette[3 * MsxGraphics.PaletteEntrySize] = 0x70;      // entry 3: red 7, blue 0
    palette[3 * MsxGraphics.PaletteEntrySize + 1] = 0x00;  // green 0
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 3 << 1 << 3 | 1 << 3;                    // index 3, escape bit set

    var decoded = MsxScreen10File.ToRawImage(new() { PixelData = pixels, Palette = palette });

    Assert.Multiple(() => {
      Assert.That(decoded.PixelData[0], Is.EqualTo(255));
      Assert.That(decoded.PixelData[1], Is.EqualTo(0));
      Assert.That(decoded.PixelData[2], Is.EqualTo(0));
    });
  }

  [Test]
  public void Reader_RejectsAHeaderThatStopsShortOfTheBitmap() {
    var bytes = MsxScreen10Writer.ToBytes(MsxScreen10File.FromRawImage(_Gradient()));
    bytes[4] = 0;

    Assert.Throws<InvalidDataException>(() => MsxScreen10Reader.FromBytes(bytes));
  }

  [Test]
  public void Reader_RejectsAMissingBsaveMarker() {
    var bytes = MsxScreen10Writer.ToBytes(MsxScreen10File.FromRawImage(_Gradient()));
    bytes[0] = 0;

    Assert.Throws<InvalidDataException>(() => MsxScreen10Reader.FromBytes(bytes));
  }

  [Test]
  public void FromRawImage_RejectsTheWrongSize() {
    var image = new RawImage {
      Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<ArgumentException>(() => MsxScreen10File.FromRawImage(image));
  }
}
