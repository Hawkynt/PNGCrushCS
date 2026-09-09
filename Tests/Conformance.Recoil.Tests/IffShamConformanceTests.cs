using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffSham;
using Hawkynt.FileFormats.Images;

namespace Conformance.Recoil.Tests;

[TestFixture]
public sealed class IffShamConformanceTests {

  [Test]
  [Category("Conformance")]
  public void WriterOutput_DecodesIdenticallyInRecoil() {
    RecoilOracle.RequireAvailable();

    var encoded = IffShamWriter.ToBytes(IffShamFile.FromRawImage(_CreateProbe()));
    var path = Path.Combine(Path.GetTempPath(), $"iffsham_{Guid.NewGuid():N}.sham");
    byte[]? png;
    string output;
    try {
      File.WriteAllBytes(path, encoded);
      (png, output) = RecoilOracle.TryDecodeToPng(path);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }

    Assert.That(png, Is.Not.Null, $"RECOIL rejected the SHAM file — {output}");

    var ours = PixelConverter.Convert(IffShamFile.ToRawImage(IffShamReader.FromBytes(encoded)), PixelFormat.Rgb24);
    var theirs = PixelConverter.Convert(FormatRegistry.Read(png!)!, PixelFormat.Rgb24);

    Assert.Multiple(() => {
      Assert.That(theirs.Width, Is.EqualTo(ours.Width));
      Assert.That(theirs.Height, Is.EqualTo(ours.Height));
      Assert.That(theirs.PixelData, Is.EqualTo(ours.PixelData));
    });
  }

  private static RawImage _CreateProbe() {
    var pixels = new byte[320 * 200 * 3];
    for (var y = 0; y < 200; ++y)
    for (var x = 0; x < 320; ++x) {
      var at = (y * 320 + x) * 3;
      pixels[at] = (byte)(x * 255 / 319);
      pixels[at + 1] = (byte)(y * 255 / 199);
      pixels[at + 2] = (byte)(((x / 11 + y / 7) & 1) == 0 ? 0x22 : 0xDD);
    }

    return new() {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }
}
