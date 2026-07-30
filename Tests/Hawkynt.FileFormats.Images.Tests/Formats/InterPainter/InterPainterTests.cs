using System;
using System.IO;
using FileFormat.Core;
using FileFormat.InterPainter;

namespace FileFormat.InterPainter.Tests;

[TestFixture]
public sealed class InterPainterTests {

  private static InterPainterFile _Sample(Func<int, byte> first, Func<int, byte> second) {
    var a = new byte[InterPainterFile.FrameDataSize];
    var b = new byte[InterPainterFile.FrameDataSize];
    for (var i = 0; i < a.Length; ++i) {
      a[i] = first(i);
      b[i] = second(i);
    }

    return new() { FirstFrame = a, SecondFrame = b, Colors = [0x00, 0x28, 0x86, 0x0E] };
  }

  private static RawImage _Gradient() {
    var data = new byte[InterPainterFile.DisplayWidth * InterPainterFile.DisplayHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 1] = (byte)(i % 199);
      data[i + 2] = (byte)(i % 173);
      data[i + 3] = 255;
    }

    return new() {
      Width = InterPainterFile.DisplayWidth,
      Height = InterPainterFile.DisplayHeight,
      Format = PixelFormat.Rgba32,
      PixelData = data,
    };
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesTheFixedFileSize()
    => Assert.That(InterPainterWriter.ToBytes(_Sample(_ => 0, _ => 0)), Has.Length.EqualTo(16004));

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesBothFramesAndTheColors() {
    var file = _Sample(i => (byte)(i * 13 % 256), i => (byte)(i * 29 % 256));
    var restored = InterPainterReader.FromBytes(InterPainterWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.FirstFrame, Is.EqualTo(file.FirstFrame));
      Assert.That(restored.SecondFrame, Is.EqualTo(file.SecondFrame));
      Assert.That(restored.Colors, Is.EqualTo(file.Colors));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsATruncatedFile()
    => Assert.Throws<InvalidDataException>(() => InterPainterReader.FromBytes(new byte[8000]));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ExposesEveryPairingOfTheFourRegisters() {
    var raw = InterPainterFile.ToRawImage(_Sample(_ => 0, _ => 0));

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(InterPainterFile.DisplayWidth));
      Assert.That(raw.Height, Is.EqualTo(InterPainterFile.DisplayHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(10));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AveragesTheTwoFrames() {
    // Frame one is all pixel value 0 (background), frame two all value 3 (PF2).
    var raw = InterPainterFile.ToRawImage(_Sample(_ => 0x00, _ => 0xFF));
    var gtia = Atari8BitGraphics.CreatePalette();
    var slot = raw.PixelData[0] * 3;

    Assert.Multiple(() => {
      for (var channel = 0; channel < 3; ++channel)
        Assert.That(raw.Palette![slot + channel], Is.EqualTo((gtia[0x00 * 3 + channel] + gtia[0x0E * 3 + channel]) >> 1));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UsesBothFramesToReachTheInBetweenShades() {
    var file = InterPainterFile.FromRawImage(_Gradient());

    Assert.That(file.FirstFrame, Is.Not.EqualTo(file.SecondFrame), "a gradient needs the blended shades, not just the four registers");
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAReadableFile() {
    var bytes = InterPainterWriter.ToBytes(InterPainterFile.FromRawImage(_Gradient()));

    Assert.That(() => InterPainterReader.FromBytes(bytes), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsOtherSizes() {
    var raw = new RawImage { Width = 320, Height = 192, Format = PixelFormat.Rgba32, PixelData = new byte[320 * 192 * 4] };

    Assert.Throws<ArgumentException>(() => InterPainterFile.FromRawImage(raw));
  }
}
