using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IconLibrary;

/// <summary>In-memory representation of a Windows Icon Library (ICL) container.</summary>
public sealed class IconLibraryFile : IImageFileFormat<IconLibraryFile> {

  /// <summary>Default icon dimensions when not detectable.</summary>
  internal const int DefaultSize = 32;

  static string IImageFileFormat<IconLibraryFile>.PrimaryExtension => ".icl";
  static string[] IImageFileFormat<IconLibraryFile>.FileExtensions => [".icl"];
  static IconLibraryFile IImageFileFormat<IconLibraryFile>.FromFile(FileInfo file) => IconLibraryReader.FromFile(file);
  static IconLibraryFile IImageFileFormat<IconLibraryFile>.FromBytes(byte[] data) => IconLibraryReader.FromBytes(data);
  static IconLibraryFile IImageFileFormat<IconLibraryFile>.FromStream(Stream stream) => IconLibraryReader.FromStream(stream);
  static RawImage IImageFileFormat<IconLibraryFile>.ToRawImage(IconLibraryFile file) => ToRawImage(file);
  static IconLibraryFile IImageFileFormat<IconLibraryFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<IconLibraryFile>.ToBytes(IconLibraryFile file) => IconLibraryWriter.ToBytes(file);

  /// <summary>Icon width (default 32).</summary>
  public int Width { get; init; } = DefaultSize;

  /// <summary>Icon height (default 32).</summary>
  public int Height { get; init; } = DefaultSize;

  /// <summary>Raw file data.</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>Converts to a placeholder Rgb24 raw image.</summary>
  public static RawImage ToRawImage(IconLibraryFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelData = new byte[file.Width * file.Height * 3];
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = pixelData,
    };
  }

  /// <summary>Not supported. Icon libraries require NE/PE resource parsing.</summary>
  public static IconLibraryFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to IconLibraryFile is not supported.");
  }
}
