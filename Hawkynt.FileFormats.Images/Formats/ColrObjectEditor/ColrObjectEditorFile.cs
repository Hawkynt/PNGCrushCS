using System;
using System.IO;
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
  : IImageFormatReader<ColrObjectEditorFile>, IImageToRawImage<ColrObjectEditorFile>,
    IImageFromRawImage<ColrObjectEditorFile>, IImageFormatWriter<ColrObjectEditorFile> {

  static byte[] IImageFormatWriter<ColrObjectEditorFile>.ToBytes(ColrObjectEditorFile file)
    => ColrObjectEditorWriter.ToBytes(file);

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

  /// <summary>Reads the drawing and the palette beside it, which it cannot be shown without.</summary>
  static ColrObjectEditorFile IImageFormatReader<ColrObjectEditorFile>.FromFile(FileInfo file)
    => ColrObjectEditorReader.FromFile(file);
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

  /// <summary>The extension the colours live under, beside the drawing.</summary>
  public const string CompanionExtension = ".pal";

  /// <summary>Builds a drawing. The colours it needs are written beside it, not in it.</summary>
  /// <remarks>
  /// This format keeps its sixteen colours in a file of its own, and nothing will open the drawing
  /// without one — so a writer that emits only the drawing produces something no tool can read. The
  /// companion goes out through the write that names a file; taking the bytes alone is not enough
  /// for this format, which is the whole reason that path exists.
  /// </remarks>
  public static ColrObjectEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var quantized = ColorQuantizer.Quantize(
      PixelConverter.Convert(rgb, PixelFormat.Bgra32).PixelData, Width * Height, ColorCount);

    var indices = new byte[Width * Height];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)quantized.Indices[i];

    // The palette is carried on the file rather than recomputed when the companion is written: the
    // reduction is a choice, not a derivation, and running it twice can settle differently — which
    // would leave the palette beside the drawing describing colours the drawing does not use.
    return new() {
      Data = AtariStGraphics.PackBitplanes(indices, Width / 8 * Planes, Planes, Width, Height),
      Palette = quantized.Palette,
    };
  }

  /// <summary>Writes the palette file the drawing cannot be read without.</summary>
  static void IImageFormatWriter<ColrObjectEditorFile>.WriteCompanions(ColrObjectEditorFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);

    var vdi = new byte[PaletteFileSize];
    AtariStGraphics.WriteVdiPalette(file.Palette ?? [], ColorCount, Planes, vdi);

    File.WriteAllBytes(Path.ChangeExtension(target.FullName, CompanionExtension), vdi);
  }
}
