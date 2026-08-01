using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ccitt;

/// <summary>In-memory representation of a CCITT-compressed bi-level image.</summary>
public sealed class CcittFile :
  IImageFormatReader<CcittFile>, IImageToRawImage<CcittFile>,
  IImageFromRawImage<CcittFile>, IImageFormatWriter<CcittFile> {

  static string IImageFormatMetadata<CcittFile>.PrimaryExtension => ".g3";
  static string[] IImageFormatMetadata<CcittFile>.FileExtensions => [".g3", ".g4", ".ccitt"];
  static CcittFile IImageFormatReader<CcittFile>.FromSpan(ReadOnlySpan<byte> data) => ReadBareStream(data);
  static VideoMode[] IImageFormatMetadata<CcittFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];
  static byte[] IImageFormatWriter<CcittFile>.ToBytes(CcittFile file) => CcittWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public CcittFormat Format { get; init; }

  /// <summary>1bpp packed pixel data (MSB first, ceil(width/8) bytes per row).</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>The scan line a fax uses at standard resolution, in pixels.</summary>
  public const int StandardWidth = 1728;

  /// <summary>As many rows as a page could hold; decoding stops when the coding runs out.</summary>
  public const int MaximumRows = 4400;

  /// <summary>Reads a file that is nothing but coding, assuming the standard page width.</summary>
  /// <remarks>
  /// A bare .g4 says nothing about its own size, so something has to be assumed and every tool
  /// assumes something different — this takes the fax scan line, and counts the rows by decoding
  /// until the coding runs out. Refusing the file outright, as this used to, is the one answer that
  /// helps nobody.
  /// </remarks>
  public static CcittFile ReadBareStream(ReadOnlySpan<byte> data) {
    if (data.Length < 1)
      throw new InvalidDataException("Data too small for valid CCITT compressed data.");

    // Which of the two codings a bare stream holds is not stated either, and the extension is no
    // guide — tools write Group 3 coding to a .g4 quite happily. Group 3 marks its line ends and
    // Group 4 has no such code at all, so a leading run of eleven zeros settles it.
    var bytes = data.ToArray();
    var isGroup3 = _StartsWithEndOfLine(data);

    var pixelData = isGroup3
      ? CcittG3Decoder.Decode(bytes, StandardWidth, MaximumRows, out var height)
      : CcittG4Decoder.Decode(bytes, StandardWidth, MaximumRows, out height);

    if (height <= 0)
      throw new InvalidDataException("No CCITT rows could be decoded.");

    var stride = (StandardWidth + 7) / 8;

    return new() {
      Width = StandardWidth,
      Height = height,
      Format = isGroup3 ? CcittFormat.Group3_1D : CcittFormat.Group4,
      PixelData = pixelData[..(stride * height)],
    };
  }

  /// <summary>Whether the stream opens with a Group 3 end-of-line marker, allowing for fill.</summary>
  private static bool _StartsWithEndOfLine(ReadOnlySpan<byte> data) {
    var zeros = 0;

    for (var i = 0; i < data.Length && i < 8; ++i)
    for (var bit = 7; bit >= 0; --bit) {
      if (((data[i] >> bit) & 1) == 0) {
        ++zeros;
        continue;
      }

      return zeros >= 11;
    }

    return false;
  }

  public static RawImage ToRawImage(CcittFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = Core.PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static CcittFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != Core.PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Format = CcittFormat.Group4,
      PixelData = image.PixelData[..],
    };
  }
}
