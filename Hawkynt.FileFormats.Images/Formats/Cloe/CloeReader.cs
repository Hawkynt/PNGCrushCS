using System;
using System.IO;

namespace FileFormat.Cloe;

/// <summary>Reads Cloe Ray-Tracer image files from bytes, streams, or file paths.</summary>
public static class CloeReader {

  public static CloeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Cloe file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CloeFile FromStream(Stream stream) {
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

  public static CloeFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < CloeFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid Cloe file.");

    // Two little-endian 32-bit lengths and nothing else — there is no signature, so the header's
    // own arithmetic is the whole of the identification and it has to be taken literally.
    // Inventing 320x200 when the header states neither, which is what stood here, meant any file
    // long enough was drawn as a picture of a size it never claimed.
    var width = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
    var height = data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24);

    if (width <= 0 || width > CloeFile.MaxDimension)
      throw new InvalidDataException($"A Cloe picture states a width of {width}.");
    if (height <= 0 || height > CloeFile.MaxDimension)
      throw new InvalidDataException($"A Cloe picture states a height of {height}.");

    var pixelBytes = width * height * 3;
    var pixelData = new byte[pixelBytes];
    // Padding what the file does not contain turns a misread size into a picture: a header taken
    // from the wrong offset asked for millions of pixels, the few hundred bytes present were
    // copied in, and the rest was zeros reported as a successful read.
    var available = data.Length - CloeFile.HeaderSize;
    if (available < pixelBytes)
      throw new InvalidDataException($"Expected {pixelBytes} bytes of pixel data, got {available}.");

    data.Slice(CloeFile.HeaderSize, pixelBytes).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
  }

  public static CloeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
