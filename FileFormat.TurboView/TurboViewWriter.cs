using System;

namespace FileFormat.TurboView;

/// <summary>Assembles Turbo View file bytes from an in-memory representation.</summary>
public static class TurboViewWriter {

  private const int _HEADER_SIZE = 34;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _FILE_SIZE = _HEADER_SIZE + _PIXEL_DATA_SIZE;

  public static byte[] ToBytes(TurboViewFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[_FILE_SIZE];
    var span = result.AsSpan();

    // 2-byte resolution big-endian (0 = low-res)
    span[0] = 0;
    span[1] = 0;

    for (var i = 0; i < 16; ++i) {
      var offset = 2 + i * 2;
      var entry = file.Palette[i];
      span[offset] = (byte)((entry >> 8) & 0xFF);
      span[offset + 1] = (byte)(entry & 0xFF);
    }

    file.PixelData.AsSpan(0, Math.Min(_PIXEL_DATA_SIZE, file.PixelData.Length)).CopyTo(result.AsSpan(_HEADER_SIZE));

    return result;
  }
}
