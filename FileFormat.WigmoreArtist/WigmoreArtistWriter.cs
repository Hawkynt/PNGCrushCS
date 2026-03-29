using System;

namespace FileFormat.WigmoreArtist;

/// <summary>Assembles Wigmore Artist (.wig) file bytes from a WigmoreArtistFile.</summary>
public static class WigmoreArtistWriter {

  public static byte[] ToBytes(WigmoreArtistFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[WigmoreArtistFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(WigmoreArtistFile.LoadAddressSize));

    return result;
  }
}
