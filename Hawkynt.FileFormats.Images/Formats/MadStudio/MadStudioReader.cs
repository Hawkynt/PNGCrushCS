using System;
using System.IO;

namespace FileFormat.MadStudio;

/// <summary>Reads Mad Studio character screens from bytes, streams, or file paths.</summary>
public static class MadStudioReader {

  public static MadStudioFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Mad Studio screen not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName), MadStudioLayout.ModeFromExtension(file.Extension));
  }

  public static MadStudioFile FromStream(Stream stream) {
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

  public static MadStudioFile FromSpan(ReadOnlySpan<byte> data) {
    // Each mode has a distinct file size, so the bytes alone say which one this is.
    try {
      return FromSpan(data, MadStudioLayout.ModeFromLength(data.Length));
    } catch (ArgumentOutOfRangeException) {
      throw new InvalidDataException($"{data.Length} bytes is not the size of any Mad Studio screen.");
    }
  }

  public static MadStudioFile FromSpan(ReadOnlySpan<byte> data, MadStudioMode mode) {
    // The five colour registers sit at the end of the modes that put them after the characters, and
    // a file may simply stop before them — two samples here do, and every other reader shows them in
    // the mode's own default colours rather than refusing the file.
    var expected = MadStudioLayout.FileSizeFor(mode);
    var withoutColors = MadStudioLayout.ColorsFollowCharacters(mode) ? expected - MadStudioLayout.ColorCount : expected;
    if (data.Length != expected && data.Length != withoutColors)
      throw new InvalidDataException($"A Mad Studio {mode} screen is {expected} bytes, got {data.Length}.");

    var headerSize = MadStudioLayout.HeaderSizeFor(mode);
    if (headerSize > 0
        && (data[0] + 1 != MadStudioLayout.ColumnsFor(mode) || data[1] + 1 != MadStudioLayout.RowsFor(mode)))
      throw new InvalidDataException($"A Mad Studio {mode} screen is {MadStudioLayout.ColumnsFor(mode)}x{MadStudioLayout.RowsFor(mode)} cells; this one is not.");

    var mapSize = MadStudioLayout.CharacterMapSizeFor(mode);
    var characters = new byte[mapSize];
    data.Slice(headerSize, mapSize).CopyTo(characters);

    var colors = Array.Empty<byte>();
    if (mode != MadStudioMode.Antic2) {
      // What the machine powers up with, which is what a file that stops before its registers is
      // shown in: black behind, then the orange, green and blue the operating system sets. Read off
      // RECOIL's rendering of two such files, where all three land on a colour byte exactly.
      colors = [0x00, 0x28, 0xCA, 0x94, 0x46];
      var offset = MadStudioLayout.ColorsFollowCharacters(mode) ? headerSize + mapSize : 2;
      if (offset + MadStudioLayout.ColorCount <= data.Length)
        data.Slice(offset, MadStudioLayout.ColorCount).CopyTo(colors);
    }

    return new() { Mode = mode, Characters = characters, Colors = colors };
  }

  public static MadStudioFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
