using System;
using FileFormat.Core;

namespace FileFormat.GrfBitmap;

/// <summary>In-memory representation of a .grf bitmap whose header states its length.</summary>
/// <remarks>
/// Five bytes of header — the length of the bitmap as a little-endian word, then the address it
/// loads to, then a byte — and after that one bit a pixel, most significant bit leftmost.
/// <para/>
/// The width is taken as 256, which is what the tool draws and what the one sample is; the height
/// follows from the stated length, 32 bytes to a row. That is the part to be sceptical of if a
/// second sample ever turns up at another width, because nothing in the header distinguishes a
/// wider picture from a taller one. The length being stated is what makes the file identifiable at
/// all, and it is checked rather than assumed.
/// <para/>
/// <c>.grf</c> was claimed only by Profi, which takes 30848 bytes and refused this at 6154.
/// </remarks>
public readonly record struct GrfBitmapFile
  : IImageFormatReader<GrfBitmapFile>, IImageToRawImage<GrfBitmapFile>,
    IImageFromRawImage<GrfBitmapFile>, IImageFormatWriter<GrfBitmapFile> {

  /// <summary>The stated length, the load address, then one byte.</summary>
  public const int HeaderSize = 5;

  /// <summary>Pixels across, which the header does not state.</summary>
  public const int Width = 256;

  /// <summary>Bytes one row takes.</summary>
  public const int BytesPerRow = Width / 8;

  public const int ColorCount = 2;

  static string IImageFormatMetadata<GrfBitmapFile>.PrimaryExtension => ".grf";
  static string[] IImageFormatMetadata<GrfBitmapFile>.FileExtensions => [".grf"];
  static GrfBitmapFile IImageFormatReader<GrfBitmapFile>.FromSpan(ReadOnlySpan<byte> data) => GrfBitmapReader.FromSpan(data);
  static byte[] IImageFormatWriter<GrfBitmapFile>.ToBytes(GrfBitmapFile file) => GrfBitmapWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<GrfBitmapFile>.VideoModes => [
    new("Default", [(Width, new IntegerRange(1, 2048))], [ColorCount])
  ];

  public int Height { get; init; }

  /// <summary>Where the picture loads on the machine, which the header carries and this keeps.</summary>
  public ushort LoadAddress { get; init; }

  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(GrfBitmapFile file)
    => MonochromePage.Decode(file.PixelData ?? [], Width, file.Height, inkIsWhite: true);

  public static GrfBitmapFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Height < 1)
      throw new ArgumentException($"A picture needs at least one row; got {image.Height}.", nameof(image));

    var sampled = image.SampleTo(Width, image.Height);

    return new() {
      Height = image.Height,
      LoadAddress = 0,
      PixelData = MonochromePage.Encode(sampled, Width, image.Height, inkIsWhite: true),
    };
  }
}
