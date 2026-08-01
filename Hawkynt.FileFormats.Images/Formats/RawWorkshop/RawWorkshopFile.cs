using System;
using FileFormat.Core;

namespace FileFormat.RawWorkshop;

/// <summary>In-memory representation of a Raw Workshop greyscale dump (.rwh, .rwl).</summary>
/// <remarks>
/// One byte a pixel and nothing else — no header, no palette, not even a magic number. The size is
/// the whole of the identification: three lengths are legal and each names one Falcon screen, so a
/// file that is not exactly one of them is not one of these.
/// <para/>
/// The byte is a grey level directly rather than an index into anything, which is why the format
/// can afford to store nothing beside it — but it runs the other way round from what that suggests:
/// zero is white and 255 is black, so the value is an amount of ink rather than of light.
/// </remarks>
public readonly record struct RawWorkshopFile
  : IImageFormatReader<RawWorkshopFile>, IImageToRawImage<RawWorkshopFile>,
    IImageFromRawImage<RawWorkshopFile>, IImageFormatWriter<RawWorkshopFile> {

  /// <summary>The three sizes a file may be, and the screen each names.</summary>
  public static (int Length, int Width, int Height)[] Screens => [
    (64000, 320, 200),
    (128000, 640, 200),
    (256000, 640, 400),
  ];

  static string IImageFormatMetadata<RawWorkshopFile>.PrimaryExtension => ".rwl";
  static string[] IImageFormatMetadata<RawWorkshopFile>.FileExtensions => [".rwl", ".rwh"];
  static RawWorkshopFile IImageFormatReader<RawWorkshopFile>.FromSpan(ReadOnlySpan<byte> data)
    => RawWorkshopReader.FromSpan(data);
  static byte[] IImageFormatWriter<RawWorkshopFile>.ToBytes(RawWorkshopFile file)
    => RawWorkshopWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<RawWorkshopFile>.VideoModes => [
    new("Falcon", [(320, 200), (640, 200), (640, 400)], [256])
  ];

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>One grey level a pixel.</summary>
  public byte[] Pixels { get; init; }

  public static RawImage ToRawImage(RawWorkshopFile file) {
    // Zero is white and 255 is black: the stored value is ink, not light.
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i)
      palette[i * 3] = palette[i * 3 + 1] = palette[i * 3 + 2] = (byte)(i ^ 255);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.Pixels ?? new byte[file.Width * file.Height],
      Palette = palette,
      PaletteCount = 256,
    };
  }

  /// <summary>Builds a dump at whichever of the three sizes the picture is nearest.</summary>
  /// <remarks>
  /// Only three lengths are legal, so a picture cannot simply keep its own size: it is sampled to
  /// the screen whose shape is closest, measured on the ratio rather than the area so that a wide
  /// picture does not land on a tall screen merely because the pixel counts agree.
  /// </remarks>
  public static RawWorkshopFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var wanted = (double)image.Width / image.Height;
    var (_, width, height) = Screens[0];
    var best = double.MaxValue;

    foreach (var (_, candidateWidth, candidateHeight) in Screens) {
      var distance = Math.Abs((double)candidateWidth / candidateHeight - wanted);
      if (distance >= best)
        continue;

      best = distance;
      (width, height) = (candidateWidth, candidateHeight);
    }

    var rgb = image.SampleTo(width, height);
    var pixels = new byte[width * height];

    for (var i = 0; i < pixels.Length; ++i) {
      var at = i * 3;
      var luminance = rgb.PixelData[at] * 77 + rgb.PixelData[at + 1] * 150 + rgb.PixelData[at + 2] * 29;
      pixels[i] = (byte)((luminance >> 8) ^ 255);
    }

    return new() { Width = width, Height = height, Pixels = pixels };
  }
}
