using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxTrefiBorderScreen;

/// <summary>Reads Border Screens by Trefi from bytes, streams, or file paths.</summary>
public static class ZxTrefiBorderScreenReader {

  /// <summary>Set in the flag byte when the border is stored as well as the screen.</summary>
  private const int _HAS_BORDER = 64;

  /// <summary>Set in the flag byte when the picture is two alternating fields.</summary>
  private const int _TWO_FIELDS = 128;

  public static ZxTrefiBorderScreenFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Border screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxTrefiBorderScreenFile FromStream(Stream stream) {
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

  public static ZxTrefiBorderScreenFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < ZxTrefiBorderScreenFile.FirstBitmapOffset + ZxTrefiBorderScreenFile.ScreenSize)
      throw new InvalidDataException("Not a border screen: too short for even one screen.");

    var flags = data[3];
    var twoFields = (flags & _TWO_FIELDS) != 0;
    var stored = data.ToArray();

    if ((flags & _HAS_BORDER) == 0) {
      // No border: the picture is the screen, and a second field simply follows the first.
      if (!twoFields)
        return _Screen(stored, [(ZxTrefiBorderScreenFile.FirstBitmapOffset, -1)]);

      if (data.Length != ZxTrefiBorderScreenFile.FirstBitmapOffset + ZxTrefiBorderScreenFile.ScreenSize * 2)
        throw new InvalidDataException($"A two-field border screen is not {data.Length} bytes.");

      return _Screen(stored, [
        (ZxTrefiBorderScreenFile.FirstBitmapOffset, -1),
        (ZxTrefiBorderScreenFile.FirstBitmapOffset + ZxTrefiBorderScreenFile.ScreenSize, -1),
      ]);
    }

    if (!twoFields)
      return _Bordered(stored, [
        (ZxTrefiBorderScreenFile.FirstBitmapOffset,
         ZxTrefiBorderScreenFile.FirstBitmapOffset + ZxTrefiBorderScreenFile.ScreenSize),
      ]);

    // With two bordered fields the header grows by the two bytes that say where the second field's
    // border runs start, since neither field's run count is fixed.
    const int firstBitmap = ZxTrefiBorderScreenFile.FirstBitmapOffset + 2;
    const int secondBitmap = firstBitmap + ZxTrefiBorderScreenFile.ScreenSize;
    var secondBorder = data[ZxTrefiBorderScreenFile.FirstBitmapOffset]
                       | (data[ZxTrefiBorderScreenFile.FirstBitmapOffset + 1] << 8);

    return _Bordered(stored, [
      (firstBitmap, secondBitmap + ZxTrefiBorderScreenFile.ScreenSize),
      (secondBitmap, secondBorder),
    ]);
  }

  private static ZxTrefiBorderScreenFile _Screen(byte[] data, (int Bitmap, int Border)[] fields) => new() {
    Data = data,
    Width = ZxSpectrumGraphics.ScreenWidth,
    Height = ZxSpectrumGraphics.ScreenHeight,
    Fields = fields,
  };

  private static ZxTrefiBorderScreenFile _Bordered(byte[] data, (int Bitmap, int Border)[] fields) => new() {
    Data = data,
    Width = ZxTrefiBorderScreenFile.BorderedWidth,
    Height = ZxTrefiBorderScreenFile.BorderedHeight,
    Fields = fields,
  };

  public static ZxTrefiBorderScreenFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
