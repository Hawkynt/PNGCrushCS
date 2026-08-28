using System;
using FileFormat.Core;

namespace FileFormat.NcrImage;

/// <summary>In-memory representation of an NCR Image (.ncr).</summary>
/// <remarks>
/// NCR made the scanners and the cheque-processing machines that wrote these; the format is a fax
/// coding under a fixed header and nothing describing it has ever been published. The layout here
/// comes from XnView's own reader and every field in it was put back to that reader before it was
/// written down.
/// <para/>
/// Four bytes of signature — <c>6E 6E 0A 00</c>, which reads as <c>nn</c> and a newline — then
/// nothing that is used until offset 64. There the size stands as four sixteen-bit little-endian
/// numbers of which the second is the width and the fourth the height; the first and third are not
/// read. One byte at offset 74 selects the coding, and the coded raster starts at offset 94.
/// <para/>
/// Only one setting of the coding byte is read: anything from one upwards, which is Group 4
/// two-dimensional coding. A file built that way is read by XnView's converter at the size it states
/// and comes back as the picture that was coded, pixel for pixel. Zero selects something else in
/// XnView and there is no file to check a reading of it against, so it is refused by name rather
/// than decoded as Group 4 — which would draw a page of noise at exactly the right size.
/// </remarks>
public readonly record struct NcrImageFile : IImageFormatReader<NcrImageFile>, IImageToRawImage<NcrImageFile>, IImageFromRawImage<NcrImageFile>, IImageFormatWriter<NcrImageFile> {

  /// <summary>The four bytes a file opens with.</summary>
  public static ReadOnlySpan<byte> Signature => [0x6E, 0x6E, 0x0A, 0x00];

  /// <summary>Where the width stands.</summary>
  public const int WidthOffset = 0x42;

  /// <summary>Where the height stands.</summary>
  public const int HeightOffset = 0x46;

  /// <summary>Where the byte choosing the coding stands.</summary>
  public const int CodingOffset = 0x4A;

  /// <summary>Where the coded raster starts.</summary>
  public const int CodedDataOffset = 0x5E;

  static string IImageFormatMetadata<NcrImageFile>.PrimaryExtension => ".ncr";
  static string[] IImageFormatMetadata<NcrImageFile>.FileExtensions => [".ncr"];
  static NcrImageFile IImageFormatReader<NcrImageFile>.FromSpan(ReadOnlySpan<byte> data) => NcrImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<NcrImageFile>.ToBytes(NcrImageFile file) => NcrImageWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<NcrImageFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];

  static bool? IImageFormatMetadata<NcrImageFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < Signature.Length ? null : header[..Signature.Length].SequenceEqual(Signature);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Packed 1bpp rows, most significant bit leftmost, a set bit being ink.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>A set bit is ink, which is what the fax coding underneath counts in.</summary>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(NcrImageFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = BilevelRows.Unpack(file.PixelData ?? [], file.Width, file.Height),
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  /// <summary>Creates a conforming Group-4 NCR Image from any source image.</summary>
  public static NcrImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > ushort.MaxValue || image.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"NCR dimensions must fit 16-bit fields; got {image.Width}x{image.Height}.", nameof(image));
    var mono = image.EnsureIndexed(PixelFormat.Indexed1, _BlackWhitePalette);
    return new() { Width = mono.Width, Height = mono.Height, PixelData = mono.PixelData[..] };
  }
}