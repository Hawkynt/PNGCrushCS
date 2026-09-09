using System;
using System.Linq;
using FileFormat.Core;
using FileFormat.ElectricImage;

namespace FileFormat.ElectricImage.Tests;

[TestFixture]
public sealed class ElectricImageWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ElectricImageWriter.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_IndexedFrame_UsesTheObservedPlainModeAndRoundTrips() {
    byte[] indices = [1, 1, 1, 1, 2, 3, 4, 5];
    var palette = Enumerable.Range(0, 16)
      .SelectMany(static i => new[] { (byte)(i * 11), (byte)(255 - i * 7), (byte)(i * 3) })
      .ToArray();
    var file = new ElectricImageFile {
      Frames = [new() {
        Width = 4,
        Height = 2,
        BytesPerPixel = 1,
        PixelData = indices,
        Palette = palette,
      }]
    };

    var bytes = ElectricImageWriter.ToBytes(file);
    var read = ElectricImageReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 6).ToArray(), Is.EqualTo(new byte[] { 0, 5, 0, 0, 0, 1 }));
      Assert.That(bytes[18], Is.EqualTo(8), "depth");
      Assert.That(bytes.AsSpan(28, 2).ToArray(), Is.EqualTo(new byte[] { 1, 0 }), "extra");
      Assert.That(bytes.AsSpan(30, 4).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 7 }), "packed byte count");
      Assert.That(bytes.AsSpan(34, 2).ToArray(), Is.EqualTo(new byte[] { 1, 0 }), "mode");
      Assert.That(bytes.AsSpan(36, 2).ToArray(), Is.EqualTo(new byte[] { 0, 5 }), "palette range");
      Assert.That(bytes.AsSpan(56).ToArray(), Is.EqualTo(new byte[] { 3, 1, 0x83, 2, 3, 4, 5 }), "run-length stream");
      Assert.That(read.Frames[0].PixelData, Is.EqualTo(indices));
      Assert.That(read.Frames[0].Palette, Is.EqualTo(palette[..18]));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AFullRepeatPacket_CodesOneHundredTwentyEightRgbPixelsInFourBytes() {
    var pixels = Enumerable.Range(0, 128).SelectMany(static _ => new byte[] { 11, 22, 33 }).ToArray();
    var file = new ElectricImageFile {
      Frames = [new() {
        Width = 128,
        Height = 1,
        BytesPerPixel = 3,
        PixelData = pixels,
      }]
    };

    var bytes = ElectricImageWriter.ToBytes(file);
    var read = ElectricImageReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(45));
      Assert.That(bytes[18], Is.EqualTo(24), "depth");
      Assert.That(bytes.AsSpan(28, 2).ToArray(), Is.EqualTo(new byte[] { 1, 0 }), "extra");
      Assert.That(bytes.AsSpan(30, 4).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 4 }), "packed byte count");
      Assert.That(bytes.AsSpan(34, 2).ToArray(), Is.EqualTo(new byte[] { 0, 1 }), "mode");
      Assert.That(bytes.AsSpan(36, 5).ToArray(), Is.EqualTo(new byte[5]), "mode payload");
      Assert.That(bytes.AsSpan(41, 4).ToArray(), Is.EqualTo(new byte[] { 127, 11, 22, 33 }));
      Assert.That(read.Frames[0].PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgba_IsWrittenAsTheObservedAlphaFirstVariant() {
    var source = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [10, 20, 30, 40, 50, 60, 70, 80],
    };

    var file = ElectricImageFile.FromRawImage(source);
    var bytes = ElectricImageWriter.ToBytes(file);
    var decoded = ElectricImageFile.ToRawImage(ElectricImageReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(file.Frames[0].BytesPerPixel, Is.EqualTo(4));
      Assert.That(bytes[18], Is.EqualTo(24), "the measured alpha form still declares depth 24");
      Assert.That(bytes.AsSpan(28, 2).ToArray(), Is.EqualTo(new byte[] { 1, 8 }), "extra 0108 signals the fourth channel");
      Assert.That(bytes.AsSpan(34, 2).ToArray(), Is.EqualTo(new byte[] { 0, 1 }), "true-colour mode");
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Argb32));
      Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 40, 10, 20, 30, 80, 50, 60, 70 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Indexed8_PreservesIndicesAndThePaletteTheyUse() {
    var source = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = [3, 1, 3, 2, 0, 1],
      Palette = [
        1, 2, 3,
        4, 5, 6,
        7, 8, 9,
        10, 11, 12,
      ],
      PaletteCount = 4,
    };

    var decoded = ElectricImageFile.ToRawImage(
      ElectricImageReader.FromBytes(ElectricImageWriter.ToBytes(ElectricImageFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
      Assert.That(decoded.Palette, Is.EqualTo(source.Palette));
      Assert.That(decoded.PaletteCount, Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AnIndexedFrameUsingAMissingPaletteEntry_IsRefused() {
    var file = new ElectricImageFile {
      Frames = [new() {
        Width = 2,
        Height = 1,
        BytesPerPixel = 1,
        PixelData = [0, 2],
        Palette = [0, 0, 0, 255, 255, 255],
      }]
    };

    Assert.Throws<ArgumentException>(() => ElectricImageWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleFrames_AreAllWrittenAndReadBack() {
    var file = new ElectricImageFile {
      Frames = [
        new() { Width = 2, Height = 1, BytesPerPixel = 3, PixelData = [1, 2, 3, 4, 5, 6] },
        new() { Width = 1, Height = 2, BytesPerPixel = 4, PixelData = [7, 8, 9, 10, 11, 12, 13, 14] },
      ]
    };

    var read = ElectricImageReader.FromBytes(ElectricImageWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(read.Frames, Has.Count.EqualTo(2));
      Assert.That(read.Frames[0].PixelData, Is.EqualTo(file.Frames[0].PixelData));
      Assert.That(read.Frames[1].PixelData, Is.EqualTo(file.Frames[1].PixelData));
    });
  }
}
