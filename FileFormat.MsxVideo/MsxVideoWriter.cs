using System;

namespace FileFormat.MsxVideo;

/// <summary>Assembles Video MSX file bytes from a <see cref="MsxVideoFile"/>.</summary>
public static class MsxVideoWriter {

  public static byte[] ToBytes(MsxVideoFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MsxVideoFile.ExpectedFileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, MsxVideoFile.ExpectedFileSize)).CopyTo(result);
    return result;
  }
}
