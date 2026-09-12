using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Ico;

namespace FileFormat.Cur;

/// <summary>Reads CUR files from bytes, streams, or file paths.</summary>
public static class CurReader {

  public static CurFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CUR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CurFile FromStream(Stream stream) {
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

  public static CurFile FromSpan(ReadOnlySpan<byte> data) {
    // One directory walk, which already reads the hotspot because the file said it was a cursor.
    // This used to parse the file twice — once as an icon for the pictures and again by hand for
    // the hotspots — and the icon pass read the hotspot's lower half as the depth.
    var bundle = IcoReader.ReadBundle(data);

    if (bundle.Kind != IcoFileType.Cursor)
      throw new InvalidDataException($"Invalid CUR type field: expected 2, got {(ushort)bundle.Kind}.");

    var images = new List<CurImage>(bundle.Entries.Count);
    foreach (var entry in bundle.Entries)
      images.Add(new CurImage {
        Width = entry.Width,
        Height = entry.Height,
        BitsPerPixel = entry.BitsPerPixel,
        Format = entry.Format,
        Data = entry.Data,
        HotspotX = entry.HotspotX,
        HotspotY = entry.HotspotY
      });

    return new CurFile { Images = images };
  }

  public static CurFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
