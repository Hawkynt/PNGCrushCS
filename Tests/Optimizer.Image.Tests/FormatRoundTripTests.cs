using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FileFormat.Core;
using Optimizer.Image;

namespace Optimizer.Image.Tests;

/// <summary>
/// Parameterised round-trip tests for every format in <see cref="FormatRegistry"/> that supports
/// both reading and writing. Each format gets its own test case so failures are visible per format
/// rather than as a single noisy aggregate.
/// </summary>
/// <remarks>
/// The test tries a sequence of input pixel formats / dimension candidates until the writer accepts
/// one. This avoids per-format heuristics — if a writer requires e.g. Gray8 + 320x192, the test
/// will discover that by trial. The chosen input is then saved → reloaded → verified.
/// <list type="bullet">
/// <item>Dimensions: from <c>AllowedDimensions</c> if declared, else 32x32 (extracted from writer error message on retry).</item>
/// <item>Pixel format: tries Indexed1, Indexed8 (varying palette sizes), Gray8, Rgb24, Rgba32, Bgra32.</item>
/// <item>Sidecar: writes <c>.pal</c> next to the saved file so palette-less formats restore on load.</item>
/// </list>
/// Lossy formats only get dimension checks; lossless formats also get pixel-data comparison.
/// </remarks>
[TestFixture]
public sealed class FormatRoundTripTests {

  private static readonly HashSet<ImageFormat> _LossyFormats = [
    ImageFormat.Jpeg, ImageFormat.Jpeg2000, ImageFormat.JpegXr, ImageFormat.JpegXl, ImageFormat.JpegLs,
    ImageFormat.Heif, ImageFormat.Avif, ImageFormat.WebP, ImageFormat.Bpg, ImageFormat.Wsq,
    ImageFormat.DjVu, ImageFormat.Flif,
    // RGB565 — stores 5-6-5 bits per channel; 8-bit Rgb24 input loses low bits on quantize.
    // Format unit tests for these explicitly exercise the lossy Rgb24 path.
    ImageFormat.CokeAtari, ImageFormat.Rembrandt,
  ];

  /// <summary>Formats whose on-disk payload deliberately lacks self-describing metadata (width/height/format
  /// supplied externally). The generic round-trip path cannot exercise these — their own format-specific
  /// unit tests cover the read/write API.</summary>
  private static readonly HashSet<ImageFormat> _NoSelfDescribingMetadata = [
    ImageFormat.Ccitt, // CCITT G3/G4: raw bitstream codec, no header
  ];

  public static IEnumerable<TestCaseData> WritableFormats() {
    var seen = new HashSet<ImageFormat>();
    foreach (var entry in FormatRegistry.ConversionTargets) {
      if (entry.ConvertFromRawImage == null || entry.LoadRawImage == null) continue;
      if (entry.Format == ImageFormat.Unknown) continue;
      if (_NoSelfDescribingMetadata.Contains(entry.Format)) continue;
      if (!seen.Add(entry.Format)) continue;
      yield return new TestCaseData(entry.Format).SetName($"RoundTrip_{entry.Name}");
    }
  }

  [Test]
  [Category("Regression")]
  [TestCaseSource(nameof(WritableFormats))]
  public void RoundTrip(ImageFormat format) {
    var entry = FormatRegistry.GetEntry(format)!;
    Assert.That(entry, Is.Not.Null);

    // Discover dimensions and the input pixel format the writer accepts by trial.
    var (input, saved) = _FindAcceptedInput(entry);
    if (input == null || saved == null) return; // Assert.Fail or Assert.Ignore already raised inside.

    var tempFile = Path.Combine(Path.GetTempPath(), $"roundtrip_{Guid.NewGuid():N}{entry.PrimaryExtension}");
    try {
      File.WriteAllBytes(tempFile, saved);
      PaletteSidecar.TryWrite(tempFile, input);

      var loaded = entry.LoadRawImage!(new FileInfo(tempFile));
      Assert.That(loaded, Is.Not.Null, $"{entry.Name}: reader returned null for round-tripped bytes");
      loaded = PaletteSidecar.Apply(tempFile, loaded!);

      Assert.That(loaded.Width, Is.EqualTo(input.Width), $"{entry.Name}: width changed across round-trip");
      Assert.That(loaded.Height, Is.EqualTo(input.Height), $"{entry.Name}: height changed across round-trip");

      if (loaded.IsIndexed) {
        Assert.That(loaded.Palette, Is.Not.Null, $"{entry.Name}: indexed image has no palette after round-trip");
        Assert.That(loaded.PaletteCount, Is.GreaterThan(0));
        Assert.That(loaded.Palette!.Length, Is.GreaterThanOrEqualTo(loaded.PaletteCount * 3),
          $"{entry.Name}: palette truncated below PaletteCount × 3 bytes");
        // For Indexed8 format claimed by ToRawImage, validate pixel-index range strictly.
        // Indexed1/4 are packed bit-fields where raw bytes legitimately exceed PaletteCount.
        if (loaded.Format == PixelFormat.Indexed8) {
          var max = 0;
          foreach (var p in loaded.PixelData!) if (p > max) max = p;
          Assert.That(max, Is.LessThan(loaded.PaletteCount),
            $"{entry.Name}: pixel index {max} ≥ palette count {loaded.PaletteCount}");
        }
      }

      if (!_LossyFormats.Contains(entry.Format)
          && loaded.Format == input.Format
          && loaded.PixelData!.Length == input.PixelData.Length) {
        Assert.That(loaded.PixelData, Is.EqualTo(input.PixelData),
          $"{entry.Name}: pixel data changed across lossless round-trip");
      }
    } finally {
      try { File.Delete(tempFile); } catch { }
      try { File.Delete(tempFile + PaletteSidecar.SidecarSuffix); } catch { }
    }
  }

