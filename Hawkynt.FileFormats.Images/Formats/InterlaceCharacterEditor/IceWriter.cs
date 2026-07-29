using System;

namespace FileFormat.InterlaceCharacterEditor;

/// <summary>Assembles Interlace Character Editor picture bytes from an <see cref="IceFile"/>.</summary>
public static class IceWriter {

  public static byte[] ToBytes(IceFile file) {
    var mode = file.Mode;
    var result = new byte[IceLayout.FileSizeFor(mode)];

    _Copy(file.Header, result, 0, IceLayout.HeaderSizeFor(mode));
    result[0] = 1;
    _Copy(file.FontData, result, IceLayout.HeaderSizeFor(mode), IceLayout.FontSize);
    _Copy(file.Characters1, result, IceLayout.Characters1OffsetFor(mode), IceLayout.CharacterMapSize);
    if (!IceLayout.SharesCharacterMap(mode))
      _Copy(file.Characters2, result, IceLayout.Characters2OffsetFor(mode), IceLayout.CharacterMapSize);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
