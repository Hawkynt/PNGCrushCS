using System;
using FileFormat.Core;

namespace FileFormat.InShape;

/// <summary>In-memory representation of an InShape picture (.iim).</summary>
/// <remarks>
/// A Falcon image format that spans the whole range from one bit a pixel to twenty-four, chosen by
/// a byte in the header. It is a scanner and processing program's working format rather than a
/// screen dump, which is why it stores true colour at all on a machine that could not display it.
/// <para/>
/// Its greyscale is stored inverted — 0 is white — because the values came off a scanner measuring
/// how much light a page absorbed rather than how much it emitted. The 32-bit form pads each pixel
/// to four bytes and puts the padding first, so the colours start one byte later than the header
/// ends.
/// </remarks>
public readonly record struct InShapeFile
  : IImageFormatReader<InShapeFile>, IImageToRawImage<InShapeFile>,
    IImageFromRawImage<InShapeFile>, IImageFormatWriter<InShapeFile> {

  /// <summary>The text every file starts with.</summary>
  public const string Signature = "IS_IMAGE";

  /// <summary>The widest and tallest picture the sixteen-bit fields can state.</summary>
  public const int MaximumExtent = 65535;

  /// <summary>Offset of the pixels.</summary>
  public const int PixelsOffset = 16;

  /// <summary>The mode byte of the one-bit form.</summary>
  public const byte MonochromeMode = 0;

  /// <summary>The mode byte of the inverted greyscale form.</summary>
  public const byte GrayscaleMode = 1;

  /// <summary>The mode byte of the three-bytes-a-pixel form.</summary>
  public const byte TrueColorMode = 4;

  /// <summary>The mode byte of the four-bytes-a-pixel form.</summary>
  public const byte PaddedTrueColorMode = 5;

  static string IImageFormatMetadata<InShapeFile>.PrimaryExtension => ".iim";
  static string[] IImageFormatMetadata<InShapeFile>.FileExtensions => [".iim"];
  static InShapeFile IImageFormatReader<InShapeFile>.FromSpan(ReadOnlySpan<byte> data)
    => InShapeReader.FromSpan(data);
  static byte[] IImageFormatWriter<InShapeFile>.ToBytes(InShapeFile file) => InShapeWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<InShapeFile>.VideoModes => [
    new("InShape", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Which of the four forms the file holds.</summary>
  public byte Mode { get; init; }

  public static RawImage ToRawImage(InShapeFile file) {
    var data = file.Data ?? [];
    var count = file.Width * file.Height;

    switch (file.Mode) {
      case MonochromeMode: {
        var stride = (file.Width + 7) >> 3;
        var pixels = new byte[count];
        for (var y = 0; y < file.Height; ++y)
        for (var x = 0; x < file.Width; ++x) {
          var at = PixelsOffset + y * stride + (x >> 3);
          if (at < data.Length && ((data[at] >> (~x & 7)) & 1) != 0)
            pixels[y * file.Width + x] = 1;
        }

        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Indexed8,
          PixelData = pixels,
          Palette = [255, 255, 255, 0, 0, 0],
          PaletteCount = 2,
        };
      }

      case GrayscaleMode: {
        // The samples measure absorbed light, so a high value is dark.
        var pixels = new byte[count];
        for (var i = 0; i < count; ++i)
          pixels[i] = (byte)(data[PixelsOffset + i] ^ 255);

        return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Gray8, PixelData = pixels };
      }

      default: {
        var step = file.Mode == TrueColorMode ? 3 : 4;
        var offset = file.Mode == TrueColorMode ? PixelsOffset : PixelsOffset + 1;
        var rgb = new byte[count * 3];
        for (var i = 0; i < count; ++i)
          data.AsSpan(offset + i * step, 3).CopyTo(rgb.AsSpan(i * 3));

        return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
      }
    }
  }

  /// <summary>Writes a picture in the three-bytes-a-pixel form, at whatever size it comes in.</summary>
  /// <remarks>
  /// Of the four forms this is the only one that loses nothing, and the header states the size, so
  /// there is neither a reduction nor a scaling to make. The greyscale form is not chosen even for a
  /// grey picture: it is stored inverted, and a reader that does not know that shows the negative.
  /// </remarks>
  public static InShapeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width > MaximumExtent || image.Height > MaximumExtent)
      throw new ArgumentException(
        $"An InShape header states its size in sixteen bits, so {image.Width}x{image.Height} cannot be written.",
        nameof(image));

    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    var data = new byte[PixelsOffset + image.Width * image.Height * 3];
    rgb.PixelData.AsSpan(0, data.Length - PixelsOffset).CopyTo(data.AsSpan(PixelsOffset));

    return new() { Data = data, Width = image.Width, Height = image.Height, Mode = TrueColorMode };
  }
}
