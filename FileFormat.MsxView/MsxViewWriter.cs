using System;

namespace FileFormat.MsxView;

/// <summary>Assembles MSX View file bytes from a <see cref="MsxViewFile"/>.</summary>
public static class MsxViewWriter {

  public static byte[] ToBytes(MsxViewFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MsxViewFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, MsxViewFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
