using System;
using FileFormat.Core;

namespace FileFormat.DuoMedium;

/// <summary>In-memory representation of a medium-resolution Duo picture (.du2) for the Atari ST.</summary>
/// <remarks>
/// The same two-frames-averaged trick as the low-resolution Duo, in the ST's medium mode: twice the
/// horizontal resolution for a quarter of the colours. Four colours per frame become at most ten
/// distinct ones once the frames are averaged, which is the whole point of showing two.
/// <para/>
/// Medium resolution halves the vertical resolution as well, so every stored row is drawn on two
/// scanlines and a 273-row picture is 546 scanlines tall.
/// </remarks>
public readonly record struct DuoMediumFile
  : IImageFormatReader<DuoMediumFile>, IImageToRawImage<DuoMediumFile>,
    IImageFromRawImage<DuoMediumFile>, IImageFormatWriter<DuoMediumFile> {

  static byte[] IImageFormatWriter<DuoMediumFile>.ToBytes(DuoMediumFile file) => DuoMediumWriter.ToBytes(file);

  /// <summary>Picture width.</summary>
  public const int Width = 832;

  /// <summary>Stored rows.</summary>
  public const int StoredHeight = 273;

  /// <summary>Displayed scanlines; medium resolution draws every stored row twice.</summary>
  public const int DisplayHeight = StoredHeight * 2;

  /// <summary>Bitplanes a medium-resolution screen uses.</summary>
  public const int Planes = 2;

  /// <summary>Colours one frame can show.</summary>
  public const int ColorCount = 1 << Planes;

  /// <summary>Size of the palette.</summary>
  public const int PaletteSize = ColorCount * AtariStGraphics.PaletteEntrySize;

  /// <summary>Offset of the first bitmap.</summary>
  public const int FirstFrameOffset = PaletteSize;

  /// <summary>Bytes one frame's bitmap occupies.</summary>
  public static readonly int FrameSize = AtariStGraphics.BytesPerRow(Width, Planes) * StoredHeight;

  /// <summary>Smallest a file can be: the palette and both bitmaps.</summary>
  public static readonly int MinFileSize = FirstFrameOffset + FrameSize * 2;

  /// <summary>The size some files pad out to.</summary>
  public const int PaddedFileSize = 113600;

  static string IImageFormatMetadata<DuoMediumFile>.PrimaryExtension => ".du2";
  static string[] IImageFormatMetadata<DuoMediumFile>.FileExtensions => [".du2"];
  static DuoMediumFile IImageFormatReader<DuoMediumFile>.FromSpan(ReadOnlySpan<byte> data) => DuoMediumReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DuoMediumFile>.VideoModes => [
    new("Duo medium", [(Width, DisplayHeight)], [ColorCount * (ColorCount + 1) / 2])
  ];

  /// <summary>The file's bytes, kept whole because both bitmaps share one palette.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(DuoMediumFile file) {
    var data = file.Data ?? [];
    var palette = AtariStGraphics.ReadPalette(data, 0, ColorCount);

    var first = _RenderFrame(data, FirstFrameOffset, palette);
    var second = _RenderFrame(data, FirstFrameOffset + FrameSize, palette);

    return new() {
      Width = Width,
      Height = DisplayHeight,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.BlendFrames(first, second),
    };
  }

  /// <summary>Unpacks one frame to RGB, doubling every stored row as the display does.</summary>
  private static byte[] _RenderFrame(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> palette) {
    var planar = offset + FrameSize <= data.Length ? data.Slice(offset, FrameSize) : default;
    var indices = planar.IsEmpty
      ? new byte[Width * StoredHeight]
      : PlanarConverter.AtariStToChunky(planar, Width, StoredHeight, Planes);

    var stored = AtariStGraphics.ToRgb(indices, palette, ColorCount);
    var rgb = new byte[Width * DisplayHeight * 3];
    for (var y = 0; y < StoredHeight; ++y) {
      var source = stored.AsSpan(y * Width * 3, Width * 3);
      source.CopyTo(rgb.AsSpan(y * 2 * Width * 3));
      source.CopyTo(rgb.AsSpan((y * 2 + 1) * Width * 3));
    }

    return rgb;
  }

  /// <summary>Builds a picture, putting the same field in both halves.</summary>
  /// <remarks>
  /// Each stored row is drawn twice, so the picture is taken at half its displayed height and the
  /// doubling left to the reader. Both fields hold the same rows, whose average is themselves.
  /// </remarks>
  public static DuoMediumFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, StoredHeight);
    var quantized = ColorQuantizer.Quantize(
      PixelConverter.Convert(rgb, PixelFormat.Bgra32).PixelData, Width * StoredHeight, ColorCount);

    var indices = new byte[Width * StoredHeight];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)quantized.Indices[i];

    var data = new byte[MinFileSize];

    var stPalette = PlanarConverter.RgbToStPalette(quantized.Palette, ColorCount);
    for (var i = 0; i < ColorCount; ++i) {
      data[i * 2] = (byte)(stPalette[i] >> 8);
      data[i * 2 + 1] = (byte)stPalette[i];
    }

    var planar = PlanarConverter.ChunkyToAtariSt(indices, Width, StoredHeight, Planes);
    var length = Math.Min(planar.Length, FrameSize);
    planar.AsSpan(0, length).CopyTo(data.AsSpan(FirstFrameOffset));
    planar.AsSpan(0, length).CopyTo(data.AsSpan(FirstFrameOffset + FrameSize));

    return new() { Data = data };
  }
}
