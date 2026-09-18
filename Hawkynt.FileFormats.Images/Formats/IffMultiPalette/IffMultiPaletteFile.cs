using System;
using FileFormat.Core;
using FileFormat.Ilbm;

namespace FileFormat.IffMultiPalette;

/// <summary>In-memory representation of an IFF ILBM image with PCHG line-by-line palette changes.</summary>
public readonly record struct IffMultiPaletteFile : IImageFormatReader<IffMultiPaletteFile>, IImageToRawImage<IffMultiPaletteFile>, IImageFromRawImage<IffMultiPaletteFile>, IImageFormatWriter<IffMultiPaletteFile> {

  /// <summary>Minimum valid file size (FORM header plus form type).</summary>
  internal const int MinFileSize = 12;

  /// <summary>Palette registers used by the writer's OCS-style Dynamic HiRes representation.</summary>
  internal const int PaletteEntries = 16;

  /// <summary>RGB bytes in one complete scanline palette.</summary>
  internal const int PaletteBytes = PaletteEntries * 3;

  /// <summary>Maximum number of per-line changes permitted by the original PCHG Copper policy.</summary>
  internal const int MaxChangesPerLine = 7;

  static string IImageFormatMetadata<IffMultiPaletteFile>.PrimaryExtension => ".mpl";
  static string[] IImageFormatMetadata<IffMultiPaletteFile>.FileExtensions => [".mpl", ".mpal"];
  static IffMultiPaletteFile IImageFormatReader<IffMultiPaletteFile>.FromSpan(ReadOnlySpan<byte> data) => IffMultiPaletteReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffMultiPaletteFile>.ToBytes(IffMultiPaletteFile file) => IffMultiPaletteWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of ILBM source bitplanes.</summary>
  public int NumPlanes { get; init; }

  /// <summary>CAMG viewport-mode bits.</summary>
  public uint ViewportMode { get; init; }

  /// <summary>One palette index/HAM code per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Initial CMAP palette as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>Expanded palette state for every scanline, sixteen RGB triplets per line.</summary>
  public byte[] ScanlinePalettes { get; init; }

  /// <summary>Original file bytes when this value came from the reader.</summary>
  /// <remarks>
  /// Kept for callers that need the source container. The writer does not replay this blob: it
  /// serializes the semantic fields above, so edits to pixels or palettes are reflected in output.
  /// </remarks>
  public byte[] RawData { get; init; }

  /// <summary>Converts this MultiPalette image to a platform-independent image.</summary>
  public static RawImage ToRawImage(IffMultiPaletteFile file) {
    if (file.PixelData is not { Length: > 0 } && file.RawData is { Length: > 0 })
      file = IffMultiPaletteReader.FromBytes(file.RawData);

    return IlbmFile.ToRawImage(new() {
      Width = file.Width,
      Height = file.Height,
      NumPlanes = file.NumPlanes,
      Compression = IlbmCompression.None,
      Masking = IlbmMasking.None,
      TransparentColor = 0,
      XAspect = 1,
      YAspect = 1,
      PageWidth = file.Width,
      PageHeight = file.Height,
      PixelData = file.PixelData,
      Palette = file.Palette,
      ScanlinePalettes = file.ScanlinePalettes,
      ViewportMode = file.ViewportMode,
    });
  }

  /// <summary>Creates a Copper-displayable 16-register PCHG picture from an arbitrary image.</summary>
  /// <remarks>
  /// PCHG's original policy permits no more than seven colour-register writes per scanline. The
  /// encoder therefore starts with a global sixteen-colour RGB444 palette and lets each later line
  /// replace at most seven registers with colours useful on that line. Pixels are then mapped to the
  /// palette that is actually available on that scanline. This is deliberately lossy for truecolour
  /// sources; pictures already representable by the resulting palettes stay exact.
  /// </remarks>
  public static IffMultiPaletteFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Width is <= 0 or > short.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(image), image.Width, $"PCHG writer requires a width between 1 and {short.MaxValue} pixels.");
    if (image.Height is <= 0 or > short.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(image), image.Height, $"PCHG writer requires a height between 1 and {short.MaxValue} pixels.");

    var source = image.EnsureFormat(PixelFormat.Bgra32);
    var width = source.Width;
    var height = source.Height;
    var pixelCount = checked(width * height);

    var global = ColorQuantizer.Quantize(source.PixelData, pixelCount, PaletteEntries);
    var currentPalette = _ToRgb444Palette(global.Palette, global.Count);
    var indices = new byte[pixelCount];
    var scanlinePalettes = new byte[checked(height * PaletteBytes)];
    var lineBytes = checked(width * 4);
    var line = new byte[lineBytes];

    for (var y = 0; y < height; ++y) {
      source.PixelData.AsSpan(y * lineBytes, lineBytes).CopyTo(line);
      if (y != 0)
        _AdaptPalette(line, width, currentPalette);

      var mapped = ColorQuantizer.MapToPalette(line, width, currentPalette);
      for (var x = 0; x < width; ++x)
        indices[y * width + x] = (byte)mapped.Indices[x];

      currentPalette.CopyTo(scanlinePalettes, y * PaletteBytes);
    }

    var initialPalette = new byte[PaletteBytes];
    scanlinePalettes.AsSpan(0, PaletteBytes).CopyTo(initialPalette);

    return new() {
      Width = width,
      Height = height,
      NumPlanes = 4,
      ViewportMode = 0,
      PixelData = indices,
      Palette = initialPalette,
      ScanlinePalettes = scanlinePalettes,
      RawData = [],
    };
  }

  /// <summary>Moves the current palette toward the line's ideal palette without exceeding seven writes.</summary>
  private static void _AdaptPalette(byte[] lineBgra, int width, byte[] currentPalette) {
    var ideal = ColorQuantizer.Quantize(lineBgra, width, PaletteEntries);
    var desired = _ToRgb444Palette(ideal.Palette, ideal.Count);

    var frequencies = new int[ideal.Count];
    foreach (var index in ideal.Indices)
      if ((uint)index < (uint)frequencies.Length)
        ++frequencies[index];

    // Registers already carrying a desired colour are worth keeping. Only one duplicate register is
    // protected; duplicate colours do not buy another representable colour and are good replacement
    // candidates.
    var protectedRegisters = new bool[PaletteEntries];
    for (var i = 0; i < ideal.Count; ++i) {
      var register = _FindColour(currentPalette, desired, i);
      if (register >= 0)
        protectedRegisters[register] = true;
    }

    // Estimate the value of every current register before changing anything: the least-used
    // non-protected register is sacrificed first.
    var currentMapping = ColorQuantizer.MapToPalette(lineBgra, width, currentPalette);
    var usage = new int[PaletteEntries];
    foreach (var index in currentMapping.Indices)
      if ((uint)index < PaletteEntries)
        ++usage[index];

    var replacementRegisters = new int[PaletteEntries];
    var replacementCount = 0;
    for (var register = 0; register < PaletteEntries; ++register)
      if (!protectedRegisters[register])
        replacementRegisters[replacementCount++] = register;

    for (var i = 1; i < replacementCount; ++i) {
      var register = replacementRegisters[i];
      var at = i;
      while (at > 0 && usage[replacementRegisters[at - 1]] > usage[register]) {
        replacementRegisters[at] = replacementRegisters[at - 1];
        --at;
      }
      replacementRegisters[at] = register;
    }

    // Visit desired colours most-frequent-first. Median-cut's ordering is an implementation detail;
    // the frequency is what tells us which seven register writes buy the most pixels on this line.
    var candidates = new int[ideal.Count];
    for (var i = 0; i < candidates.Length; ++i)
      candidates[i] = i;
    for (var i = 1; i < candidates.Length; ++i) {
      var candidate = candidates[i];
      var at = i;
      while (at > 0 && frequencies[candidates[at - 1]] < frequencies[candidate]) {
        candidates[at] = candidates[at - 1];
        --at;
      }
      candidates[at] = candidate;
    }

    var replacementAt = 0;
    var changes = 0;
    foreach (var candidate in candidates) {
      if (changes == MaxChangesPerLine || replacementAt == replacementCount)
        break;
      if (_FindColour(currentPalette, desired, candidate) >= 0)
        continue;

      var register = replacementRegisters[replacementAt++];
      desired.AsSpan(candidate * 3, 3).CopyTo(currentPalette.AsSpan(register * 3, 3));
      ++changes;
    }
  }

  private static byte[] _ToRgb444Palette(byte[] palette, int count) {
    var result = new byte[PaletteBytes];
    count = Math.Min(Math.Min(count, PaletteEntries), palette.Length / 3);
    for (var i = 0; i < count; ++i) {
      result[i * 3] = _ToRgb4(palette[i * 3]);
      result[i * 3 + 1] = _ToRgb4(palette[i * 3 + 1]);
      result[i * 3 + 2] = _ToRgb4(palette[i * 3 + 2]);
    }
    return result;
  }

  private static int _FindColour(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> palette, int entry) {
    var at = entry * 3;
    if (at + 2 >= palette.Length)
      return -1;

    for (var i = 0; i < PaletteEntries; ++i) {
      var p = i * 3;
      if (haystack[p] == palette[at] && haystack[p + 1] == palette[at + 1] && haystack[p + 2] == palette[at + 2])
        return i;
    }

    return -1;
  }

  /// <summary>Rounds an eight-bit channel to the RGB444 value that PCHG stores, expanded back to eight bits.</summary>
  private static byte _ToRgb4(byte value) => (byte)(((value + 8) / 17) * 17);
}
