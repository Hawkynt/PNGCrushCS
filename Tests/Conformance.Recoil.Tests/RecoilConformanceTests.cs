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
    new(ImageFormat.Hp48Grob, "HP 48 GROB", 131, 37),
    new(ImageFormat.Kitty, "Kitty", 640, 400),
    new(ImageFormat.ArtMaster88, "Art Master 88", 640, 400),
    new(ImageFormat.MsxScc, "MSX2+ Screen 12", 256, 212),
    new(ImageFormat.DegasIcon, "DEGAS Elite icon", 37, 23),
    new(ImageFormat.Printfox, "Printfox", 88, 40),
    new(ImageFormat.TrueColorImg, "True-colour GEM image", 96, 40),
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
    new(ImageFormat.ZxNextImage, "256x192 format", 256, 192),
    new(ImageFormat.TextureMaker0, "Texture Maker0 16x16x16", 64, 64),
    new(ImageFormat.BbcMicroScreen, "Mode 4", 320, 256, ".bb4"),
    new(ImageFormat.ZxMulticolor, "Multicolor 8x1", 256, 192, ".mc"),
    new(ImageFormat.ZxAttributes, "Attributes", 256, 192),
    new(ImageFormat.AtariGraphics3, "Mad Studio Graphics 3", 320, 192, ".gr3"),
    new(ImageFormat.Paradox, "Paradox", 320, 200),
    new(ImageFormat.SevenuP, "SevenuP", 256, 192),
    new(ImageFormat.ZxAttributesGigascreen, "Attributes Gigascreen", 256, 192),
    new(ImageFormat.LastWordFont, "The Last Word font", 128, 32),
    new(ImageFormat.DaliCompressed, "Dali (compressed)", 320, 200, ".lpk"),
    new(ImageFormat.RamBrandt, "Rambrandt", 320, 192, ".rm0"),
    new(ImageFormat.InterPainter, "InterPainter", 320, 200, ".inp"),
    new(ImageFormat.Imagic, "Imagic", 320, 200, ".ic1"),
    new(ImageFormat.Imagic, "Imagic (medium)", 640, 200, ".ic2"),
    new(ImageFormat.Imagic, "Imagic (high)", 640, 400, ".ic3"),
    new(ImageFormat.AtariTt, "TT Low", 640, 480, ".pi4"),
    new(ImageFormat.AtariTt, "TT Low as .pi5", 640, 480, ".pi5"),
    new(ImageFormat.AtariTt, "TT High", 1280, 960, ".pi6"),
    new(ImageFormat.IcDraw, "ICDRAW icon", 32, 32, ".ibi"),
    new(ImageFormat.IcDraw, "ICDRAW icon as .ib3", 32, 32, ".ib3"),
    new(ImageFormat.Ice, "Super IRG", 320, 192, ".irg"),
    new(ImageFormat.MadStudio, "Mad Studio ANTIC 4", 320, 192, ".an4"),
    new(ImageFormat.HiResEditor, "Hires-Editor", 320, 200, ".het"),
    new(ImageFormat.HiResEditor, "Run Paint", 320, 200, ".rph"),
    new(ImageFormat.AtariTools800, "AtariTools-800 players", 80, 240, ".4pl"),
    new(ImageFormat.AtariTools800, "AtariTools-800 missiles", 32, 240, ".4mi"),
    new(ImageFormat.AtariTools800, "AtariTools-800 players and missiles", 112, 240, ".4pm"),
    new(ImageFormat.AtariTools800Font, "AtariTools-800 character set", 128, 64, ".acs"),
    new(ImageFormat.ZxRgb3, "ZX Spectrum RGB3", 256, 192, ".3"),
    new(ImageFormat.MsxScreen6, "MSX2 Screen 6", 512, 424, ".sc6"),
    new(ImageFormat.MonoStar, "MonoSTar object", 64, 48, ".obj"),
    // The V9958's YJK family. RECOIL picks the palette-less reading from the extension alone, so
    // .glc is the one we can hand it without a companion .PLA palette file beside it.
    new(ImageFormat.MsxScreen10, "MSX2+ Screen 10", 256, 212, ".sca"),
    new(ImageFormat.MsxScreen10, "MSX2+ Screen 10 as .scb", 256, 212, ".scb"),
    new(ImageFormat.MsxGlYjk, "MSX2+ GL YJK", 256, 212, ".glc"),
    new(ImageFormat.MsxGlYjk, "MSX2+ GL YJK as .gls", 256, 212, ".gls"),
    new(ImageFormat.MsxGl6, "MSX2 GL6", 512, 424, ".gl6"),
    new(ImageFormat.MsxGl6, "Dynamic Publisher stamp", 512, 424, ".stp"),
    new(ImageFormat.MsxGl16, "MSX2 GL5", 256, 212, ".gl5"),
    new(ImageFormat.MsxGl16, "MSX2 SH5", 256, 212, ".sh5"),
    new(ImageFormat.MadDesigner, "Mad Designer", 512, 256, ".mbg"),
    new(ImageFormat.AtariTxs, "Atari texture", 64, 64, ".txs"),
    new(ImageFormat.Commodore64Font, "C64 8x8 font", 256, 64, ".64c"),
    new(ImageFormat.PaintShop, "PaintShop", 640, 800, ".da4"),
    new(ImageFormat.HandyScanner, "Handy Scanner 2000 POSTERING", 840, 120, ".hs2"),
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
