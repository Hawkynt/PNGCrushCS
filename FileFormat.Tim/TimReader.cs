using System;
using System.IO;

namespace FileFormat.Tim;

/// <summary>Reads PlayStation 1 TIM texture files from bytes, streams, or file paths.</summary>
public static class TimReader {

  public static TimFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TIM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TimFile FromStream(Stream stream) {
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

  public static TimFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < TimHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid TIM file.");

    var span = data.AsSpan();
    var header = TimHeader.ReadFrom(span);
    if (header.Magic != TimHeader.ExpectedMagic)
      throw new InvalidDataException($"Invalid TIM magic: 0x{header.Magic:X8}, expected 0x{TimHeader.ExpectedMagic:X8}.");

    var bpp = header.Bpp;
    var hasClut = header.HasClut;
    var offset = TimHeader.StructSize;

    byte[]? clutData = null;
    int clutX = 0, clutY = 0, clutWidth = 0, clutHeight = 0;

    if (hasClut) {
      if (offset + TimBlockHeader.StructSize > data.Length)
        throw new InvalidDataException("Data too small for CLUT block header.");

      var clutBlock = TimBlockHeader.ReadFrom(span[offset..]);
      clutX = clutBlock.X;
      clutY = clutBlock.Y;
      clutWidth = clutBlock.Width;
      clutHeight = clutBlock.Height;
      offset += TimBlockHeader.StructSize;

      var clutDataSize = (int)clutBlock.BlockSize - TimBlockHeader.StructSize;
      if (clutDataSize < 0 || offset + clutDataSize > data.Length)
        throw new InvalidDataException("Invalid CLUT block size.");

      clutData = new byte[clutDataSize];
      data.AsSpan(offset, clutDataSize).CopyTo(clutData.AsSpan(0));
      offset += clutDataSize;
    }

    if (offset + TimBlockHeader.StructSize > data.Length)
      throw new InvalidDataException("Data too small for image block header.");

    var imageBlock = TimBlockHeader.ReadFrom(span[offset..]);
    var imageX = imageBlock.X;
    var imageY = imageBlock.Y;
    var vramWidth = imageBlock.Width;
    var imageHeight = imageBlock.Height;
    offset += TimBlockHeader.StructSize;

    var pixelDataSize = (int)imageBlock.BlockSize - TimBlockHeader.StructSize;
    if (pixelDataSize < 0 || offset + pixelDataSize > data.Length)
      throw new InvalidDataException("Invalid image block size.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(offset, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    var realWidth = _VramWidthToPixels(vramWidth, bpp);

    return new TimFile {
      Width = realWidth,
      Height = imageHeight,
      Bpp = bpp,
      HasClut = hasClut,
      ClutData = clutData,
      ClutX = clutX,
      ClutY = clutY,
      ClutWidth = clutWidth,
      ClutHeight = clutHeight,
      ImageX = imageX,
      ImageY = imageY,
      PixelData = pixelData
    };
  }

  private static int _VramWidthToPixels(int vramWidth, TimBpp bpp) => bpp switch {
    TimBpp.Bpp4 => vramWidth * 4,
    TimBpp.Bpp8 => vramWidth * 2,
    TimBpp.Bpp16 => vramWidth,
    TimBpp.Bpp24 => vramWidth * 2 / 3,
    _ => vramWidth
  };
}
