using System;
using System.IO;

namespace FileFormat.RamBrandt;

/// <summary>Reads Ram Brandt files from bytes, streams, or file paths.</summary>
public static class RamBrandtReader {

  public static RamBrandtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ram Brandt file not found.", file.FullName);

    // All five modes share one layout and one size, so only the extension says which is which.
    return FromSpan(File.ReadAllBytes(file.FullName), ModeFromExtension(file.Extension));
  }

  public static RamBrandtFile FromStream(Stream stream) {
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

  public static RamBrandtFile FromSpan(ReadOnlySpan<byte> data) => FromSpan(data, RamBrandtMode.Graphics7);

  public static RamBrandtFile FromSpan(ReadOnlySpan<byte> data, RamBrandtMode mode) {
    if (data.Length != RamBrandtFile.ExpectedFileSize)
      throw new InvalidDataException($"Ram Brandt file must be exactly {RamBrandtFile.ExpectedFileSize} bytes, got {data.Length}.");

    var bitmap = new byte[RamBrandtFile.BitmapDataSize];
    data[..RamBrandtFile.BitmapDataSize].CopyTo(bitmap);

    var colors = new byte[RamBrandtFile.ColorCount];
    data.Slice(RamBrandtFile.ColorsOffset, RamBrandtFile.ColorCount).CopyTo(colors);

    var displayList = new byte[RamBrandtFile.DisplayListSize];
    data.Slice(RamBrandtFile.DisplayListOffset, RamBrandtFile.DisplayListSize).CopyTo(displayList);

    return new() { Mode = mode, BitmapData = bitmap, Colors = colors, DisplayList = displayList };
  }

  public static RamBrandtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Maps a Ram Brandt extension to the ANTIC mode it names.</summary>
  public static RamBrandtMode ModeFromExtension(string extension) => extension.ToLowerInvariant() switch {
    ".rm1" => RamBrandtMode.Graphics9,
    ".rm2" => RamBrandtMode.Graphics10,
    ".rm3" => RamBrandtMode.Graphics11,
    ".rm4" => RamBrandtMode.Graphics15,
    _ => RamBrandtMode.Graphics7,
  };
}
