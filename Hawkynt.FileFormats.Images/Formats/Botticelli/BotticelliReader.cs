using System;
using System.IO;

namespace FileFormat.Botticelli;

/// <summary>Reads Botticelli pictures from bytes, streams, or file paths.</summary>
public static class BotticelliReader {

  public static BotticelliFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Botticelli picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BotticelliFile FromStream(Stream stream) {
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

  public static BotticelliFile FromSpan(ReadOnlySpan<byte> data) {
    // Length alone separates a screen from the logo; within a screen the marker separates the two
    // colour modes. There is no other header, so anything else is not a Botticelli picture.
    var mode = data.Length switch {
      BotticelliFile.ScreenFileSize =>
        data.Slice(BotticelliFile.MarkerOffset, BotticelliFile.MulticolorMarker.Length)
          .SequenceEqual(BotticelliFile.MulticolorMarker)
          ? BotticelliMode.Multicolor
          : BotticelliMode.Hires,
      BotticelliFile.LogoFileSize => BotticelliMode.Logo,
      _ => throw new InvalidDataException(
        $"A Botticelli picture is {BotticelliFile.ScreenFileSize} or {BotticelliFile.LogoFileSize} bytes, got {data.Length}."),
    };

    return new() { Mode = mode, Data = data.ToArray() };
  }

  public static BotticelliFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
