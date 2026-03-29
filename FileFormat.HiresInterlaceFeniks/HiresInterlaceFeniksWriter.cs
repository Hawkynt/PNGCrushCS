using System;

namespace FileFormat.HiresInterlaceFeniks;

/// <summary>Assembles Hires Interlace by Feniks (.hlf) file bytes from a HiresInterlaceFeniksFile.</summary>
public static class HiresInterlaceFeniksWriter {

  public static byte[] ToBytes(HiresInterlaceFeniksFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HiresInterlaceFeniksFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(HiresInterlaceFeniksFile.LoadAddressSize));

    return result;
  }
}
