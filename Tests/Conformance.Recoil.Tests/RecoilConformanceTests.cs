using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Conformance.Recoil.Tests;

/// <summary>
/// Encodes a synthetic image with each retro format we share with RECOIL and asks the reference
/// decoder to read it back.
/// </summary>
/// <remarks>
/// The pairings below are curated, not derived from file extensions. Extensions collide heavily in
/// this space — <c>.pic</c> alone means five different things across Atari, MSX and FM Towns, and
/// our Bio-Rad <c>.pic</c> has nothing to do with any of them. Only formats where both sides
/// implement the same thing belong here; anything else would report a phantom failure.
/// </remarks>
[TestFixture]
public sealed class RecoilConformanceTests {

  /// <param name="Format">Our registry entry.</param>
  /// <param name="RecoilName">The format's name in RECOIL's <c>formats.xml</c>, for traceability.</param>
  /// <param name="Width">Native width RECOIL expects.</param>
  /// <param name="Height">Native height RECOIL expects.</param>
  /// <param name="Extension">Extension to write the probe file under. RECOIL dispatches purely on
  /// the extension, and it does not always use the same one we treat as primary — GodPaint is
  /// <c>.gpn</c> here and <c>.god</c> there. Null means use our primary extension.</param>
  public readonly record struct Pairing(ImageFormat Format, string RecoilName, int Width, int Height, string? Extension = null) {
    public override string ToString() => $"{this.Format} ({this.RecoilName})";
  }

  /// <summary>Formats implemented on both sides, with the dimensions RECOIL decodes them at.</summary>
  public static readonly Pairing[] Pairings = [
    new(ImageFormat.Degas, "DEGAS", 320, 200),
    new(ImageFormat.MacPaint, "MacPaint", 576, 720),
    new(ImageFormat.Neochrome, "NEOchrome", 320, 200),
    new(ImageFormat.PrismPaint, "Prism Paint", 320, 200),
    new(ImageFormat.Spectrum512, "Spectrum 512", 320, 199),
    new(ImageFormat.AmigaIcon, "Icon", 64, 64),
    new(ImageFormat.AtariPaintworks, "Paintworks", 320, 200),
    new(ImageFormat.CokeAtari, "COKE", 320, 200),
    new(ImageFormat.CrackArt, "CrackArt", 320, 200),
    new(ImageFormat.DaliST, "Dali", 320, 200),
    new(ImageFormat.DrawIt, "DrawIt", 320, 192),
    new(ImageFormat.DuneGraph, "DuneGraph", 320, 200),
    new(ImageFormat.Hireslace, "Hireslace Editor", 320, 200),
    new(ImageFormat.MagicPainter, "Magic Painter", 320, 192),
    new(ImageFormat.PabloPaint, "Pablo Paint 2.5", 640, 400),
    new(ImageFormat.PublicPainter, "Public Painter", 640, 400),
    new(ImageFormat.QuantumPaint, "QuantumPaint", 320, 200),
    new(ImageFormat.Rembrandt, "Rembrandt", 320, 200),
    new(ImageFormat.SinbadSlideshow, "Sinbad Slideshow", 320, 200),
    new(ImageFormat.Spectrum512Ext, "Spectrum 512 extended", 320, 199),
    new(ImageFormat.SyntheticArts, "Synthetic Arts", 640, 200),
    new(ImageFormat.MovieMakerBackground, "Movie Maker background", 320, 192),
    new(ImageFormat.Graphics9Plus, "Graphics 9+", 320, 240),
    new(ImageFormat.FloorDesigner, "Floor Designer", 256, 160),
    new(ImageFormat.AtariGrayscale9, "160x192 grayscale", 320, 192),
    new(ImageFormat.Zoom4, "Zoom-4 graphics editor", 256, 256),
    new(ImageFormat.ZxFont, "8x8 font", 256, 64),
    new(ImageFormat.SamCoupeMode4, "Mode 4", 256, 192),
    new(ImageFormat.KssPaint, "KSS-Paint", 320, 160),
    new(ImageFormat.GodPaint, "GodPaint", 320, 240, ".god"),
    new(ImageFormat.IndyPaint, "IndyPaint", 320, 240, ".tru"),
    new(ImageFormat.TextureEditorMikey, "Texture Editor by Mikey", 320, 192),
    new(ImageFormat.Mamut, "Mamut", 320, 192),
    new(ImageFormat.VidigPaint, "Vidig Paint", 320, 192),
    new(ImageFormat.TurboRascal, "Turbo Rascal Syntax Error", 320, 200),
    // The same ILBM writer under each extension RECOIL routes through its IFF decoder.
    new(ImageFormat.Ilbm, "Hold-And-Modify 6", 64, 64, ".ham"),
    new(ImageFormat.Ilbm, "Hold-And-Modify 8", 64, 64, ".ham8"),
    new(ImageFormat.Ilbm, "Paint 256", 64, 64, ".256"),
    new(ImageFormat.Ilbm, "DEGAS Elite block 1", 64, 64, ".bl1"),
    new(ImageFormat.Ilbm, "DEGAS Elite block 2", 64, 64, ".bl2"),
    new(ImageFormat.Ilbm, "DEGAS Elite block 3", 64, 64, ".bl3"),
  ];

  [Test]
  [Category("Conformance")]
  [TestCaseSource(nameof(Pairings))]
  public void Encoded_IsReadableByRecoil(Pairing pairing) {
    RecoilOracle.RequireAvailable();

    var entry = FormatRegistry.GetEntry(pairing.Format);
    Assert.That(entry, Is.Not.Null, $"{pairing.Format} is not registered");
    Assert.That(entry!.SupportsWrite, Is.True, $"{pairing.Format} has no encoder");

    byte[] encoded;
    try {
      encoded = entry.ConvertFromRawImage!(_Sample(pairing.Width, pairing.Height));
    } catch (Exception ex) {
      Assert.Fail($"{pairing}: encoding {pairing.Width}x{pairing.Height} threw {ex.GetType().Name}: {ex.Message}");
      return;
    }

    var extension = pairing.Extension ?? entry.PrimaryExtension;
    var path = Path.Combine(Path.GetTempPath(), $"recoilconf_{pairing.Format}{extension}");
    try {
      File.WriteAllBytes(path, encoded);
      var (decoded, output) = RecoilOracle.TryDecode(path);
      Assert.That(decoded, Is.True,
        $"{pairing}: RECOIL rejected our {encoded.Length}-byte output — {output}");
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Conformance")]
  public void Pairings_ReferenceRegisteredWritableFormats() {
    // Guards the table itself: a renamed or de-registered format should surface here rather than
    // as a confusing decode failure.
    foreach (var pairing in Pairings) {
      var entry = FormatRegistry.GetEntry(pairing.Format);
      Assert.That(entry, Is.Not.Null, $"{pairing.Format} is not registered");
      Assert.That(entry!.SupportsWrite, Is.True, $"{pairing.Format} lost its encoder");
    }
  }

  /// <summary>A deterministic, high-contrast, asymmetric pattern — a flipped or transposed result
  /// stays visible, and it survives heavy palette reduction.</summary>
  private static RawImage _Sample(int width, int height) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      data[o] = (byte)(x * 255 / Math.Max(1, width - 1));
      data[o + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      data[o + 2] = (byte)(((x / 8) + (y / 8)) % 2 == 0 ? 255 : 0);
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }
}
