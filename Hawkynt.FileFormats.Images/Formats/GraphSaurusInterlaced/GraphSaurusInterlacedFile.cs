using System;
using FileFormat.Core;

namespace FileFormat.GraphSaurusInterlaced;

/// <summary>In-memory representation of a Graph Saurus interlaced picture (.sri).</summary>
/// <remarks>
/// A Screen 7 picture at twice the vertical resolution, achieved by interlacing: the V9938 can show
/// 424 lines if it draws alternate halves on alternate television fields. So the file is simply
/// 512 by 424 at four bits a pixel, with no header at all — the fixed size is what identifies it.
/// <para/>
/// The palette lives in a companion <c>.PL7</c>. Read on its own the picture means what the machine
/// starts up showing, which is the sixteen colours an MSX2 boots with.
/// </remarks>
public readonly record struct GraphSaurusInterlacedFile
  : IImageFormatReader<GraphSaurusInterlacedFile>, IImageToRawImage<GraphSaurusInterlacedFile>,
    IImageFromRawImage<GraphSaurusInterlacedFile>, IImageFormatWriter<GraphSaurusInterlacedFile> {

  static byte[] IImageFormatWriter<GraphSaurusInterlacedFile>.ToBytes(GraphSaurusInterlacedFile file)
    => GraphSaurusInterlacedWriter.ToBytes(file);

  /// <summary>Picture width.</summary>
  public const int Width = 512;

  /// <summary>Picture height, doubled by interlacing.</summary>
  public const int Height = 424;

  /// <summary>Bytes one row occupies, at two pixels per byte.</summary>
  public const int BytesPerRow = Width / 2;

  /// <summary>Colours the picture can show at once.</summary>
  public const int ColorCount = 16;

  /// <summary>Total file size, which is the bitmap and nothing else.</summary>
  public const int FileSize = BytesPerRow * Height;

  static string IImageFormatMetadata<GraphSaurusInterlacedFile>.PrimaryExtension => ".sri";
  static string[] IImageFormatMetadata<GraphSaurusInterlacedFile>.FileExtensions => [".sri"];
  static GraphSaurusInterlacedFile IImageFormatReader<GraphSaurusInterlacedFile>.FromSpan(ReadOnlySpan<byte> data)
    => GraphSaurusInterlacedReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<GraphSaurusInterlacedFile>.VideoModes => [
    new("Screen 7 interlaced", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The bitmap, two pixels per byte.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(GraphSaurusInterlacedFile file) {
    var data = file.PixelData ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x)
      pixels[y * Width + x] = (byte)MsxGraphics.GetNibble(data, y * BytesPerRow, x);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = MsxGraphics.PaletteToRgb(MsxGraphics.DefaultPalette, ColorCount),
      PaletteCount = ColorCount,
    };
  }

  /// <summary>Builds a picture against the machine's fixed sixteen colours.</summary>
  /// <remarks>
  /// There is no palette in the file — the colours are the ones the machine has — so the picture is
  /// matched to those rather than to a set chosen for it.
  /// </remarks>
  public static GraphSaurusInterlacedFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var palette = MsxGraphics.PaletteToRgb(MsxGraphics.DefaultPalette, ColorCount);
    var indices = PaletteQuantizer.Quantize(rgb.PixelData, Width, Height, palette, ColorCount);

    var data = new byte[FileSize];
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x)
      MsxGraphics.SetNibble(data, y * BytesPerRow, x, indices[y * Width + x]);

    return new() { PixelData = data };
  }
}
