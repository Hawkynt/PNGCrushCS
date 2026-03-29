using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariPlayer;

/// <summary>In-memory representation of Atari 8-bit Player/Missile Graphics. 4 players arranged as 32x256 monochrome.</summary>
public sealed class AtariPlayerFile : IImageFileFormat<AtariPlayerFile> {

  /// <summary>Number of players.</summary>
  internal const int PlayerCount = 4;

  /// <summary>Width of each player in pixels.</summary>
  internal const int PlayerWidth = 8;

  /// <summary>Height of each player in pixels (scanlines).</summary>
  internal const int PlayerHeight = 256;

  /// <summary>Bytes per player (256 bytes, one byte per scanline).</summary>
  internal const int BytesPerPlayer = PlayerHeight;

  /// <summary>Image width in pixels (4 players x 8 pixels each).</summary>
  internal const int PixelWidth = PlayerCount * PlayerWidth;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = PlayerHeight;

  /// <summary>Exact file size in bytes (4 players x 256 bytes each).</summary>
  internal const int FileSize = PlayerCount * BytesPerPlayer;

  static string IImageFileFormat<AtariPlayerFile>.PrimaryExtension => ".pmg";
  static string[] IImageFileFormat<AtariPlayerFile>.FileExtensions => [".pmg", ".plm"];
  static FormatCapability IImageFileFormat<AtariPlayerFile>.Capabilities => FormatCapability.IndexedOnly;
  static AtariPlayerFile IImageFileFormat<AtariPlayerFile>.FromFile(FileInfo file) => AtariPlayerReader.FromFile(file);
  static AtariPlayerFile IImageFileFormat<AtariPlayerFile>.FromBytes(byte[] data) => AtariPlayerReader.FromBytes(data);
  static AtariPlayerFile IImageFileFormat<AtariPlayerFile>.FromStream(Stream stream) => AtariPlayerReader.FromStream(stream);
  static RawImage IImageFileFormat<AtariPlayerFile>.ToRawImage(AtariPlayerFile file) => ToRawImage(file);
  static AtariPlayerFile IImageFileFormat<AtariPlayerFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<AtariPlayerFile>.ToBytes(AtariPlayerFile file) => AtariPlayerWriter.ToBytes(file);

  /// <summary>Always 32.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 256.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw player data (1024 bytes: 4 players x 256 bytes each). Each byte is one scanline of 8 pixels, MSB-first.</summary>
  public byte[] PlayerData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the Player/Missile data to an Indexed1 raw image (32x256, B&amp;W palette). 4 player columns side by side.</summary>
  public static RawImage ToRawImage(AtariPlayerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rowStride = PixelWidth / 8; // 4 bytes per row
    var pixelData = new byte[rowStride * PixelHeight];

    for (var player = 0; player < PlayerCount; ++player)
      for (var line = 0; line < PlayerHeight; ++line) {
        var srcIndex = player * BytesPerPlayer + line;
        var b = srcIndex < file.PlayerData.Length ? file.PlayerData[srcIndex] : (byte)0;

        // Each player is 8 pixels wide = 1 byte in the output row
        var byteOffset = line * rowStride + player;
        pixelData[byteOffset] = b;
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates Player/Missile data from an Indexed1 raw image (32x256). Extracts 4 player columns.</summary>
  public static AtariPlayerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rowStride = PixelWidth / 8;
    var playerData = new byte[FileSize];

    for (var player = 0; player < PlayerCount; ++player)
      for (var line = 0; line < PlayerHeight; ++line) {
        var byteOffset = line * rowStride + player;
        playerData[player * BytesPerPlayer + line] = image.PixelData[byteOffset];
      }

    return new() { PlayerData = playerData };
  }
}
