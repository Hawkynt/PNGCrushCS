using System;

namespace FileFormat.MadStudio;

/// <summary>Assembles Mad Studio screen bytes from a <see cref="MadStudioFile"/>.</summary>
public static class MadStudioWriter {

  public static byte[] ToBytes(MadStudioFile file) {
    var mode = file.Mode;
    var result = new byte[MadStudioLayout.FileSizeFor(mode)];
    var headerSize = MadStudioLayout.HeaderSizeFor(mode);

    if (headerSize > 0) {
      result[0] = (byte)(MadStudioLayout.ColumnsFor(mode) - 1);
      result[1] = (byte)(MadStudioLayout.RowsFor(mode) - 1);
    }

    var mapSize = MadStudioLayout.CharacterMapSizeFor(mode);
    _Copy(file.Characters, result, headerSize, mapSize);

    if (mode != MadStudioMode.Antic2)
      _Copy(file.Colors, result, MadStudioLayout.ColorsFollowCharacters(mode) ? headerSize + mapSize : 2, MadStudioLayout.ColorCount);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