  // -------------------- input-finder --------------------

  /// <summary>Iterates pixel-format / dimension candidates until the writer accepts one.</summary>
  private static (RawImage? Input, byte[]? Saved) _FindAcceptedInput(FormatRegistry.FormatEntry entry) {
    var candidates = _BuildCandidates(entry).ToList();
    if (candidates.Count == 0) {
      Assert.Ignore($"{entry.Name}: cannot construct any compatible test image");
      return (null, null);
    }

    string? lastErrorContext = null;
    foreach (var (raw, label) in candidates) {
      var attempt = _TryWrite(entry, raw, label);
      if (attempt.Success) return (attempt.Input, attempt.Bytes);
      lastErrorContext = attempt.Error ?? lastErrorContext;
    }

    Assert.Fail($"{entry.Name}: no input format worked. Last attempt — {lastErrorContext}");
    return (null, null);
  }

  private readonly record struct _WriteAttempt(bool Success, RawImage? Input, byte[]? Bytes, string? Error);

  /// <summary>Single write attempt with adaptive retries on dimension/pixel-format error messages.</summary>
  private static _WriteAttempt _TryWrite(FormatRegistry.FormatEntry entry, RawImage raw, string label) {
    try {
      var bytes = entry.ConvertFromRawImage!(raw);
      if (bytes is { Length: > 0 }) return new(true, raw, bytes, null);
      return new(false, null, null, $"{label}: writer returned empty bytes");
    } catch (Exception ex) {
      var ctx = $"{label}: {ex.GetType().Name}: {ex.Message}";

      // Adaptive: required-dimensions hint? Rebuild candidates at those dimensions.
      if (_ExtractDimensions(ex.Message) is { } dims) {
        foreach (var (retry, retryLabel) in _BuildCandidates(entry, dims.Width, dims.Height)) {
          try {
            var bytes = entry.ConvertFromRawImage!(retry);
            if (bytes is { Length: > 0 }) return new(true, retry, bytes, null);
          } catch (Exception ex2) { ctx = $"{retryLabel}: {ex2.Message}"; }
        }
      }
      return new(false, null, null, ctx);
    }
  }

  private static readonly Regex _FormatRegex = new(
    @"(?:Expected|Only|requires|must use)\s+(?:PixelFormat\.)?(?<fmt>Indexed1|Indexed4|Indexed8|Gray8|Gray16|Rgb24|Rgb48|Rgba32|Rgba64|Argb32|Bgra32|GrayAlpha16)",
    RegexOptions.IgnoreCase);

  private static PixelFormat? _ExtractExpectedFormat(string message) {
    var m = _FormatRegex.Match(message);
    if (!m.Success) return null;
    return Enum.TryParse<PixelFormat>(m.Groups["fmt"].Value, ignoreCase: true, out var pf) ? pf : null;
  }

  /// <summary>Yields a sequence of candidate input images in priority order.</summary>
  private static IEnumerable<(RawImage Image, string Label)> _BuildCandidates(
      FormatRegistry.FormatEntry entry, int? forcedW = null, int? forcedH = null) {
    var (w, h) = (forcedW, forcedH) is (int fw, int fh)
      ? (fw, fh)
      : _ResolveDimensions(entry);
    if (w <= 0 || h <= 0) yield break;

    var caps = entry.Capabilities;
    var ranges = SaveAsPlanner.AllowedPaletteRangesFor(entry);
    var fixedPalettes = entry.FixedPalettes;

    var preferIndexed = (caps & FormatCapability.MonochromeOnly) != 0
                       || (caps & FormatCapability.IndexedOnly) != 0
                       || ranges is { Length: > 0 }
                       || fixedPalettes is { Length: > 0 };

    if (preferIndexed) {
      // Format thinks it's indexed — try indexed inputs first.
      yield return (_Indexed1(w, h), "Indexed1");
      yield return (_Indexed8(w, h, entry), "Indexed8");
    }

    yield return (_Gray8(w, h), "Gray8");
    yield return (_Rgb24(w, h), "Rgb24");
    yield return (_Rgba32(w, h), "Rgba32");
    yield return (_Bgra32(w, h), "Bgra32");
    yield return (_Argb32(w, h), "Argb32");

    if (!preferIndexed) {
      yield return (_Indexed8(w, h, entry), "Indexed8 (fallback)");
      yield return (_Indexed1(w, h), "Indexed1 (fallback)");
    }
  }

