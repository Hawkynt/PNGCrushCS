using System;
using System.IO;
using System.Text;

namespace FileFormat.AtariGraphicsStudio;

/// <summary>Reads Atari Graphics Studio pictures from bytes, streams, or file paths.</summary>
public static class AtariGraphicsStudioReader {

  public static AtariGraphicsStudioFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGraphicsStudioFile FromStream(Stream stream) {
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

  public static AtariGraphicsStudioFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 17
        || Encoding.ASCII.GetString(data[..AtariGraphicsStudioFile.Signature.Length]) != AtariGraphicsStudioFile.Signature)
      throw new InvalidDataException("Not an Atari Graphics Studio picture.");

    // The header's dimensions are in storage units, not pixels: bytes across and pairs of rows down.
    var stored = data[4];
    var rows = data[5] | (data[6] << 8);
    if (data.Length != AtariGraphicsStudioFile.BitmapOffset + (stored * rows << 1))
      throw new InvalidDataException($"A {stored}x{rows} picture does not occupy {data.Length} bytes.");

    var (width, height) = data[3] switch {
      AtariGraphicsStudioFile.InterleavedMode => (stored << 3, rows << 1),
      AtariGraphicsStudioFile.QuadrupledMode => (stored << 3, rows << 2),
      _ => throw new InvalidDataException($"An Atari Graphics Studio picture is mode 11 or 19, not {data[3]}."),
    };

    if (width == 0 || height == 0)
      throw new InvalidDataException($"An Atari Graphics Studio picture is not {width}x{height}.");

    return new() { Data = data.ToArray(), Mode = data[3], Width = width, Height = height };
  }

  public static AtariGraphicsStudioFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
