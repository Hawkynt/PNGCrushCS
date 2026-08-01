using System;
using FileFormat.Core;

namespace FileFormat.ColrObjectEditor;

/// <summary>In-memory representation of a C.O.L.R. Object Editor picture (.mur).</summary>
/// <remarks>
/// Half a picture, like Technicolor Dream on the Atari 8-bit but for a different reason: the .mur
/// file is a bare four-plane bitmap and the colours live in a .pal file of the same name beside it.
/// The editor kept them apart because a palette was a thing you reused across drawings.
/// <para/>
/// That palette is in GEM's own format rather than the hardware's — six bytes a colour, three
/// big-endian words of intensity per thousand — and numbered by what each colour is for rather than
/// by where it sits in hardware, so the entries have to be permuted before a bitplane index finds
/// them.
/// </remarks>
public readonly record struct ColrObjectEditorFile
  : IImageFormatReader<ColrObjectEditorFile>, IImageToRawImage<ColrObjectEditorFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Bitplanes a pixel is spread over.</summary>
  public const int Planes = 4;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 1 << Planes;

  /// <summary>Total file size.</summary>
  public const int FileSize = Width / 8 * Planes * Height;

  /// <summary>Size of the companion palette: six bytes a colour.</summary>
  public const int PaletteFileSize = ColorCount * 6;

  static string IImageFormatMetadata<ColrObjectEditorFile>.PrimaryExtension => ".mur";
  static string[] IImageFormatMetadata<ColrObjectEditorFile>.FileExtensions => [".mur"];
  static ColrObjectEditorFile IImageFormatReader<ColrObjectEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => ColrObjectEditorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ColrObjectEditorFile>.VideoModes => [
    new("Object", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The bitmap.</summary>
  public byte[] Data { get; init; }

  /// <summary>The companion palette, or null when there was none to read.</summary>
  public byte[]? Palette { get; init; }

  public static RawImage ToRawImage(ColrObjectEditorFile file) {
    var data = file.Data ?? [];

    // Without the companion the drawing still has its shapes, and a grey ramp shows them.
    var palette = file.Palette is { } stored
      ? AtariStGraphics.ReadVdiPalette(stored, 0, ColorCount, Planes)
      : _GrayRamp();

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = AtariStGraphics.UnpackBitplanes(data, 0, Width / 8 * Planes, Planes, Width, Height),
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  private static byte[] _GrayRamp() {
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i)
      palette[i * 3] = palette[i * 3 + 1] = palette[i * 3 + 2] = (byte)(i * 255 / (ColorCount - 1));

    return palette;
  }

  // No writer. The colours live in a separate 96-byte file, and the reference decoder will not open
  // a drawing without one — but everything that writes here returns a single byte array, so there is
  // nowhere for the companion to go. A drawing on its own would be a file no tool could read, which
  // is worse than not offering to write it.
}
