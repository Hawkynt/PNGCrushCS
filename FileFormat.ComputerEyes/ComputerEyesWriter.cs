using System;

namespace FileFormat.ComputerEyes;

/// <summary>Assembles ComputerEyes file bytes from a <see cref="ComputerEyesFile"/>.</summary>
public static class ComputerEyesWriter {

  public static byte[] ToBytes(ComputerEyesFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[ComputerEyesFile.HeaderSize + file.PixelData.Length];
    result[0] = (byte)(file.Width & 0xFF);
    result[1] = (byte)((file.Width >> 8) & 0xFF);
    result[2] = (byte)(file.Height & 0xFF);
    result[3] = (byte)((file.Height >> 8) & 0xFF);
    file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(result.AsSpan(ComputerEyesFile.HeaderSize));
    return result;
  }
}
