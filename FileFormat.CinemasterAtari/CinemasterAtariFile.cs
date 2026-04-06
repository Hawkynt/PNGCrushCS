using System;
using FileFormat.Core;

namespace FileFormat.CinemasterAtari;

/// <summary>In-memory representation of a Cinemaster Atari animation (.cin8) file.</summary>
public readonly record struct CinemasterAtariFile : IImageFormatReader<CinemasterAtariFile>, IImageToRawImage<CinemasterAtariFile>, IImageFromRawImage<CinemasterAtariFile>, IImageFormatWriter<CinemasterAtariFile> {

  /// <summary>Size of one frame in bytes (40 bytes/row x 192 rows).</summary>
  public const int FrameSize = 7680;

  /// <summary>Size of the frame count header in bytes.</summary>
  public const int HeaderSize = 2;

  /// <summary>Width in pixels per frame.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Height in pixels per frame.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per row in each frame.</summary>
  internal const int BytesPerRow = 40;

  static string IImageFormatMetadata<CinemasterAtariFile>.PrimaryExtension => ".cin8";
  static string[] IImageFormatMetadata<CinemasterAtariFile>.FileExtensions => [".cin8"];
  static CinemasterAtariFile IImageFormatReader<CinemasterAtariFile>.FromSpan(ReadOnlySpan<byte> data) => CinemasterAtariReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<CinemasterAtariFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<CinemasterAtariFile>.ToBytes(CinemasterAtariFile file) => CinemasterAtariWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Number of frames stored in the header.</summary>
  public ushort FrameCount { get; init; }

  /// <summary>Animation frames (each frame is 7680 bytes of 1bpp MSB-first screen data).</summary>
  public byte[][] Frames { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the first frame to an Indexed1 raw image (320x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(CinemasterAtariFile file) {

    var pixelData = new byte[FrameSize];
    if (file.Frames.Length > 0)
      file.Frames[0].AsSpan(0, Math.Min(file.Frames[0].Length, FrameSize)).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a single-frame Cinemaster file from an Indexed1 raw image (320x192).</summary>
  public static CinemasterAtariFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var frameData = new byte[FrameSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, FrameSize)).CopyTo(frameData);

    return new() {
      FrameCount = 1,
      Frames = [frameData],
    };
  }
}
