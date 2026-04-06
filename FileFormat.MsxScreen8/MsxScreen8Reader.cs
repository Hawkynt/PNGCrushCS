using System;
using System.IO;

namespace FileFormat.MsxScreen8;

/// <summary>Reads MSX2 Screen 8 image files from bytes, streams, or file paths.</summary>
public static class MsxScreen8Reader {

  public static MsxScreen8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MSX Screen 8 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxScreen8File FromStream(Stream stream) {
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

  public static MsxScreen8File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MsxScreen8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MsxScreen8File.PixelDataSize)
      throw new InvalidDataException($"Data too small for a valid MSX Screen 8 file: got {data.Length} bytes, need at least {MsxScreen8File.PixelDataSize}.");

    var hasBsave = data.Length >= MsxScreen8File.BsaveHeaderSize + MsxScreen8File.PixelDataSize && data[0] == MsxScreen8File.BsaveMagic;
    var rawOffset = hasBsave ? MsxScreen8File.BsaveHeaderSize : 0;

    if (data.Length - rawOffset < MsxScreen8File.PixelDataSize)
      throw new InvalidDataException($"Data too small for MSX Screen 8 pixel data after header: got {data.Length - rawOffset} bytes, need {MsxScreen8File.PixelDataSize}.");

    var pixelData = new byte[MsxScreen8File.PixelDataSize];
    data.AsSpan(rawOffset, MsxScreen8File.PixelDataSize).CopyTo(pixelData);

    return new() {
      PixelData = pixelData,
      HasBsaveHeader = hasBsave,
    };
  }
}
