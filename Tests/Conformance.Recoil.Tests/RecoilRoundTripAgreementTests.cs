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

    var sample = _Sample(pairing.Width, pairing.Height);

    byte[] encoded;
    try {
      encoded = entry.ConvertFromRawImage!(sample);
    } catch (Exception ex) {
      Assert.Ignore($"{pairing}: encoding threw {ex.GetType().Name} — covered by the acceptance test");
      return;
    }

    var extension = pairing.Extension ?? entry.PrimaryExtension;
    var path = Path.Combine(Path.GetTempPath(), $"recoilrt_{Guid.NewGuid():N}{extension}");
    byte[]? png;
    string output;
    RawImage? readBack;
    try {
      // Through the write that names a file, so a format keeping its palette beside the picture puts
      // that there too rather than leaving the reference decoder without it.
      FormatRegistry.Write(sample, pairing.Format, new FileInfo(path));
      (png, output) = RecoilOracle.TryDecodeToPng(path);

      // And read back from the file for the same reason: by bytes alone, a drawing whose colours
      // live beside it comes back in the grey ramp its reader falls back on, and the comparison
      // would be measuring that rather than the writer.
      readBack = entry.LoadRawImage(new FileInfo(path));
    } finally {
      foreach (var written in Directory.GetFiles(Path.GetTempPath(), Path.GetFileNameWithoutExtension(path) + ".*"))
        try { File.Delete(written); } catch { /* best effort */ }
    }

    Assert.That(png, Is.Not.Null, $"{pairing}: RECOIL rejected our output — {output}");

    if (_KnownDisagreements.TryGetValue(pairing.RecoilName, out var reason))
      Assert.Ignore($"{pairing}: known decoder disagreement — {reason}");

    var theirs = PixelConverter.Convert(FormatRegistry.Read(png!)!, PixelFormat.Rgb24);
    // A couple of formats put the thing that decides how to read them in the file name rather than
    // the file, and RECOIL dispatches on the extension too — so reading by bytes alone would be
    // comparing two different questions.
    var mine = extension.ToLowerInvariant() == ".stp"
      ? FileFormat.MsxGl6.MsxGl6File.ToRawImage(
          FileFormat.MsxGl6.MsxGl6Reader.FromSpan(encoded, FileFormat.MsxGl6.MsxGl6Kind.Stamp))
      : readBack ?? entry.LoadRawImageFromBytes(encoded);
    if (mine == null)
      Assert.Fail($"{pairing}: we cannot read back what we just wrote");

    var ours = PixelConverter.Convert(mine!, PixelFormat.Rgb24);

    // A wide-pixel mode is one picture drawn two ways. The C64's multicolour modes are 160 pixels
    // across and RECOIL hands them back at 320, each pixel twice, because that is the shape of the
    // screen they are shown on; we hand back the 160 the format actually stores. Comparing those
    // through the doubling is the same comparison, and skipping it instead — which is what happened
    // before — left every multicolour format checked only for "RECOIL agrees this is a file".
    if (ours.Height != theirs.Height || theirs.Width % ours.Width != 0)
      Assert.Ignore($"{pairing}: sizes differ — ours {ours.Width}x{ours.Height}, RECOIL {theirs.Width}x{theirs.Height}");

    var scale = theirs.Width / ours.Width;
    for (var i = 0; i < theirs.PixelData.Length; ++i) {
      var channel = i % 3;
      var pixel = i / 3;
      var x = pixel % theirs.Width / scale;
      var y = pixel / theirs.Width;
      var at = (y * ours.Width + x) * 3 + channel;

      if (ours.PixelData[at] == theirs.PixelData[i])
        continue;

      Assert.Fail(
        $"{pairing}: pixel {pixel % theirs.Width},{y} channel {channel} — " +
        $"ours {ours.PixelData[at]}, RECOIL {theirs.PixelData[i]}");
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
      ["CrackArt"] = "our writer always compresses, and our RLE and the reference's disagree — the two decoders read the same bytes to different pictures, so the fault is in the compressor rather than anything on the read path",
      ["Spectrum 512 extended"] = "our encoder fills sixteen of the forty-eight entries a line holds, so the two zones past the first are unwritten; the decoder reads all three correctly",
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
