using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Mrc;

/// <summary>Reads MRC2014 files from bytes, streams, or file paths.</summary>
public static class MrcReader {

  public static MrcFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MRC file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MrcFile FromStream(Stream stream) {
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

  public static MrcFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MrcFile.HeaderSize)
      throw new InvalidDataException($"Data too small for a valid MRC file: expected at least {MrcFile.HeaderSize} bytes, got {data.Length}.");

    // Validate MAP magic at offset 208
    if (data[208] != MrcFile.MapMagic[0]
        || data[209] != MrcFile.MapMagic[1]
        || data[210] != MrcFile.MapMagic[2]
        || data[211] != MrcFile.MapMagic[3])
      throw new InvalidDataException("Invalid MRC file: missing MAP magic at offset 208.");

    // Detect endianness from MACHST at offset 212
    var machineStamp = data[212];
    var isBigEndian = machineStamp == 0x11;

    int nx, ny, nz, mode, nsymbt;
    if (isBigEndian) {
      nx = BinaryPrimitives.ReadInt32BigEndian(data);
      ny = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
      nz = BinaryPrimitives.ReadInt32BigEndian(data[8..]);
      mode = BinaryPrimitives.ReadInt32BigEndian(data[12..]);
      nsymbt = BinaryPrimitives.ReadInt32BigEndian(data[92..]);
    } else {
      nx = BinaryPrimitives.ReadInt32LittleEndian(data);
      ny = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
      nz = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
      mode = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
      nsymbt = BinaryPrimitives.ReadInt32LittleEndian(data[92..]);
    }

    if (nx <= 0)
      throw new InvalidDataException($"Invalid MRC NX (columns): {nx}.");
    if (ny <= 0)
      throw new InvalidDataException($"Invalid MRC NY (rows): {ny}.");
    if (nz <= 0)
      throw new InvalidDataException($"Invalid MRC NZ (sections): {nz}.");
    if (nsymbt < 0)
      throw new InvalidDataException($"Invalid MRC NSYMBT (extended header size): {nsymbt}.");

    var extendedHeader = Array.Empty<byte>();
    if (nsymbt > 0) {
      if (data.Length < MrcFile.HeaderSize + nsymbt)
        throw new InvalidDataException($"Data too small for extended header: expected {MrcFile.HeaderSize + nsymbt} bytes, got {data.Length}.");

      extendedHeader = new byte[nsymbt];
      data.Slice(MrcFile.HeaderSize, nsymbt).CopyTo(extendedHeader.AsSpan(0));
    }

    var dataOffset = MrcFile.HeaderSize + nsymbt;
    var bytesPerVoxel = mode switch {
      0 => 1,
      1 => 2,
      2 => 4,
      6 => 2,
      _ => throw new InvalidDataException($"Unsupported MRC mode: {mode}.")
    };

    var expectedPixelBytes = (long)nx * ny * nz * bytesPerVoxel;
    var available = data.Length - dataOffset;
    var copyLen = (int)Math.Min(expectedPixelBytes, available);

    var pixelData = new byte[expectedPixelBytes];
    if (copyLen > 0)
      data.Slice(dataOffset, copyLen).CopyTo(pixelData.AsSpan(0));

    return new MrcFile {
      Width = nx,
      Height = ny,
      Sections = nz,
      Mode = mode,
      IsBigEndian = isBigEndian,
      ExtendedHeaderSize = nsymbt,
      ExtendedHeader = extendedHeader,
      PixelData = pixelData,
    };
  }

  public static MrcFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
