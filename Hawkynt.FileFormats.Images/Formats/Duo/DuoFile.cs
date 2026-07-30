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
  : IImageFormatReader<DuoFile>, IImageToRawImage<DuoFile> {

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
    var palette = _PaletteRgb(data);

    var first = _RenderFrame(data, FirstFrameOffset, palette);
    var second = _RenderFrame(data, SecondFrameOffset, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.BlendFrames(first, second),
    };
  }

  /// <summary>Reads the sixteen colours; each is one word holding three bits per channel.</summary>
  private static byte[] _PaletteRgb(ReadOnlySpan<byte> data) {
    var rgb = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount && i * 2 + 1 < data.Length; ++i) {
      int high = data[i * 2], low = data[i * 2 + 1];
      rgb[i * 3] = ChannelScaling.Expand4(_Ste(high & 15));
      rgb[i * 3 + 1] = ChannelScaling.Expand4(_Ste((low >> 4) & 15));
      rgb[i * 3 + 2] = ChannelScaling.Expand4(_Ste(low & 15));
    }

    return rgb;
  }

  /// <summary>
  /// Reorders an STE colour nibble. The STE widened the ST's three-bit channels to four, and put
  /// the new bit at the bottom of the word rather than the top so that an ST picture still reads
  /// correctly on the newer machine — which means the stored bits are rotated, not simply extended.
  /// </summary>
  private static int _Ste(int value) => ((value & 7) << 1) | ((value >> 3) & 1);

  /// <summary>Unpacks one bitplane frame straight to RGB.</summary>
  private static byte[] _RenderFrame(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> palette) {
    var planar = offset + FrameSize <= data.Length ? data.Slice(offset, FrameSize) : default;
    var indices = planar.IsEmpty ? new byte[Width * Height] : PlanarConverter.AtariStToChunky(planar, Width, Height, Planes);

    var rgb = new byte[Width * Height * 3];
    for (var i = 0; i < indices.Length; ++i) {
      var entry = (indices[i] & (ColorCount - 1)) * 3;
      rgb[i * 3] = palette[entry];
      rgb[i * 3 + 1] = palette[entry + 1];
      rgb[i * 3 + 2] = palette[entry + 2];
    }

    return rgb;
  }
}
