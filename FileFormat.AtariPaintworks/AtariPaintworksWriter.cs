using System;

namespace FileFormat.AtariPaintworks;

/// <summary>Assembles Atari ST Paintworks/GFA/DeskPic file bytes from an AtariPaintworksFile.</summary>
public static class AtariPaintworksWriter {

  /// <summary>Standard file size: 32-byte palette + 32000-byte pixel data.</summary>
  private const int _FILE_SIZE = AtariPaintworksHeader.StructSize + 32000;

  public static byte[] ToBytes(AtariPaintworksFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[_FILE_SIZE];
    var span = result.AsSpan();

    var header = new AtariPaintworksHeader(file.Palette);
    header.WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length))
      .CopyTo(result.AsSpan(AtariPaintworksHeader.StructSize));

    return result;
  }
}
