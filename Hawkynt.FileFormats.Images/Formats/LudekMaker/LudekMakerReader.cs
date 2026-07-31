using System;
using System.IO;

namespace FileFormat.LudekMaker;

/// <summary>Reads Ludek Maker sheets from bytes, streams, or file paths.</summary>
public static class LudekMakerReader {

  public static LudekMakerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Sheet not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LudekMakerFile FromStream(Stream stream) {
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

  public static LudekMakerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < LudekMakerFile.ShapesOffset)
      throw new InvalidDataException($"Not a Ludek Maker sheet: {data.Length} bytes.");

    // The signature is the text with every byte's high bit set, which is how the machine's own
    // character set encodes it — so the file reads as the words on screen and as nothing elsewhere.
    for (var i = 0; i < LudekMakerFile.Signature.Length; ++i)
      if (data[i] != (byte)(LudekMakerFile.Signature[i] + 128))
        throw new InvalidDataException("Not a Ludek Maker sheet.");

    var shapes = data[24] - data[23];
    if (shapes <= 0 || shapes > 100 || data.Length < LudekMakerFile.ShapesOffset + shapes * LudekMakerFile.FigureLength)
      throw new InvalidDataException($"A sheet of {shapes} figures does not fit {data.Length} bytes.");

    return new() { Data = data.ToArray(), Shapes = shapes };
  }

  public static LudekMakerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
