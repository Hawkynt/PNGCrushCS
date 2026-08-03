using System;
using FileFormat.Core;

namespace FileFormat.GemImg;

/// <summary>In-memory representation of a GEM IMG raster image.</summary>
public readonly record struct GemImgFile : IImageFormatReader<GemImgFile>, IImageToRawImage<GemImgFile>, IImageFromRawImage<GemImgFile>, IImageFormatWriter<GemImgFile> {

  static string IImageFormatMetadata<GemImgFile>.PrimaryExtension => ".img";
  static string[] IImageFormatMetadata<GemImgFile>.FileExtensions => [".img"];
  static GemImgFile IImageFormatReader<GemImgFile>.FromSpan(ReadOnlySpan<byte> data) => GemImgReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<GemImgFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])
  ];
  static byte[] IImageFormatWriter<GemImgFile>.ToBytes(GemImgFile file) => GemImgWriter.ToBytes(file);
  public int Version { get; init; }
  public int Width { get; init; }
  public int Height { get; init; }
  public int NumPlanes { get; init; }
  public int PatternLength { get; init; }
  public int PixelWidth { get; init; }
  public int PixelHeight { get; init; }
  public byte[] PixelData { get; init; }

  /// <summary>Converts this GEM IMG file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(GemImgFile file) {

    var chunky = PlanarConverter.NonInterleavedPlanarToChunky(file.PixelData, file.Width, file.Height, file.NumPlanes);
    var paletteCount = Math.Min(1 << file.NumPlanes, 256);
    var palette = _BuildPalette(paletteCount);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>The sixteen colours VDI starts with, which is what a GEM picture without a palette means.</summary>
  private static readonly byte[] _VdiPalette = [
    255, 255, 255,  0,   0,   0,    255, 0,   0,    0,   255, 0,
    0,   0,   255,  0,   255, 255,  255, 255, 0,    255, 0,   255,
    192, 192, 192,  128, 128, 128,  255, 128, 128,  128, 255, 128,
    128, 128, 255,  128, 255, 255,  255, 255, 128,  255, 128, 255,
  ];

  /// <summary>
  /// Gives each index a colour of its own.
  /// </summary>
  /// <remarks>
  /// This used to be white, black, and an even grey ramp over what was left — which put index fifteen
  /// at the same black as index one, so a four-plane picture came back with two of its colours merged
  /// into one. The picture itself was right; two of its regions simply could not be told apart.
  /// <para/>
  /// A GEM IMG carries no palette, so what the indices mean is the reader's choice, and the tools
  /// choose differently: XnView draws this sample in muted colours where RECOIL draws it in primaries.
  /// They agree on which pixels belong together and nothing else. RECOIL's are the VDI defaults, which
  /// is the documented convention and the one taken here.
  /// </remarks>
  private static byte[] _BuildPalette(int paletteCount) {
    var palette = new byte[paletteCount * 3];
    var shared = Math.Min(paletteCount, _VdiPalette.Length / 3);
    _VdiPalette.AsSpan(0, shared * 3).CopyTo(palette);

    // Beyond the sixteen there is no convention at all, so the rest get a ramp that repeats nothing.
    for (var i = shared; i < paletteCount; ++i) {
      palette[i * 3] = (byte)(i * 255 / (paletteCount - 1));
      palette[i * 3 + 1] = (byte)(255 - i * 255 / (paletteCount - 1));
      palette[i * 3 + 2] = (byte)i;
    }

    return palette;
  }

  /// <summary>Creates a <see cref="GemImgFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static GemImgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);

    var numPlanes = Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(image.PaletteCount, 2))));
    var planar = PlanarConverter.ChunkyToNonInterleavedPlanar(image.PixelData, image.Width, image.Height, numPlanes);

    return new() {
      Version = 1,
      Width = image.Width,
      Height = image.Height,
      NumPlanes = numPlanes,
      PatternLength = 2,
      PixelWidth = 1,
      PixelHeight = 1,
      PixelData = planar,
    };
  }
}
