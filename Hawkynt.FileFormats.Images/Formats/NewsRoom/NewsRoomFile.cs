using System;
using FileFormat.Core;

namespace FileFormat.NewsRoom;

/// <summary>In-memory representation of a NewsRoom panel (.nsr, .ph, .bn).</summary>
/// <remarks>
/// Springboard's The NewsRoom was a newsletter program for the Apple II and the Commodore 64. Its
/// pieces come in three extensions — <c>.nsr</c> for a panel, <c>.ph</c> for a photo and <c>.bn</c>
/// for a banner — and XnView reads all three with one reader.
/// <para/>
/// What stood here before read no header at all: it took any file of exactly 7680 bytes as a
/// 320x192 panel, which is a reading of a length rather than of a format, and would have drawn any
/// other 7680-byte file as a picture. The header below is XnView's, recovered from its own reader
/// and then put back to it: a file built this way is read at the size it states and the bits come
/// back as the picture that was put in.
/// <para/>
/// Ten bytes stand in front of the picture. The first two are <c>00 A0</c>. The next two are not
/// read. Then two bytes whose difference is the height, two more whose difference plus one is the
/// width, and finally <c>00</c> and <c>FF</c>, which are what lets this refuse a file of some other
/// format. Both sizes are rounded up to a multiple of eight, as XnView rounds them.
/// <para/>
/// A set bit is paper and a clear bit is ink, which is the way round XnView draws it.
/// </remarks>
public readonly record struct NewsRoomFile
  : IImageFormatReader<NewsRoomFile>, IImageToRawImage<NewsRoomFile>, IImageFromRawImage<NewsRoomFile>,
    IImageFormatWriter<NewsRoomFile> {

  static string IImageFormatMetadata<NewsRoomFile>.PrimaryExtension => ".nsr";
  static string[] IImageFormatMetadata<NewsRoomFile>.FileExtensions => [".nsr", ".ph", ".bn"];
  static NewsRoomFile IImageFormatReader<NewsRoomFile>.FromSpan(ReadOnlySpan<byte> data) => NewsRoomReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<NewsRoomFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<NewsRoomFile>.ToBytes(NewsRoomFile file) => NewsRoomWriter.ToBytes(file);

  /// <summary>The two bytes a file opens with.</summary>
  public static ReadOnlySpan<byte> Signature => [0x00, 0xA0];

  /// <summary>Where the pair whose difference is the height stands.</summary>
  public const int HeightPairOffset = 4;

  /// <summary>Where the pair whose difference plus one is the width stands.</summary>
  public const int WidthPairOffset = 6;

  /// <summary>The byte at offset 8, which is always zero.</summary>
  public const int LowMarkerOffset = 8;

  /// <summary>The byte at offset 9, which is always 255.</summary>
  public const int HighMarkerOffset = 9;

  /// <summary>The whole header, behind which the bits start.</summary>
  public const int HeaderSize = 10;

  static bool? IImageFormatMetadata<NewsRoomFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderSize)
      return null;

    return header[..Signature.Length].SequenceEqual(Signature)
           && header[LowMarkerOffset] == 0x00
           && header[HighMarkerOffset] == 0xFF;
  }

  /// <summary>Image width in pixels, always a multiple of eight.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, always a multiple of eight.</summary>
  public int Height { get; init; }

  /// <summary>Packed 1bpp rows, most significant bit leftmost, a set bit being paper.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Bytes one row of a picture this wide takes.</summary>
  public static int StrideOf(int width) => (width + 7) / 8;

  /// <summary>Index 0 is ink and index 1 is paper, which is the way round the bits stand.</summary>
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this NewsRoom panel to a bilevel raw image.</summary>
  public static RawImage ToRawImage(NewsRoomFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData ?? [], file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  /// <summary>The widest panel the header can state, its two coordinates being one byte each.</summary>
  public const int MaximumWidth = 256;

  /// <summary>The tallest, which is one row of bytes less because its pair is a difference.</summary>
  public const int MaximumHeight = 248;

  /// <summary>Creates a NewsRoom panel from a picture, one bit a pixel.</summary>
  /// <remarks>
  /// The panel's size is the picture's, rounded up to a multiple of eight in both directions and cut
  /// to what the header can state, which is 256 by 248: both sizes stand in it as a pair of
  /// single-byte coordinates. A pixel at or above half brightness is paper and sets its bit, as
  /// <see cref="ToRawImage"/> reads it.
  /// </remarks>
  public static NewsRoomFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Min((image.Width + 7) / 8 * 8, MaximumWidth);
    var height = Math.Min((image.Height + 7) / 8 * 8, MaximumHeight);
    var gray = image.SampleTo(width, height).EnsureFormat(PixelFormat.Gray8);
    var stride = StrideOf(width);
    var pixels = new byte[stride * height];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        if (gray.PixelData[y * width + x] < 128)
          continue;

        pixels[y * stride + x / 8] |= (byte)(1 << (7 - x % 8));
      }

    return new() { Width = width, Height = height, PixelData = pixels };
  }
}
