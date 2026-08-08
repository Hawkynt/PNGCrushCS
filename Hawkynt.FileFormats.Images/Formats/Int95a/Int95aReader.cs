using System;
using System.IO;

namespace FileFormat.Int95a;

/// <summary>Reads INT95a pictures from bytes, streams, or file paths.</summary>
public static class Int95aReader {

  public static Int95aFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("INT95a picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Int95aFile FromStream(Stream stream) {
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

  public static Int95aFile FromSpan(ReadOnlySpan<byte> data) {
    // Nothing states the size, so the length has to divide into two whole frames and four registers.
    var payload = data.Length - Int95aFile.RegisterCount;
    if (payload <= 0 || payload % (Int95aFile.BytesPerRow * 2) != 0)
      throw new InvalidDataException(
        $"An INT95a picture is four registers and two frames of {Int95aFile.BytesPerRow} bytes a row; {data.Length} bytes is neither.");

    var height = payload / (Int95aFile.BytesPerRow * 2);
    if (height is < 1 or > Int95aFile.MaxHeight)
      throw new InvalidDataException($"An INT95a picture runs to {Int95aFile.MaxHeight} rows; this one states {height}.");

    var frame = Int95aFile.BytesPerRow * height;

    return new() {
      Height = height,
      FirstFrame = data[..frame].ToArray(),
      SecondFrame = data.Slice(frame, frame).ToArray(),
      Registers = data.Slice(frame * 2, Int95aFile.RegisterCount).ToArray(),
    };
  }

  public static Int95aFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
