using System;
using FileFormat.Core;

namespace FileFormat.ApplePreferred;

/// <summary>In-memory representation of an Apple Preferred Format picture (.32k).</summary>
/// <remarks>
/// The IIGS's own picture format, and the only one here built out of named chunks: a length, a name
/// and a body, repeated. Only two chunks matter. MAIN holds the palettes, a directory saying how
/// many bytes each scanline packs into and which palette it uses, and then the packed bitmap; the
/// optional MULTIPAL replaces the directory's choice with a palette per line, which is how a
/// picture gets more than sixteen colours.
/// <para/>
/// Two screen modes exist and they are not a resolution setting so much as two different pictures.
/// The 320-wide one is four bits a pixel against the whole palette. The 640-wide one is two bits a
/// pixel, but each of the four pixels in a byte draws from a different quarter of the palette, so a
/// row still shows all sixteen colours — and its rows are drawn twice, because at 640 across the
/// machine ran only 200 lines.
/// </remarks>
public readonly record struct ApplePreferredFile
  : IImageFormatReader<ApplePreferredFile>, IImageToRawImage<ApplePreferredFile> {

  /// <summary>Where the palettes start.</summary>
  public const int PalettesOffset = 15;

  /// <summary>Bytes a scanline's directory entry occupies.</summary>
  public const int DirectoryEntrySize = 4;

  /// <summary>Length of a MULTIPAL chunk: its header and two hundred palettes.</summary>
  public const int MultipalChunkSize = 6415;

  static string IImageFormatMetadata<ApplePreferredFile>.PrimaryExtension => ".32k";
  static string[] IImageFormatMetadata<ApplePreferredFile>.FileExtensions => [".32k", ".gs", ".iigs"];
  static ApplePreferredFile IImageFormatReader<ApplePreferredFile>.FromSpan(ReadOnlySpan<byte> data)
    => ApplePreferredReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ApplePreferredFile>.VideoModes => [
    new("Apple IIGS", [(new(1, 640), new(1, 400))], [3200])
  ];

  /// <summary>The whole file, which every offset is relative to.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows the picture is drawn as, which is twice the stored count in the 640 mode.</summary>
  public int Height { get; init; }

  /// <summary>Stored scanlines.</summary>
  public int StoredHeight { get; init; }

  /// <summary>Whether the picture is the 640-wide two-bit mode.</summary>
  public bool IsWideMode { get; init; }

  /// <summary>Where the scanline directory starts.</summary>
  public int DirectoryOffset { get; init; }

  /// <summary>Where the packed bitmap starts.</summary>
  public int BitmapOffset { get; init; }

  /// <summary>Where the per-scanline palettes start, or -1 if the picture has none.</summary>
  public int MultipalOffset { get; init; }

  public static RawImage ToRawImage(ApplePreferredFile file) {
    var data = file.Data ?? [];
    var rgb = new byte[file.Width * file.Height * 3];
    var bytesPerLine = file.IsWideMode ? file.Width >> 2 : file.Width >> 1;
    var stream = new PackBytesStream(file.BitmapOffset);

    for (var y = 0; y < file.StoredHeight; ++y) {
      var entry = file.DirectoryOffset + y * DirectoryEntrySize;
      var palette = file.MultipalOffset >= 0
        ? AppleIIGSGraphics.ReadPalette(data, file.MultipalOffset + y * AppleIIGSGraphics.PaletteSize, reversed: false)
        : AppleIIGSGraphics.ReadPalette(
          data, PalettesOffset + (data[entry + 2] & 15) * AppleIIGSGraphics.PaletteSize, reversed: false);

      // Each line says how many packed bytes it occupies, so a line that unpacks short does not
      // drag the rest of the picture out of step.
      var nextLine = stream.Offset + data[entry] + (data[entry + 1] << 8);

      for (var x = 0; x < bytesPerLine; ++x) {
        var value = stream.ReadByte(data);
        if (value < 0)
          throw new System.IO.InvalidDataException($"Scanline {y} ends before the picture does.");

        if (file.IsWideMode)
          _WriteWide(rgb, palette, file.Width, y, x, value);
        else
          _WriteNarrow(rgb, palette, file.Width, y, x, value);
      }

      stream.Offset = nextLine;
    }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Draws the two pixels of a 320-mode byte.</summary>
  private static void _WriteNarrow(Span<byte> rgb, ReadOnlySpan<byte> palette, int width, int y, int x, int value) {
    _Plot(rgb, palette, (y * width + (x << 1)) * 3, value >> 4);
    _Plot(rgb, palette, (y * width + (x << 1) + 1) * 3, value & 15);
  }

  /// <summary>
  /// Draws the four pixels of a 640-mode byte, on the two rows it occupies.
  /// </summary>
  /// <remarks>
  /// The quarters are taken in the order 8, 12, 0, 4 rather than 0, 4, 8, 12. That is not a
  /// convention but where the bits land: the hardware reads the byte's pairs in the order it does
  /// and pairs them with palette quarters in the order it does, and the two orders do not agree.
  /// </remarks>
  private static void _WriteWide(Span<byte> rgb, ReadOnlySpan<byte> palette, int width, int y, int x, int value) {
    ReadOnlySpan<int> quarters = [8, 12, 0, 4];

    for (var i = 0; i < 4; ++i) {
      var index = quarters[i] + ((value >> (6 - i * 2)) & 3);
      var at = ((y << 1) * width + (x << 2) + i) * 3;
      _Plot(rgb, palette, at, index);
      _Plot(rgb, palette, at + width * 3, index);
    }
  }

  private static void _Plot(Span<byte> rgb, ReadOnlySpan<byte> palette, int target, int index) {
    var entry = index * 3;
    rgb[target] = palette[entry];
    rgb[target + 1] = palette[entry + 1];
    rgb[target + 2] = palette[entry + 2];
  }
}
