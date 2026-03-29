using System;
using System.Buffers.Binary;

namespace FileFormat.SinbadSlideshow;

/// <summary>Assembles Atari ST Sinbad Slideshow file bytes from a <see cref="SinbadSlideshowFile"/>.</summary>
public static class SinbadSlideshowWriter {

  public static byte[] ToBytes(SinbadSlideshowFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[SinbadSlideshowFile.FileSize];
    var span = result.AsSpan();

    for (var i = 0; i < 16; ++i)
      BinaryPrimitives.WriteInt16BigEndian(span.Slice(i * 2, 2), i < file.Palette.Length ? file.Palette[i] : (short)0);

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, SinbadSlideshowFile.PixelDataSize))
      .CopyTo(span.Slice(SinbadSlideshowFile.PaletteSize));

    return result;
  }
}
