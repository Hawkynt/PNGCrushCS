using System;
using System.IO;
using System.Text;

namespace FileFormat.Mtv;

/// <summary>Reads MTV Ray Tracer files from bytes, streams, or file paths.</summary>
public static class MtvReader {

  public static MtvFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MTV file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MtvFile FromStream(Stream stream) {
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

  public static MtvFile FromSpan(ReadOnlySpan<byte> data) {

    var newlineIndex = data.IndexOf((byte)'\n');
    if (newlineIndex < 0)
      throw new InvalidDataException("No newline found in MTV header.");

    var headerText = Encoding.ASCII.GetString(data.Slice(0, newlineIndex));
    var parts = headerText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Two numbers and nothing else. The size line is the only thing telling this format from the
    // others that answer to .pic, so a line carrying anything more is not one of ours.
    if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
      throw new InvalidDataException("Invalid MTV header dimensions.");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("MTV image dimensions must be positive.");

    var pixelOffset = newlineIndex + 1;
    var expectedPixelBytes = (long)width * height * 3;
    var available = (long)data.Length - pixelOffset;

    // nconvert puts one 0x00 between the size line and the samples and will not read a file back
    // without it, though neither Rayshade nor the MTV tracer itself writes one. It is taken as
    // padding only when the payload is otherwise one byte too long, so a genuinely black first
    // pixel in an exactly-sized file stays a sample.
    if (available == expectedPixelBytes + 1 && data[pixelOffset] == 0) {
      ++pixelOffset;
      --available;
    }

    // A file that cannot fill the size it states is not this format; padding it out would hand back
    // a picture half of which was never in the file.
    if (available < expectedPixelBytes)
      throw new InvalidDataException($"MTV payload holds {available} bytes but {width}x{height} needs {expectedPixelBytes}.");

    var pixelData = new byte[expectedPixelBytes];
    data.Slice(pixelOffset, (int)expectedPixelBytes).CopyTo(pixelData.AsSpan(0));

    return new MtvFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

  }

  public static MtvFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
