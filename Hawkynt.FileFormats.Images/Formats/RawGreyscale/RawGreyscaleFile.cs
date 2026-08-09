using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RawGreyscale;

/// <summary>In-memory representation of a raw greyscale dump: one byte a pixel and nothing else.</summary>
/// <remarks>
/// The whole file is the picture. No header, no signature, no length, no order — a byte is a grey,
/// zero is black, the first byte is the top-left pixel and the rows run down. That is confirmed
/// against XnView's own converter, which writes these: a 320 by 240 picture handed to it comes back
/// as exactly 76,800 bytes byte-identical to the pixels that went in.
/// <para/>
/// What it will not do is read one back. Its reader takes the size from the operator — there is a
/// size box in the dialog and a <c>-size</c> switch on the command line — and refuses a file that
/// carries only pixels, so nothing it writes can be opened again without being told the shape. That
/// is what left this row open, and it is the same thing that left the <c>.qtl</c> rows open until
/// they were closed the same way: by taking the length as the only evidence there is and requiring
/// it to be exactly one of the sizes the layout is made in.
/// <para/>
/// A stream matching none of them is refused rather than shown at a shape picked out of the air. A
/// wrong shape is worse than no picture: it is a picture that looks like a reading and is not one.
/// </remarks>
public readonly record struct RawGreyscaleFile : IImageFormatReader<RawGreyscaleFile>, IImageToRawImage<RawGreyscaleFile> {

  /// <summary>Bytes one pixel takes.</summary>
  public const int BytesPerPixel = 1;

  static string IImageFormatMetadata<RawGreyscaleFile>.PrimaryExtension => ".gry";
  static string[] IImageFormatMetadata<RawGreyscaleFile>.FileExtensions => [".gry", ".grey"];
  static RawGreyscaleFile IImageFormatReader<RawGreyscaleFile>.FromSpan(ReadOnlySpan<byte> data) => RawGreyscaleReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<RawGreyscaleFile>.VideoModes => [
    new("Greyscale", _Dimensions, [256]),
  ];

  /// <summary>
  /// The sizes a headerless greyscale dump is taken to be, in the order a tie is settled.
  /// </summary>
  /// <remarks>
  /// Two kinds of file end up under these names and both are in the list. The squares are the sizes
  /// a greyscale test picture or a sensor dump comes in, which is where a bare <c>.gry</c> most often
  /// comes from; the rest are the frame sizes XnView's own raw readers carry, this being one of that
  /// family and sharing its dialog.
  /// <para/>
  /// One length is claimed twice: 720 by 512 and 640 by 576 both come to 368,640 bytes. The first is
  /// taken, which is the order XnView's own table has them in, so that a stream of that length lands
  /// where that reader lands it.
  /// </remarks>
  internal static readonly (int Width, int Height)[] KnownResolutions = [
    (64, 64),
    (128, 128),
    (176, 144),
    (256, 256),
    (320, 240),
    (352, 240),
    (352, 288),
    (360, 240),
    (360, 288),
    (352, 480),
    (360, 480),
    (480, 480),
    (512, 512),
    (528, 480),
    (544, 480),
    (640, 480),
    (704, 480),
    (720, 480),
    (720, 486),
    (720, 512),
    (352, 576),
    (360, 576),
    (480, 576),
    (528, 576),
    (544, 576),
    (640, 576),
    (704, 576),
    (720, 576),
    (720, 608),
    (1024, 1024),
    (1280, 720),
    (1280, 1080),
    (1440, 1080),
    (1920, 1080),
    (2048, 2048),
  ];

  /// <summary>
  /// The sizes above, as the mode picker wants them — all of them, there being no others.
  /// </summary>
  /// <remarks>
  /// Declared after the table it is built from and not before it: a static field is initialised in
  /// the order it is written, so reading the table from a field above it hands back a null and takes
  /// the whole registry down with it when the type is first touched.
  /// </remarks>
  private static readonly (IntegerRange Width, IntegerRange Height)[] _Dimensions =
    Array.ConvertAll(KnownResolutions, size => ((IntegerRange)size.Width, (IntegerRange)size.Height));

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The picture as it lies, one byte a pixel from the top-left corner.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Which picture a dump of a given length is.</summary>
  internal static (int Width, int Height) SizeOf(int length) {
    foreach (var (width, height) in KnownResolutions)
      if (width * height * BytesPerPixel == length)
        return (width, height);

    throw new InvalidDataException(
      $"A raw greyscale dump states no size, and {length} bytes is not one of the sizes it comes in.");
  }

  public static RawImage ToRawImage(RawGreyscaleFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Gray8,
    PixelData = file.PixelData[..],
  };
}
