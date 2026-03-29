using System;

namespace FileFormat.RockyInterlace;

/// <summary>Assembles Rocky Interlace (.rip) file bytes from a RockyInterlaceFile.</summary>
public static class RockyInterlaceWriter {

  public static byte[] ToBytes(RockyInterlaceFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[RockyInterlaceFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(RockyInterlaceFile.LoadAddressSize));

    return result;
  }
}
