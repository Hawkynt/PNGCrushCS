using System;
using System.IO;

namespace FileFormat.Wal;

/// <summary>Reads WAL (Quake 2 Texture) files from bytes, streams, or file paths.</summary>
public static class WalReader {

  private const int _MIN_FILE_SIZE = WalHeader.StructSize; // 100

  public static WalFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WAL file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WalFile FromStream(Stream stream) {
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

  public static WalFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid WAL file.");

    var span = data.AsSpan();
    var header = WalHeader.ReadFrom(span);

    var width = (int)header.Width;
    var height = (int)header.Height;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException("WAL dimensions must be greater than zero.");

    var mip0Size = width * height;
    var mip0Offset = (int)header.MipOffset0;

    if (mip0Offset + mip0Size > data.Length)
      throw new InvalidDataException("WAL mip level 0 data extends beyond file.");

    var pixelData = new byte[mip0Size];
    data.AsSpan(mip0Offset, mip0Size).CopyTo(pixelData.AsSpan(0));

    byte[][]? mipMaps = null;

    var mipOffsets = new[] { header.MipOffset1, header.MipOffset2, header.MipOffset3 };
    var mipWidth = width;
    var mipHeight = height;
    var allMipsPresent = true;

    for (var i = 0; i < 3; ++i) {
      mipWidth /= 2;
      mipHeight /= 2;
      if (mipWidth < 1 || mipHeight < 1) {
        allMipsPresent = false;
        break;
      }

      var offset = (int)mipOffsets[i];
      var size = mipWidth * mipHeight;
      if (offset == 0 || offset + size > data.Length) {
        allMipsPresent = false;
        break;
      }
    }

    if (allMipsPresent) {
      mipMaps = new byte[3][];
      mipWidth = width;
      mipHeight = height;

      for (var i = 0; i < 3; ++i) {
        mipWidth /= 2;
        mipHeight /= 2;
        var offset = (int)mipOffsets[i];
        var size = mipWidth * mipHeight;
        mipMaps[i] = new byte[size];
        data.AsSpan(offset, size).CopyTo(mipMaps[i].AsSpan(0));
      }
    }

    return new WalFile {
      Name = header.Name,
      Width = width,
      Height = height,
      NextFrameName = header.NextFrameName,
      Flags = header.Flags,
      Contents = header.Contents,
      Value = header.Value,
      PixelData = pixelData,
      MipMaps = mipMaps
    };
  }
}
