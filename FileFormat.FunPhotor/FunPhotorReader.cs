using System;
using System.IO;

namespace FileFormat.FunPhotor;

/// <summary>Reads Fun Photor C64 multicolor files from bytes, streams, or file paths.</summary>
public static class FunPhotorReader {

  public static FunPhotorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fun Photor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FunPhotorFile FromStream(Stream stream) {
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

  public static FunPhotorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != FunPhotorFile.ExpectedFileSize)
      throw new InvalidDataException($"Fun Photor file must be exactly {FunPhotorFile.ExpectedFileSize} bytes, got {data.Length}.");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += FunPhotorFile.LoadAddressSize;

    var bitmapData = new byte[FunPhotorFile.BitmapDataSize];
    data.AsSpan(offset, FunPhotorFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += FunPhotorFile.BitmapDataSize;

    var screenData = new byte[FunPhotorFile.ScreenDataSize];
    data.AsSpan(offset, FunPhotorFile.ScreenDataSize).CopyTo(screenData.AsSpan(0));
    offset += FunPhotorFile.ScreenDataSize;

    var colorData = new byte[FunPhotorFile.ColorDataSize];
    data.AsSpan(offset, FunPhotorFile.ColorDataSize).CopyTo(colorData.AsSpan(0));
    offset += FunPhotorFile.ColorDataSize;

    var reserved = new byte[FunPhotorFile.ReservedSize];
    data.AsSpan(offset, FunPhotorFile.ReservedSize).CopyTo(reserved.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      Reserved = reserved,
    };
  }
}
