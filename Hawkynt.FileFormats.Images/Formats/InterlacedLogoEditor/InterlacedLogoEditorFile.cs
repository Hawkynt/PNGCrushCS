using System;
using FileFormat.Core;

namespace FileFormat.InterlacedLogoEditor;

/// <summary>In-memory representation of an Interlaced Logo Editor picture (.ile) for the Commodore 64.</summary>
/// <remarks>
/// A logo rather than a screen: 320 by 48, two four-colour bitmaps shown on alternate television
/// fields with the second displaced a pixel. Four colours interlaced against four gives ten
/// distinct ones, which for a logo is generous.
/// <para/>
/// The four colours sit in the last bytes of the file. The last of them is masked to three bits
/// rather than four — it is the border register, which on the VIC-II has only eight values.
/// </remarks>
public readonly record struct InterlacedLogoEditorFile
  : IImageFormatReader<InterlacedLogoEditorFile>, IImageToRawImage<InterlacedLogoEditorFile>,
    IImageFromRawImage<InterlacedLogoEditorFile>, IImageFormatWriter<InterlacedLogoEditorFile> {

  static byte[] IImageFormatWriter<InterlacedLogoEditorFile>.ToBytes(InterlacedLogoEditorFile file)
    => InterlacedLogoEditorWriter.ToBytes(file);

  /// <summary>Picture width.</summary>
  public const int Width = 320;

  /// <summary>Picture height.</summary>
  public const int Height = 48;

  /// <summary>Colours one field can show.</summary>
  public const int ColorCount = 4;

  /// <summary>Offset of the second field's bitmap.</summary>
  public const int SecondFieldOffset = 2;

  /// <summary>Offset of the first field's bitmap.</summary>
  public const int FirstFieldOffset = 2050;

  /// <summary>Offset of the four colour registers.</summary>
  public const int ColorsOffset = 4094;

  /// <summary>How far the second field sits from the first.</summary>
  public const int SecondFieldShift = 1;

  /// <summary>Total file size.</summary>
  public const int FileSize = 4098;

  static string IImageFormatMetadata<InterlacedLogoEditorFile>.PrimaryExtension => ".ile";
  static string[] IImageFormatMetadata<InterlacedLogoEditorFile>.FileExtensions => [".ile"];
  static InterlacedLogoEditorFile IImageFormatReader<InterlacedLogoEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => InterlacedLogoEditorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<InterlacedLogoEditorFile>.VideoModes => [
    new("Interlaced logo", [(Width, Height)], [10])
  ];

  /// <summary>The file's bytes, kept whole because both fields share one palette.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(InterlacedLogoEditorFile file) {
    var data = file.Data ?? [];
    var c64 = Commodore64Graphics.CreatePalette();

    // The fourth register is the border, which has only eight values rather than sixteen.
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var color = _At(data, ColorsOffset + i) & (i < 3 ? 15 : 7);
      c64.AsSpan(color * 3, 3).CopyTo(palette.AsSpan(i * 3));
    }

    var first = Commodore64Graphics.DecodeFourColor(data, FirstFieldOffset, 0, Width, Height, palette);
    var second = Commodore64Graphics.DecodeFourColor(data, SecondFieldOffset, SecondFieldShift, Width, Height, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Builds a logo with the same field in both halves.</summary>
  /// <remarks>
  /// The second field is displaced by one pixel, so writing the two identically means each output
  /// pixel averages the stored positions on either side of it — the result is very slightly softened
  /// horizontally rather than exactly what was written. A pair of fields averaging to a given
  /// picture is not determined by that picture.
  /// <para/>
  /// The fourth register is the border, which the machine gives only eight values rather than
  /// sixteen, so it is chosen from that half alone.
  /// </remarks>
  public static InterlacedLogoEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var c64 = Commodore64Graphics.CreatePalette();

    var quantized = ColorQuantizer.Quantize(
      PixelConverter.Convert(rgb, PixelFormat.Bgra32).PixelData, Width * Height, ColorCount);

    var registers = new byte[ColorCount];
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var entry = i * 3;
      registers[i] = _NearestC64(
        c64, i < 3 ? 16 : 8, quantized.Palette[entry], quantized.Palette[entry + 1], quantized.Palette[entry + 2]);
      c64.AsSpan(registers[i] * 3, 3).CopyTo(palette.AsSpan(entry));
    }

    // Re-match against the colours the machine actually has, not the ones the reduction asked for.
    var indices = PaletteQuantizer.Quantize(rgb.PixelData, Width, Height, palette, ColorCount);

    // Six rows of cells, forty across, eight bytes down each: the bitmap a field needs.
    var fieldSize = Height / 8 * Commodore64Graphics.Columns * 8;
    var field = Commodore64Graphics.PackFourColor(indices, 0, 0, Width, Height, fieldSize);

    var data = new byte[FileSize];
    field.CopyTo(data.AsSpan(FirstFieldOffset, fieldSize));
    field.CopyTo(data.AsSpan(SecondFieldOffset, fieldSize));
    registers.CopyTo(data.AsSpan(ColorsOffset));

    return new() { Data = data };
  }

  /// <summary>The machine colour nearest a given one, within however many it is allowed.</summary>
  private static byte _NearestC64(ReadOnlySpan<byte> c64, int available, int red, int green, int blue) {
    byte best = 0;
    var bestCost = int.MaxValue;

    for (var candidate = 0; candidate < available; ++candidate) {
      var entry = candidate * 3;
      int dr = red - c64[entry], dg = green - c64[entry + 1], db = blue - c64[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (byte)candidate;
    }

    return best;
  }
}
