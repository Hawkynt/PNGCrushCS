using System;
using System.IO;

namespace FileFormat.Ico;

/// <summary>Assembles ICO file bytes from an <see cref="IcoFile"/>.</summary>
public static class IcoWriter {

  public static byte[] ToBytes(IcoFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file, IcoFileType.Icon);
  }

  internal static byte[] _Assemble(IcoFile file, IcoFileType fileType, Func<int, (ushort field4, ushort field5)>? directoryFieldOverride = null) {
    var count = file.Images.Count;

    // Calculate total size
    var dataStart = IcoHeader.StructSize + count * IcoDirectoryEntry.StructSize;
    var totalDataSize = 0;
    for (var i = 0; i < count; ++i)
      totalDataSize += file.Images[i].Data.Length;

    var result = new byte[dataStart + totalDataSize];

    // Write ICO header
    var header = new IcoHeader(0, (ushort)fileType, (ushort)count);
    header.WriteTo(result);

    // Write directory entries
    var currentDataOffset = dataStart;
    for (var i = 0; i < count; ++i) {
      var image = file.Images[i];

      ushort field4, field5;
      if (directoryFieldOverride != null)
        (field4, field5) = directoryFieldOverride(i);
      else {
        field4 = 1;                           // Planes
        field5 = (ushort)image.BitsPerPixel;  // Bit count
      }

      var entry = new IcoDirectoryEntry(
        image.Width >= 256 ? (byte)0 : (byte)image.Width,
        image.Height >= 256 ? (byte)0 : (byte)image.Height,
        0,  // ColorCount
        0,  // Reserved
        field4,
        field5,
        image.Data.Length,
        currentDataOffset
      );
      entry.WriteTo(result.AsSpan(IcoHeader.StructSize + i * IcoDirectoryEntry.StructSize));

      currentDataOffset += image.Data.Length;
    }

    // Write image data
    var dataOffset = dataStart;
    for (var i = 0; i < count; ++i) {
      var data = file.Images[i].Data;
      data.AsSpan(0, data.Length).CopyTo(result.AsSpan(dataOffset));
      dataOffset += data.Length;
    }

    return result;
  }
}
