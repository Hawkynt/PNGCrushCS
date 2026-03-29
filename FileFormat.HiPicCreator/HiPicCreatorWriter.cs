using System;

namespace FileFormat.HiPicCreator;

/// <summary>Assembles Hi-Pic Creator (.hpc) file bytes from a HiPicCreatorFile.</summary>
public static class HiPicCreatorWriter {

  public static byte[] ToBytes(HiPicCreatorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[HiPicCreatorFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(HiPicCreatorFile.LoadAddressSize));

    return result;
  }
}
