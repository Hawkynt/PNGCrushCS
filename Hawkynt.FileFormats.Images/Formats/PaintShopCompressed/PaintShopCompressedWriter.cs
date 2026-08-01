using System;
using System.IO;
using System.Text;

namespace FileFormat.PaintShopCompressed;

/// <summary>Assembles a compressed PaintShop picture, using the command that means "not compressed".</summary>
/// <remarks>
/// The format's own commands copy runs and repeat earlier rows, and none of them is needed: it
/// carries one command meaning the bitmap follows outright, and readers of it accept that. The file
/// is larger and exactly as correct.
/// </remarks>
public static class PaintShopCompressedWriter {

  /// <summary>The command that says the whole bitmap follows as it stands.</summary>
  public const byte Uncompressed = 99;

  public static byte[] ToBytes(PaintShopCompressedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var stride = (file.Width + 7) >> 3;
    var length = stride * file.Height;
    var bitmap = file.Bitmap ?? new byte[length];

    var result = new byte[16 + length];
    Encoding.ASCII.GetBytes(PaintShopCompressedFile.Signature).CopyTo(result.AsSpan(0));
    result[8] = 2;
    result[9] = 1;

    // The sizes are stored one less than they are, so 640 and 400 still fit in two bytes.
    result[10] = (byte)((file.Width - 1) >> 8);
    result[11] = (byte)(file.Width - 1);
    result[12] = (byte)((file.Height - 1) >> 8);
    result[13] = (byte)(file.Height - 1);

    result[PaintShopCompressedFile.CommandsOffset] = Uncompressed;
    bitmap.AsSpan(0, Math.Min(bitmap.Length, length)).CopyTo(result.AsSpan(15));
    result[15 + length] = PaintShopCompressedFile.Terminator;

    return result;
  }

  public static void ToFile(PaintShopCompressedFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
