using System;
using FileFormat.Core;
using FileFormat.SamCoupeMode4;

namespace FileFormat.SamCoupeLce;

/// <summary>In-memory representation of a SAM Coupe interlaced picture (.lce).</summary>
/// <remarks>
/// Two complete mode 4 screens in one file, shown on alternate television fields. Each brings its
/// own palette and its own list of line interrupts, so the pair can hold twice the colours a single
/// screen can — and because the fields alternate rather than blend, the result is displayed as
/// interleaved scanlines rather than an average.
/// <para/>
/// The second screen begins wherever the first one's interrupt list ends, which is the only thing
/// in the file that says how long either of them is.
/// </remarks>
public readonly record struct SamCoupeLceFile
  : IImageFormatReader<SamCoupeLceFile>, IImageToRawImage<SamCoupeLceFile> {

  /// <summary>Pixels one screen stores per row.</summary>
  public const int StoredWidth = 256;

  /// <summary>Rows one screen stores.</summary>
  public const int StoredHeight = 192;

  /// <summary>Displayed width; every stored pixel is drawn twice.</summary>
  public const int Width = StoredWidth * 2;

  /// <summary>Displayed height; the two screens interleave by scanline.</summary>
  public const int Height = StoredHeight * 2;

  /// <summary>Bytes one screen's bitmap occupies, at two pixels per byte.</summary>
  public const int BitmapSize = StoredWidth / 2 * StoredHeight;

  /// <summary>Offset of a screen's interrupt list, relative to the screen's start.</summary>
  public const int InterruptOffset = 24616;

  /// <summary>Bytes between a screen's palette and its interrupt list.</summary>
  public const int PaletteToInterruptGap = 40;

  /// <summary>Colours one screen's palette holds.</summary>
  public const int PaletteSize = 16;

  /// <summary>Bytes in one interrupt record.</summary>
  public const int InterruptRecordSize = 4;

  /// <summary>Byte that closes an interrupt list.</summary>
  public const byte InterruptTerminator = 0xFF;

  static string IImageFormatMetadata<SamCoupeLceFile>.PrimaryExtension => ".lce";
  static string[] IImageFormatMetadata<SamCoupeLceFile>.FileExtensions => [".lce"];
  static SamCoupeLceFile IImageFormatReader<SamCoupeLceFile>.FromSpan(ReadOnlySpan<byte> data)
    => SamCoupeLceReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SamCoupeLceFile>.VideoModes => [
    new("Interlaced", [(Width, Height)], [PaletteSize * 2])
  ];

  /// <summary>The file's bytes, kept whole because both screens are addressed by absolute offset.</summary>
  public byte[] Data { get; init; }

  /// <summary>Offset at which the second screen begins.</summary>
  public int SecondScreenOffset { get; init; }

  public static RawImage ToRawImage(SamCoupeLceFile file) {
    var data = file.Data ?? [];
    var rgb = new byte[Width * Height * 3];

    // Each screen owns one parity of the display: the first the even scanlines, the second the odd.
    _RenderScreen(data, 0, rgb, 0);
    _RenderScreen(data, file.SecondScreenOffset, rgb, 1);

    return new() { Width = Width, Height = Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Draws one screen onto every second scanline.</summary>
  private static void _RenderScreen(ReadOnlySpan<byte> data, int screen, byte[] rgb, int parity) {
    var palette = new int[PaletteSize];
    var paletteOffset = screen + InterruptOffset - PaletteToInterruptGap;
    for (var i = 0; i < PaletteSize; ++i)
      palette[i] = SamCoupePalette.ToRgb(_At(data, paletteOffset + i));

    var interrupt = screen + InterruptOffset;
    for (var y = 0; y < StoredHeight; ++y) {
      // Records are in line order and each names the line before the one it takes effect on.
      while (interrupt + InterruptRecordSize - 1 < data.Length && y == data[interrupt] + 1) {
        var entry = data[interrupt + 1];
        if (entry >= PaletteSize)
          break;

        palette[entry] = SamCoupePalette.ToRgb(data[interrupt + 2]);
        interrupt += InterruptRecordSize;
      }

      var row = (y * 2 + parity) * Width;
      for (var x = 0; x < StoredWidth; ++x) {
        // Mode 4 is four bits a pixel, high half of a byte first.
        var index = _At(data, screen + (y << 7) + (x >> 1));
        var color = palette[((x & 1) == 0 ? index >> 4 : index & 15)];

        // Every stored pixel is drawn two screen pixels wide.
        for (var repeat = 0; repeat < 2; ++repeat) {
          var target = (row + x * 2 + repeat) * 3;
          rgb[target] = (byte)(color >> 16);
          rgb[target + 1] = (byte)(color >> 8);
          rgb[target + 2] = (byte)color;
        }
      }
    }
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
