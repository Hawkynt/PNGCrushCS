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
public readonly record struct RawGreyscaleFile : IImageFormatReader<RawGreyscaleFile>, IImageToRawImage<RawGreyscaleFile>, IImageFromRawImage<RawGreyscaleFile>, IImageFormatWriter<RawGreyscaleFile> {

  /// <summary>Bytes one pixel takes.</summary>
  public const int BytesPerPixel = 1;

  static string IImageFormatMetadata<RawGreyscaleFile>.PrimaryExtension => ".gry";

  /// <summary>The three names XnView files this one reader under, <c>.raw</c> among them.</summary>
  /// <remarks>
  /// Its catalogue puts <c>raw</c>, <c>gry</c> and <c>grey</c> on a single row called "Raw" served
  /// by a single reader whose channel type defaults to greyscale, and that reader is this one. Only
  /// the two that spell out grey were claimed here, so a dump arriving as <c>.raw</c> was offered to
  /// the camera-raw reader alone — which wants a TIFF byte-order mark and refuses a file carrying
  /// only pixels. Both readers hold the name now: that one takes anything that really does open with
  /// a byte-order mark, and this one takes the bare dump.
  /// <para/>
  /// The name is not read as freely as the other two, though. See <see cref="SizeOfBareDump"/>.
  /// </remarks>
  static string[] IImageFormatMetadata<RawGreyscaleFile>.FileExtensions => [".gry", ".grey", ".raw"];
  static RawGreyscaleFile IImageFormatReader<RawGreyscaleFile>.FromSpan(ReadOnlySpan<byte> data) => RawGreyscaleReader.FromSpan(data);
  static RawGreyscaleFile IImageFormatReader<RawGreyscaleFile>.FromFile(FileInfo file) => RawGreyscaleReader.FromFile(file);
  static byte[] IImageFormatWriter<RawGreyscaleFile>.ToBytes(RawGreyscaleFile file) => RawGreyscaleWriter.ToBytes(file);
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

  /// <summary>
  /// Which picture a dump of a given length is when its name did not say greyscale.
  /// </summary>
  /// <remarks>
  /// <c>.gry</c> and <c>.grey</c> name the channel type, so one byte a pixel is settled and the
  /// length only has to place the shape. <c>.raw</c> names the row rather than the member of it: the
  /// same converter writes three bytes a pixel under that name whenever the picture handed to it had
  /// colour, and four when it had an alpha as well.
  /// <para/>
  /// A length can then mean two shapes at once. 230,400 bytes is 480 by 480 in grey and 320 by 240
  /// in colour, both of them sizes in the table, and 307,200 is 640 by 480 in grey and 320 by 240
  /// with an alpha. Nothing in a file that is only pixels distinguishes them, so such a length is
  /// refused here rather than drawn at whichever reading the table reaches first. A picture at the
  /// wrong shape looks like a reading and is not one.
  /// <para/>
  /// Only the reading is held to this. The writer still produces greyscale lengths and the two names
  /// that spell out grey still place every one of them, so nothing that could be read before stops
  /// being readable.
  /// </remarks>
  internal static (int Width, int Height) SizeOfBareDump(int length) {
    var grey = (Width: 0, Height: 0);
    var colour = (Width: 0, Height: 0);

    foreach (var (width, height) in KnownResolutions) {
      var pixels = (long)width * height;

      if (grey.Width == 0 && pixels * BytesPerPixel == length)
        grey = (width, height);

      // The same length read as three bytes a pixel, or four with an alpha. Neither is read here;
      // what matters is only whether one of them could explain the file as well as grey does.
      if (colour.Width == 0 && (pixels * 3 == length || pixels * 4 == length))
        colour = (width, height);
    }

    if (grey.Width == 0)
      throw new InvalidDataException(
        colour.Width == 0
          ? $"A raw dump states no size, and {length} bytes is not one of the sizes it comes in."
          : $"A raw dump states no size, and {length} bytes is none of the greyscale sizes it comes "
            + $"in — it is a {colour.Width} by {colour.Height} colour one, which is not read here.");

    if (colour.Width != 0)
      throw new InvalidDataException(
        $"A raw dump states neither its size nor how many channels it has, and {length} bytes is "
        + $"both a {grey.Width} by {grey.Height} greyscale picture and a {colour.Width} by "
        + $"{colour.Height} colour one. Name it .gry to read it as greyscale.");

    return grey;
  }

  public static RawImage ToRawImage(RawGreyscaleFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Gray8,
    PixelData = file.PixelData[..],
  };

  /// <summary>
  /// Writes the picture at the nearest size the table holds, resampling it to get there.
  /// </summary>
  /// <remarks>
  /// A dump states no size, so the only thing that can ever be read back is a length the table
  /// recognises — write the pixels at their own size and the file is unreadable by this library and
  /// by the converter alike, since neither has anywhere to learn the shape from. So the picture is
  /// moved to a size rather than refused, which is what the rest of the writers here do when a format
  /// holds fewer shapes than a caller may hand it.
  /// <para/>
  /// Nearest is by the plain distance between the two shapes, and a tie goes to whichever entry the
  /// table lists first — the same order that settles the one length two entries share.
  /// </remarks>
  public static RawGreyscaleFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var (width, height) = _NearestResolution(image.Width, image.Height);
    var source = image.SampleTo(width, height).EnsureFormat(PixelFormat.Gray8);

    return new() { Width = width, Height = height, PixelData = source.PixelData[..] };
  }

  /// <summary>The size in the table closest to the one asked for.</summary>
  private static (int Width, int Height) _NearestResolution(int width, int height) {
    var best = KnownResolutions[0];
    var bestDistance = long.MaxValue;
    foreach (var (w, h) in KnownResolutions) {
      long dx = w - width;
      long dy = h - height;
      var distance = dx * dx + dy * dy;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = (w, h);
    }

    return best;
  }
}
