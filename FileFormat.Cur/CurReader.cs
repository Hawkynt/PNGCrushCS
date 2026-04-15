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
    // Parse image data using IcoReader's internal parser with Cursor type
    var icoFile = IcoReader._Parse(data, IcoFileType.Cursor);

    // Re-read directory entries to extract hotspot fields
    var header = IcoHeader.ReadFrom(data);
    var count = header.Count;
    var images = new List<CurImage>(count);
    for (var i = 0; i < count; ++i) {
      var entry = IcoDirectoryEntry.ReadFrom(data[(IcoHeader.StructSize + i * IcoDirectoryEntry.StructSize)..]);
      var hotspotX = entry.Field4;
      var hotspotY = entry.Field5;

      var icoImage = icoFile.Images[i];
      images.Add(new CurImage {
        Width = icoImage.Width,
        Height = icoImage.Height,
        BitsPerPixel = icoImage.BitsPerPixel,
        Format = icoImage.Format,
        Data = icoImage.Data,
        HotspotX = hotspotX,
        HotspotY = hotspotY
      });
    }

    return new CurFile { Images = images };
  }

  public static CurFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
