using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Conformance.Recoil.Tests;

/// <summary>
/// Writes every format that can be written and asks another tool to read it back.
/// </summary>
/// <remarks>
/// A writer checked only against our own reader proves nothing a pair of matched mistakes would not
/// also prove, and most of the writers here had never been checked against anything else — the
/// hand-written pairing list covers a fraction of them. This walks the registry instead, so a format
/// gaining a writer is covered the moment it does rather than when somebody remembers to add a line.
/// <para/>
/// A format neither reference tool knows is recorded as unverifiable rather than counted as a pass:
/// those are the ones where nothing but our own reader has ever looked at the output, and pretending
/// otherwise is what this fixture exists to stop.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class WriterAcceptanceTests {

  /// <summary>Formats whose output cannot be judged this way, each for a stated reason.</summary>
  /// <remarks>
  /// Mostly extensions two unrelated formats share. A reference tool that knows the name but means
  /// something else by it turns our file down for being what it is, which says nothing about the
  /// writer — measuring the coincidence rather than the format.
  /// </remarks>
  private static readonly Dictionary<ImageFormat, string> _NotJudgeable = new() {
    [ImageFormat.Ccitt] = "a bare fax stream states no page size, so a decoder picks its own and the sizes never match",
    [ImageFormat.SunIcon] = "the reference tool reads .icon as the Microsoft one, which is a different format",
    [ImageFormat.Analyze] = "the reference tool reads .hdr as Radiance, which is a different format",
    [ImageFormat.Envi] = "the reference tool reads .hdr as Radiance, which is a different format",
    [ImageFormat.PalmPdb] = "the reference tool reads .pdb as the ImageViewer variant, which is a different record layout",
    [ImageFormat.Art] = ".art belongs to several unrelated programs and the tool means another one",
    [ImageFormat.FirstPublisher] = ".art belongs to several unrelated programs and the tool means another one",
    [ImageFormat.AliasPix] =
      "the reference tool reads one pixel more than each run states and cannot take a run of one at "
      + "all, so it rejects any picture whose neighbouring pixels differ, however it is encoded",
    [ImageFormat.MatLab] =
      "the reference tool cannot read the .mat files it writes itself, so its verdict is about its "
      + "own reader rather than about ours",
    [ImageFormat.XvThumbnail] = "the reference tool reads .xv as Khoros VIFF, which is a different format",
    [ImageFormat.PrintShop] = "the reference tool reads .psb as the large Photoshop format, which is a different one",
    [ImageFormat.TrsPix] = "the reference tool reads .pix as a Falcon or CoCoMax picture, neither of which this is",
    [ImageFormat.MasterSystemTile] = "the reference tool reads .gg as a packed Koala picture, which is a different format",
    [ImageFormat.NesChr] = "the reference tool reads .chr as Blazing Paddles vectors, which is a different format",
    [ImageFormat.NeoGeoSprite] = "the reference tool reads .neo as Atari NEOchrome, which is a different format",
    [ImageFormat.HighResST] = "the reference tool reads .hrs as an Oric screen, which is a different format",
    [ImageFormat.KofaxKfx] = "the reference tool reads .kfx as a raw Atari screen dump, which is a different format",
    [ImageFormat.Clp] = "the reference tool reads .clp as a GoDot or CoCo clip, neither of which this is",
    [ImageFormat.PcPaint] = ".pic belongs to a dozen unrelated programs and the tool means none of ours by it",
    [ImageFormat.SoftImage] = ".pic belongs to a dozen unrelated programs and the tool means none of ours by it",
    [ImageFormat.BioRadPic] = ".pic belongs to a dozen unrelated programs and the tool means none of ours by it",
    [ImageFormat.AtariSif] =
      "the reference tool reads .sif as a 2048-byte character dump shown at 256 by 32, which is "
      + "neither this layout nor this size",
    [ImageFormat.AtariPlayer] = "the reference tool reads .pmg as a Paint Magic C64 picture, which is a different format",
    [ImageFormat.C16Plus4] = "the reference tool reads .c16 as a universal image, which is a different format",
    [ImageFormat.CameraRaw] = "the reference tool reads .raw as a ZX81 or Atari screen dump, neither of which this is",
    [ImageFormat.MonoMagic] = "the reference tool reads .mon as a C64 high-resolution picture, which is a different format",
    [ImageFormat.Mrc] = "the reference tool reads .map as an Envision character set, which is a different format",
    [ImageFormat.Thomson] = "the reference tool reads .map as an Envision character set, which is a different format",
    [ImageFormat.PhotoPaint] = "the reference tool reads .cpt as the Canvas compressed picture, which is a different format",
    [ImageFormat.Cel] =
      ".cel belongs to three unrelated programs: this is the paper-doll cell of KiSS, and the tool "
      + "means the Atari ST one, which is now a format of its own here",
    [ImageFormat.AutodeskCel] =
      ".cel belongs to three unrelated programs: this is the Animator's frame, and the tool means "
      + "the Atari ST one, which is now a format of its own here",
  };

  private static IEnumerable<TestCaseData> Writable() {
    foreach (var entry in FormatRegistry.SupportedWriteFormats.OrderBy(e => e.Format.ToString()))
      yield return new TestCaseData(entry.Format).SetName($"{{m}}({entry.Format})");
  }

  [TestCaseSource(nameof(Writable))]
  public void WhatWeWrite_IsReadableBySomethingElse(ImageFormat format) {
    if (_NotJudgeable.TryGetValue(format, out var why))
      Assert.Ignore(why);

    var entry = FormatRegistry.GetEntry(format)!;

    // Every name a reference tool recognises, primary first. A format is readable if any of them
    // is: extensions are shared between unrelated formats, so a tool refusing one may simply mean
    // it knows that name as somebody else's format rather than that our bytes are wrong.
    var extensions = new List<string>();
    if (_Known(entry.PrimaryExtension))
      extensions.Add(entry.PrimaryExtension);
    extensions.AddRange(entry.AllExtensions.Where(_Known).Where(e => !extensions.Contains(e)));
    if (extensions.Count == 0)
      extensions.Add(entry.PrimaryExtension);

    var directory = Directory.CreateTempSubdirectory("writeraccept");

    try {
      string? refused = null;
      string? rejected = null;
      var written = false;

      foreach (var (width, height) in _SizesFor(entry))
      foreach (var extension in extensions) {
        var path = Path.Combine(directory.FullName, "sample" + extension);

        try {
          if (!FormatRegistry.Write(_Sample(width, height), format, new FileInfo(path))) {
            refused ??= "the registry declined to write a format it says it can";
            continue;
          }
        } catch (Exception failure) {
          refused ??= $"writing {width}x{height} threw {failure.GetType().Name}: {failure.Message}";
          continue;
        }

        written = true;

        // Both tools, not the first that has an opinion. Where two of them know an extension and
        // mean different formats by it, one turning the file down says only that it meant the other
        // format; the file is readable if either reads it.
        var verdicts = new[] { _AskRecoil(path), _AskImageMagick(path), _AskXnView(path), _AskIrfanView(path) }
          .Where(v => v != null).ToArray();

        // The service is somebody else's machine and every question costs it work, so it is asked
        // only where no installed tool has an opinion — which is exactly where its answer is worth
        // having.
        if (verdicts.Length == 0) {
          var remote = _AskTomsEditor(path);
          if (remote == null)
            continue;

          if (remote.Value.Accepted)
            return;

          rejected ??= $"at {width}x{height} as {extension}, {remote.Value.Reason}";
          continue;
        }

        if (verdicts.Any(v => v!.Value.Accepted))
          return;

        rejected ??= $"at {width}x{height} as {extension}, {string.Join(" and ", verdicts.Select(v => v!.Value.Reason))}";
      }

      if (rejected != null)
        Assert.Fail($"{format}: {rejected}");

      // Written, but nothing that knows the format was there to look at it.
      if (written)
        Assert.Ignore($"{format}: no reference tool knows any of its names, so nothing else has read this");

      Assert.Fail($"{format}: none of the sizes it declares could be written — {refused}");
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  private static bool _Known(string extension)
    => _RecoilExtensions.Value.Contains(extension)
      || _ImageMagickExtensions.Value.Contains(extension)
      || XnViewOracle.Extensions.Contains(extension)
      || IrfanViewOracle.Extensions.Contains(extension)
      || (TomsEditorOracle.Enabled && TomsEditorOracle.Extensions.Contains(extension));

  /// <summary>The sizes a format says it takes, most likely first.</summary>
  /// <remarks>
  /// Most of these formats accept one size or a handful, and writing at any other throws rather than
  /// producing a file. Trying the declared ones in turn keeps the fixture measuring what it is for —
  /// whether the bytes are readable — instead of whether a size was guessed correctly.
  /// </remarks>
  private static IEnumerable<(int Width, int Height)> _SizesFor(FormatEntry entry) {
    var seen = new HashSet<(int, int)>();

    foreach (var mode in entry.VideoModes ?? [])
    foreach (var (widths, heights) in mode.Dimensions)
    foreach (var width in _Candidates(widths, 320))
    foreach (var height in _Candidates(heights, 200)) {
      if ((long)width * height is <= 0 or > 4096L * 4096L || !seen.Add((width, height)))
        continue;

      yield return (width, height);
    }

    if (seen.Add((320, 200)))
      yield return (320, 200);
  }

  /// <summary>What to try for one dimension: the stated values, or a default where anything goes.</summary>
  private static IEnumerable<int> _Candidates(IntegerRange range, int whenUnbounded) {
    if (range.Min == 1 && range.Max == int.MaxValue) {
      yield return whenUnbounded;
      yield break;
    }

    yield return range.Min;
    if (range.Max != range.Min && range.Max < 4096)
      yield return range.Max;
  }

  /// <summary>The extensions the reference decoder says it reads, taken from its own catalogue.</summary>
  /// <remarks>
  /// Asking it and reading the answer is not enough: it reports the same decoding error whether the
  /// bytes are wrong or the format is one it has never heard of. Without telling those apart, every
  /// format it does not support counts as a writer that produces unreadable files, which is a
  /// measurement of the wrong thing entirely.
  /// </remarks>
  private static readonly Lazy<HashSet<string>> _RecoilExtensions = new(() => {
    var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var catalogue = RecoilOracle.ExecutablePath == null
      ? null
      : Path.Combine(Path.GetDirectoryName(RecoilOracle.ExecutablePath)!, "formats.xml");

    if (catalogue == null || !File.Exists(catalogue))
      return found;

    // An entry may carry attributes — <ext skip="uwp">PPH</ext> — so the opening tag is matched up
    // to its closing bracket rather than literally. Matching "<ext>" alone hid every such format in
    // the unjudgeable bucket, which is the one place a missing verdict looks like a decision.
    foreach (var line in File.ReadLines(catalogue))
    for (var at = line.IndexOf("<ext", StringComparison.Ordinal); at >= 0;
         at = line.IndexOf("<ext", at + 1, StringComparison.Ordinal)) {
      var opens = line.IndexOf('>', at);
      var end = opens < 0 ? -1 : line.IndexOf("</ext>", opens, StringComparison.Ordinal);
      if (end > opens)
        found.Add("." + line[(opens + 1)..end].ToLowerInvariant());
    }

    return found;
  });

  /// <summary>The formats the other reference tool says it reads.</summary>
  private static readonly Lazy<HashSet<string>> _ImageMagickExtensions = new(() => {
    var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    try {
      using var process = Process.Start(new ProcessStartInfo("identify", ["-list", "format"]) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
      });

      if (process == null)
        return found;

      var listing = process.StandardOutput.ReadToEnd();
      process.WaitForExit(20000);

      foreach (var line in listing.Split('\n')) {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[2].StartsWith('r'))
          continue;

        found.Add("." + parts[0].TrimEnd('*').ToLowerInvariant());
      }
    } catch (Exception) {
      // Absent is the same as knowing nothing, which the caller already handles.
    }

    return found;
  });

  private static (bool Accepted, string Reason)? _AskRecoil(string path) {
    if (RecoilOracle.ExecutablePath == null || !_RecoilExtensions.Value.Contains(Path.GetExtension(path)))
      return null;

    var (decoded, output) = RecoilOracle.TryDecode(path);

    return (decoded, $"RECOIL rejected it — {output}");
  }

  private static (bool Accepted, string Reason)? _AskTomsEditor(string path) {
    if (!TomsEditorOracle.Enabled || !TomsEditorOracle.Extensions.Contains(Path.GetExtension(path)))
      return null;

    if (TomsEditorOracle.TryDecode(path) is not { } verdict)
      return null;

    return (verdict.Decoded, $"Tom's Editor rejected it — {verdict.Output}");
  }

  private static (bool Accepted, string Reason)? _AskIrfanView(string path) {
    if (!IrfanViewOracle.Available || !IrfanViewOracle.Extensions.Contains(Path.GetExtension(path)))
      return null;

    var (decoded, output) = IrfanViewOracle.TryDecode(path);

    return (decoded, $"IrfanView rejected it — {output}");
  }

  private static (bool Accepted, string Reason)? _AskXnView(string path) {
    // Asked for every extension, not only the catalogued ones. Its catalogue is older than the
    // binary and misses formats it reads perfectly well — DNG among them — and its own message for
    // a format it cannot load tells the two cases apart better than a stale list does.
    if (XnViewOracle.ExecutablePath == null)
      return null;

    var (decoded, output) = XnViewOracle.TryDecode(path);

    // Its catalogue is the Windows build's; this one lacks some of those readers and says so with
    // one message for every format it cannot load. That is it declining to judge rather than
    // judging, and counting it as a rejection would blame the writer for the tool's build.
    if (!decoded && output.Contains("Don't know how to read", StringComparison.OrdinalIgnoreCase))
      return null;

    return (decoded, $"XnView rejected it — {output}");
  }

  private static (bool Accepted, string Reason)? _AskImageMagick(string path) {
    if (!_ImageMagickExtensions.Value.Contains(Path.GetExtension(path)))
      return null;

    try {
      using var process = Process.Start(new ProcessStartInfo("identify", ["-quiet", path]) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
      });

      if (process == null)
        return null;

      var error = process.StandardError.ReadToEnd();
      process.StandardOutput.ReadToEnd();
      process.WaitForExit(20000);

      // Both of these are it declining to judge rather than judging: the first says it has never
      // heard of the format, the second that the format states no size and it will not guess one.
      // Neither is a statement about our bytes.
      if (error.Contains("no decode delegate", StringComparison.OrdinalIgnoreCase)
          || error.Contains("must specify image size", StringComparison.OrdinalIgnoreCase))
        return null;

      return (process.ExitCode == 0, $"ImageMagick rejected it — {error.Trim()}");
    } catch (Exception) {
      return null;
    }
  }

  private static RawImage _Sample(int width, int height) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 4;
      data[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      data[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      data[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
      data[at + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }
}
