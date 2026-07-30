using System;
using FileFormat.Core;

namespace FileFormat.AtariHardInterlace;

/// <summary>In-memory representation of an Atari 8-bit Hard Interlace Picture (.hip, .hps).</summary>
/// <remarks>
/// Two screens shown on alternate television fields and averaged by the eye — the same idea as
/// APAC, but with the halves in different graphics modes rather than on different scanlines. One
/// field is Graphics 9, sixteen luminances of a single hue; the other is Graphics 10, whose pixels
/// index colour registers. Averaging a luminance ramp against freely chosen colours reaches shades
/// neither mode can show on its own.
/// <para/>
/// The two fields are stored one after the other at forty bytes a row, and the Graphics 10 one sits
/// a pixel to the left of the other — a consequence of how the mode is timed, and something the
/// picture is drawn to expect rather than something to correct.
/// </remarks>
public readonly record struct AtariHardInterlaceFile
  : IImageFormatReader<AtariHardInterlaceFile>, IImageToRawImage<AtariHardInterlaceFile> {

  /// <summary>Displayed width.</summary>
  public const int Width = 320;

  /// <summary>Bytes one field's row occupies.</summary>
  public const int RowStride = 40;

  /// <summary>Bytes one row of the picture occupies across both fields.</summary>
  public const int PairStride = RowStride * 2;

  /// <summary>Bytes of colour registers a file carries when it has room for them.</summary>
  public const int RegisterBlockSize = Atari8BitGraphics.RegisterCount;

  /// <summary>Largest picture the display can show.</summary>
  public const int MaxHeight = 240;

  /// <summary>The registers a file uses when it stores none: a plain luminance ramp.</summary>
  public static ReadOnlySpan<byte> DefaultRegisters => [0, 0, 2, 4, 6, 8, 10, 12, 14];

  static string IImageFormatMetadata<AtariHardInterlaceFile>.PrimaryExtension => ".hip";
  static string[] IImageFormatMetadata<AtariHardInterlaceFile>.FileExtensions => [".hip", ".hps"];
  static AtariHardInterlaceFile IImageFormatReader<AtariHardInterlaceFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariHardInterlaceReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariHardInterlaceFile>.VideoModes => [
    new("Hard Interlace", [(Width, IntegerRange.Any)], [256])
  ];

  /// <summary>Picture height in scanlines.</summary>
  public int Height { get; init; }

  /// <summary>The Graphics 9 field, one nibble per logical pixel.</summary>
  public byte[] Luminances { get; init; }

  /// <summary>The Graphics 10 field, one nibble per logical pixel.</summary>
  public byte[] Colors { get; init; }

  /// <summary>The nine colour registers the Graphics 10 field draws from.</summary>
  public byte[] Registers { get; init; }

  /// <summary>Reads a nibble; each covers four screen pixels, high half of a byte first.</summary>
  private static int _Nibble(ReadOnlySpan<byte> data, int rowOffset, int x) {
    if (x < 0 || x >= Width)
      return 0;

    var index = rowOffset + (x >> 3);
    if (index >= data.Length)
      return 0;

    return (x & 4) == 0 ? data[index] >> 4 : data[index] & 15;
  }

  public static RawImage ToRawImage(AtariHardInterlaceFile file) {
    var height = file.Height;
    var gtia = Atari8BitGraphics.Palette;
    var entries = Atari8BitGraphics.ExpandGr10Registers(file.Registers ?? []);
    var luminances = file.Luminances ?? [];
    var colors = file.Colors ?? [];

    var first = new byte[Width * height * 3];
    var second = new byte[Width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < Width; ++x) {
      var target = (y * Width + x) * 3;

      // Graphics 9: a luminance on a black background, one pixel right of the other field.
      _Write(first, target, gtia, _Nibble(luminances, y * RowStride, x + 1));

      // Graphics 10: an index into the sixteen entries the nine registers fill.
      _Write(second, target, gtia, entries[_Nibble(colors, y * RowStride, x - 1)]);
    }

    return new() {
      Width = Width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.BlendFrames(first, second),
    };
  }

  private static void _Write(byte[] rgb, int offset, ReadOnlySpan<byte> gtia, int color) {
    var entry = (color & 0xFF) * 3;
    rgb[offset] = gtia[entry];
    rgb[offset + 1] = gtia[entry + 1];
    rgb[offset + 2] = gtia[entry + 2];
  }
}
