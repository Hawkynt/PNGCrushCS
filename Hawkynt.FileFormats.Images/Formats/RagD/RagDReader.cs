using System;
using System.IO;
using System.Text;

namespace FileFormat.RagD;

/// <summary>Reads RAG-D pictures from bytes, streams, or file paths.</summary>
public static class RagDReader {

  public static RagDFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName), file.Extension);
  }

  public static RagDFile FromStream(Stream stream) {
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

  public static RagDFile FromSpan(ReadOnlySpan<byte> data) => FromSpan(data, false);

  /// <summary>
  /// Reads a picture, told whether its pixels are chunky rather than spread across bitplanes.
  /// </summary>
  /// <remarks>
  /// Eight bitplanes and one byte a pixel take exactly the same number of bytes for the same
  /// picture, and the header says nothing about which is which — only the extension does. So the
  /// caller has to say, and a file read without its name is taken as bitplanes.
  /// </remarks>
  public static RagDFile FromSpan(ReadOnlySpan<byte> data, bool chunky) {
    if (data.Length < 55 || Encoding.ASCII.GetString(data[..RagDFile.Signature.Length]) != RagDFile.Signature
        || data[6] != 0 || data[7] != 0 || data[16] != 0)
      throw new InvalidDataException("Not a RAG-D picture.");

    var width = (data[12] << 8) | data[13];
    // The stored height is one less than the real one, so a 256-row picture still fits two bytes.
    var height = ((data[14] << 8) + data[15]) + 1;
    var planes = data[17];
    var paletteLength = (data[18] << 24) | (data[19] << 16) | (data[20] << 8) | data[21];

    if ((width & 15) != 0 || width == 0)
      throw new InvalidDataException($"A RAG-D picture is a whole number of words across, not {width}.");

    if (paletteLength != RagDFile.StPaletteLength && paletteLength != RagDFile.FalconPaletteLength)
      throw new InvalidDataException($"Not a RAG-D picture: a palette of {paletteLength} bytes.");

    if (chunky && (planes != 8 || paletteLength != RagDFile.FalconPaletteLength))
      throw new InvalidDataException("A chunky RAG-D picture is eight bits a pixel against 256 colours.");

    var needed = planes switch {
      16 => paletteLength == RagDFile.FalconPaletteLength
        ? RagDFile.FalconBitmapOffset + width * height * 2
        : throw new InvalidDataException("A true-colour RAG-D picture carries a Falcon palette."),
      1 or 2 or 4 => paletteLength == RagDFile.StPaletteLength || paletteLength == RagDFile.FalconPaletteLength
        ? RagDFile.PaletteOffset + paletteLength + height * (width >> 3) * planes
        : 0,
      8 => paletteLength == RagDFile.FalconPaletteLength
        ? RagDFile.FalconBitmapOffset + width * height
        : throw new InvalidDataException("An eight-plane RAG-D picture needs more than sixteen colours."),
      _ => throw new InvalidDataException($"A RAG-D picture has one to eight planes or sixteen bits, not {planes}."),
    };

    if (needed > data.Length)
      throw new InvalidDataException($"A {width}x{height} picture of {planes} planes does not fit {data.Length} bytes.");

    return new() {
      Data = data.ToArray(),
      Width = width,
      Height = height,
      Planes = planes,
      PaletteLength = paletteLength,
      IsChunky = chunky,
    };
  }

  public static RagDFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Reads a picture, taking the layout from the file name as the format requires.</summary>
  public static RagDFile FromBytes(byte[] data, string extension) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, extension.Equals(".ragc", StringComparison.OrdinalIgnoreCase));
  }
}
