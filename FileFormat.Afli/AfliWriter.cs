using System;

namespace FileFormat.Afli;

/// <summary>Assembles AFLI (Advanced FLI) hires image file bytes from an AfliFile.</summary>
public static class AfliWriter {

  public static byte[] ToBytes(AfliFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[AfliFile.ExpectedFileSize];
    var offset = 0;

    // Load address (2 bytes, little-endian)
    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += AfliFile.LoadAddressSize;

    // Raw FLI data (9216 bytes)
    var copyLength = Math.Min(file.RawData.Length, AfliFile.RawDataSize);
    file.RawData.AsSpan(0, copyLength).CopyTo(result.AsSpan(offset));

    return result;
  }
}
