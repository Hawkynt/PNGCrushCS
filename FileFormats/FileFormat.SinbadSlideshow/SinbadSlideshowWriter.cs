using System;

namespace FileFormat.SinbadSlideshow;

/// <summary>Assembles Atari ST Sinbad Slideshow file bytes from a <see cref="SinbadSlideshowFile"/>.</summary>
public static class SinbadSlideshowWriter {

  public static byte[] ToBytes(SinbadSlideshowFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[SinbadSlideshowFile.FileSize];

    new SinbadSlideshowHeader(file.Palette).WriteTo(result);

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, SinbadSlideshowFile.PixelDataSize))
      .CopyTo(result.AsSpan(SinbadSlideshowFile.PaletteSize));

    return result;
  }
}
