using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Miff;

/// <summary>In-memory representation of a MIFF (ImageMagick) image.</summary>
[FormatMagicBytes([0x69, 0x64, 0x3D, 0x49, 0x6D, 0x61, 0x67, 0x65, 0x4D, 0x61, 0x67, 0x69, 0x63, 0x6B])]
public readonly record struct MiffFile : IImageFormatReader<MiffFile>, IImageToRawImage<MiffFile>, IImageFromRawImage<MiffFile>, IImageFormatWriter<MiffFile> {

  static string IImageFormatMetadata<MiffFile>.PrimaryExtension => ".miff";
  static string[] IImageFormatMetadata<MiffFile>.FileExtensions => [".miff", ".mif"];
  static MiffFile IImageFormatReader<MiffFile>.FromSpan(ReadOnlySpan<byte> data) => MiffReader.FromSpan(data);
  static byte[] IImageFormatWriter<MiffFile>.ToBytes(MiffFile file) => MiffWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Depth { get; init; }
  public MiffColorClass ColorClass { get; init; }
  public MiffCompression Compression { get; init; }
  public string Colorspace { get; init; }
  public string Type { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }

  /// <summary>Scales each sample down to eight bits when the file stores more than eight.</summary>
  /// <remarks>
  /// The header states a depth, and it is routinely sixteen or thirty-two — the reference tool writes
  /// sixteen by default. Reading those bytes as though each were a sample does not fail; it produces
  /// a picture of the right size in which every other value is a low byte, which looks like noise
  /// laid over the picture rather than like a misread.
  /// <para/>
  /// The sample is scaled by the whole range and rounded, which is what ImageMagick does with its own
  /// files. Taking the leading byte instead is a floor, and it lands one level low on two fifths of
  /// the values a sixteen-bit sample can take: on a 61x37 reference that was 366 of 2257 pixels dark
  /// by one, on every truecolour file, which is small but is not zero and hides anything smaller.
  /// <para/>
  /// The top two bytes are enough to decide the answer for any depth: the bytes below them move the
  /// scaled sample by less than one part in sixty-five thousand, which cannot change a value in 255.
  /// </remarks>
  private static byte[] _NarrowSamples(byte[] data, int depth) {
    if (depth <= 8)
      return data[..];

    var step = depth / 8;
    var narrowed = new byte[data.Length / step];
    for (var i = 0; i < narrowed.Length; ++i) {
      var sample = (data[i * step] << 8) | data[i * step + 1];
      narrowed[i] = (byte)((sample * 255 + 32767) / 65535);
    }

    return narrowed;
  }

  public static RawImage ToRawImage(MiffFile file) {
    var pixelData = _NarrowSamples(file.PixelData, file.Depth);

    if (file.ColorClass == MiffColorClass.PseudoClass && file.Palette != null) {
      // An index and an alpha sample per pixel, which Indexed8 has nowhere to put: taking the data
      // as indices would read every second byte as one. ImageMagick writes such a file for
      // `-colors 16 -alpha set`, and reading the channel count properly is what makes it visible.
      if (file.Type != null && file.Type.Contains("Alpha", StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("A MIFF palette with an alpha channel is not supported; its indices carry an alpha sample the indexed pixel format cannot hold.");

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = file.PixelData[..],
        Palette = file.Palette[..],
        PaletteCount = file.Palette.Length / 3,
      };
    }

    var format = file.Type switch {
      "TrueColorAlpha" or "TrueColorMatte" => PixelFormat.Rgba32,
      "GrayscaleAlpha" or "GrayscaleMatte" => PixelFormat.GrayAlpha16,
      "Grayscale" => PixelFormat.Gray8,
      _ => PixelFormat.Rgb24,
    };

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = pixelData,
    };
  }

  public static MiffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Depth = 8,
          ColorClass = MiffColorClass.DirectClass,
          Colorspace = "sRGB",
          Type = "TrueColor",
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgba32:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Depth = 8,
          ColorClass = MiffColorClass.DirectClass,
          Colorspace = "sRGB",
          Type = "TrueColorAlpha",
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Depth = 8,
          ColorClass = MiffColorClass.DirectClass,
          Colorspace = "Gray",
          Type = "Grayscale",
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Indexed8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Depth = 8,
          ColorClass = MiffColorClass.PseudoClass,
          Colorspace = "sRGB",
          Type = "TrueColor",
          PixelData = image.PixelData[..],
          Palette = image.Palette != null ? image.Palette[..] : null,
        };
      default:
        image = PixelConverter.Convert(image, PixelFormat.Rgba32);
        return new() {
          Width = image.Width,
          Height = image.Height,
          Depth = 8,
          ColorClass = MiffColorClass.DirectClass,
          Colorspace = "sRGB",
          Type = "TrueColorAlpha",
          PixelData = image.PixelData[..],
        };
    }
  }
}
