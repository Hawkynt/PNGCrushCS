using System;
using FileFormat.Core;

namespace FileFormat.Paradox;

/// <summary>In-memory representation of an Atari 8-bit Paradox (.mcpp) screen.</summary>
/// <remarks>
/// Paradox gets more colours out of the hardware than a single mode E screen allows by storing
/// two half-height fields, each with its own set of playfield registers, and interleaving their
/// scanlines. The display alternates between them fast enough that the eye mixes adjacent rows —
/// so the file is two 4000-byte fields plus two four-byte colour sets, 8008 bytes in all.
/// </remarks>
public readonly record struct ParadoxFile
  : IImageFormatReader<ParadoxFile>, IImageToRawImage<ParadoxFile>,
    IImageFromRawImage<ParadoxFile>, IImageFormatWriter<ParadoxFile> {

  /// <summary>Displayed width.</summary>
  public const int DisplayWidth = 320;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = 200;

  /// <summary>Scanlines each field stores.</summary>
  public const int FieldRows = DisplayHeight / 2;

  /// <summary>Size of one field.</summary>
  public const int FieldDataSize = Atari8BitGraphics.Gr7BytesPerRow * FieldRows;

  /// <summary>Offset of the second field.</summary>
  public const int SecondFieldOffset = FieldDataSize;

  /// <summary>Offset of the colour sets.</summary>
  public const int ColorsOffset = FieldDataSize * 2;

  /// <summary>Colour bytes per field, stored PF0, PF1, PF2 then background.</summary>
  public const int ColorsPerField = 4;

  /// <summary>Total file size.</summary>
  public const int FileSize = ColorsOffset + ColorsPerField * 2;

  /// <summary>Colours one field can show.</summary>
  public const int ColorCount = 4;

  static string IImageFormatMetadata<ParadoxFile>.PrimaryExtension => ".mcpp";
  static string[] IImageFormatMetadata<ParadoxFile>.FileExtensions => [".mcpp"];
  static ParadoxFile IImageFormatReader<ParadoxFile>.FromSpan(ReadOnlySpan<byte> data) => ParadoxReader.FromSpan(data);
  static byte[] IImageFormatWriter<ParadoxFile>.ToBytes(ParadoxFile file) => ParadoxWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ParadoxFile>.VideoModes => [
    new("Interlaced Graphics 15", [(DisplayWidth, DisplayHeight)], [ColorCount * 2])
  ];

  /// <summary>Packed mode E bitmap for the even scanlines.</summary>
  public byte[] FirstField { get; init; }

  /// <summary>Packed mode E bitmap for the odd scanlines.</summary>
  public byte[] SecondField { get; init; }

  /// <summary>Colour bytes for the first field: PF0, PF1, PF2, background.</summary>
  public byte[] FirstFieldColors { get; init; }

  /// <summary>Colour bytes for the second field.</summary>
  public byte[] SecondFieldColors { get; init; }

  /// <summary>Maps a mode E pixel value to its slot in a PF0/PF1/PF2/background block.</summary>
  private static int _ColorSlot(int pixel) => pixel == 0 ? 3 : pixel - 1;

  public static RawImage ToRawImage(ParadoxFile file) {
    var gtia = Atari8BitGraphics.CreatePalette();

    // Both fields' colours share one palette; the second field's four entries follow the first.
    var palette = new byte[ColorCount * 2 * 3];
    for (var value = 0; value < ColorCount; ++value) {
      Array.Copy(gtia, file.FirstFieldColors[_ColorSlot(value)] * 3, palette, value * 3, 3);
      Array.Copy(gtia, file.SecondFieldColors[_ColorSlot(value)] * 3, palette, (ColorCount + value) * 3, 3);
    }

    var first = Atari8BitGraphics.UnpackGr7(file.FirstField, 0, FieldRows);
    var second = Atari8BitGraphics.UnpackGr7(file.SecondField, 0, FieldRows);

    var pixels = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y) {
      var fromFirst = (y & 1) == 0;
      var source = fromFirst ? first : second;
      var bias = fromFirst ? 0 : ColorCount;
      for (var x = 0; x < DisplayWidth; ++x)
        pixels[y * DisplayWidth + x] = (byte)(bias + source[(y >> 1) * Atari8BitGraphics.Gr7Width + (x >> 1)]);
    }

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = ColorCount * 2,
    };
  }

  public static ParadoxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    // Each field carries its own four colours, so they are quantized separately over the rows
    // each one actually draws.
    var (firstField, firstColors) = _EncodeField(image, evenRows: true);
    var (secondField, secondColors) = _EncodeField(image, evenRows: false);

    return new() {
      FirstField = firstField,
      SecondField = secondField,
      FirstFieldColors = firstColors,
      SecondFieldColors = secondColors,
    };
  }

  private static (byte[] Field, byte[] Colors) _EncodeField(RawImage image, bool evenRows) {
    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var fieldPixels = new byte[DisplayWidth * FieldRows * 4];
    for (var y = 0; y < FieldRows; ++y) {
      var sourceRow = y * 2 + (evenRows ? 0 : 1);
      Array.Copy(bgra.PixelData, sourceRow * DisplayWidth * 4, fieldPixels, y * DisplayWidth * 4, DisplayWidth * 4);
    }

    var quantized = ColorQuantizer.Quantize(fieldPixels, DisplayWidth * FieldRows, ColorCount);
    var gtia = Atari8BitGraphics.CreatePalette();

    var colors = new byte[ColorsPerField];
    for (var value = 0; value < ColorCount && value < quantized.Count; ++value)
      colors[_ColorSlot(value)] = Atari8BitGraphics.FindNearestColorByte(
        gtia, quantized.Palette[value * 3], quantized.Palette[value * 3 + 1], quantized.Palette[value * 3 + 2]);

    var pixels = new byte[Atari8BitGraphics.Gr7Width * FieldRows];
    for (var y = 0; y < FieldRows; ++y)
    for (var x = 0; x < Atari8BitGraphics.Gr7Width; ++x) {
      var index = quantized.Indices[y * DisplayWidth + x * 2];
      pixels[y * Atari8BitGraphics.Gr7Width + x] = (byte)(index < ColorCount ? index : 0);
    }

    return (Atari8BitGraphics.PackGr7(pixels, FieldRows), colors);
  }
}