  private static (int W, int H) _ResolveDimensions(FormatRegistry.FormatEntry entry) {
    if (entry.AllowedDimensions is { Length: > 0 } dims) {
      var (wRange, hRange) = dims[0];
      return (wRange.SnapToValid(32), hRange.SnapToValid(32));
    }
    return (32, 32);
  }

  private static readonly Regex _DimensionRegex = new(@"(?:Expected|requires|must be|Image must be)\s*(\d+)\s*[x×]\s*(\d+)", RegexOptions.IgnoreCase);
  private static (int Width, int Height)? _ExtractDimensions(string message) {
    var m = _DimensionRegex.Match(message);
    if (!m.Success) return null;
    if (!int.TryParse(m.Groups[1].Value, out var w)) return null;
    if (!int.TryParse(m.Groups[2].Value, out var h)) return null;
    return (w, h);
  }

  // -------------------- image factories --------------------

  private static RawImage _Indexed1(int w, int h) {
    var stride = (w + 7) / 8;
    var pixels = new byte[stride * h];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        if (((x + y) & 1) != 0)
          pixels[y * stride + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
    return new() {
      Width = w, Height = h,
      Format = PixelFormat.Indexed1,
      PixelData = pixels,
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };
  }

  private static RawImage _Indexed8(int w, int h, FormatRegistry.FormatEntry entry) {
    var ranges = SaveAsPlanner.AllowedPaletteRangesFor(entry) ?? [new IntegerRange(2, 16)];
    var maxColours = Math.Min(ranges[^1].Max, 16);
    var minColours = Math.Max(ranges[0].Min, 2);
    var paletteCount = Math.Max(minColours, Math.Min(maxColours, 4));

    byte[] palette;
    if (entry.FixedPalettes is { Length: > 0 } fps) {
      var take = Math.Min(fps[0].Count, paletteCount);
      palette = new byte[take * 3];
      Array.Copy(fps[0].ToPackedRgb(), palette, take * 3);
      paletteCount = take;
    } else {
      palette = new byte[paletteCount * 3];
      for (var i = 0; i < paletteCount; ++i) {
        var v = paletteCount == 1 ? (byte)128 : (byte)(i * 255 / (paletteCount - 1));
        palette[i * 3] = v; palette[i * 3 + 1] = v; palette[i * 3 + 2] = v;
      }
    }

    var pixels = new byte[w * h];
    for (var i = 0; i < pixels.Length; ++i) pixels[i] = (byte)(i % paletteCount);

    return new() {
      Width = w, Height = h,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  private static RawImage _Gray8(int w, int h) {
    var pixels = new byte[w * h];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        pixels[y * w + x] = (byte)((x + y) * 255 / Math.Max(1, w + h - 2));
    return new() { Width = w, Height = h, Format = PixelFormat.Gray8, PixelData = pixels };
  }

  private static RawImage _Rgb24(int w, int h) {
    var pixels = new byte[w * h * 3];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var i = (y * w + x) * 3;
        pixels[i] = (byte)((x + y) * 255 / Math.Max(1, w + h - 2));
        pixels[i + 1] = (byte)(y * 255 / Math.Max(1, h - 1));
        pixels[i + 2] = (byte)(x * 255 / Math.Max(1, w - 1));
      }
    return new() { Width = w, Height = h, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Rgba32(int w, int h) {
    var pixels = new byte[w * h * 4];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var i = (y * w + x) * 4;
        pixels[i] = (byte)((x + y) * 255 / Math.Max(1, w + h - 2));
        pixels[i + 1] = (byte)(y * 255 / Math.Max(1, h - 1));
        pixels[i + 2] = (byte)(x * 255 / Math.Max(1, w - 1));
        pixels[i + 3] = 255;
      }
    return new() { Width = w, Height = h, Format = PixelFormat.Rgba32, PixelData = pixels };
  }

  private static RawImage _Argb32(int w, int h) {
    var pixels = new byte[w * h * 4];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var i = (y * w + x) * 4;
        pixels[i] = 255;                                              // A
        pixels[i + 1] = (byte)((x + y) * 255 / Math.Max(1, w + h - 2)); // R
        pixels[i + 2] = (byte)(y * 255 / Math.Max(1, h - 1));            // G
        pixels[i + 3] = (byte)(x * 255 / Math.Max(1, w - 1));            // B
      }
    return new() { Width = w, Height = h, Format = PixelFormat.Argb32, PixelData = pixels };
  }

  private static RawImage _Bgra32(int w, int h) {
    var pixels = new byte[w * h * 4];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var i = (y * w + x) * 4;
        pixels[i] = (byte)(x * 255 / Math.Max(1, w - 1));
        pixels[i + 1] = (byte)(y * 255 / Math.Max(1, h - 1));
        pixels[i + 2] = (byte)((x + y) * 255 / Math.Max(1, w + h - 2));
        pixels[i + 3] = 255;
      }
    return new() { Width = w, Height = h, Format = PixelFormat.Bgra32, PixelData = pixels };
  }
}
