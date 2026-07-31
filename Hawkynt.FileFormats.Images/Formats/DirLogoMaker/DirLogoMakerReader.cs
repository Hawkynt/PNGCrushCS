using System;
using System.IO;

namespace FileFormat.DirLogoMaker;

/// <summary>Reads Dir Logo Maker logos from bytes, streams, or file paths.</summary>
public static class DirLogoMakerReader {

  public static DirLogoMakerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Logo not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DirLogoMakerFile FromStream(Stream stream) {
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

  public static DirLogoMakerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != DirLogoMakerFile.FileSize)
      throw new InvalidDataException($"Not a Dir Logo Maker logo: {data.Length} bytes.");

    var characters = new byte[DirLogoMakerFile.Rows * DirLogoMakerFile.Columns];
    for (var row = 0; row < DirLogoMakerFile.Rows; ++row)
    for (var column = 0; column < DirLogoMakerFile.Columns; ++column)
      characters[row * DirLogoMakerFile.Columns + column] =
        ToAtariCharacter(data[row * DirLogoMakerFile.EntrySize + DirLogoMakerFile.NameOffset + column]);

    return new() { Characters = characters };
  }

  /// <summary>Translates an ASCII code into the Atari's own character order.</summary>
  /// <remarks>
  /// The machine's set begins with the punctuation that ASCII puts at 32, then the letters, and
  /// only then the control characters — so the first three blocks of thirty-two rotate by one
  /// block and everything above 96 is already in place.
  /// </remarks>
  public static byte ToAtariCharacter(byte ascii) => (byte)((ascii & 96) switch {
    0 => ascii + 64,
    32 or 64 => ascii - 32,
    _ => ascii,
  });

  public static DirLogoMakerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
