using System;

namespace FileFormat.Bfli;

/// <summary>Assembles BFLI (.bfl/.bfli) file bytes from a BfliFile.</summary>
public static class BfliWriter {

  public static byte[] ToBytes(BfliFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[BfliFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(BfliFile.LoadAddressSize));

    return result;
  }
}
