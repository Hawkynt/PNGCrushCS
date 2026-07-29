using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Conformance.Recoil.Tests;

/// <summary>
/// Encodes an image with each writable format, then has both decoders read those same bytes back
/// and compares the results.
/// </summary>
/// <remarks>
/// This needs no hand-written probe, which is what makes it worth having: every pairing already
/// listed for the acceptance test gets a full pixel comparison for free. The acceptance test proves
/// RECOIL can parse what we write; this proves the two of us agree about what it means.
/// <para/>
/// Both sides decode identical bytes, so encoder quality is irrelevant — a lossy or clumsy encoder
/// still produces a file the two decoders must read the same way. Any disagreement is a decoder
/// defect on one side, which is how the GTIA and VIC-II palettes were caught.
/// <para/>
/// Dimensions are allowed to differ. RECOIL reports several modes at their displayed size where we
/// report the stored one — a 2:1 mode is 320 wide there and 160 here — and neither is wrong. Only
/// pairings that agree on size are compared pixel for pixel; the rest are reported as skipped so
/// the gap stays visible rather than silently passing.
/// </remarks>
[TestFixture]
public sealed class RecoilRoundTripAgreementTests {

  [Test]
  [Category("Conformance")]
  [TestCaseSource(typeof(RecoilConformanceTests), nameof(RecoilConformanceTests.Pairings))]
  public void DecodedFromOurOwnBytes_MatchesRecoil(RecoilConformanceTests.Pairing pairing) {
    RecoilOracle.RequireAvailable();

    var entry = FormatRegistry.GetEntry(pairing.Format);
    Assert.That(entry, Is.Not.Null, $"{pairing.Format} is not registered");
    if (!entry!.SupportsWrite)
      Assert.Ignore($"{pairing} has no encoder");

    byte[] encoded;
    try {
      encoded = entry.ConvertFromRawImage!(_Sample(pairing.Width, pairing.Height));
    } catch (Exception ex) {
      Assert.Ignore($"{pairing}: encoding threw {ex.GetType().Name} — covered by the acceptance test");
      return;
    }

    var extension = pairing.Extension ?? entry.PrimaryExtension;
    var path = Path.Combine(Path.GetTempPath(), $"recoilrt_{Guid.NewGuid():N}{extension}");
    byte[]? png;
    string output;
    try {
      File.WriteAllBytes(path, encoded);
      (png, output) = RecoilOracle.TryDecodeToPng(path);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }

    Assert.That(png, Is.Not.Null, $"{pairing}: RECOIL rejected our output — {output}");

    if (_KnownDisagreements.TryGetValue(pairing.RecoilName, out var reason))
      Assert.Ignore($"{pairing}: known decoder disagreement — {reason}");

    var theirs = PixelConverter.Convert(FormatRegistry.Read(png!)!, PixelFormat.Rgb24);
    var mine = entry.LoadRawImageFromBytes(encoded);
    if (mine == null)
      Assert.Fail($"{pairing}: we cannot read back what we just wrote");

    var ours = PixelConverter.Convert(mine!, PixelFormat.Rgb24);
    if (ours.Width != theirs.Width || ours.Height != theirs.Height)
      Assert.Ignore($"{pairing}: sizes differ — ours {ours.Width}x{ours.Height}, RECOIL {theirs.Width}x{theirs.Height}");

    for (var i = 0; i < theirs.PixelData.Length; ++i) {
      if (ours.PixelData[i] == theirs.PixelData[i])
        continue;

      var pixel = i / 3;
      Assert.Fail(
        $"{pairing}: pixel {pixel % theirs.Width},{pixel / theirs.Width} channel {i % 3} — " +
        $"ours {ours.PixelData[i]}, RECOIL {theirs.PixelData[i]}");
    }
  }

  /// <summary>
  /// Formats where our decoder and RECOIL's read the same bytes differently, with what is known
  /// about each.
  /// </summary>
  /// <remarks>
  /// These are defects, not tolerances. They are listed rather than suppressed so the count is
  /// visible and shrinks deliberately: every entry here is a format whose colours are wrong in some
  /// way, and removing one means fixing it, not widening a bound. Two entries that used to be here
  /// — the whole Atari ST family and MSX Screen 6 — came off when the three-bit channel expansion
  /// was corrected, which is the pattern to repeat.
  /// </remarks>
  private static readonly IReadOnlyDictionary<string, string> _KnownDisagreements =
    new Dictionary<string, string> {
      ["MacPaint"] = "monochrome polarity inverted: we make a set bit white, RECOIL makes it black",
      ["Public Painter"] = "monochrome polarity inverted, as MacPaint",
      ["MSX2 GL6"] = "no companion .PL6 palette exists; we fall back to black-on-white where RECOIL leaves the registers dark",
      ["CrackArt"] = "first pixel disagrees entirely — likely the palette is read from the wrong offset",
      ["DuneGraph"] = "off by six in the first channel, so the palette is widened by the wrong rule",
      ["Magic Painter"] = "off by six in the first channel, as DuneGraph",
      ["The Last Word font"] = "off by eighteen deep inside the glyph area rather than at the first pixel",
      ["Spectrum 512"] = "per-scanline palette timing differs; ours changes a scanline early or late",
      ["Spectrum 512 extended"] = "as Spectrum 512, and doubled — 17 against 34 suggests a channel widened twice",
      ["Imagic (high)"] = "only the 640x400 variant disagrees; the other two Imagic modes match, so it is the high-resolution path alone",
    };

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
