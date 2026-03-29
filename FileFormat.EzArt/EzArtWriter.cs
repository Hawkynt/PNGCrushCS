using System;
using System.Buffers.Binary;

namespace FileFormat.EzArt;

/// <summary>Assembles EZ-Art Professional (.eza) file bytes from an EzArtFile.</summary>
public static class EzArtWriter {

  private const int _PALETTE_SIZE = 32;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _PALETTE_ENTRIES = 16;

  public static byte[] ToBytes(EzArtFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[EzArtFile.FileSize];
    var span = result.AsSpan();

    for (var i = 0; i < _PALETTE_ENTRIES; ++i)
      BinaryPrimitives.WriteInt16BigEndian(span[(i * 2)..], file.Palette[i]);

    file.PixelData.AsSpan(0, Math.Min(_PIXEL_DATA_SIZE, file.PixelData.Length)).CopyTo(result.AsSpan(_PALETTE_SIZE));

    return result;
  }
}
