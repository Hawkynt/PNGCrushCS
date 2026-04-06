using System;
using System.IO;

namespace FileFormat.CreateWithGarfield;

/// <summary>Reads Commodore 64 Create with Garfield hires files from bytes, streams, or file paths.</summary>
public static class CreateWithGarfieldReader {

  public static CreateWithGarfieldFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Create with Garfield file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CreateWithGarfieldFile FromStream(Stream stream) {
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

  public static CreateWithGarfieldFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static CreateWithGarfieldFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CreateWithGarfieldFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Create with Garfield file (expected {CreateWithGarfieldFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != CreateWithGarfieldFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Create with Garfield file size (expected {CreateWithGarfieldFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += CreateWithGarfieldFile.LoadAddressSize;

    var bitmapData = new byte[CreateWithGarfieldFile.BitmapDataSize];
    data.AsSpan(offset, CreateWithGarfieldFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += CreateWithGarfieldFile.BitmapDataSize;

    var screenRam = new byte[CreateWithGarfieldFile.ScreenRamSize];
    data.AsSpan(offset, CreateWithGarfieldFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += CreateWithGarfieldFile.ScreenRamSize;

    var borderColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      BorderColor = borderColor,
    };
  }
}
