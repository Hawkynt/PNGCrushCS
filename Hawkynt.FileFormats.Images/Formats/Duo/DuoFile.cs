using System;
using FileFormat.Core;

namespace FileFormat.Duo;

/// <summary>In-memory representation of a Duo picture (.du1, .duo) for the Atari ST.</summary>
/// <remarks>
/// A sixteen-entry palette, then two low-resolution bitmaps of the same size. The ST cannot show
/// more than sixteen colours at once, so Duo shows the two pictures on alternate television frames
/// and lets the eye average them — the same trick McPainter plays on the Atari 8-bit, and it buys
/// the same thing: colours the hardware has no register for.
/// <para/>
/// The averaged result is the picture, so this decodes to RGB. It is also wider and taller than a
/// normal ST screen, at 416x273: the program drives the display beyond its usual borders, which is
/// why the size is fixed here rather than read from a header the file does not have.
/// </remarks>
public readonly record struct DuoFile
  : IImageFormatReader<DuoFile>, IImageToRawImage<DuoFile>,
    IImageFromRawImage<DuoFile>, IImageFormatWriter<DuoFile> {

  static byte[] IImageFormatWriter<DuoFile>.ToBytes(DuoFile file) => DuoWriter.ToBytes(file);

  /// <summary>Picture width, past the normal border.</summary>
  public const int Width = 416;

  /// <summary>Picture height, past the normal border.</summary>
  public const int Height = 273;

  /// <summary>Bitplanes a low-resolution screen uses.</summary>
  public const int Planes = 4;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 1 << Planes;

  /// <summary>Size of the palette: one big-endian word per entry.</summary>
  public const int PaletteSize = ColorCount * 2;

  /// <summary>Bytes one bitmap occupies: whole 16-pixel groups across all four planes.</summary>
  public const int FrameSize = ((Width + 15) >> 4 << 3) * Height;

  /// <summary>Offset of the first bitmap.</summary>
  public const int FirstFrameOffset = PaletteSize;

  /// <summary>Offset of the second bitmap.</summary>
  public const int SecondFrameOffset = FirstFrameOffset + FrameSize;

  /// <summary>Total file size.</summary>
  public const int FileSize = SecondFrameOffset + FrameSize;

  static string IImageFormatMetadata<DuoFile>.PrimaryExtension => ".duo";
  static string[] IImageFormatMetadata<DuoFile>.FileExtensions => [".duo", ".du1"];
  static DuoFile IImageFormatReader<DuoFile>.FromSpan(ReadOnlySpan<byte> data) => DuoReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DuoFile>.VideoModes => [
    new("Duo", [(Width, Height)], [ColorCount * ColorCount])
  ];

  /// <summary>The file's bytes, kept whole because both bitmaps share one palette.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(DuoFile file) {
    var data = file.Data ?? [];
    var palette = AtariStGraphics.ReadPalette(data, 0, ColorCount);

    var first = _RenderFrame(data, FirstFrameOffset, palette);
    var second = _RenderFrame(data, SecondFrameOffset, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.BlendFrames(first, second),
    };
  }

  /// <summary>Unpacks one bitplane frame straight to RGB.</summary>
  private static byte[] _RenderFrame(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> palette) {
    var planar = offset + FrameSize <= data.Length ? data.Slice(offset, FrameSize) : default;
    var indices = planar.IsEmpty ? new byte[Width * Height] : PlanarConverter.AtariStToChunky(planar, Width, Height, Planes);

    return AtariStGraphics.ToRgb(indices, palette, ColorCount);
  }

  /// <summary>Builds a picture, putting the same field in both halves.</summary>
  /// <remarks>
  /// The format shows two fields in quick succession and the eye averages them, which is how sixteen
  /// hardware colours become more. Going the other way, a single picture has no second field to
  /// recover — so both hold the same one, whose average is itself. That gives back exactly what was
  /// written and uses none of the extra colour the trick was for, which is the honest trade rather
  /// than inventing a difference between the fields.
  /// </remarks>
  public static DuoFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var quantized = ColorQuantizer.Quantize(
      PixelConverter.Convert(rgb, PixelFormat.Bgra32).PixelData, Width * Height, ColorCount);

    var indices = new byte[Width * Height];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)quantized.Indices[i];

    var data = new byte[FileSize];

    var stPalette = PlanarConverter.RgbToStPalette(quantized.Palette, ColorCount);
    for (var i = 0; i < ColorCount; ++i) {
      data[i * 2] = (byte)(stPalette[i] >> 8);
      data[i * 2 + 1] = (byte)stPalette[i];
    }

    var planar = PlanarConverter.ChunkyToAtariSt(indices, Width, Height, Planes);
    planar.AsSpan(0, Math.Min(planar.Length, FrameSize)).CopyTo(data.AsSpan(FirstFrameOffset));
    planar.AsSpan(0, Math.Min(planar.Length, FrameSize)).CopyTo(data.AsSpan(SecondFrameOffset));

    return new() { Data = data };
  }
}
