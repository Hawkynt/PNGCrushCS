using System;

namespace FileFormat.Ffli;

/// <summary>Assembles Full FLI (.ffli) file bytes from an FfliFile.</summary>
public static class FfliWriter {

  public static byte[] ToBytes(FfliFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FfliFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(FfliFile.LoadAddressSize));

    return result;
  }
}
