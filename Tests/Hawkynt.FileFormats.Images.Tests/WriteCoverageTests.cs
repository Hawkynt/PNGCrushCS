using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// <see cref="FormatRegistry.Write"/> takes an arbitrary <see cref="RawImage"/>, so a writer that
/// only accepts its own internal layout is unusable through the public API. These tests pin the
/// coverage so it can't silently regress.
/// </summary>
[TestFixture]
public sealed class WriteCoverageTests {

  /// <summary>Formats the CLI and README present as first-class write targets.</summary>
  private static readonly ImageFormat[] _HeadlineFormats = [
    ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Gif, ImageFormat.Tiff, ImageFormat.Bmp,
    ImageFormat.Tga, ImageFormat.Pcx, ImageFormat.WebP, ImageFormat.Qoi,
  ];

  /// <summary>Encoding must succeed from any source layout, not just one privileged one.</summary>
  private static readonly PixelFormat[] _SourceFormats = [
    PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Indexed8,
  ];

  private static RawImage _Sample(PixelFormat format, int width = 32, int height = 32) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      data[o] = (byte)(x * 8);
      data[o + 1] = (byte)(y * 8);
      data[o + 2] = (byte)((x + y) % 2 == 0 ? 255 : 0);
      data[o + 3] = 255;
    }

    var bgra = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = data };
    return format == PixelFormat.Bgra32 ? bgra : PixelConverter.Convert(bgra, format);
  }

  [Test]
  [Category("Unit")]
  public void Write_HeadlineFormats_AcceptEverySourcePixelFormat() {
    var failures = new List<string>();

    foreach (var format in _HeadlineFormats)
    foreach (var source in _SourceFormats) {
      try {
        var bytes = FormatRegistry.Write(_Sample(source), format);
        if (bytes is not { Length: > 0 })
          failures.Add($"{format} from {source}: produced no bytes");
      } catch (Exception ex) {
        failures.Add($"{format} from {source}: {ex.GetType().Name}: {ex.Message}");
      }
    }

    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
  }

  [Test]
  [Category("Unit")]
  public void Write_HeadlineFormats_RoundTripToTheSameDimensions() {
    foreach (var format in _HeadlineFormats) {
      var source = _Sample(PixelFormat.Rgba32);
      var bytes = FormatRegistry.Write(source, format);
      Assert.That(bytes, Is.Not.Null.And.Not.Empty, $"{format}: encode failed");

      var back = FormatRegistry.Read(bytes!);
      Assert.That(back, Is.Not.Null, $"{format}: could not read back its own output");
      Assert.That((back!.Width, back.Height), Is.EqualTo((source.Width, source.Height)),
        $"{format}: dimensions changed across a round-trip");
    }
  }

  /// <summary>Common native resolutions for formats whose declared dimensions don't pin a usable size.</summary>
  private static readonly (int W, int H)[] _FallbackSizes = [
    (64, 64), (320, 200), (256, 192), (320, 192), (160, 200), (256, 240),
    (512, 342), (640, 400), (640, 200), (32, 32), (8, 8), (16, 16), (128, 128),
  ];

  /// <summary>Every geometry worth trying for a format, its own declared modes first.</summary>
  private static IEnumerable<(int W, int H)> _GeometriesFor(FormatEntry entry) {
    var seen = new HashSet<(int, int)>();

    foreach (var mode in entry.VideoModes ?? [])
    foreach (var (width, height) in mode.Dimensions) {
      var size = (width.SnapToValid(64), height.SnapToValid(64));
      if (seen.Add(size))
        yield return size;
    }

    foreach (var size in _FallbackSizes)
      if (seen.Add(size))
        yield return size;
  }

  /// <summary>
  /// Every format the registry says it can write must write a file and read that file back as a
  /// picture something can draw.
  /// </summary>
  /// <remarks>
  /// This is the test behind the Write column of the package README. A tick there is a promise that
  /// <see cref="FormatRegistry.Write(RawImage, ImageFormat, FileInfo)"/> turns an arbitrary picture
  /// into a file, and the promise is only worth having if the file is one this package can open
  /// again — a writer whose own reader refuses its output has written nothing useful.
  /// <para/>
  /// It goes through a file rather than through bytes on purpose. Nine formats keep something beside
  /// the picture — a .hd stating the size, a detached .img of voxels, a palette — and only the
  /// file-taking path writes those companions. Judged on bytes alone those formats look broken when
  /// what is actually wrong is the question.
  /// <para/>
  /// The list of permitted failures is empty and is meant to stay empty. A format that cannot hold
  /// up its end does not belong in the Write column: either it gains a working writer or it loses
  /// the registration, and both of those are visible changes rather than a quietly growing ledger.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void Write_EveryWritableFormat_ReadsItsOwnFileBack() {
    var writable = FormatRegistry.AllFormats.Where(e => e.SupportsWrite)
      .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    Assert.That(writable, Is.Not.Empty, "the registry registered no writers at all");

    var failures = new List<string>();
    var root = Directory.CreateTempSubdirectory("writereadback");

    try {
      foreach (var entry in writable) {
        var directory = Directory.CreateDirectory(Path.Combine(root.FullName, entry.Format.ToString()));
        var extension = string.IsNullOrEmpty(entry.PrimaryExtension) ? ".bin" : entry.PrimaryExtension;

        FileInfo? written = null;
        var refusal = "no geometry was accepted";
        foreach (var (width, height) in _GeometriesFor(entry)) {
          if (width <= 0 || height <= 0 || (long)width * height > _MAXIMUM_TRIAL_PIXELS)
            continue;

          var candidate = new FileInfo(Path.Combine(directory.FullName, "sample" + extension));
          try {
            if (FormatRegistry.Write(_Sample(PixelFormat.Rgba32, width, height), entry.Format, candidate)) {
              candidate.Refresh();
              if (candidate is { Exists: true, Length: > 0 }) {
                written = candidate;
                break;
              }
            }

            refusal = "the registry declined to write a format it says it can";
          } catch (Exception failure) {
            refusal = $"{width}x{height} threw {failure.GetType().Name}: {failure.Message}";
          }
        }

        if (written == null) {
          failures.Add($"{entry.Name}: wrote nothing — {refusal}");
          continue;
        }

        // The entry's own reader, not detection: extensions are shared between unrelated formats,
        // so detecting would sometimes hand the file to a different reader and blame this one.
        RawImage? read;
        try {
          read = entry.LoadRawImageOrThrow is { } strict ? strict(written) : entry.LoadRawImage(written);
        } catch (Exception failure) {
          failures.Add($"{entry.Name}: could not read its own file back — {failure.GetType().Name}: {failure.Message}");
          continue;
        }

        if (read == null) {
          failures.Add($"{entry.Name}: reading its own file back returned null");
          continue;
        }

        try {
          if (read.Format != PixelFormat.Rgba32)
            PixelConverter.Convert(read, PixelFormat.Rgba32);
        } catch (Exception failure) {
          failures.Add(
            $"{entry.Name}: read back {read.Width}x{read.Height} {read.Format} with {read.PaletteCount} palette "
            + $"entries, which will not convert to pixels — {failure.GetType().Name}: {failure.Message}");
        }
      }
    } finally {
      try { root.Delete(recursive: true); } catch (IOException) { /* the temp tree is the OS's problem now */ }
    }

    Assert.That(failures, Is.Empty,
      $"{failures.Count} of {writable.Count} formats claim they can write and cannot read the result back:"
      + Environment.NewLine + string.Join(Environment.NewLine, failures));
  }

  /// <summary>An arbitrary picture stays inside this many pixels, so a declared "any size" mode cannot
  /// ask the sweep for a gigabyte.</summary>
  private const long _MAXIMUM_TRIAL_PIXELS = 4_000_000;

  /// <summary>
  /// Formats that store the pixels they were given, with the number of colours each can hold —
  /// zero meaning it stores colour directly and has no ceiling.
  /// </summary>
  /// <remarks>
  /// GIF is exactly as lossless as the others and only within 256 colours, so it is asked a question
  /// it can answer. Handing it a truecolour sample and calling the quantisation a defect would be
  /// testing the format's definition rather than this package's writer.
  /// </remarks>
  private static readonly (ImageFormat Format, int MaximumColors)[] _LosslessFormats = [
    (ImageFormat.Png, 0), (ImageFormat.Qoi, 0), (ImageFormat.Bmp, 0), (ImageFormat.Tga, 0),
    (ImageFormat.Pcx, 0), (ImageFormat.Tiff, 0), (ImageFormat.Farbfeld, 0),
    (ImageFormat.Gif, 256),
  ];

  /// <summary>A picture of exactly <paramref name="colors"/> distinct opaque colours.</summary>
  private static RawImage _BoundedColorSample(int colors, int width = 32, int height = 32) {
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      // One colour per index, spread so no two indices collide after any channel rounding.
      var index = (y * width + x) % colors;
      var offset = (y * width + x) * 4;
      data[offset] = (byte)index;
      data[offset + 1] = (byte)(255 - index);
      data[offset + 2] = (byte)((index * 7) & 0xFF);
      data[offset + 3] = 255;
    }

    var bgra = new RawImage { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = data };
    return PixelConverter.Convert(bgra, PixelFormat.Rgba32);
  }

  /// <summary>A lossless writer has to give back the picture it was handed, not one the same size.</summary>
  /// <remarks>
  /// Checking dimensions alone passes a writer that stores a blank field of the right shape, which
  /// is the failure worth catching here — the ones that dropped pixels still reported the geometry
  /// correctly.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void Write_LosslessFormats_ReadBackTheSamePixels() {
    var failures = new List<string>();

    foreach (var (format, maximumColors) in _LosslessFormats) {
      var source = maximumColors == 0 ? _Sample(PixelFormat.Rgba32) : _BoundedColorSample(maximumColors);
      var bytes = FormatRegistry.Write(source, format);
      if (bytes is not { Length: > 0 }) {
        failures.Add($"{format}: encode produced nothing");
        continue;
      }

      var entry = FormatRegistry.GetEntry(format)!;
      var read = entry.LoadRawImageFromBytes(bytes);
      if (read == null) {
        failures.Add($"{format}: could not read its own output back");
        continue;
      }

      if ((read.Width, read.Height) != (source.Width, source.Height)) {
        failures.Add($"{format}: wrote {source.Width}x{source.Height} and read {read.Width}x{read.Height}");
        continue;
      }

      var actual = read.Format == PixelFormat.Rgba32 ? read : PixelConverter.Convert(read, PixelFormat.Rgba32);
      var differing = 0;
      for (var i = 0; i < source.PixelData.Length && i < actual.PixelData.Length; ++i)
        if (source.PixelData[i] != actual.PixelData[i])
          ++differing;

      if (differing != 0)
        failures.Add($"{format}: {differing} of {source.PixelData.Length} bytes changed across a round-trip");
    }

    Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
  }

  [Test]
  [Category("Unit")]
  public void Write_ReadOnlyFormat_ReturnsNullRatherThanThrowing() {
    var readOnly = FormatRegistry.AllFormats.FirstOrDefault(e => !e.SupportsWrite);
    if (readOnly == null)
      Assert.Pass("every registered format supports writing");

    Assert.That(FormatRegistry.Write(_Sample(PixelFormat.Rgba32), readOnly!.Format), Is.Null);
  }
}
