using System;
using System.IO;

namespace FileFormat.TobiasRichterSlideshow;

/// <summary>Reads Tobias Richter Fullscreen Slideshow pictures from bytes, streams, or file paths.</summary>
public static class TobiasRichterSlideshowReader {

  public static TobiasRichterSlideshowFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Slideshow picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TobiasRichterSlideshowFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static TobiasRichterSlideshowFile FromSpan(ReadOnlySpan<byte> data) {
    // Everything is at a fixed offset and nothing identifies the format, so the length is all there
    // is to go on.
    if (data.Length != TobiasRichterSlideshowFile.FileSize)
      throw new InvalidDataException($"Not a slideshow picture: {data.Length} bytes.");

    return new() { Data = data.ToArray() };
  }

  public static TobiasRichterSlideshowFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
