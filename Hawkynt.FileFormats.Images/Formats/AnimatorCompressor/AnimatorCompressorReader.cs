using System;
using System.IO;
using FileFormat.AtariPi8;

namespace FileFormat.AnimatorCompressor;

/// <summary>Reads Kompresor do Animatora sheets from bytes, streams, or file paths.</summary>
public static class AnimatorCompressorReader {

  public static AnimatorCompressorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Animation not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AnimatorCompressorFile FromStream(Stream stream) {
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

  public static AnimatorCompressorFile FromSpan(ReadOnlySpan<byte> data) {
    // The file is an Atari executable, and being one is the whole of its signature.
    if (data.Length < AnimatorCompressorFile.MapOffset || AtariPi8Reader.ExecutableOffset(data) != 6)
      throw new InvalidDataException("Not a Kompresor do Animatora animation.");

    int frames = data[8], columns = data[9], rows = data[10];
    var map = frames * columns * rows;
    if (frames == 0 || columns == 0 || rows == 0 || data.Length < AnimatorCompressorFile.MapOffset + map)
      throw new InvalidDataException($"An animation of {frames}x{columns}x{rows} tiles does not fit {data.Length} bytes.");

    return new() { Data = data.ToArray(), Frames = frames, Columns = columns, Rows = rows };
  }

  public static AnimatorCompressorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
