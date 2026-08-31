using System;
using System.Collections.Generic;

namespace FileFormat.Stad;

/// <summary>Assembles STAD compressed screen bytes from a <see cref="StadFile"/>.</summary>
public static class StadWriter {

  public static byte[] ToBytes(StadFile file) {
    StadFile.Validate(file, nameof(file));

    var ordered = file.Packing == StadPacking.Horizontal ? file.RawData : _TransposeToColumns(file.RawData);
    var (idByte, packByte, specialByte) = file.HasCompressionParameters
      ? (file.IdByte, file.PackByte, file.SpecialByte)
      : _ChooseParameters(ordered);

    var encoded = _Compress(ordered, idByte, packByte, specialByte);
    var output = new byte[7 + encoded.Length];
    output[0] = (byte)'p';
    output[1] = (byte)'M';
    output[2] = (byte)'8';
    output[3] = file.Packing == StadPacking.Horizontal ? (byte)'5' : (byte)'6';
    output[4] = idByte;
    output[5] = packByte;
    output[6] = specialByte;
    encoded.CopyTo(output, 7);
    return output;
  }

  internal static StadPacking SelectPacking(ReadOnlySpan<byte> screen) {
    if (screen.Length != StadFile.ScreenDataSize)
      throw new ArgumentException($"STAD screen data must contain exactly {StadFile.ScreenDataSize} bytes.", nameof(screen));

    var horizontalParameters = _ChooseParameters(screen);
    var horizontalLength = _Compress(screen, horizontalParameters.IdByte, horizontalParameters.PackByte, horizontalParameters.SpecialByte).Length;
    var vertical = _TransposeToColumns(screen);
    var verticalParameters = _ChooseParameters(vertical);
    var verticalLength = _Compress(vertical, verticalParameters.IdByte, verticalParameters.PackByte, verticalParameters.SpecialByte).Length;
    return verticalLength < horizontalLength ? StadPacking.Vertical : StadPacking.Horizontal;
  }

  private static (byte IdByte, byte PackByte, byte SpecialByte) _ChooseParameters(ReadOnlySpan<byte> screen) {
    var histogram = new int[256];
    foreach (var value in screen)
      ++histogram[value];

    var pack = 0;
    for (var value = 1; value < histogram.Length; ++value)
      if (histogram[value] > histogram[pack])
        pack = value;

    var id = _Rarest(histogram, pack, -1);
    var special = _Rarest(histogram, pack, id);
    return ((byte)id, (byte)pack, (byte)special);
  }

  private static int _Rarest(int[] histogram, int excluded1, int excluded2) {
    var result = -1;
    for (var value = 0; value < histogram.Length; ++value) {
      if (value == excluded1 || value == excluded2)
        continue;
      if (result < 0 || histogram[value] < histogram[result])
        result = value;
    }
    return result;
  }

  private static byte[] _Compress(ReadOnlySpan<byte> screen, byte idByte, byte packByte, byte specialByte) {
    var output = new List<byte>(screen.Length / 2);

    for (var at = 0; at < screen.Length;) {
      var value = screen[at];
      var run = 1;
      while (run < 256 && at + run < screen.Length && screen[at + run] == value)
        ++run;

      if (value == packByte && run >= 2) {
        output.Add(idByte);
        output.Add((byte)(run - 1));
      } else if (value == idByte || value == specialByte || run >= 3) {
        // The arbitrary-value packet also stores count-minus-one. This matches the existing sample
        // corpus and its RECOIL/XnView oracle checks.
        output.Add(specialByte);
        output.Add(value);
        output.Add((byte)(run - 1));
      } else {
        for (var i = 0; i < run; ++i)
          output.Add(value);
      }

      at += run;
    }

    return output.ToArray();
  }

  private static byte[] _TransposeToColumns(ReadOnlySpan<byte> rows) {
    var columns = new byte[rows.Length];
    for (var column = 0; column < StadFile.BytesPerRow; ++column)
      for (var row = 0; row < StadFile.PixelHeight; ++row)
        columns[column * StadFile.PixelHeight + row] = rows[row * StadFile.BytesPerRow + column];
    return columns;
  }
}
