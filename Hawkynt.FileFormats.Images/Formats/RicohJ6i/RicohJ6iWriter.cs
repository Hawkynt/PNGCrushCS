using System;

namespace FileFormat.RicohJ6i;

/// <summary>Assembles Ricoh J6I picture bytes.</summary>
public static class RicohJ6iWriter {

  public static byte[] ToBytes(RicohJ6iFile file) {
    var jpeg = file.JpegData ?? [];
    var result = new byte[RicohJ6iFile.HeaderSize + jpeg.Length];

    var header = file.Header ?? [];
    header.AsSpan(0, Math.Min(header.Length, RicohJ6iFile.HeaderSize)).CopyTo(result);
    jpeg.CopyTo(result.AsSpan(RicohJ6iFile.HeaderSize));

    return result;
  }
}
