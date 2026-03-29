using System;

namespace FileFormat.IconLibrary;

/// <summary>Assembles Icon Library bytes from an <see cref="IconLibraryFile"/>.</summary>
public static class IconLibraryWriter {

  public static byte[] ToBytes(IconLibraryFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[file.RawData.Length];
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result);
    return result;
  }
}
