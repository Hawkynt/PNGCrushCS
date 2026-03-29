using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.DaliRaw;

/// <summary>Reads Atari ST Dali raw images from bytes, streams, or file paths.</summary>
public static class DaliRawReader {

  public static DaliRawFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Dali raw file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DaliRawFile FromStream(Stream stream) {
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

  public static DaliRawFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != DaliRawFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Dali raw data size: expected exactly {DaliRawFile.ExpectedFileSize} bytes, got {data.Length}.");

    var span = data.AsSpan();
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = BinaryPrimitives.ReadInt16BigEndian(span[(i * 2)..]);

    var pixelData = new byte[DaliRawFile.PlanarDataSize];
    data.AsSpan(DaliRawFile.PaletteSize + DaliRawFile.PaddingSize, DaliRawFile.PlanarDataSize).CopyTo(pixelData.AsSpan(0));

    return new DaliRawFile {
      Palette = palette,
      PixelData = pixelData
    };
  }
}
