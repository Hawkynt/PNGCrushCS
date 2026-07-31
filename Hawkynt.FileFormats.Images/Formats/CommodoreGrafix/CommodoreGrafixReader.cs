using System;
using System.IO;
using System.Text;

namespace FileFormat.CommodoreGrafix;

/// <summary>Reads Commodore Grafix files from bytes, streams, or file paths.</summary>
public static class CommodoreGrafixReader {

  public static CommodoreGrafixFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CommodoreGrafixFile FromStream(Stream stream) {
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

  public static CommodoreGrafixFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 40 || Encoding.ASCII.GetString(data[..4]) != "RIFF"
        || _LittleEndian(data, 4) + 8 != data.Length || Encoding.ASCII.GetString(data.Slice(8, 4)) != "CGFX")
      throw new InvalidDataException("Not a Commodore Grafix file.");

    int matrixRows = 0, matrixColumns = 0, frameRows = 0, frameColumns = 0;

    for (var at = 12; at + 8 <= data.Length;) {
      var length = _LittleEndian(data, at + 4);
      var next = at + 8 + length;
      if (next < 0 || next > data.Length)
        throw new InvalidDataException("A Commodore Grafix chunk runs past the end of the file.");

      var kind = Encoding.ASCII.GetString(data.Slice(at, 4));

      if (kind == "FRMT" && length == 12) {
        matrixRows = data[at + 8];
        matrixColumns = data[at + 9];
        frameRows = data[at + 16];
        frameColumns = data[at + 17];

        // The frame count is stored a second time, and four is the only pixel depth there is.
        if (data[at + 12] != matrixRows * matrixColumns || data[at + 18] != 4 || data[at + 19] != 0)
          throw new InvalidDataException("A Commodore Grafix format chunk contradicts itself.");
      } else if (kind == "DATA") {
        var characters = frameRows * frameColumns;
        var frameLength = characters * CommodoreGrafixFile.BytesPerCharacter + CommodoreGrafixFile.FrameTrailer;
        var frames = matrixRows * matrixColumns;

        if (characters == 0 || frames == 0 || length != frames * frameLength)
          throw new InvalidDataException($"A Commodore Grafix data chunk of {length} bytes holds no whole frames.");

        return new() {
          Data = data.ToArray(),
          DataOffset = at + 8,
          MatrixColumns = matrixColumns,
          MatrixRows = matrixRows,
          FrameColumns = frameColumns,
          FrameRows = frameRows,
        };
      } else if (kind != "META")
        throw new InvalidDataException($"A Commodore Grafix file holds no '{kind}' chunk.");

      at = next;
    }

    throw new InvalidDataException("A Commodore Grafix file with no data chunk holds no picture.");
  }

  private static int _LittleEndian(ReadOnlySpan<byte> data, int offset)
    => data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

  public static CommodoreGrafixFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
