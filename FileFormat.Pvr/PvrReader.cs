using System;
using System.IO;

namespace FileFormat.Pvr;

/// <summary>Reads PVR (PowerVR Texture v3) files from bytes, streams, or file paths.</summary>
public static class PvrReader {

  private const int _MIN_FILE_SIZE = PvrHeader.StructSize; // 52

  public static PvrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PVR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PvrFile FromStream(Stream stream) {
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

  public static PvrFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PvrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid PVR file.");

    var span = data.AsSpan();
    var header = PvrHeader.ReadFrom(span);

    if (header.Version != PvrHeader.Magic)
      throw new InvalidDataException("Invalid PVR magic number.");

    var metadataSize = (int)header.MetadataSize;
    var dataOffset = PvrHeader.StructSize + metadataSize;

    var metadata = Array.Empty<byte>();
    if (metadataSize > 0 && dataOffset <= data.Length) {
      metadata = new byte[metadataSize];
      data.AsSpan(PvrHeader.StructSize, Math.Min(metadataSize, data.Length - PvrHeader.StructSize)).CopyTo(metadata.AsSpan(0));
    }

    var compressedDataSize = data.Length - dataOffset;
    var compressedData = Array.Empty<byte>();
    if (compressedDataSize > 0) {
      compressedData = new byte[compressedDataSize];
      data.AsSpan(dataOffset, compressedDataSize).CopyTo(compressedData.AsSpan(0));
    }

    return new PvrFile {
      Width = (int)header.Width,
      Height = (int)header.Height,
      Depth = (int)header.Depth,
      PixelFormat = (PvrPixelFormat)header.PixelFormat,
      ColorSpace = (PvrColorSpace)header.ColorSpace,
      ChannelType = header.ChannelType,
      Flags = header.Flags,
      Surfaces = (int)header.Surfaces,
      Faces = (int)header.Faces,
      MipmapCount = (int)header.MipmapCount,
      MetadataSize = metadataSize,
      Metadata = metadata,
      CompressedData = compressedData
    };
  }
}
