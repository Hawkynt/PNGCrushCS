using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ccitt;

/// <summary>In-memory representation of a CCITT-compressed bi-level image.</summary>
public sealed class CcittFile :
  IImageFormatReader<CcittFile>, IImageToRawImage<CcittFile>,
  IImageFromRawImage<CcittFile>, IImageFormatWriter<CcittFile> {

  static string IImageFormatMetadata<CcittFile>.PrimaryExtension => ".g3";
  static string[] IImageFormatMetadata<CcittFile>.FileExtensions => [".g3", ".g4", ".ccitt", ".fax"];
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

  /// <summary>
  /// What the two values draw: an unset bit is white, a set one is the ink.
  /// </summary>
  /// <remarks>
  /// This was the other way about, so every bare stream came back as its own negative — a page of
  /// white ink on black. The coding counts runs of white first and the decoder sets a bit for black,
  /// so nought is paper.
  /// <para/>
  /// A CALS raster reverses this, and deliberately: that format defines a set bit as white. The two
  /// therefore cannot share one palette, which is what made the mistake easy to carry.
  /// </remarks>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

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

    // Some files carry fax coding inside a container of their own. Reading one as though the coding
    // began at its first byte turns the header into rows: a ZyXEL fax decoded to four lines of
    // nothing and reported no trouble. What sits after such a header is not read here, so the file
    // is refused rather than half-decoded.
    foreach (var container in _KnownContainers)
      if (data.Length > container.Length && data[..container.Length].SequenceEqual(container))
        throw new InvalidDataException(
          "This is fax coding inside a container that states its own header, which is not read here; only bare coding is.");

    // Which of the two codings a bare stream holds is not stated either, and the extension is no
    // guide — tools write Group 3 coding to a .g4 quite happily. Group 3 marks its line ends and
    // Group 4 has no such code at all, so a leading run of eleven zeros settles it.
    var bytes = data.ToArray();
    var isGroup3 = _StartsWithEndOfLine(data);

    // Group 3 marks its line ends, so the first line can be added up and the width taken from the
    // coding rather than assumed. Only where that cannot be read does the fax scan line stand in.
    var width = StandardWidth;
    if (isGroup3 && CcittG3Decoder.MeasureWidth(bytes) is var measured and > 0)
      width = measured;

    var pixelData = isGroup3
      ? CcittG3Decoder.Decode(bytes, width, MaximumRows, out var height)
      : CcittG4Decoder.Decode(bytes, width, MaximumRows, out height);

    if (height <= 0)
      throw new InvalidDataException("No CCITT rows could be decoded.");

    var stride = (width + 7) / 8;

    return new() {
      Width = width,
      Height = height,
      Format = isGroup3 ? CcittFormat.Group3_1D : CcittFormat.Group4,
      PixelData = pixelData[..(stride * height)],
    };
  }

/// <summary>Signatures of containers that carry fax coding behind a header of their own.</summary>
  private static ReadOnlySpan<byte> ZyxelSignature => "ZyXEL"u8;

  private static byte[][] _KnownContainers => [ZyxelSignature.ToArray()];

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
    // Reduced rather than refused. A fax is one bit a pixel and always will be, so demanding the
    // caller arrive already at Indexed1 turned every ordinary picture into an exception and left
    // the format writable in name only.
    image = image.EnsureFormat(Core.PixelFormat.Indexed1);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Format = CcittFormat.Group4,
      PixelData = image.PixelData[..],
    };
  }
}
