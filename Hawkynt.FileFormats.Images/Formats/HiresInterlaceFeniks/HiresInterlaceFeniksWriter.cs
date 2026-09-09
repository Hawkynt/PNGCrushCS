using System;
using System.Buffers.Binary;

namespace FileFormat.HiresInterlaceFeniks;

/// <summary>Assembles Hires Interlace (.hlf, .hie) file bytes from a <see cref="HiresInterlaceFeniksFile"/>.</summary>
public static class HiresInterlaceFeniksWriter {

  public static byte[] ToBytes(HiresInterlaceFeniksFile file) {
    ArgumentNullException.ThrowIfNull(file.FirstBitmap);
    ArgumentNullException.ThrowIfNull(file.FirstScreen);
    ArgumentNullException.ThrowIfNull(file.SecondBitmap);
    ArgumentNullException.ThrowIfNull(file.SecondScreen);

    var result = new byte[HiresInterlaceFeniksFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    _Copy(file.FirstBitmap, result, HiresInterlaceFeniksFile.FirstBitmapOffset, HiresInterlaceFeniksFile.BitmapDataSize);
    _Copy(file.SecondScreen, result, HiresInterlaceFeniksFile.SecondScreenOffset, HiresInterlaceFeniksFile.ScreenRamSize);
    _Copy(file.FirstScreen, result, HiresInterlaceFeniksFile.FirstScreenOffset, HiresInterlaceFeniksFile.ScreenRamSize);
    _Copy(file.SecondBitmap, result, HiresInterlaceFeniksFile.SecondBitmapOffset, HiresInterlaceFeniksFile.BitmapDataSize);

    return result;
  }

  private static void _Copy(byte[] source, byte[] destination, int at, int length)
    => source.AsSpan(0, Math.Min(source.Length, length)).CopyTo(destination.AsSpan(at));
}
