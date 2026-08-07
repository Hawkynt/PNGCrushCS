using System;
using System.Buffers.Binary;

namespace FileFormat.FliGraph;

/// <summary>Assembles FLI Graph picture bytes.</summary>
public static class FliGraphWriter {

  public static byte[] ToBytes(FliGraphFile file) {
    ArgumentNullException.ThrowIfNull(file.BitmapData);
    ArgumentNullException.ThrowIfNull(file.Screens);
    ArgumentNullException.ThrowIfNull(file.ColorRam);

    var result = new byte[FliGraphFile.MinimumFileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    file.ColorRam.AsSpan(0, Math.Min(file.ColorRam.Length, FliGraphFile.BankSize))
      .CopyTo(result.AsSpan(FliGraphFile.ColorRamOffset));

    for (var bank = 0; bank < FliGraphFile.ScreenBankCount; ++bank) {
      var from = bank * FliGraphFile.BankSize;
      if (from >= file.Screens.Length)
        break;

      file.Screens.AsSpan(from, Math.Min(FliGraphFile.BankSize, file.Screens.Length - from))
        .CopyTo(result.AsSpan(FliGraphFile.ScreensOffset + bank * FliGraphFile.BankStride));
    }

    file.BitmapData.AsSpan(0, Math.Min(file.BitmapData.Length, FliGraphFile.BitmapDataSize))
      .CopyTo(result.AsSpan(FliGraphFile.BitmapOffset));

    return result;
  }
}
