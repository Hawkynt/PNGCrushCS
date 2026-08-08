using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.ArtDirector;

/// <summary>In-memory representation of an Atari ST Art Director image (128-byte header + 32000 bytes planar data).</summary>
public readonly record struct ArtDirectorFile() : IImageFormatReader<ArtDirectorFile>, IImageToRawImage<ArtDirectorFile>, IImageFromRawImage<ArtDirectorFile>, IImageFormatWriter<ArtDirectorFile> {

  /// <summary>Header size in bytes.</summary>
  public const int HeaderSize = 128;

  /// <summary>Offset of the palette within the header.</summary>
  public const int PaletteOffset = 2;

  /// <summary>Palette size in bytes (16 words = 32 bytes).</summary>
  public const int PaletteSize = 32;

  /// <summary>Planar pixel data size.</summary>
  public const int PlanarDataSize = 32000;

  /// <summary>The exact file size: 128 + 32000 = 32128 bytes.</summary>
  public const int ExpectedFileSize = HeaderSize + PlanarDataSize;

  /// <summary>
  /// The size of the form every sample takes: the screen first, then the palettes.
  /// </summary>
  /// <remarks>
  /// All three Art Director pictures in the corpus are 32512 bytes and none was read, the reader
  /// wanting 32128 with a header in front. They put the 32000 bytes of screen at the very start and
  /// 512 after it — sixteen copies of a sixteen-colour Atari palette, which is what the program used
  /// for colour cycling. RECOIL draws the first copy and so does this.
  /// <para/>
  /// Established against RECOIL: the screen read as a four-plane Atari picture from byte nought puts
  /// every pixel in the same region as RECOIL's. Which of the eight palettes it draws with took a
  /// third sample to settle — two of them repeat one palette eight times, and the third does not, and
  /// on that one RECOIL uses the second. All three agree on the second; only two agree on the first.
  /// </remarks>
  public const int ScreenFirstFileSize = PlanarDataSize + PaletteCycleSize;

  /// <summary>Bytes after the screen: eight palettes and then 256 of settings.</summary>
  public const int PaletteCycleSize = 512;

  /// <summary>Which of the eight palettes is the one the picture is drawn with.</summary>
  public const int DisplayedPaletteIndex = 1;

  static string IImageFormatMetadata<ArtDirectorFile>.PrimaryExtension => ".art";
  static string[] IImageFormatMetadata<ArtDirectorFile>.FileExtensions => [".art"];
  static ArtDirectorFile IImageFormatReader<ArtDirectorFile>.FromSpan(ReadOnlySpan<byte> data) => ArtDirectorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ArtDirectorFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 16)])
  ];
  static byte[] IImageFormatWriter<ArtDirectorFile>.ToBytes(ArtDirectorFile file) => ArtDirectorWriter.ToBytes(file);

  /// <summary>Image width (depends on resolution).</summary>
  public int Width { get; init; } = 320;

  /// <summary>Image height (depends on resolution).</summary>
  public int Height { get; init; } = 200;

  /// <summary>Resolution: 0=low (320x200), 1=medium (640x200), 2=high (640x400).</summary>
  public short Resolution { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(ArtDirectorFile file) {

    var numPlanes = file.Resolution switch {
      0 => 4,
      1 => 2,
      2 => 1,
      _ => 4
    };

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, file.Width, file.Height, numPlanes);
    var paletteCount = Math.Min(1 << numPlanes, file.Palette.Length);
    var rgb = PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, paletteCount));

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }


  /// <summary>Encodes a picture as an Art Director picture, scaling it to 320x200 first.</summary>
  /// <remarks>
  /// An Atari ST low-resolution screen: sixteen colours, four bitplanes interleaved a word at a
  /// time, and a palette of nine-bit values. The palette is built from the picture rather than fixed
  /// by the machine, so the colours are quantised first and the indices then split into planes —
  /// the exact inverse of what <see cref="ToRawImage"/> puts back together.
  /// </remarks>
  public static ArtDirectorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.SampleTo(320, 200).EnsureFormat(PixelFormat.Indexed8);
    var quantised = ColorQuantizer.Quantize(
      PixelConverter.Convert(indexed, PixelFormat.Bgra32).PixelData, 320 * 200, 16);

    var chunky = new byte[320 * 200];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)quantised.Indices[i];

    var palette = new short[16];
    PlanarConverter.RgbToStPalette(quantised.Palette, quantised.Count).AsSpan(0, Math.Min(quantised.Count, 16)).CopyTo(palette);

    return new() {
      Width = 320,
      Height = 200,
      Resolution = 0,
      Palette = palette,
      PixelData = PlanarConverter.ChunkyToAtariSt(chunky, 320, 200, 4),
    };
  }

}
