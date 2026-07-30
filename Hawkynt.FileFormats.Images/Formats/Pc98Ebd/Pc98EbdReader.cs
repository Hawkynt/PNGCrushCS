using System;
using System.IO;

namespace FileFormat.Pc98Ebd;

/// <summary>Reads PC-98 EBD pictures from bytes, streams, or file paths.</summary>
public static class Pc98EbdReader {

  public static Pc98EbdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Pc98EbdFile FromStream(Stream stream) {
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

  public static Pc98EbdFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < Pc98EbdFile.BitmapOffset + Pc98EbdFile.Stride
        || data.Length % Pc98EbdFile.Stride != Pc98EbdFile.BitmapOffset)
      throw new InvalidDataException($"Not an EBD picture: {data.Length} bytes.");

    // A file that does not store its palette already widened must store it as bare nibbles.
    for (var i = 0; i < Pc98EbdFile.BitmapOffset; i += 3) {
      var widened = true;
      for (var channel = 0; channel < 3; ++channel) {
        var c = data[i + channel];
        widened &= (c >> 4) == (c & 15);
      }

      if (widened)
        continue;

      for (var channel = 0; channel < 3; ++channel)
        if ((data[i + channel] & 0xF0) != 0)
          throw new InvalidDataException($"Not an EBD picture: colour {i / 3} is neither form of palette entry.");
    }

    return new() { Data = data.ToArray(), Height = data.Length / Pc98EbdFile.Stride };
  }

  public static Pc98EbdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
