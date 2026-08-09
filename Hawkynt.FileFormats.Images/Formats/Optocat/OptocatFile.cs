using System;
using FileFormat.Core;

namespace FileFormat.Optocat;

/// <summary>An Optocat picture (.abs): a byte-order word, a handful of shorts and uncompressed rows.</summary>
/// <remarks>
/// Optocat is Breuckmann's scanner software and nothing published describes what it writes, so the
/// layout came from XnView's own reader. The header is nine shorts in the byte order the first two
/// bytes announce: <c>II</c> or <c>MM</c>, a word this does not read, the offset the picture stands
/// at, two more words this does not read, the samples per pixel, one more unread word, the width and
/// the height. The depth is the samples times eight, the rows are uncompressed and each is
/// (width times depth plus seven) over eight bytes long.
/// <para/>
/// The two bytes it opens with are also TIFF's, which is what made this name look unusable. It is
/// usable, and the evidence is a file built to be a valid TIFF and a valid Optocat picture at once:
/// under the name <c>both.tif</c> XnView read it as TIFF and under <c>both.abs</c> it read it as
/// Optocat, so XnView resolves the collision by extension and hands <c>.abs</c> to this reader ahead
/// of TIFF. This reader keeps the collision at arm's length in two ways. It is never offered first
/// for content sniffing, and it refuses anything whose picture does not fit: the stated offset has to
/// be at least 2048 and inside the file, and the rows have to be there behind it. A TIFF renamed to
/// <c>.abs</c> puts its first IFD at offset 8, which is below the 2048 the offset word has to clear,
/// and one that does not is read through pixel bytes as its samples, width and height, which then
/// have to describe a picture that fits — three independent conditions on unrelated bytes.
/// <para/>
/// One, two, three and four samples were all read by XnView and nothing else was; zero and five made
/// it refuse the file. Two samples are fifteen-bit colour in a little-endian word regardless of what
/// the byte-order mark says — a file written <c>MM</c> gave the same colours as the same bytes
/// written <c>II</c> — and each five-bit channel is widened by multiplying by 255 and dividing by 31.
/// Four samples are read as three: XnView drops the fourth, and so does this, because what the fourth
/// holds was never visible in anything the converter would write out.
/// </remarks>
[FormatDetectionPriority(999)]
public readonly record struct OptocatFile
  : IImageFormatReader<OptocatFile>, IImageToRawImage<OptocatFile> {

  /// <summary>The smallest file, and the smallest picture offset, the reader accepts.</summary>
  public const int MinimumOffset = 2048;

  /// <summary>The header words this reads, in bytes.</summary>
  public const int HeaderSize = 18;

  /// <summary>The fewest samples a pixel may carry.</summary>
  public const int MinimumSamples = 1;

  /// <summary>The most samples a pixel may carry.</summary>
  public const int MaximumSamples = 4;

  static string IImageFormatMetadata<OptocatFile>.PrimaryExtension => ".abs";
  static string[] IImageFormatMetadata<OptocatFile>.FileExtensions => [".abs"];
  static OptocatFile IImageFormatReader<OptocatFile>.FromSpan(ReadOnlySpan<byte> data)
    => OptocatReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<OptocatFile>.VideoModes => [
    new("Optocat", [(IntegerRange.Any, IntegerRange.Any)], [256, 32768, 16777216])
  ];

  /// <summary>Says yes only when every header word is consistent, so that a TIFF does not fall in here.</summary>
  static bool? IImageFormatMetadata<OptocatFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderSize)
      return null;

    var littleEndian = header[0] == (byte)'I' && header[1] == (byte)'I';
    if (!littleEndian && !(header[0] == (byte)'M' && header[1] == (byte)'M'))
      return null;

    var offset = _Word(header, 4, littleEndian);
    var samples = _Word(header, 10, littleEndian);
    var width = _Word(header, 14, littleEndian);
    var height = _Word(header, 16, littleEndian);
    if (offset < MinimumOffset || samples is < MinimumSamples or > MaximumSamples || width == 0 || height == 0)
      return null;

    // Detection is normally handed a short peek, so the picture can only be checked when the whole
    // file arrived. When it did, the picture has to fit or this is not an Optocat file.
    if (header.Length > MinimumOffset) {
      var need = (long)offset + (long)height * width * samples;
      if (need > header.Length)
        return null;
    }

    return true;
  }

  private static int _Word(ReadOnlySpan<byte> data, int at, bool littleEndian)
    => littleEndian ? data[at] | (data[at + 1] << 8) : (data[at] << 8) | data[at + 1];

  /// <summary>Whether the header words stand least significant byte first.</summary>
  public bool IsLittleEndian { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Samples a pixel, which is one, two, three or four.</summary>
  public int SamplesPerPixel { get; init; }

  /// <summary>Where in the file the rows begin.</summary>
  public int PixelOffset { get; init; }

  /// <summary>The rows as they stand in the file, uncompressed.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>How many bytes one row takes.</summary>
  public int BytesPerRow => (this.Width * this.SamplesPerPixel * 8 + 7) / 8;

  public static RawImage ToRawImage(OptocatFile file) {
    var source = file.PixelData;
    if (source == null)
      throw new InvalidOperationException("No Optocat picture was read.");

    var width = file.Width;
    var height = file.Height;
    var stride = file.BytesPerRow;
    var count = width * height;

    switch (file.SamplesPerPixel) {
      case 1:
        return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = source[..] };
      case 2: {
        var pixels = new byte[count * 3];
        for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var at = y * stride + x * 2;
          var value = source[at] | (source[at + 1] << 8);
          var to = (y * width + x) * 3;
          pixels[to] = (byte)(((value >> 10) & 31) * 255 / 31);
          pixels[to + 1] = (byte)(((value >> 5) & 31) * 255 / 31);
          pixels[to + 2] = (byte)((value & 31) * 255 / 31);
        }

        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
      }
      case 3:
        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = source[..] };
      case 4: {
        var pixels = new byte[count * 3];
        for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var at = y * stride + x * 4;
          var to = (y * width + x) * 3;
          pixels[to] = source[at];
          pixels[to + 1] = source[at + 1];
          pixels[to + 2] = source[at + 2];
        }

        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
      }
      default:
        throw new InvalidOperationException($"Optocat: {file.SamplesPerPixel} samples a pixel is not one this reads.");
    }
  }
}
