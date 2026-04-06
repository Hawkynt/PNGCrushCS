using System;
using FileFormat.Core;

namespace FileFormat.IconLibrary;

/// <summary>In-memory representation of a Windows Icon Library (ICL) container.</summary>
public readonly record struct IconLibraryFile : IImageFormatReader<IconLibraryFile>, IImageToRawImage<IconLibraryFile>, IImageFormatWriter<IconLibraryFile> {

  /// <summary>Default icon dimensions when not detectable.</summary>
  internal const int DefaultSize = 32;

  static string IImageFormatMetadata<IconLibraryFile>.PrimaryExtension => ".icl";
  static string[] IImageFormatMetadata<IconLibraryFile>.FileExtensions => [".icl"];
  static IconLibraryFile IImageFormatReader<IconLibraryFile>.FromSpan(ReadOnlySpan<byte> data) => IconLibraryReader.FromSpan(data);
  static byte[] IImageFormatWriter<IconLibraryFile>.ToBytes(IconLibraryFile file) => IconLibraryWriter.ToBytes(file);

  /// <summary>Icon width (default 32).</summary>
  public int Width { get; init; }

  /// <summary>Icon height (default 32).</summary>
  public int Height { get; init; }

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts to a placeholder Rgb24 raw image.</summary>
  public static RawImage ToRawImage(IconLibraryFile file) {

    var pixelData = new byte[file.Width * file.Height * 3];
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = pixelData,
    };
  }

}
