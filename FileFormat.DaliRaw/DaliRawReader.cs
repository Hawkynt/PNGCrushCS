using System;
using System.IO;

namespace FileFormat.DaliRaw;

/// <summary>Reads Atari ST Dali raw images from bytes, streams, or file paths.</summary>
public static class DaliRawReader {

  public static DaliRawFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Dali raw file not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName));
  }

  public static DaliRawFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static DaliRawFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != DaliRawFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Dali raw data size: expected exactly {DaliRawFile.ExpectedFileSize} bytes, got {data.Length}.");

    var header = DaliRawHeader.ReadFrom(data);

    return new DaliRawFile {
      Palette = header.Palette,
      PixelData = data.Slice(DaliRawFile.PaletteSize + DaliRawFile.PaddingSize, DaliRawFile.PlanarDataSize).ToArray()
    };
  }

  public static DaliRawFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
