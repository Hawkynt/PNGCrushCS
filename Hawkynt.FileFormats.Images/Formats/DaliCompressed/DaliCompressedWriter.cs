using System.IO;

namespace FileFormat.DaliCompressed;

/// <summary>Assembles compressed Atari ST Dali screen bytes.</summary>
public static class DaliCompressedWriter {

  public static byte[] ToBytes(DaliCompressedFile file) {
    DaliCompressedFile.Validate(file, nameof(file));
    var (counts, values) = DaliCompressor.Compress(file.ScreenData);

    using var ms = new MemoryStream();
    ms.Write(file.Palette);
    ms.Write(DaliCompressedFile.FormatLength(counts.Length));
    ms.Write(DaliCompressedFile.FormatLength(values.Length));
    ms.Write(counts);
    ms.Write(values);
    return ms.ToArray();
  }
}
