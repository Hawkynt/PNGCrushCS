using System;
using System.IO;

namespace FileFormat.SinbadSlideshow;

/// <summary>Reads Atari ST Sinbad Slideshow files from bytes, streams, or file paths.</summary>
public static class SinbadSlideshowReader {

  public static SinbadSlideshowFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sinbad Slideshow file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static SinbadSlideshowFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static SinbadSlideshowFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SinbadSlideshowFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid Sinbad Slideshow file: expected at least {SinbadSlideshowFile.FileSize} bytes, got {data.Length}.");

    var header = SinbadSlideshowHeader.ReadFrom(data);

    return new SinbadSlideshowFile {
      Palette = header.Palette,
      PixelData = data.Slice(SinbadSlideshowFile.PaletteSize, SinbadSlideshowFile.PixelDataSize).ToArray(),
    };
  }

  public static SinbadSlideshowFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
