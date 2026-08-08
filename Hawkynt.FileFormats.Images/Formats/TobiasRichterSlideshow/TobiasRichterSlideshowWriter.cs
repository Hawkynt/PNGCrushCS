using System;
using System.IO;

namespace FileFormat.TobiasRichterSlideshow;

/// <summary>Assembles Tobias Richter Fullscreen Slideshow (.pci) file bytes.</summary>
public static class TobiasRichterSlideshowWriter {

  public static byte[] ToBytes(TobiasRichterSlideshowFile file) {
    var data = file.Data ?? [];
    if (data.Length != TobiasRichterSlideshowFile.FileSize)
      throw new InvalidDataException(
        $"A slideshow picture is {TobiasRichterSlideshowFile.FileSize} bytes, got {data.Length}.");

    return (byte[])data.Clone();
  }
}
