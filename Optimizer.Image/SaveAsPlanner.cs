using System.Linq;
using FileFormat.Core;

namespace Optimizer.Image;

/// <summary>
/// Pure decision logic that drives the Save-As flow: determines whether a given image
/// requires resizing and/or colour reduction before it can be written to a target format,
/// and what constraints to enforce in the colour-reduction dialog.
/// </summary>
/// <remarks>
/// Extracted from the WinForms <c>MainForm._SaveAsDialog</c> so that it can be exercised by
/// regression tests without spinning up the UI. All members are pure: no I/O, no UI, no shared state.
/// </remarks>
internal static class SaveAsPlanner {

  /// <summary>Outcome of the colour-reduction analysis.</summary>
  /// <param name="NeedsReduction">True when the current image's palette/format violates the target's constraint.</param>
  /// <param name="AllowedRanges">Constraint to pass to <c>ReduceColorsWindow.SetAllowedPaletteRanges</c>; null when the format imposes no size constraint.</param>
  /// <param name="FixedPalettes">Non-null when the format requires a specific palette (e.g. DOOM, NES); the dialog hides the quantizer in that case.</param>
  internal readonly record struct ReductionPlan(
    bool NeedsReduction,
    IntegerRange[]? AllowedRanges,
    FixedPalette[]? FixedPalettes
  );

  /// <summary>Returns the allowed palette-size ranges declared by <paramref name="entry"/>, or <c>null</c> if it imposes no constraint.</summary>
  /// <remarks>
  /// Preference order: the format's own <see cref="IImageFormatMetadata{TSelf}.AllowedPaletteRanges"/> declaration ➜
  /// <see cref="FormatCapability.MonochromeOnly"/> ⇒ <c>[2]</c> ➜
  /// <see cref="FormatCapability.IndexedOnly"/> ⇒ <c>[new IntegerRange(2, 256)]</c> ➜
  /// <c>null</c> (no constraint).
  /// </remarks>
  internal static IntegerRange[]? AllowedPaletteRangesFor(FormatRegistry.FormatEntry entry) {
    if (entry.AllowedPaletteRanges is { Length: > 0 } declared) return declared;
    var caps = entry.Capabilities;
    if ((caps & FormatCapability.MonochromeOnly) != 0) return [2];
    if ((caps & FormatCapability.IndexedOnly) != 0) return [new IntegerRange(2, 256)];
    return null;
  }

  /// <summary>
  /// Determines whether the target format requires colour reduction for the given image,
  /// and which constraints to apply.
  /// </summary>
  internal static ReductionPlan PlanReduction(FormatRegistry.FormatEntry entry, RawImage image) {
    var fixedPalettes = entry.FixedPalettes;
    var ranges = AllowedPaletteRangesFor(entry);

    // Fixed palettes always require reduction (the image must be dithered into one of them).
    if (fixedPalettes is { Length: > 0 })
      return new(NeedsReduction: true, AllowedRanges: ranges, FixedPalettes: fixedPalettes);

    // No fixed palettes and no size constraint => no reduction needed.
    if (ranges == null)
      return new(NeedsReduction: false, AllowedRanges: null, FixedPalettes: null);

    var maxAllowed = ranges[ranges.Length - 1].Max;
    var isIndexed = image.Format is PixelFormat.Indexed1 or PixelFormat.Indexed4 or PixelFormat.Indexed8;
    var paletteEntryCount = image.Palette is { Length: > 0 } ? image.Palette.Length / 3 : int.MaxValue;
    var needsReduction = !isIndexed || paletteEntryCount > maxAllowed;

    return new(needsReduction, ranges, null);
  }

  /// <summary>True when the format declares <see cref="FormatCapability.FixedResolution"/> —
  /// i.e. the writer requires specific pixel dimensions (e.g. Apple II HGR = 280x192, NES tile = 8x8).
  /// Used by the UI to decide whether to show a resize prompt before saving.</summary>
  internal static bool NeedsResizePrompt(FormatRegistry.FormatEntry entry)
    => (entry.Capabilities & FormatCapability.FixedResolution) != 0;

  /// <summary>Picks the <c>AllowedDimensions</c> entry whose centre is closest to <paramref name="sourceWidth"/>x<paramref name="sourceHeight"/>,
  /// and snaps the source dimensions to valid values inside that entry. Returns the chosen entry index plus
  /// the snapped (width, height). Returns <c>null</c> when the format has no dimension constraint.</summary>
  internal static (int EntryIndex, int Width, int Height)? PickClosestDimensions(
    FormatRegistry.FormatEntry entry, int sourceWidth, int sourceHeight
  ) {
    var allowed = entry.AllowedDimensions;
    if (allowed is null || allowed.Length == 0) return null;

    var bestIdx = 0;
    var bestDist = double.PositiveInfinity;
    for (var i = 0; i < allowed.Length; ++i) {
      var (w, h) = allowed[i];
      // Distance from source to the centre of this option's W×H box.
      var cw = (w.Min + w.Max) / 2.0;
      var ch = (h.Min + h.Max) / 2.0;
      var dw = sourceWidth - cw;
      var dh = sourceHeight - ch;
      var dist = dw * dw + dh * dh;
      if (dist < bestDist) { bestDist = dist; bestIdx = i; }
    }

    var (bw, bh) = allowed[bestIdx];
    return (bestIdx, bw.SnapToValid(sourceWidth), bh.SnapToValid(sourceHeight));
  }
}
