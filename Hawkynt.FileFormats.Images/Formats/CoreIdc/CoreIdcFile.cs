using System;
using FileFormat.Core;

namespace FileFormat.CoreIdc;

/// <summary>A Core IDC picture (.idc): uncompressed rows first and a thirty-two byte trailer last.</summary>
/// <remarks>
/// This is not the icdraw format the similarly named reader in this library takes; the two only share
/// three letters. Nothing published describes Core IDC, so the layout came from XnView's own reader,
/// which seeks to thirty-two bytes before the end of the file and reads its whole header from there:
/// a big-endian width, a big-endian height, a big-endian plane count, a big-endian depth, twelve bytes
/// it never looks at, and then the five characters <c>IDC21</c> followed by three more it never looks
/// at. Everything before the trailer is the picture, uncompressed, beginning at byte zero.
/// <para/>
/// The rows are (width times depth plus seven) over eight bytes long. When there is more than one
/// plane the planes are stored whole, one after another, not interleaved — a three-plane file whose
/// planes carried three different ramps came back with the first plane as red, the second as green and
/// the third as blue. XnView also reads two and four planes; it maps the first three onto red, green
/// and blue and leaves the rest, which is why this reader takes one plane or three and refuses the
/// others rather than inventing a meaning for them.
/// <para/>
/// Depths of one, four, eight and twenty-four were all read. One bit a pixel is black where the bit is
/// clear, four bits are a sixteen-step grey where a nibble is worth seventeen levels, eight bits are
/// grey as they stand, and twenty-four are red, green and blue in that order inside the row.
/// </remarks>
[FormatDetectionPriority(999)]
public readonly record struct CoreIdcFile
  : IImageFormatReader<CoreIdcFile>, IImageToRawImage<CoreIdcFile>, IImageFromRawImage<CoreIdcFile>, IImageFormatWriter<CoreIdcFile> {

  /// <summary>How long the trailer is.</summary>
  public const int TrailerSize = 32;

  /// <summary>Where the signature stands, counted back from the end of the file.</summary>
  public const int SignatureFromEnd = 8;

  /// <summary>The five characters the trailer carries.</summary>
  public static ReadOnlySpan<byte> Signature => "IDC21"u8;

  static string IImageFormatMetadata<CoreIdcFile>.PrimaryExtension => ".idc";
  static string[] IImageFormatMetadata<CoreIdcFile>.FileExtensions => [".idc"];
  static CoreIdcFile IImageFormatReader<CoreIdcFile>.FromSpan(ReadOnlySpan<byte> data)
    => CoreIdcReader.FromSpan(data);
  static byte[] IImageFormatWriter<CoreIdcFile>.ToBytes(CoreIdcFile file) => CoreIdcWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CoreIdcFile>.VideoModes => [
    new("Core IDC", [(IntegerRange.Any, IntegerRange.Any)], [2, 16, 256, 16777216])
  ];

  /// <summary>The signature stands at the end, so this can only answer when the whole file is at hand.</summary>
  static bool? IImageFormatMetadata<CoreIdcFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= TrailerSize
       && header.Slice(header.Length - SignatureFromEnd, Signature.Length).SequenceEqual(Signature)
      ? true
      : null;

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>How many planes stand one after another, which is one or three.</summary>
  public int Planes { get; init; }

  /// <summary>Bits a pixel inside one plane, which is one, four, eight or twenty-four.</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>The planes as they stand in the file.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>How many bytes one row of one plane takes.</summary>
  public int BytesPerRow => (this.Width * this.BitsPerPixel + 7) / 8;

  public static RawImage ToRawImage(CoreIdcFile file) {
    var source = file.PixelData;
    if (source == null)
      throw new InvalidOperationException("No Core IDC picture was read.");

    var width = file.Width;
    var height = file.Height;
    var stride = file.BytesPerRow;
    var count = width * height;

    if (file.Planes == 3) {
      var planeSize = stride * height;
      var pixels = new byte[count * 3];
      for (var plane = 0; plane < 3; ++plane)
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixels[(y * width + x) * 3 + plane] = source[plane * planeSize + y * stride + x];

      return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
    }

    switch (file.BitsPerPixel) {
      case 1: {
        var pixels = new byte[count];
        for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          pixels[y * width + x] = ((source[y * stride + (x >> 3)] >> (~x & 7)) & 1) != 0 ? (byte)255 : (byte)0;

        return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
      }
      case 4: {
        var pixels = new byte[count];
        for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var value = source[y * stride + (x >> 1)];
          var nibble = (x & 1) == 0 ? value >> 4 : value & 15;
          pixels[y * width + x] = (byte)(nibble * 17);
        }

        return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
      }
      case 8:
        return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = source[..] };
      case 24:
        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = source[..] };
      default:
        throw new InvalidOperationException($"Core IDC: {file.BitsPerPixel} bits a pixel is not a depth this reads.");
    }
  }

  /// <summary>Creates the lossless colour representation: three whole eight-bit planes in R, G, B order.</summary>
  public static CoreIdcFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("Core IDC requires positive dimensions.", nameof(image));

    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    var pixels = checked(rgb.Width * rgb.Height);
    return new() {
      Width = rgb.Width,
      Height = rgb.Height,
      Planes = 3,
      BitsPerPixel = 8,
      PixelData = PixelConverter.InterleavedToBandSequential(rgb.PixelData, pixels, 3),
    };
  }
}
