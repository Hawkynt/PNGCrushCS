using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.PrintTechnik;

/// <summary>In-memory representation of a Print-Technik greyscale scan (.hir).</summary>
/// <remarks>
/// Ten bytes of header carrying the size — the width as a big-endian word at 4 and the height at 6 —
/// and then one byte a pixel, rows tight, no padding.
/// <para/>
/// The samples store seven bits and not eight: every value is 0 to 127 and the shade drawn is twice
/// it, so 127 is 254 rather than white. Reading the byte as an ordinary grey level halves the whole
/// picture's contrast, which is a difference no amount of moving the data about would have fixed —
/// it was found by mapping every stored value onto what the reference draws and getting 46 values,
/// no two disagreeing, and a factor of two between them.
/// <para/>
/// <c>.hir</c> was claimed only by the Commodore 64 hires screen, which takes 8002 bytes.
/// </remarks>
public readonly record struct PrintTechnikFile
  : IImageFormatReader<PrintTechnikFile>, IImageToRawImage<PrintTechnikFile>,
    IImageFromRawImage<PrintTechnikFile>, IImageFormatWriter<PrintTechnikFile> {

  /// <summary>The size sits inside a ten-byte header.</summary>
  public const int HeaderSize = 10;

  internal const int WidthAt = 4, HeightAt = 6;

  /// <summary>Shades a scan holds: seven bits, drawn at twice the stored value.</summary>
  public const int ColorCount = 128;

  static string IImageFormatMetadata<PrintTechnikFile>.PrimaryExtension => ".hir";
  static string[] IImageFormatMetadata<PrintTechnikFile>.FileExtensions => [".hir"];
  static PrintTechnikFile IImageFormatReader<PrintTechnikFile>.FromSpan(ReadOnlySpan<byte> data)
    => PrintTechnikReader.FromSpan(data);
  static byte[] IImageFormatWriter<PrintTechnikFile>.ToBytes(PrintTechnikFile file)
    => PrintTechnikWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PrintTechnikFile>.VideoModes => [
    new("Scan", [(new IntegerRange(1, ushort.MaxValue), new IntegerRange(1, ushort.MaxValue))], [ColorCount])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>Whatever the header carries besides the size, kept so writing one back preserves it.</summary>
  public byte[] Header { get; init; }

  /// <summary>One seven-bit shade a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PrintTechnikFile file) {
    var source = file.PixelData ?? [];
    var grey = new byte[file.Width * file.Height];
    for (var i = 0; i < grey.Length && i < source.Length; ++i)
      grey[i] = (byte)((source[i] & 0x7F) << 1);

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Gray8, PixelData = grey };
  }

  public static PrintTechnikFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var grey = PixelConverter.Convert(image, PixelFormat.Gray8).PixelData;
    var pixels = new byte[image.Width * image.Height];
    for (var i = 0; i < pixels.Length && i < grey.Length; ++i)
      pixels[i] = (byte)(grey[i] >> 1);

    return new() { Width = image.Width, Height = image.Height, Header = new byte[HeaderSize], PixelData = pixels };
  }
}
