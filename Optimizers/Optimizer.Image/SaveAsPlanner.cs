using System.Collections.Generic;
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
/// All constraint information comes from the target format's <see cref="VideoMode"/> declarations.
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

  /// <summary>Result of dimension-matching against a chosen mode.</summary>
  internal readonly record struct PickedDimensions(int EntryIndex, int Width, int Height);

  /// <summary>Returns the chosen mode (or auto-picks the closest one) for the source dimensions.</summary>
  internal static VideoMode? PickClosestMode(FormatRegistry.FormatEntry entry, int srcW, int srcH) {
    if (entry.VideoModes is not { Length: > 0 } modes) return null;
    if (modes.Length == 1) return modes[0];

    var bestIdx = 0;
    var bestDist = double.PositiveInfinity;
    for (var i = 0; i < modes.Length; ++i) {
      foreach (var (w, h) in modes[i].Dimensions) {
        var cw = (w.Min + w.Max) / 2.0;
        var ch = (h.Min + h.Max) / 2.0;
        var dw = srcW - cw;
        var dh = srcH - ch;
        var dist = dw * dw + dh * dh;
        if (dist < bestDist) { bestDist = dist; bestIdx = i; }
      }
    }
    return modes[bestIdx];
  }

  /// <summary>Snaps the source dimensions to the closest valid <c>(W, H)</c> pair within the chosen mode.</summary>
  internal static PickedDimensions PickClosestDimensionsInMode(VideoMode mode, int srcW, int srcH) {
    var bestIdx = 0;
    var bestDist = double.PositiveInfinity;
    for (var i = 0; i < mode.Dimensions.Length; ++i) {
      var (w, h) = mode.Dimensions[i];
      var cw = (w.Min + w.Max) / 2.0;
      var ch = (h.Min + h.Max) / 2.0;
      var dw = srcW - cw;
      var dh = srcH - ch;
      var dist = dw * dw + dh * dh;
      if (dist < bestDist) { bestDist = dist; bestIdx = i; }
    }
    var (bw, bh) = mode.Dimensions[bestIdx];
    return new(bestIdx, bw.SnapToValid(srcW), bh.SnapToValid(srcH));
  }

  /// <summary>True when the source dimensions don't match any of the chosen mode's allowed (W, H) pairs.</summary>
  internal static bool NeedsResizePromptInMode(VideoMode mode, RawImage source)
    => !mode.MatchesDimensions(source.Width, source.Height);

  /// <summary>Plans colour reduction for a chosen <see cref="VideoMode"/>.</summary>
  internal static ReductionPlan PlanReductionInMode(VideoMode mode, RawImage image) {
    var fixedPalettes = mode.AvailablePalettes;
    var ranges = mode.AllowedPaletteRanges;

    if (fixedPalettes is { Length: > 0 })
      return new(NeedsReduction: true, AllowedRanges: ranges, FixedPalettes: fixedPalettes);

    if (ranges is null || ranges.Length == 0)
      return new(NeedsReduction: false, AllowedRanges: null, FixedPalettes: null);

    var maxAllowed = ranges[ranges.Length - 1].Max;
    var isIndexed = image.Format is PixelFormat.Indexed1 or PixelFormat.Indexed4 or PixelFormat.Indexed8;
    var paletteEntryCount = image.Palette is { Length: > 0 } ? image.Palette.Length / 3 : int.MaxValue;
    var needsReduction = !isIndexed || paletteEntryCount > maxAllowed;
    return new(needsReduction, ranges, null);
  }

  /// <summary>Returns the chosen mode's <see cref="VideoMode.Dimensions"/> for the resize dialog.
  /// Returns <c>null</c> if no mode is provided.</summary>
  internal static (IntegerRange Width, IntegerRange Height)[]? DimensionsForResizeDialog(VideoMode? mode)
    => mode?.Dimensions;

  /// <summary>Convenience entry: determines colour reduction for an image targeted at <paramref name="entry"/>.
  /// Picks the closest <see cref="VideoMode"/> automatically.</summary>
  internal static ReductionPlan PlanReduction(FormatRegistry.FormatEntry entry, RawImage image) {
    var mode = PickClosestMode(entry, image.Width, image.Height);
    return mode is null
      ? new(NeedsReduction: false, AllowedRanges: null, FixedPalettes: null)
      : PlanReductionInMode(mode, image);
  }

  /// <summary>True when the source dimensions don't match any allowed (W, H) of the closest <see cref="VideoMode"/>.
  /// Returns false when the format has no modes.</summary>
  internal static bool NeedsResizePrompt(FormatRegistry.FormatEntry entry, RawImage source) {
    var mode = PickClosestMode(entry, source.Width, source.Height);
    return mode is not null && NeedsResizePromptInMode(mode, source);
  }

  /// <summary>Picks the closest valid <c>(W, H)</c> entry across the format's <see cref="VideoMode"/>s
  /// and snaps the source dimensions to it. Returns <c>null</c> when the format declares no modes.</summary>
  internal static (int EntryIndex, int Width, int Height)? PickClosestDimensions(
    FormatRegistry.FormatEntry entry, int sourceWidth, int sourceHeight
  ) {
    var mode = PickClosestMode(entry, sourceWidth, sourceHeight);
    if (mode is null) return null;
    var picked = PickClosestDimensionsInMode(mode, sourceWidth, sourceHeight);
    return (picked.EntryIndex, picked.Width, picked.Height);
  }
}
