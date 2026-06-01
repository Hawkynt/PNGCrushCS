using System;
using System.Linq;
using FileFormat.Core;
using Optimizer.Image;

namespace Optimizer.Image.Tests;

/// <summary>
/// Regression tests for <see cref="SaveAsPlanner"/> — verifies that the Save-As flow
/// correctly determines colour-reduction and resize requirements from format <see cref="VideoMode"/> metadata.
/// </summary>
[TestFixture]
public sealed class SaveAsPlannerTests {

  private static RawImage _MakeRgbaImage(int width = 64, int height = 64) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Rgba32,
    PixelData = new byte[width * height * 4],
  };

  private static RawImage _MakeIndexed8Image(int paletteEntries, int width = 16, int height = 16) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Indexed8,
    PixelData = new byte[width * height],
    Palette = new byte[paletteEntries * 3],
    PaletteCount = paletteEntries,
  };

  private static RawImage _MakeIndexed1Image(int width = 16, int height = 16) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Indexed1,
    PixelData = new byte[(width + 7) / 8 * height],
    Palette = new byte[6], // 2 entries
    PaletteCount = 2,
  };

  private static VideoMode _Mode(FormatRegistry.FormatEntry entry, int srcW = 64, int srcH = 64)
    => SaveAsPlanner.PickClosestMode(entry, srcW, srcH)!;

  // -------------------- Mode shape: palette constraints --------------------

  [Test]
  [Category("Unit")]
  public void VideoMode_MonochromeFormat_PaletteRangeIs2() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Xbm);
    Assert.That(entry, Is.Not.Null, "Xbm should be registered");
    var mode = _Mode(entry!);
    Assert.That(mode.AllowedPaletteRanges, Is.Not.Null.And.Not.Empty);
    var ranges = mode.AllowedPaletteRanges!;
    Assert.That(ranges[ranges.Length - 1].Max, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void VideoMode_AppleII_PaletteRangeIs2() {
    // Regression test: AppleII previously had no declaration; Save-As bypassed reduction.
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII);
    Assert.That(entry, Is.Not.Null);
    var mode = _Mode(entry!);
    Assert.That(mode.AllowedPaletteRanges, Is.Not.Null.And.Not.Empty,
      "AppleII must declare palette ranges so Save-As triggers reduction");
    var ranges = mode.AllowedPaletteRanges!;
    Assert.That(ranges[ranges.Length - 1].Max, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void VideoMode_UnconstrainedFormat_HasNoPaletteRange() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Png);
    Assert.That(entry, Is.Not.Null);
    var mode = _Mode(entry!);
    Assert.That(mode.AllowedPaletteRanges, Is.Null.Or.Empty,
      "PNG accepts any palette size; no constraint should be declared");
  }

  // -------------------- PlanReduction: RGB source --------------------

  [Test]
  [Category("Unit")]
  public void PlanReduction_RgbSourceToPng_DoesNotNeedReduction() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Png)!;
    var plan = SaveAsPlanner.PlanReduction(entry, _MakeRgbaImage());
    Assert.That(plan.NeedsReduction, Is.False);
    Assert.That(plan.AllowedRanges, Is.Null);
    Assert.That(plan.FixedPalettes, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void PlanReduction_RgbSourceToMonochromeFormat_NeedsReduction_With2() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Xbm)!;
    var plan = SaveAsPlanner.PlanReduction(entry, _MakeRgbaImage());
    Assert.That(plan.NeedsReduction, Is.True);
    Assert.That(plan.AllowedRanges, Is.Not.Null);
    Assert.That(plan.AllowedRanges![plan.AllowedRanges.Length - 1].Max, Is.EqualTo(2));
    Assert.That(plan.FixedPalettes, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void PlanReduction_RgbSourceToAppleII_NeedsReductionWith2() {
    // Regression: this scenario previously bypassed reduction and crashed mid-write.
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII)!;
    var plan = SaveAsPlanner.PlanReduction(entry, _MakeRgbaImage(280, 192));
    Assert.That(plan.NeedsReduction, Is.True);
    Assert.That(plan.AllowedRanges, Is.Not.Null);
    Assert.That(plan.AllowedRanges![plan.AllowedRanges.Length - 1].Max, Is.EqualTo(2));
  }

  // -------------------- PlanReduction: indexed source --------------------

  [Test]
  [Category("Unit")]
  public void PlanReduction_Indexed8With100ColorsToMonochrome_NeedsReduction() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Xbm)!;
    var plan = SaveAsPlanner.PlanReduction(entry, _MakeIndexed8Image(paletteEntries: 100));
    Assert.That(plan.NeedsReduction, Is.True, "100-colour indexed image exceeds monochrome 2-colour limit");
    Assert.That(plan.AllowedRanges![plan.AllowedRanges.Length - 1].Max, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void PlanReduction_Indexed1To8BitFormat_DoesNotNeedReduction() {
    // Indexed1 has 2 colours which fits even the strictest indexed-only target's max (256).
    var entry = FormatRegistry.GetEntry(ImageFormat.Bmp)!;
    var bmpMode = _Mode(entry);
    if (bmpMode.AllowedPaletteRanges is null || bmpMode.AllowedPaletteRanges.Length == 0)
      Assert.Ignore("BMP doesn't declare palette ranges; test only meaningful for indexed-constrained formats");

    var plan = SaveAsPlanner.PlanReduction(entry, _MakeIndexed1Image());
    Assert.That(plan.NeedsReduction, Is.False, "2-colour palette fits any indexed format");
  }

  [Test]
  [Category("Unit")]
  public void PlanReduction_Indexed8WithFewerColors_DoesNotNeedReduction() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Bmp)!;
    var bmpMode = _Mode(entry);
    if (bmpMode.AllowedPaletteRanges is null || bmpMode.AllowedPaletteRanges.Length == 0)
      Assert.Ignore("BMP doesn't declare palette ranges; test only meaningful for indexed-constrained formats");

    var plan = SaveAsPlanner.PlanReduction(entry, _MakeIndexed8Image(paletteEntries: 32));
    Assert.That(plan.NeedsReduction, Is.False, "32 colours fits within any indexed target's 256-colour cap");
  }

  // -------------------- PlanReduction: fixed palettes --------------------

  [Test]
  [Category("Unit")]
  public void PlanReduction_RgbSourceToDoom_NeedsReduction_WithFixedPalettes() {
    var entry = FormatRegistry.GetEntry(ImageFormat.DoomFlat)!;
    var mode = _Mode(entry);
    Assert.That(mode.AvailablePalettes, Is.Not.Null.And.Not.Empty,
      "DOOM must declare its PLAYPAL palette so the Save-As dialog can show it");

    var plan = SaveAsPlanner.PlanReduction(entry, _MakeRgbaImage());
    Assert.That(plan.NeedsReduction, Is.True);
    Assert.That(plan.FixedPalettes, Is.Not.Null);
    Assert.That(plan.FixedPalettes![0].Count, Is.EqualTo(256), "DOOM PLAYPAL is 256 colours");
  }

  [Test]
  [Category("Unit")]
  public void PlanReduction_AlreadyIndexed8ToFixedPaletteFormat_StillNeedsReduction() {
    // Even if the source is indexed with the right count, the colours need to be dithered to the fixed palette.
    var entry = FormatRegistry.GetEntry(ImageFormat.DoomFlat)!;
    var plan = SaveAsPlanner.PlanReduction(entry, _MakeIndexed8Image(paletteEntries: 256));
    Assert.That(plan.NeedsReduction, Is.True, "Fixed palettes always require re-dithering");
    Assert.That(plan.FixedPalettes, Is.Not.Null);
  }

  // -------------------- NeedsResizePrompt --------------------

  [Test]
  [Category("Unit")]
  public void NeedsResizePrompt_Png_False() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Png)!;
    Assert.That(SaveAsPlanner.NeedsResizePrompt(entry, _MakeRgbaImage()), Is.False, "PNG accepts any resolution");
  }

  [Test]
  [Category("Unit")]
  public void NeedsResizePrompt_AppleII_True() {
    // Regression: AppleII previously had implicit VariableResolution and crashed when sizes mismatched.
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII)!;
    // Use a deliberately wrong size so the chosen mode's MatchesDimensions returns false.
    Assert.That(SaveAsPlanner.NeedsResizePrompt(entry, _MakeRgbaImage(100, 100)), Is.True,
      "AppleII requires specific HGR/DHGR dimensions");
  }

  [Test]
  [Category("Unit")]
  public void NeedsResizePrompt_Xbm_False() {
    // XBM is monochrome but accepts arbitrary dimensions.
    var entry = FormatRegistry.GetEntry(ImageFormat.Xbm)!;
    Assert.That(SaveAsPlanner.NeedsResizePrompt(entry, _MakeRgbaImage(123, 77)), Is.False,
      "XBM is monochrome but accepts any dimensions");
  }

  [Test]
  [Category("Unit")]
  public void NeedsResizePrompt_NesChr_True() {
    // NES CHR FromRawImage requires width==128 and height%8==0.
    var entry = FormatRegistry.GetEntry(ImageFormat.NesChr)!;
    Assert.That(SaveAsPlanner.NeedsResizePrompt(entry, _MakeRgbaImage(1405, 1405)), Is.True,
      "NES CHR has fixed 128-pixel width; resize prompt must fire");
  }

  // -------------------- VideoMode dimensions / PickClosestDimensions --------------------

  [Test]
  [Category("Unit")]
  public void VideoMode_AppleII_DeclaresHgrAndDhgrDimensions() {
    // HGR and DHGR share the same palette profile (2-colour), so per the VideoMode convention
    // they're coupled within a single mode's Dimensions array (not split into two modes).
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII)!;
    Assert.That(entry.VideoModes, Is.Not.Null.And.Not.Empty);

    var allDims = entry.VideoModes!.SelectMany(m => m.Dimensions).ToArray();
    Assert.That(allDims.Any(d => d.Width.Min == 280 && d.Width.Max == 280 && d.Height.Min == 192), Is.True,
      "HGR (280x192) should be declared");
    Assert.That(allDims.Any(d => d.Width.Min == 560 && d.Width.Max == 560 && d.Height.Min == 192), Is.True,
      "DHGR (560x192) should be declared");
  }

  [Test]
  [Category("Unit")]
  public void PickClosestDimensions_AppleIIWithSmallSource_ChoosesHgr() {
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII)!;
    var pick = SaveAsPlanner.PickClosestDimensions(entry, sourceWidth: 320, sourceHeight: 200);
    Assert.That(pick, Is.Not.Null);
    Assert.That(pick!.Value.Width, Is.EqualTo(280), "320 is closer to 280 (HGR) than 560 (DHGR)");
    Assert.That(pick.Value.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void PickClosestDimensions_AppleIIWithLargeSource_ChoosesDhgr() {
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII)!;
    var pick = SaveAsPlanner.PickClosestDimensions(entry, sourceWidth: 800, sourceHeight: 600);
    Assert.That(pick, Is.Not.Null);
    Assert.That(pick!.Value.Width, Is.EqualTo(560), "800 is closer to 560 (DHGR) than 280 (HGR)");
  }

  [Test]
  [Category("Unit")]
  public void PickClosestDimensions_NesChr_SnapsHeightToMultipleOf8() {
    var entry = FormatRegistry.GetEntry(ImageFormat.NesChr)!;
    var pick = SaveAsPlanner.PickClosestDimensions(entry, sourceWidth: 1405, sourceHeight: 1405);
    Assert.That(pick, Is.Not.Null);
    Assert.That(pick!.Value.Width, Is.EqualTo(128), "NES CHR width is fixed at 128");
    Assert.That(pick.Value.Height % 8, Is.EqualTo(0), "NES CHR height must be a multiple of 8");
  }

  [Test]
  [Category("Unit")]
  public void PickClosestDimensions_Png_ReturnsAnyAnyMode() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Png)!;
    var pick = SaveAsPlanner.PickClosestDimensions(entry, 100, 100);
    Assert.That(pick, Is.Not.Null, "PNG declares a single unbounded mode");
    Assert.That(pick!.Value.Width, Is.EqualTo(100));
    Assert.That(pick.Value.Height, Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void IntegerRange_SnapToValid_MultipleOf8() {
    var r = new IntegerRange(8, 4096, step: 8);
    Assert.That(r.SnapToValid(1), Is.EqualTo(8));
    Assert.That(r.SnapToValid(11), Is.EqualTo(8));
    Assert.That(r.SnapToValid(12), Is.EqualTo(16));
    Assert.That(r.SnapToValid(16), Is.EqualTo(16));
    Assert.That(r.SnapToValid(100), Is.EqualTo(104));
    Assert.That(r.SnapToValid(99), Is.EqualTo(96));
    Assert.That(r.SnapToValid(99999), Is.EqualTo(4096));
  }

  [Test]
  [Category("Unit")]
  public void IntegerRange_Contains_RespectsStep() {
    var r = new IntegerRange(0, 100, step: 5);
    Assert.That(r.Contains(0), Is.True);
    Assert.That(r.Contains(5), Is.True);
    Assert.That(r.Contains(7), Is.False);
    Assert.That(r.Contains(100), Is.True);
    Assert.That(r.Contains(101), Is.False);
    Assert.That(r.Contains(-1), Is.False);
  }

  // -------------------- Cross-cutting: declared palette ranges are well-formed --------------------

  [Test]
  [Category("Unit")]
  public void AllRegisteredFormats_HaveValidVideoModePaletteRanges() {
    // Walk every registered format and assert each declared mode's AllowedPaletteRanges
    // is sorted, disjoint, and has Min<=Max.
    var formats = FormatRegistry.ConversionTargets.ToList();
    Assert.That(formats, Is.Not.Empty);

    foreach (var entry in formats) {
      if (entry.VideoModes is null) continue;
      foreach (var mode in entry.VideoModes) {
        var ranges = mode.AllowedPaletteRanges;
        if (ranges is null || ranges.Length == 0) continue;

        for (var i = 0; i < ranges.Length; ++i) {
          Assert.That(ranges[i].Min, Is.LessThanOrEqualTo(ranges[i].Max),
            $"{entry.Name}/'{mode.Name}': range[{i}] Min={ranges[i].Min} > Max={ranges[i].Max}");
          Assert.That(ranges[i].Min, Is.GreaterThanOrEqualTo(1),
            $"{entry.Name}/'{mode.Name}': range[{i}] Min must be >= 1");
          if (i > 0)
            Assert.That(ranges[i].Min, Is.GreaterThan(ranges[i - 1].Max),
              $"{entry.Name}/'{mode.Name}': ranges must be sorted and disjoint at index {i}");
        }
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void AllRegisteredFormats_AvailablePalettes_HaveNonEmptyColors() {
    var formats = FormatRegistry.ConversionTargets.ToList();
    foreach (var entry in formats) {
      if (entry.VideoModes is null) continue;
      foreach (var mode in entry.VideoModes) {
        var palettes = mode.AvailablePalettes;
        if (palettes is null || palettes.Length == 0) continue;

        foreach (var palette in palettes) {
          Assert.That(palette.Name, Is.Not.Empty, $"{entry.Name}/'{mode.Name}': fixed palette has empty name");
          Assert.That(palette.HexColors, Is.Not.Empty, $"{entry.Name}/'{mode.Name}': palette '{palette.Name}' is empty");
          Assert.That(palette.Count, Is.EqualTo(palette.HexColors.Length));
        }
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void AllRegisteredFormats_AvailablePalettes_AtLeastMinAllowed() {
    // A declared palette can be either:
    //   (a) "use as-is" (size matches AllowedPaletteRanges max — e.g. DOOM PLAYPAL = 256, max = 256), or
    //   (b) "master pool" (size > max — e.g. NES master = 64 entries, but max = 4 — user picks subset).
    // Either way, the palette must contain at least the minimum allowed count.
    var formats = FormatRegistry.ConversionTargets.ToList();
    foreach (var entry in formats) {
      if (entry.VideoModes is null) continue;
      foreach (var mode in entry.VideoModes) {
        var palettes = mode.AvailablePalettes;
        var ranges = mode.AllowedPaletteRanges;
        if (palettes is null || palettes.Length == 0 || ranges is null || ranges.Length == 0) continue;

        var minAllowed = ranges[0].Min;
        foreach (var palette in palettes)
          Assert.That(palette.Count, Is.GreaterThanOrEqualTo(minAllowed),
            $"{entry.Name}/'{mode.Name}': palette '{palette.Name}' has {palette.Count} entries, fewer than the minimum allowed {minAllowed}");
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void PlanReduction_NesChrToFixedPaletteFormat_UsesMasterPalette() {
    // Regression: NES CHR uses a 64-entry master palette with AllowedPaletteRanges=[(2,4)] — user picks 4 from 64.
    var entry = FormatRegistry.GetEntry(ImageFormat.NesChr)!;
    var mode = _Mode(entry, 128, 128);
    Assert.That(mode.AvailablePalettes, Is.Not.Null.And.Not.Empty);
    Assert.That(mode.AvailablePalettes![0].Count, Is.GreaterThan(mode.AllowedPaletteRanges![0].Max),
      "NES master palette should exceed the per-image colour limit; the dialog presents it as a subset pool");

    var plan = SaveAsPlanner.PlanReduction(entry, _MakeRgbaImage(128, 128));
    Assert.That(plan.NeedsReduction, Is.True);
    Assert.That(plan.FixedPalettes, Is.Not.Null);
  }
}
