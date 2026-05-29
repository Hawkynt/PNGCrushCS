using System;

namespace FileFormat.DoodlePacked;

/// <summary>Assembles Doodle Packed (RLE-compressed C64 hires) file bytes from a DoodlePackedFile.</summary>
public static class DoodlePackedWriter {

  public static byte[] ToBytes(DoodlePackedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Combine bitmap + screen into one payload
    var payload = new byte[DoodlePackedFile.DecompressedSize];
    file.BitmapData.AsSpan(0, DoodlePackedFile.BitmapDataSize).CopyTo(payload.AsSpan(0));
    file.ScreenData.AsSpan(0, DoodlePackedFile.ScreenDataSize).CopyTo(payload.AsSpan(DoodlePackedFile.BitmapDataSize));

    // RLE compress
    var compressed = DoodlePackedFile.RleEncode(payload);

    // Assemble: load address + compressed data
    var result = new byte[DoodlePackedFile.LoadAddressSize + compressed.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    compressed.AsSpan(0, compressed.Length).CopyTo(result.AsSpan(DoodlePackedFile.LoadAddressSize));

    return result;
  }
}
