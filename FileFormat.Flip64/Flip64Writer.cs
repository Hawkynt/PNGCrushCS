using System;

namespace FileFormat.Flip64;

/// <summary>Assembles Flip (.fbi) file bytes from a Flip64File.</summary>
public static class Flip64Writer {

  public static byte[] ToBytes(Flip64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[Flip64File.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(Flip64File.LoadAddressSize));

    return result;
  }
}
