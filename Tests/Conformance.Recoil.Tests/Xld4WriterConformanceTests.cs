using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Xld4;
using Hawkynt.FileFormats.Images;

namespace Conformance.Recoil.Tests;

/// <summary>Checks that RECOIL interprets XLD4 files written by us exactly as our reader does.</summary>
[TestFixture]
public sealed class Xld4WriterConformanceTests {

  private static RawImage _Picture() {
    var palette = new byte[Xld4File.ColorCount * 3];
    for (var i = 0; i < Xld4File.ColorCount; ++i) {
      palette[i * 3] = (byte)(i * 17);
      palette[i * 3 + 1] = (byte)(((i * 7) & 15) * 17);
      palette[i * 3 + 2] = (byte)((15 - i) * 17);
    }

    var pixels = new byte[Xld4File.Width * Xld4File.Height];
    for (var y = 0; y < Xld4File.Height; ++y)
    for (var x = 0; x < Xld4File.Width; ++x)
      pixels[y * Xld4File.Width + x] = y < 160
        ? (byte)((x + y * 3) & 15)
        : (byte)(((x / 64) + (y / 20)) & 15);

    return new() {
      Width = Xld4File.Width,
      Height = Xld4File.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = Xld4File.ColorCount,
    };
  }

  [Test]
  [Category("Conformance")]
  public void WhatWeWrite_RecoilDecodesBackExactly() {
    RecoilOracle.RequireAvailable();

    var source = _Picture();
    var encoded = FormatIO.Encode<Xld4File>(source);
    var path = Path.Combine(Path.GetTempPath(), $"xld4conf_{Guid.NewGuid():N}.q4");

    byte[]? png;
    string output;
    try {
      File.WriteAllBytes(path, encoded);
      (png, output) = RecoilOracle.TryDecodeToPng(path);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }

    Assert.That(png, Is.Not.Null, $"RECOIL rejected our {encoded.Length}-byte XLD4 file — {output}");

    var expected = PixelConverter.Convert(source, PixelFormat.Rgb24);
    var actual = PixelConverter.Convert(FormatRegistry.Read(png!)!, PixelFormat.Rgb24);

    Assert.Multiple(() => {
      Assert.That((actual.Width, actual.Height), Is.EqualTo((Xld4File.Width, Xld4File.Height)));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  [Test]
  [Category("Conformance")]
  public void WhatWeWrite_OurReaderDecodesBackExactly() {
    var source = _Picture();
    var decoded = FormatIO.Decode<Xld4File>(FormatIO.Encode<Xld4File>(source));

    Assert.Multiple(() => {
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
      Assert.That(decoded.Palette, Is.EqualTo(source.Palette));
    });
  }
}
