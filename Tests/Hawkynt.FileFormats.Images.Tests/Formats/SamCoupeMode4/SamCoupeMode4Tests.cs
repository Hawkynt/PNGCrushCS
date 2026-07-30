using System;
using System.IO;
using FileFormat.Core;
using FileFormat.SamCoupeMode4;

namespace FileFormat.SamCoupeMode4.Tests;

[TestFixture]
public sealed class SamCoupeMode4Tests {

  private static SamCoupeMode4File _Sample() {
    var bitmap = new byte[SamCoupeMode4File.BitmapDataSize];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 17 % 256);

    var palette = new byte[SamCoupePalette.EntryCount];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 8 % 128);

    return new() { BitmapData = bitmap, Palette = palette };
  }

  [Test]
  [Category("Unit")]
  public void FileSize_Is24617() {
    // 24576-byte bitmap, 16-byte palette, padding, then the interrupt terminator.
    Assert.That(SamCoupeMode4File.FileSize, Is.EqualTo(24617));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ClosesTheInterruptBlock() {
    var bytes = SamCoupeMode4Writer.ToBytes(_Sample());

    Assert.That(bytes[SamCoupeMode4File.InterruptOffset], Is.EqualTo(SamCoupeMode4File.InterruptTerminator));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesBitmapAndPalette() {
    var original = _Sample();
    var restored = SamCoupeMode4Reader.FromBytes(SamCoupeMode4Writer.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsATruncatedScreen()
    => Assert.Throws<InvalidDataException>(() => SamCoupeMode4Reader.FromBytes(new byte[1000]));

  [Test]
  [Category("Unit")]
  public void Palette_FullySetChannelsReachFullBrightness() {
    // Low + high + brightness bits sum to exactly 0xFF per channel.
    Assert.That(SamCoupePalette.ToRgb(0b1111111), Is.EqualTo(0xFFFFFF));
    Assert.That(SamCoupePalette.ToRgb(0), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Palette_RoundTripsThroughRgb() {
    for (byte value = 0; value < 128; ++value) {
      var rgb = SamCoupePalette.ToRgb(value);
      var back = SamCoupePalette.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
      Assert.That(SamCoupePalette.ToRgb(back), Is.EqualTo(rgb), $"colour {value}");
    }
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheScreenResolution() {
    var raw = SamCoupeMode4File.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(SamCoupeMode4File.ScreenWidth));
      Assert.That(raw.Height, Is.EqualTo(SamCoupeMode4File.ScreenHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(SamCoupePalette.EntryCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[SamCoupeMode4File.ScreenWidth * SamCoupeMode4File.ScreenHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 1] = (byte)(i % 239);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = SamCoupeMode4File.ScreenWidth, Height = SamCoupeMode4File.ScreenHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(SamCoupeMode4Writer.ToBytes(SamCoupeMode4File.FromRawImage(raw)),
      Has.Length.EqualTo(SamCoupeMode4File.FileSize));
  }
}
