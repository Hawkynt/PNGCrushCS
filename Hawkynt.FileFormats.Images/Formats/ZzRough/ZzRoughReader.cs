using System;
using System.IO;
using System.Text;
using FileFormat.DaliCompressed;

namespace FileFormat.ZzRough;

/// <summary>Reads ZZ_ROUGH pictures from bytes, streams, or file paths.</summary>
public static class ZzRoughReader {

  public static ZzRoughFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZzRoughFile FromStream(Stream stream) {
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

  public static ZzRoughFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ZzRoughFile.Signature.Length + 8
        || Encoding.ASCII.GetString(data[..ZzRoughFile.Signature.Length]) != ZzRoughFile.Signature)
      throw new InvalidDataException("Not a ZZ_ROUGH picture.");

    // The count stream's length is written as decimal digits and closed by a carriage return.
    var offset = ZzRoughFile.Signature.Length;
    var countLength = 0;
    var digits = 0;
    while (offset < data.Length && data[offset] >= '0' && data[offset] <= '9') {
      countLength = countLength * 10 + (data[offset++] - '0');
      if (++digits > 5)
        throw new InvalidDataException("Not a ZZ_ROUGH picture: the count length runs on.");
    }

    if (digits == 0 || countLength < 10 || countLength > 32000)
      throw new InvalidDataException($"Not a ZZ_ROUGH picture: a count stream of {countLength} bytes.");

    if (offset + 1 >= data.Length || data[offset] != '\r' || data[offset + 1] != '\n')
      throw new InvalidDataException("Not a ZZ_ROUGH picture: the count length is not closed.");

    var paletteOffset = offset + 2;
    var countsOffset = paletteOffset + ZzRoughFile.PaletteSize;
    if (countsOffset + countLength > data.Length)
      throw new InvalidDataException("A ZZ_ROUGH picture's streams run past the end of the file.");

    var screen = DaliCompressor.Decompress(
      data.Slice(countsOffset, countLength), data[(countsOffset + countLength)..]);

    return new() {
      ScreenData = screen,
      Palette = data.Slice(paletteOffset, ZzRoughFile.PaletteSize).ToArray(),
    };
  }

  public static ZzRoughFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
