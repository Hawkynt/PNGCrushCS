using System;
using System.IO;

namespace FileFormat.Bob;

/// <summary>Reads Bob Raytracer image files from bytes, streams, or file paths.</summary>
public static class BobReader {

  public static BobFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Bob file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BobFile FromStream(Stream stream) {
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

  public static BobFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < BobFile.PixelOffset)
      throw new InvalidDataException("Data too small for a valid Bob file.");

    var width = data[0] | (data[1] << 8);
    var height = data[2] | (data[3] << 8);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A Bob picture states {width}x{height}, which is no size.");

    // Nothing in the file says it is one of these, so the length is the whole of the check: an
    // indexed picture of the stated size behind a palette comes to exactly this and nothing else
    // does. Substituting a default size when it did not, as this used to, turns a file of some other
    // format into a picture of invented dimensions rather than refusing it.
    var expected = BobFile.SizeOf(width, height);
    if (data.Length != expected)
      throw new InvalidDataException($"A Bob picture of {width}x{height} is {expected} bytes; this file is {data.Length}.");

    var palette = data.Slice(BobFile.HeaderSize, BobFile.PaletteSize).ToArray();
    var pixelData = data.Slice(BobFile.PixelOffset, width * height).ToArray();

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
      Palette = palette,
    };
  }

  public static BobFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
