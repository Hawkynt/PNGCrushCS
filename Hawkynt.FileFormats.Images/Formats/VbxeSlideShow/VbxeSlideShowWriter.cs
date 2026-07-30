using System;
using System.IO;

namespace FileFormat.VbxeSlideShow;

/// <summary>Writes SlideShow for VBXE pictures to bytes, streams, or file paths.</summary>
public static class VbxeSlideShowWriter {

  public static byte[] ToBytes(VbxeSlideShowFile file) {
    var data = new byte[VbxeSlideShowFile.FileSize];

    var pixels = file.PixelData ?? [];
    pixels.AsSpan(0, Math.Min(pixels.Length, VbxeSlideShowFile.PixelDataSize)).CopyTo(data);

    var palette = file.Palette ?? [];
    for (var i = 0; i < VbxeSlideShowFile.ColorCount; ++i)
    for (var channel = 0; channel < 3; ++channel) {
      var source = i * 3 + channel;
      if (source < palette.Length)
        data[VbxeSlideShowFile.PaletteOffset + channel * VbxeSlideShowFile.ColorCount + i] = palette[source];
    }

    return data;
  }

  public static void ToStream(VbxeSlideShowFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    var data = ToBytes(file);
    stream.Write(data, 0, data.Length);
  }

  public static void ToFile(VbxeSlideShowFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
