using System;

namespace FileFormat.Hp48Grob;

/// <summary>Assembles HP 48 graphics object bytes from a <see cref="Hp48GrobFile"/>.</summary>
public static class Hp48GrobWriter {

  /// <summary>Writes the binary form, which is the one the calculator loads directly.</summary>
  /// <remarks>
  /// The size field counts nibbles from just past itself, so it depends on the length of the whole
  /// object including its own three bytes — which is why it is computed last rather than first.
  /// </remarks>
  public static byte[] ToBytes(Hp48GrobFile file) {
    var stride = (file.Width + 7) >> 3;
    var bitmap = file.Bitmap ?? [];
    var data = new byte[Hp48GrobFile.BinaryBitmapOffset + stride * file.Height];

    "HPHP48-"u8.CopyTo(data);
    data[7] = (byte)'A';
    data[8] = 30;
    data[9] = 43;

    var nibbles = data.Length * 2 - 21;
    data[10] = (byte)(nibbles << 4);
    data[11] = (byte)(nibbles >> 4);
    data[12] = (byte)(nibbles >> 12);
    data[13] = (byte)file.Height;
    data[14] = (byte)(file.Height >> 8);
    data[15] = (byte)(((file.Height >> 16) & 15) | ((file.Width & 15) << 4));
    data[16] = (byte)(file.Width >> 4);
    data[17] = (byte)(file.Width >> 12);

    // The text form swaps each byte's nibbles; the binary one does not, so a picture read from one
    // and written to the other has to be put back the way the calculator keeps it.
    for (var i = 0; i < stride * file.Height && i < bitmap.Length; ++i)
      data[Hp48GrobFile.BinaryBitmapOffset + i] = file.SwappedNibbles
        ? (byte)((bitmap[i] >> 4) | (bitmap[i] << 4))
        : bitmap[i];

    return data;
  }
}
