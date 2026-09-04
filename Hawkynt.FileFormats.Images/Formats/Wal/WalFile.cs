using System;
using FileFormat.Core;

namespace FileFormat.Wal;

/// <summary>In-memory representation of a WAL (Quake 2 Texture) file.</summary>
public readonly record struct WalFile : IImageFormatReader<WalFile>, IImageToRawImage<WalFile>, IImageFromRawImage<WalFile>, IImageFormatWriter<WalFile> {

  static string IImageFormatMetadata<WalFile>.PrimaryExtension => ".wal";
  static string[] IImageFormatMetadata<WalFile>.FileExtensions => [".wal"];
  static WalFile IImageFormatReader<WalFile>.FromSpan(ReadOnlySpan<byte> data) => WalReader.FromSpan(data);
  static byte[] IImageFormatWriter<WalFile>.ToBytes(WalFile file) => WalWriter.ToBytes(file);
  public string Name { get; init; }
  public int Width { get; init; }
  public int Height { get; init; }
  public string NextFrameName { get; init; }
  public uint Flags { get; init; }
  public uint Contents { get; init; }
  public uint Value { get; init; }
  public byte[] PixelData { get; init; }
  public byte[][]? MipMaps { get; init; }

  /// <summary>Converts a WAL file to a <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// A WAL embeds no palette; Quake 2 colours it from <c>pics/colormap.pcx</c> in the game's own
  /// pak. Standing the ramp from <see cref="IndexedPalette"/> in its place keeps the texture
  /// drawable — declaring 256 colours and handing back a null palette made every conversion throw.
  /// </remarks>
  public static RawImage ToRawImage(WalFile file) {

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = IndexedPalette.GrayRamp(256),
      PaletteCount = 256
    };
  }

  /// <summary>Creates a WAL file from a <see cref="RawImage"/>. Must be Indexed8.</summary>
  public static WalFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);

    return new WalFile {
      Name = "texture",
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..]
    };
  }
}
