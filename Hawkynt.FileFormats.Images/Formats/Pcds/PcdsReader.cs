using System;
using System.IO;
using FileFormat.Pcd;

namespace FileFormat.Pcds;

/// <summary>Reads a Kodak Photo CD whose planes are already sRGB, from bytes, streams or paths.</summary>
public static class PcdsReader {

  public static PcdsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PCDS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PcdsFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  /// <summary>
  /// The same container the <c>.pcd</c> reader walks, stopped one step short of the colour
  /// transform.
  /// </summary>
  public static PcdsFile FromSpan(ReadOnlySpan<byte> data) {
    var (width, height, rgb) = PcdReader.ReadPlanes(data, photoYcc: false);

    return new() {
      Width = width,
      Height = height,
      PixelData = rgb,
    };
  }

  public static PcdsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
