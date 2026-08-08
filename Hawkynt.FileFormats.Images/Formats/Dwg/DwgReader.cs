using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.EmbeddedDib;
using FileFormat.EmbeddedPicture;

namespace FileFormat.Dwg;

/// <summary>Reads the thumbnail an AutoCAD drawing states the address of.</summary>
public static class DwgReader {

  /// <summary>More pictures than a thumbnail block has ever held, and it keeps a false match cheap.</summary>
  private const int _MaxImages = 16;

  public static DwgFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("AutoCAD drawing not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DwgFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static DwgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static DwgFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < DwgFile.MinimumHeaderSize || data[0] != 'A' || data[1] != 'C')
      throw new InvalidDataException("Not an AutoCAD drawing: it does not open with a version string.");

    for (var i = 2; i < 6; ++i)
      if (data[i] is < (byte)'0' or > (byte)'9')
        throw new InvalidDataException("Not an AutoCAD drawing: the version string is not AC and four digits.");

    var version = Encoding.ASCII.GetString(data[..6]);
    var seeker = BinaryPrimitives.ReadInt32LittleEndian(data[DwgFile.ImageSeekerOffset..]);
    if (seeker <= 0 || seeker + DwgFile.ImageSentinel.Length + 5 > data.Length)
      throw new InvalidDataException($"An AutoCAD drawing points its thumbnail at {seeker}, which the file cannot hold.");

    if (!data.Slice(seeker, DwgFile.ImageSentinel.Length).SequenceEqual(DwgFile.ImageSentinel))
      throw new InvalidDataException($"An AutoCAD drawing has no thumbnail sentinel at the {seeker} it states.");

    var at = seeker + DwgFile.ImageSentinel.Length;
    var blockLength = BinaryPrimitives.ReadInt32LittleEndian(data[at..]);
    var count = data[at + 4];
    var descriptors = at + 5;
    if (count is < 1 or > _MaxImages || descriptors + count * DwgFile.ImageDescriptorSize > data.Length)
      throw new InvalidDataException($"An AutoCAD thumbnail block states {count} pictures, which the file cannot hold.");

    // The block's stated length runs from just past the length itself to the closing sentinel, and
    // that sentinel is the start one with every byte complemented. Checking it is what says the
    // block has been read as the file laid it out rather than as it happened to fall.
    _CheckClosingSentinel(data, at + 4 + blockLength);

    for (var i = 0; i < count; ++i) {
      var entry = descriptors + i * DwgFile.ImageDescriptorSize;
      var type = data[entry];
      var start = BinaryPrimitives.ReadInt32LittleEndian(data[(entry + 1)..]);
      var length = BinaryPrimitives.ReadInt32LittleEndian(data[(entry + 5)..]);

      if (type == DwgFile.TypeHeaderData)
        continue;

      if (start < 0 || length < 1 || (long)start + length > data.Length)
        throw new InvalidDataException($"An AutoCAD thumbnail states {length} bytes at {start}, which the file cannot hold.");

      var picture = data.Slice(start, length);
      var decoded = type switch {
        DwgFile.TypeBitmap => EmbeddedDibReader.DecodeHeaderless(picture),
        DwgFile.TypeMetafile or DwgFile.TypePng => EmbeddedPictureReader.Decode(picture),
        _ => _Unknown(type)
      };

      return new() { Thumbnail = decoded, ThumbnailType = type, Version = version };
    }

    throw new InvalidDataException("An AutoCAD drawing's thumbnail block holds nothing but its title.");
  }

  private static RawImage _Unknown(int type)
    => throw new InvalidDataException($"An AutoCAD thumbnail of type {type} is not one this reads.");

  private static void _CheckClosingSentinel(ReadOnlySpan<byte> data, int at) {
    var sentinel = DwgFile.ImageSentinel;
    if (at < 0 || at + sentinel.Length > data.Length)
      throw new InvalidDataException($"An AutoCAD thumbnail block ends at {at}, which the file cannot hold.");

    for (var i = 0; i < sentinel.Length; ++i)
      if (data[at + i] != (byte)~sentinel[i])
        throw new InvalidDataException($"An AutoCAD thumbnail block does not close with its sentinel at {at}.");
  }
}
