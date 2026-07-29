using System;
using System.IO;
using FileFormat.Core;
using FileFormat.TurboRascal;

namespace FileFormat.TurboRascal.Tests;

[TestFixture]
public sealed class TurboRascalTests {

  private static TurboRascalFile _Sample() {
    var pixels = new byte[TurboRascalFile.PixelDataSize];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 256);

    var palette = new byte[TurboRascalFile.ColorCount * 3];
    for (var i = 0; i < TurboRascalFile.ColorCount; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 3 % 256);
    }

    return new() { PixelData = pixels, Palette = palette };
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmitsTheSignatureAndChunkyMode() {
    var bytes = TurboRascalWriter.ToBytes(_Sample());

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(TurboRascalFile.FileSize));
      Assert.That(bytes[..TurboRascalFile.Signature.Length], Is.EqualTo(TurboRascalFile.Signature.ToArray()));
      Assert.That(bytes[TurboRascalFile.ModeOffset], Is.EqualTo(TurboRascalFile.ChunkyMode));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StoresAFullPaletteAsZero() {
    // The count field is one byte, so 256 entries has to be written as 0.
    var bytes = TurboRascalWriter.ToBytes(_Sample());

    Assert.That(bytes[TurboRascalFile.ColorCountOffset], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesPixelsAndPalette() {
    var original = _Sample();
    var restored = TurboRascalReader.FromBytes(TurboRascalWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsDataWithoutTheSignature()
    => Assert.Throws<InvalidDataException>(() => TurboRascalReader.FromBytes(new byte[TurboRascalFile.FileSize]));

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAnUnsupportedMode() {
    var bytes = TurboRascalWriter.ToBytes(_Sample());
    bytes[TurboRascalFile.ModeOffset] = 7; // a PET-screen mode we do not implement

    Assert.Throws<NotSupportedException>(() => TurboRascalReader.FromBytes(bytes));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesTheImageResolution() {
    var raw = TurboRascalFile.ToRawImage(_Sample());

    Assert.Multiple(() => {
      Assert.That(raw.Width, Is.EqualTo(TurboRascalFile.ImageWidth));
      Assert.That(raw.Height, Is.EqualTo(TurboRascalFile.ImageHeight));
      Assert.That(raw.PaletteCount, Is.EqualTo(TurboRascalFile.ColorCount));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAConformantFile() {
    var data = new byte[TurboRascalFile.ImageWidth * TurboRascalFile.ImageHeight * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 1] = (byte)(i % 239);
      data[i + 3] = 255;
    }

    var raw = new RawImage {
      Width = TurboRascalFile.ImageWidth, Height = TurboRascalFile.ImageHeight,
      Format = PixelFormat.Rgba32, PixelData = data,
    };

    Assert.That(TurboRascalWriter.ToBytes(TurboRascalFile.FromRawImage(raw)),
      Has.Length.EqualTo(TurboRascalFile.FileSize));
  }
}
