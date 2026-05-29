using System;

namespace FileFormat.GigaPaint;

/// <summary>Assembles GigaPaint (.gih/.gig) file bytes from a GigaPaintFile.</summary>
public static class GigaPaintWriter {

  public static byte[] ToBytes(GigaPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[GigaPaintFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(GigaPaintFile.LoadAddressSize));

    return result;
  }
}
