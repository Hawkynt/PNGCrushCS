using System;

namespace FileFormat.FunPhotor;

/// <summary>Assembles Fun Photor C64 multicolor file bytes from a FunPhotorFile.</summary>
public static class FunPhotorWriter {

  public static byte[] ToBytes(FunPhotorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FunPhotorFile.ExpectedFileSize];
    var offset = 0;

    result[offset] = (byte)(file.LoadAddress & 0xFF);
    result[offset + 1] = (byte)(file.LoadAddress >> 8);
    offset += FunPhotorFile.LoadAddressSize;

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, FunPhotorFile.BitmapDataSize)).CopyTo(result.AsSpan(offset));
    offset += FunPhotorFile.BitmapDataSize;

    file.ScreenData.AsSpan(0, Math.Min(file.ScreenData.Length, FunPhotorFile.ScreenDataSize)).CopyTo(result.AsSpan(offset));
    offset += FunPhotorFile.ScreenDataSize;

    file.ColorData.AsSpan(0, Math.Min(file.ColorData.Length, FunPhotorFile.ColorDataSize)).CopyTo(result.AsSpan(offset));
    offset += FunPhotorFile.ColorDataSize;

    file.Reserved.AsSpan(0, Math.Min(file.Reserved.Length, FunPhotorFile.ReservedSize)).CopyTo(result.AsSpan(offset));

    return result;
  }
}
