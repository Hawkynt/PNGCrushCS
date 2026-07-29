using System;
using System.IO;

namespace FileFormat.InterlaceCharacterEditor;

/// <summary>Reads Interlace Character Editor pictures from bytes, streams, or file paths.</summary>
public static class IceReader {

  public static IceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interlace Character Editor picture not found.", file.FullName);

    return FromSpan(File.ReadAllBytes(file.FullName), ModeFromExtension(file.Extension));
  }

  public static IceFile FromStream(Stream stream) {
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

  public static IceFile FromSpan(ReadOnlySpan<byte> data) => FromSpan(data, _ModeFromLength(data.Length));

  public static IceFile FromSpan(ReadOnlySpan<byte> data, IceMode mode) {
    var expected = IceLayout.FileSizeFor(mode);
    if (data.Length != expected)
      throw new InvalidDataException($"An Interlace Character Editor {mode} picture is {expected} bytes, got {data.Length}.");
    if (data[0] != 1)
      throw new InvalidDataException("Not an Interlace Character Editor picture: the leading identifier is not 1.");

    var headerSize = IceLayout.HeaderSizeFor(mode);
    var header = new byte[headerSize];
    data[..headerSize].CopyTo(header);

    var font = new byte[IceLayout.FontSize];
    data.Slice(headerSize, IceLayout.FontSize).CopyTo(font);

    var characters1 = new byte[IceLayout.CharacterMapSize];
    data.Slice(IceLayout.Characters1OffsetFor(mode), IceLayout.CharacterMapSize).CopyTo(characters1);

    var characters2 = characters1;
    if (!IceLayout.SharesCharacterMap(mode)) {
      characters2 = new byte[IceLayout.CharacterMapSize];
      data.Slice(IceLayout.Characters2OffsetFor(mode), IceLayout.CharacterMapSize).CopyTo(characters2);
    }

    return new() { Mode = mode, Header = header, FontData = font, Characters1 = characters1, Characters2 = characters2 };
  }

  public static IceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Maps an extension to the picture format it names.</summary>
  public static IceMode ModeFromExtension(string extension) => extension.ToLowerInvariant() switch {
    ".ir2" => IceMode.SuperIrg2,
    ".icn" => IceMode.Cin,
    ".imn" => IceMode.Min,
    ".ipc" => IceMode.Pcin,
    _ => IceMode.SuperIrg,
  };

  /// <summary>
  /// Falls back to the file size when there is no extension to go on. Two pairs of modes share a
  /// size, and those are resolved to the more common of the pair.
  /// </summary>
  private static IceMode _ModeFromLength(int length) {
    foreach (var mode in new[] { IceMode.SuperIrg, IceMode.SuperIrg2, IceMode.Min, IceMode.Pcin })
      if (IceLayout.FileSizeFor(mode) == length)
        return mode;

    throw new InvalidDataException($"{length} bytes is not the size of any Interlace Character Editor picture.");
  }
}
