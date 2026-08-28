using System;
using FileFormat.Core;

namespace FileFormat.Pixibox;

/// <summary>In-memory representation of a Pixibox picture (.pxb).</summary>
/// <remarks>
/// Pixibox was a French paint and animation package for the Atari and later the PC. Nothing
/// describing its file has ever been published; what the layout below comes from is XnView's own
/// reader, and every field in it was put to that reader before it was written down here.
/// <para/>
/// A file opens with twelve bytes that never vary — <c>49 49 00 04 02 00 01 00 08 00 04 00</c> —
/// then a two-byte number that is not read, then the width and the height as sixteen-bit
/// little-endian numbers. Everything from there to offset 1024 is passed over: the picture starts at
/// 1024 exactly.
/// <para/>
/// The picture is run-length coded, four bytes a pixel: a count byte, then red, green, blue and a
/// fourth byte that is not used. A count of zero means "to the end of this row" rather than a run of
/// nothing. Rows are stored from the bottom of the picture upwards.
/// <para/>
/// A file built to this description is read by XnView's converter at the size it states and returns
/// the pixels that were put in, byte for byte, on every one of them — including the zero-count case,
/// which was checked on its own.
/// </remarks>
public readonly record struct PixiboxFile : IImageFormatReader<PixiboxFile>, IImageToRawImage<PixiboxFile>, IImageFromRawImage<PixiboxFile>, IImageFormatWriter<PixiboxFile> {

  /// <summary>The twelve bytes every file opens with.</summary>
  public static ReadOnlySpan<byte> Signature => [0x49, 0x49, 0x00, 0x04, 0x02, 0x00, 0x01, 0x00, 0x08, 0x00, 0x04, 0x00];

  /// <summary>Where the width stands, two bytes behind the signature.</summary>
  public const int WidthOffset = 14;

  /// <summary>Where the height stands.</summary>
  public const int HeightOffset = 16;

  /// <summary>Where the coded picture starts, whatever the header says.</summary>
  public const int PixelDataOffset = 1024;

  /// <summary>A count byte and four bytes of colour.</summary>
  public const int RunSize = 5;

  static string IImageFormatMetadata<PixiboxFile>.PrimaryExtension => ".pxb";
  static string[] IImageFormatMetadata<PixiboxFile>.FileExtensions => [".pxb"];
  static PixiboxFile IImageFormatReader<PixiboxFile>.FromSpan(ReadOnlySpan<byte> data) => PixiboxReader.FromSpan(data);
  static byte[] IImageFormatWriter<PixiboxFile>.ToBytes(PixiboxFile file) => PixiboxWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<PixiboxFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<PixiboxFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length < Signature.Length ? null : header[..Signature.Length].SequenceEqual(Signature);

  /// <summary>Image width in pixels, as the header states it.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, as the header states it.</summary>
  public int Height { get; init; }

  /// <summary>The picture, three bytes a pixel, red first, from the top row down.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PixiboxFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };

  /// <summary>Creates a Pixibox RLE picture from any source image.</summary>
  public static PixiboxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > ushort.MaxValue || image.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"Pixibox dimensions must fit 16-bit fields; got {image.Width}x{image.Height}.", nameof(image));
    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    return new() { Width = rgb.Width, Height = rgb.Height, PixelData = rgb.PixelData[..] };
  }
}