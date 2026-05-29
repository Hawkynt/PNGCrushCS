using System;

namespace FileFormat.FliProfi;

/// <summary>Assembles FLI Profi (.fpr) file bytes from a FliProfiFile.</summary>
public static class FliProfiWriter {

  public static byte[] ToBytes(FliProfiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FliProfiFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(FliProfiFile.LoadAddressSize));

    return result;
  }
}
