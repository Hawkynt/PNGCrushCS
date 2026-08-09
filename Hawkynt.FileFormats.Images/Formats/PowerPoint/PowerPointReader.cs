using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.EmbeddedPicture;

namespace FileFormat.PowerPoint;

/// <summary>Walks a presentation's OfficeArt records and takes the first picture one of them carries.</summary>
public static class PowerPointReader {

  public static PowerPointFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PowerPoint presentation not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PowerPointFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromSpan(memory.ToArray());
  }

  public static PowerPointFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PowerPointFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PowerPointFile.ScanStart)
      throw new InvalidDataException(
        $"Data too small for a PowerPoint presentation (minimum {PowerPointFile.ScanStart} bytes, got {data.Length}).");

    if (!data[..PowerPointFile.Signature.Length].SequenceEqual(PowerPointFile.Signature))
      throw new InvalidDataException("Not a PowerPoint presentation: it is not a Microsoft compound document.");

    var found = _FindFirstPicture(data);
    if (found < 0)
      throw new InvalidDataException("A PowerPoint presentation carries no picture behind its compound-document header.");

    var decoded = PixelConverter.Convert(EmbeddedPictureReader.Decode(data[found..]), PixelFormat.Rgb24);

    return new() { Width = decoded.Width, Height = decoded.Height, PixelData = decoded.PixelData };
  }

  /// <summary>Where the first JPEG or PNG BLIP's picture data begins, or -1 when there is none.</summary>
  private static int _FindFirstPicture(ReadOnlySpan<byte> data) {
    for (var at = PowerPointFile.ScanStart; at + PowerPointFile.RecordHeaderSize <= data.Length;) {
      var versionAndInstance = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
      var type = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 2)..]);

      var isPicture =
        type == PowerPointFile.JpegBlipType && versionAndInstance == PowerPointFile.JpegBlipVersionAndInstance
        || type == PowerPointFile.PngBlipType && versionAndInstance == PowerPointFile.PngBlipVersionAndInstance;

      if (isPicture) {
        var picture = at + PowerPointFile.RecordHeaderSize + PowerPointFile.BlipPrefixSize;
        return picture < data.Length ? picture : -1;
      }

      // Every other record is stepped over by the length it states — which for a record holding
      // others steps over all of them, exactly as XnView's walk does. A length of zero, or one
      // longer than the file, ends the walk rather than moving it nowhere or past the end.
      var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
      if (length == 0 || length > (uint)data.Length)
        return -1;

      var next = (long)at + PowerPointFile.RecordHeaderSize + length;
      if (next > data.Length)
        return -1;

      at = (int)next;
    }

    return -1;
  }
}
