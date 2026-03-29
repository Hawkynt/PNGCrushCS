using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Tim2;

/// <summary>Reads PlayStation 2/PSP TIM2 texture files from bytes, streams, or file paths.</summary>
public static class Tim2Reader {

  public static Tim2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TIM2 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Tim2File FromStream(Stream stream) {
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

  public static Tim2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Tim2Header.StructSize)
      throw new InvalidDataException("Data too small for a valid TIM2 file.");

    var span = data.AsSpan();
    var header = Tim2Header.ReadFrom(span);
    if (!header.IsValid)
      throw new InvalidDataException("Invalid TIM2 signature.");

    var version = header.Version;
    var alignment = header.Alignment;
    var pictureCount = header.PictureCount;

    var offset = Tim2Header.StructSize;
    var pictures = new List<Tim2Picture>(pictureCount);

    for (var i = 0; i < pictureCount; ++i) {
      if (offset + Tim2PictureHeader.StructSize > data.Length)
        throw new InvalidDataException($"Data too small for picture header {i}.");

      var picHeader = Tim2PictureHeader.ReadFrom(span[offset..]);

      var headerSize = picHeader.HeaderSize;
      var imageDataSize = (int)picHeader.ImageDataSize;
      var paletteSize = (int)picHeader.PaletteSize;
      var paletteColors = picHeader.PaletteColors;

      var imageDataOffset = offset + headerSize;
      if (imageDataOffset + imageDataSize > data.Length)
        throw new InvalidDataException($"Image data extends beyond file for picture {i}.");

      var pixelData = new byte[imageDataSize];
      data.AsSpan(imageDataOffset, imageDataSize).CopyTo(pixelData.AsSpan(0));

      byte[]? paletteData = null;
      if (paletteSize > 0) {
        var paletteOffset = imageDataOffset + imageDataSize;
        if (paletteOffset + paletteSize > data.Length)
          throw new InvalidDataException($"Palette data extends beyond file for picture {i}.");

        paletteData = new byte[paletteSize];
        data.AsSpan(paletteOffset, paletteSize).CopyTo(paletteData.AsSpan(0));
      }

      pictures.Add(new Tim2Picture {
        Width = picHeader.Width,
        Height = picHeader.Height,
        Format = (Tim2Format)picHeader.PictureFormat,
        MipmapCount = picHeader.Mipmaps,
        PixelData = pixelData,
        PaletteData = paletteData,
        PaletteColors = paletteColors
      });

      offset += (int)picHeader.TotalSize;
    }

    return new Tim2File {
      Version = version,
      Alignment = alignment,
      Pictures = pictures.AsReadOnly()
    };
  }
}
