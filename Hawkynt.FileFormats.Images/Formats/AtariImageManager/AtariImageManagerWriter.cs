using System;
using System.IO;

namespace FileFormat.AtariImageManager;

/// <summary>Writes Atari Image Manager pictures to bytes, streams, or file paths.</summary>
public static class AtariImageManagerWriter {

  public static byte[] ToBytes(AtariImageManagerFile file) {
    var data = new byte[file.Size * file.Size];
    var pixels = file.PixelData ?? [];
    pixels.AsSpan(0, Math.Min(pixels.Length, data.Length)).CopyTo(data);

    return data;
  }

  public static void ToStream(AtariImageManagerFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var data = ToBytes(file);
    stream.Write(data, 0, data.Length);
  }

  public static void ToFile(AtariImageManagerFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
