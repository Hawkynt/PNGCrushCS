using System;
using System.IO;

namespace FileFormat.TimexGigascreen;

/// <summary>Reads Timex hi-res gigascreen pictures from bytes, streams, or file paths.</summary>
public static class TimexGigascreenReader {

  public static TimexGigascreenFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Gigascreen picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TimexGigascreenFile FromStream(Stream stream) {
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

  public static TimexGigascreenFile FromSpan(ReadOnlySpan<byte> data) {
    // Screens of bitmap-plus-colour and nothing else; the length is the only identification. One
    // screen is a Timex hi-res picture, two are a gigascreen.
    if (data.Length != TimexGigascreenFile.FileSize && data.Length != TimexGigascreenFile.ScreenSize)
      throw new InvalidDataException($"A Timex hi-res picture is {TimexGigascreenFile.ScreenSize} bytes and a gigascreen {TimexGigascreenFile.FileSize}; this file is {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static TimexGigascreenFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
