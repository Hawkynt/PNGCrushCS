using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.SinbadSlideshow;

/// <summary>Reads Atari ST Sinbad Slideshow files from bytes, streams, or file paths.</summary>
public static class SinbadSlideshowReader {

  public static SinbadSlideshowFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sinbad Slideshow file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SinbadSlideshowFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static SinbadSlideshowFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < SinbadSlideshowFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid Sinbad Slideshow file: expected at least {SinbadSlideshowFile.FileSize} bytes, got {data.Length}.");

    var span = data;

    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(span.Slice(i * 2, 2));

    var pixelData = new byte[SinbadSlideshowFile.PixelDataSize];
    span.Slice(SinbadSlideshowFile.PaletteSize, SinbadSlideshowFile.PixelDataSize).CopyTo(pixelData);

    return new SinbadSlideshowFile {
      Palette = palette,
      PixelData = pixelData,
    };
    }

  public static SinbadSlideshowFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SinbadSlideshowFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid Sinbad Slideshow file: expected at least {SinbadSlideshowFile.FileSize} bytes, got {data.Length}.");

    var span = data.AsSpan();

    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(span.Slice(i * 2, 2));

    var pixelData = new byte[SinbadSlideshowFile.PixelDataSize];
    span.Slice(SinbadSlideshowFile.PaletteSize, SinbadSlideshowFile.PixelDataSize).CopyTo(pixelData);

    return new SinbadSlideshowFile {
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
