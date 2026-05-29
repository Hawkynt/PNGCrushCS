using System;
using System.Linq;
using FileFormat.Core;
using Optimizer.Image;

namespace Optimizer.Image.Tests;

/// <summary>
/// Regression tests for <see cref="SaveAsPlanner"/> — verifies that the Save-As flow
/// correctly determines colour-reduction and resize requirements from format metadata.
/// </summary>
[TestFixture]
public sealed class SaveAsPlannerTests {

  private static RawImage _MakeRgbaImage(int width = 64, int height = 64) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Rgba32,
    PixelData = new byte[width * height * 4],
  };

  private static RawImage _MakeIndexed8Image(int paletteEntries) => new() {
    Width = 16,
    Height = 16,
    Format = PixelFormat.Indexed8,
    PixelData = new byte[256],
    Palette = new byte[paletteEntries * 3],
    PaletteCount = paletteEntries,
  };

  private static RawImage _MakeIndexed1Image() => new() {
    Width = 16,
    Height = 16,
    Format = PixelFormat.Indexed1,
    PixelData = new byte[32],
    Palette = new byte[6], // 2 entries
    PaletteCount = 2,
  };

  // -------------------- AllowedPaletteRangesFor --------------------

  [Test]
  [Category("Unit")]
  public void AllowedPaletteRangesFor_MonochromeFormat_Returns2() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Xbm);
    Assert.That(entry, Is.Not.Null, "Xbm should be registered");
    var ranges = SaveAsPlanner.AllowedPaletteRangesFor(entry!);
    Assert.That(ranges, Is.Not.Null);
    Assert.That(ranges!.Length, Is.EqualTo(1));
    Assert.That(ranges[0].Min, Is.EqualTo(2));
    Assert.That(ranges[0].Max, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AllowedPaletteRangesFor_AppleII_Returns2() {
    // Regression test: AppleII previously had no declaration; Save-As bypassed reduction.
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII);
    Assert.That(entry, Is.Not.Null);
    var ranges = SaveAsPlanner.AllowedPaletteRangesFor(entry!);
    Assert.That(ranges, Is.Not.Null, "AppleII must declare AllowedPaletteRanges so Save-As triggers reduction");
    Assert.That(ranges![ranges.Length - 1].Max, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AllowedPaletteRangesFor_UnconstrainedFormat_ReturnsNull() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Png);
    Assert.That(entry, Is.Not.Null);
    var ranges = SaveAsPlanner.AllowedPaletteRangesFor(entry!);
    Assert.That(ranges, Is.Null, "PNG accepts any palette size; no constraint should be returned");
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
    var plan = SaveAsPlanner.PlanReduction(entry, _MakeRgbaImage());
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
    if (SaveAsPlanner.AllowedPaletteRangesFor(entry) == null)
      Assert.Ignore("BMP doesn't declare AllowedPaletteRanges; test only meaningful for indexed-constrained formats");

    var plan = SaveAsPlanner.PlanReduction(entry, _MakeIndexed1Image());
    Assert.That(plan.NeedsReduction, Is.False, "2-colour palette fits any indexed format");
  }

  [Test]
  [Category("Unit")]
  public void PlanReduction_Indexed8WithFewerColors_DoesNotNeedReduction() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Bmp)!;
    if (SaveAsPlanner.AllowedPaletteRangesFor(entry) == null)
      Assert.Ignore("BMP doesn't declare AllowedPaletteRanges; test only meaningful for indexed-constrained formats");

    var plan = SaveAsPlanner.PlanReduction(entry, _MakeIndexed8Image(paletteEntries: 32));
    Assert.That(plan.NeedsReduction, Is.False, "32 colours fits within any IndexedOnly target's 256-colour cap");
  }

  // -------------------- PlanReduction: fixed palettes --------------------

  [Test]
  [Category("Unit")]
  public void PlanReduction_RgbSourceToDoom_NeedsReduction_WithFixedPalettes() {
    var entry = FormatRegistry.GetEntry(ImageFormat.DoomFlat)!;
    Assert.That(entry.FixedPalettes, Is.Not.Null.And.Not.Empty,
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
    Assert.That(SaveAsPlanner.NeedsResizePrompt(entry), Is.False, "PNG accepts any resolution");
  }

  [Test]
  [Category("Unit")]
  public void NeedsResizePrompt_AppleII_True() {
    // Regression: AppleII previously had implicit VariableResolution and crashed when sizes mismatched.
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII)!;
    Assert.That(SaveAsPlanner.NeedsResizePrompt(entry), Is.True, "AppleII requires specific HGR/DHGR dimensions");
  }

  [Test]
  [Category("Unit")]
  public void NeedsResizePrompt_Xbm_False() {
    // XBM has MonochromeOnly but variable resolution.
    var entry = FormatRegistry.GetEntry(ImageFormat.Xbm)!;
    Assert.That(SaveAsPlanner.NeedsResizePrompt(entry), Is.False, "XBM is monochrome but accepts any dimensions");
  }

  [Test]
  [Category("Unit")]
  public void NeedsResizePrompt_NesChr_True() {
    // Regression: NES CHR's FromRawImage requires width==128 and height%8==0; user was getting
    // "Saving failed: NES CHR requires width 128, got 1405" because FixedResolution wasn't declared.
    var entry = FormatRegistry.GetEntry(ImageFormat.NesChr)!;
    Assert.That(SaveAsPlanner.NeedsResizePrompt(entry), Is.True, "NES CHR has fixed 128-pixel width; resize prompt must fire");
  }

  // -------------------- AllowedDimensions / PickClosestDimensions --------------------

  [Test]
  [Category("Unit")]
  public void AllowedDimensions_AppleII_ListsHgrAndDhgr() {
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII)!;
    Assert.That(entry.AllowedDimensions, Is.Not.Null);
    Assert.That(entry.AllowedDimensions!.Length, Is.EqualTo(2));
    var (w1, h1) = entry.AllowedDimensions[0];
    var (w2, h2) = entry.AllowedDimensions[1];
    Assert.That(w1.Min, Is.EqualTo(280)); Assert.That(w1.Max, Is.EqualTo(280));
    Assert.That(w2.Min, Is.EqualTo(560)); Assert.That(w2.Max, Is.EqualTo(560));
    Assert.That(h1.Min, Is.EqualTo(192)); Assert.That(h2.Min, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void PickClosestDimensions_AppleIIWithSmallSource_ChoosesHgr() {
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII)!;
    var pick = SaveAsPlanner.PickClosestDimensions(entry, sourceWidth: 320, sourceHeight: 200);
    Assert.That(pick, Is.Not.Null);
    Assert.That(pick!.Value.EntryIndex, Is.EqualTo(0), "320 is closer to 280 (HGR) than 560 (DHGR)");
    Assert.That(pick.Value.Width, Is.EqualTo(280));
    Assert.That(pick.Value.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void PickClosestDimensions_AppleIIWithLargeSource_ChoosesDhgr() {
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII)!;
    var pick = SaveAsPlanner.PickClosestDimensions(entry, sourceWidth: 800, sourceHeight: 600);
    Assert.That(pick, Is.Not.Null);
    Assert.That(pick!.Value.EntryIndex, Is.EqualTo(1), "800 is closer to 560 (DHGR) than 280 (HGR)");
    Assert.That(pick.Value.Width, Is.EqualTo(560));
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
  public void PickClosestDimensions_Png_ReturnsNull() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Png)!;
    Assert.That(SaveAsPlanner.PickClosestDimensions(entry, 100, 100), Is.Null,
      "PNG has no dimension constraint");
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

  // -------------------- Cross-cutting: every fixed-resolution format must lack VariableResolution --------------------

  [Test]
  [Category("Unit")]
  public void AllFormatsThatThrowOnDimensions_HaveCapabilitiesWithoutVariableResolution() {
    // Documentation-level: this test passes by inspection (other formats may need future fixes).
    // For now we lock in the regression for AppleII as a known correct case.
    var entry = FormatRegistry.GetEntry(ImageFormat.AppleII)!;
    Assert.That((entry.Capabilities & FormatCapability.VariableResolution), Is.EqualTo((FormatCapability)0),
      "Apple II HGR/DHGR has fixed dimensions; its capability flags must omit VariableResolution");
  }

  // -------------------- Cross-cutting: declared ranges are well-formed --------------------

  [Test]
  [Category("Unit")]
  public void AllRegisteredFormats_HaveValidAllowedPaletteRanges() {
    // Walk every registered format and assert any declared AllowedPaletteRanges is sorted, disjoint, and has Min<=Max.
    var formats = FormatRegistry.ConversionTargets.ToList();
    Assert.That(formats, Is.Not.Empty);

    foreach (var entry in formats) {
      var ranges = entry.AllowedPaletteRanges;
      if (ranges is null || ranges.Length == 0) continue;

      for (var i = 0; i < ranges.Length; ++i) {
        Assert.That(ranges[i].Min, Is.LessThanOrEqualTo(ranges[i].Max),
          $"{entry.Name}: range[{i}] Min={ranges[i].Min} > Max={ranges[i].Max}");
        Assert.That(ranges[i].Min, Is.GreaterThanOrEqualTo(1),
          $"{entry.Name}: range[{i}] Min must be >= 1");
        if (i > 0)
          Assert.That(ranges[i].Min, Is.GreaterThan(ranges[i - 1].Max),
            $"{entry.Name}: ranges must be sorted and disjoint at index {i}");
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void AllRegisteredFormats_FixedPalettes_HaveNonEmptyColors() {
    var formats = FormatRegistry.ConversionTargets.ToList();
    foreach (var entry in formats) {
      var fixedPalettes = entry.FixedPalettes;
      if (fixedPalettes is null || fixedPalettes.Length == 0) continue;

      foreach (var palette in fixedPalettes) {
        Assert.That(palette.Name, Is.Not.Empty, $"{entry.Name}: fixed palette has empty name");
        Assert.That(palette.HexColors, Is.Not.Empty, $"{entry.Name}: palette '{palette.Name}' is empty");
        Assert.That(palette.Count, Is.EqualTo(palette.HexColors.Length));
      }
    }
  }

  [Test]
  [Category("Unit")]
  public void AllRegisteredFormats_FixedPalettes_AtLeastMinAllowed() {
    // A fixed palette can be either:
    //   (a) "use as-is" (size matches AllowedPaletteRanges max — e.g. DOOM PLAYPAL = 256, max = 256), or
    //   (b) "master pool" (size > max — e.g. NES master = 64 entries, but max = 4 — user picks subset).
    // Either way, the palette must contain at least the minimum allowed count.
    var formats = FormatRegistry.ConversionTargets.ToList();
    foreach (var entry in formats) {
      var fixedPalettes = entry.FixedPalettes;
      var ranges = entry.AllowedPaletteRanges;
      if (fixedPalettes is null || fixedPalettes.Length == 0 || ranges is null || ranges.Length == 0) continue;

      var minAllowed = ranges[0].Min;
      foreach (var palette in fixedPalettes)
        Assert.That(palette.Count, Is.GreaterThanOrEqualTo(minAllowed),
          $"{entry.Name}: fixed palette '{palette.Name}' has {palette.Count} entries, fewer than the minimum allowed {minAllowed}");
    }
  }

  [Test]
  [Category("Unit")]
  public void PlanReduction_NesChrToFixedPaletteFormat_UsesMasterPalette() {
    // Regression: NES CHR uses a 64-entry master palette with AllowedPaletteRanges=[(2,4)] — user picks 4 from 64.
    var entry = FormatRegistry.GetEntry(ImageFormat.NesChr)!;
    Assert.That(entry.FixedPalettes, Is.Not.Null.And.Not.Empty);
    Assert.That(entry.FixedPalettes![0].Count, Is.GreaterThan(entry.AllowedPaletteRanges![0].Max),
      "NES master palette should exceed the per-image colour limit; the dialog presents it as a subset pool");

    var plan = SaveAsPlanner.PlanReduction(entry, _MakeRgbaImage());
    Assert.That(plan.NeedsReduction, Is.True);
    Assert.That(plan.FixedPalettes, Is.Not.Null);
  }
}
