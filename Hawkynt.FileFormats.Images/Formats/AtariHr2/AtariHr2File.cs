using System;
using FileFormat.Core;

namespace FileFormat.AtariHr2;

/// <summary>In-memory representation of an Atari 8-bit HR2 picture (.hci, .hr2).</summary>
/// <remarks>
/// A Graphics 8 screen and a Graphics 15 screen shown on alternate television fields. Graphics 8
/// gives full 320-pixel horizontal detail but only two colours that must share a hue; Graphics 15
/// gives four freely chosen colours at half that detail. Averaged, the pair reads as a picture with
/// both — the outlines come from one field and the colour from the other.
/// <para/>
/// The two-colour field is the reason Graphics 8 is described as monochrome: its foreground takes
/// the hue of the playfield register and only the luminance of the other, so the two colours are
/// always shades of one.
/// </remarks>
public readonly record struct AtariHr2File
  : IImageFormatReader<AtariHr2File>, IImageToRawImage<AtariHr2File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Bytes one Graphics 8 row occupies: one bit per pixel.</summary>
  public const int HiresStride = Width / 8;

  /// <summary>Bytes one Graphics 15 row occupies: two bits per logical pixel, four to a byte.</summary>
  public const int ColorStride = Width / 8;

  /// <summary>Offset of the Graphics 8 field.</summary>
  public const int HiresOffset = 0;

  /// <summary>Offset of the Graphics 15 field.</summary>
  public const int ColorOffset = HiresStride * Height;

  /// <summary>Offset of the Graphics 8 field's two registers: PF2 then PF1.</summary>
  public const int HiresRegisterOffset = ColorOffset + ColorStride * Height;

  /// <summary>Offset of the Graphics 15 field's four registers: background, PF0, PF1, PF2.</summary>
  public const int ColorRegisterOffset = HiresRegisterOffset + 2;

  /// <summary>Total file size.</summary>
  public const int FileSize = ColorRegisterOffset + 4;

  static string IImageFormatMetadata<AtariHr2File>.PrimaryExtension => ".hr2";
  static string[] IImageFormatMetadata<AtariHr2File>.FileExtensions => [".hr2", ".hci"];
  static AtariHr2File IImageFormatReader<AtariHr2File>.FromSpan(ReadOnlySpan<byte> data)
    => AtariHr2Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariHr2File>.VideoModes => [
    new("HR2", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(AtariHr2File file) {
    var data = file.Data ?? [];

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(_RenderHires(data), _RenderColor(data)),
    };
  }

  private static byte[] _RenderHires(ReadOnlySpan<byte> data) {
    var gtia = Atari8BitGraphics.Palette;
    var playfield = _At(data, HiresRegisterOffset) & 254;
    var luminance = _At(data, HiresRegisterOffset + 1);

    // The foreground keeps the playfield register's hue and takes only the other register's
    // luminance, which is what confines a Graphics 8 screen to two shades of one colour.
    ReadOnlySpan<byte> colors = [(byte)playfield, (byte)((playfield & 240) | (luminance & 14))];
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var index = HiresOffset + y * HiresStride + (x >> 3);
      var bit = (_At(data, index) >> (~x & 7)) & 1;

      var entry = colors[bit] * 3;
      var target = (y * Width + x) * 3;
      rgb[target] = gtia[entry];
      rgb[target + 1] = gtia[entry + 1];
      rgb[target + 2] = gtia[entry + 2];
    }

    return rgb;
  }

  private static byte[] _RenderColor(ReadOnlySpan<byte> data) {
    var registers = new byte[4];
    for (var i = 0; i < registers.Length; ++i)
      registers[i] = _At(data, ColorRegisterOffset + i);

    return Atari8BitGraphics.DecodeGr15Frame(data, ColorOffset, ColorStride, Width, Height, registers);
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
