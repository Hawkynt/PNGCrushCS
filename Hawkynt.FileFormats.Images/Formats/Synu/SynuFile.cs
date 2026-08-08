using System;
using FileFormat.Core;

namespace FileFormat.Synu;

/// <summary>In-memory representation of a Synu picture (.synu, .syn).</summary>
/// <remarks>
/// A text header and then the samples. The first line names the file and how many bytes of picture
/// follow — <c>image 4L 921600b</c> — and the lines after it give the width, the height, the number
/// of channels and what those channels mean, one to a line:
/// <code>
///   image 4L 921600b
///   640
///   480
///   3
///   rgb
/// </code>
/// The picture starts on the byte after that last newline, one byte a channel, rows from the bottom
/// up. Everything needed is stated, so nothing here is guessed at.
/// </remarks>
public readonly record struct SynuFile
  : IImageFormatReader<SynuFile>, IImageToRawImage<SynuFile>,
    IImageFromRawImage<SynuFile>, IImageFormatWriter<SynuFile> {

  /// <summary>What the first line opens with.</summary>
  public const string Marker = "image ";

  static string IImageFormatMetadata<SynuFile>.PrimaryExtension => ".synu";
  static string[] IImageFormatMetadata<SynuFile>.FileExtensions => [".synu", ".syn"];
  static SynuFile IImageFormatReader<SynuFile>.FromSpan(ReadOnlySpan<byte> data) => SynuReader.FromSpan(data);
  static byte[] IImageFormatWriter<SynuFile>.ToBytes(SynuFile file) => SynuWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SynuFile>.VideoModes => [
    new("Colour", [(new IntegerRange(1, ushort.MaxValue), new IntegerRange(1, ushort.MaxValue))])
  ];

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>Channels a pixel has: three for colour, one for grey.</summary>
  public int Channels { get; init; }

  /// <summary>What the channels mean, as the header words it.</summary>
  public string ColorSpace { get; init; }

  /// <summary>The samples, already turned the right way up.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SynuFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = file.Channels == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24,
    PixelData = (file.PixelData ?? [])[..],
  };

  public static SynuFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Channels = image.Format == PixelFormat.Gray8 ? 1 : 3,
      ColorSpace = image.Format == PixelFormat.Gray8 ? "bw" : "rgb",
      PixelData = image.PixelData[..],
    };
  }
}
