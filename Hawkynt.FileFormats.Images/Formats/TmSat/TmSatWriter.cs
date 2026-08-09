using System;
using System.IO;

namespace FileFormat.TmSat;

/// <summary>Writes TMSAT-1 frames (.imi).</summary>
public static class TmSatWriter {

  public static byte[] ToBytes(TmSatFile file) {
    if (file.PixelData == null)
      throw new InvalidOperationException("No frame to write.");
    if (file.PixelData.Length != TmSatFile.FileSize)
      throw new InvalidOperationException($"A TMSat frame is exactly {TmSatFile.FileSize} bytes and {file.PixelData.Length} were given.");

    return file.PixelData[..];
  }

  public static void ToStream(TmSatFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var bytes = ToBytes(file);
    stream.Write(bytes, 0, bytes.Length);
  }

  public static void ToFile(TmSatFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
