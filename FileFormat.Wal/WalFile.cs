using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Wal;

/// <summary>In-memory representation of a WAL (Quake 2 Texture) file.</summary>
public sealed class WalFile : IImageFileFormat<WalFile> {

  static string IImageFileFormat<WalFile>.PrimaryExtension => ".wal";
  static string[] IImageFileFormat<WalFile>.FileExtensions => [".wal"];
  static WalFile IImageFileFormat<WalFile>.FromFile(FileInfo file) => WalReader.FromFile(file);
  static WalFile IImageFileFormat<WalFile>.FromBytes(byte[] data) => WalReader.FromBytes(data);
  static WalFile IImageFileFormat<WalFile>.FromStream(Stream stream) => WalReader.FromStream(stream);
  static byte[] IImageFileFormat<WalFile>.ToBytes(WalFile file) => WalWriter.ToBytes(file);
  public string Name { get; init; } = string.Empty;
  public int Width { get; init; }
  public int Height { get; init; }
  public string NextFrameName { get; init; } = string.Empty;
  public uint Flags { get; init; }
  public uint Contents { get; init; }
  public uint Value { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[][]? MipMaps { get; init; }

  /// <summary>Converts a WAL file to a <see cref="RawImage"/>. No palette is embedded (uses external Quake 2 palette).</summary>
  public static RawImage ToRawImage(WalFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      PaletteCount = 256
    };
  }

  /// <summary>Creates a WAL file from a <see cref="RawImage"/>. Must be Indexed8.</summary>
  public static WalFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"WAL requires Indexed8 pixel format, got {image.Format}.", nameof(image));

    return new WalFile {
      Name = "texture",
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..]
    };
  }
}
