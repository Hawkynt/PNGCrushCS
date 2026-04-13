using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Sff;

/// <summary>Reads SFF (Structured Fax File) files from bytes, streams, or file paths.</summary>
public static class SffReader {

  public static SffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SFF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SffFile FromStream(Stream stream) {
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

  public static SffFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < SffHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid SFF file.");

    var span = data;
    var header = SffHeader.ReadFrom(span);

    if (header.Magic1 != SffHeader.MagicByte1 || header.Magic2 != SffHeader.MagicByte2 || header.Magic3 != SffHeader.MagicByte3 || header.Magic4 != SffHeader.MagicByte4)
      throw new InvalidDataException("Invalid SFF magic bytes.");

    var pages = new List<SffPage>();
    var currentOffset = (int)header.FirstPageOffset;

    for (var i = 0; i < header.PageCount && currentOffset > 0 && currentOffset + SffPageHeader.StructSize <= data.Length; ++i) {
      var pageHeader = SffPageHeader.ReadFrom(span[currentOffset..]);
      var width = (int)pageHeader.LineLength;
      var height = (int)pageHeader.PageHeight;
      var bytesPerRow = (width + 7) / 8;
      var pixelDataLength = bytesPerRow * height;

      var dataStart = currentOffset + SffPageHeader.StructSize;
      var available = Math.Min(pixelDataLength, data.Length - dataStart);
      var pixelData = new byte[pixelDataLength];
      if (available > 0)
        data.Slice(dataStart, available).CopyTo(pixelData.AsSpan(0));

      pages.Add(new SffPage {
        Width = width,
        Height = height,
        HResolution = pageHeader.ResolutionHorizontal,
        VResolution = pageHeader.ResolutionVertical,
        PixelData = pixelData
      });

      currentOffset = (int)pageHeader.NextPageOffset;
    }

    return new SffFile {
      Version = header.Version,
      Pages = pages
    };
    }

  public static SffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SffHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid SFF file.");

    var span = data.AsSpan();
    var header = SffHeader.ReadFrom(span);

    if (header.Magic1 != SffHeader.MagicByte1 || header.Magic2 != SffHeader.MagicByte2 || header.Magic3 != SffHeader.MagicByte3 || header.Magic4 != SffHeader.MagicByte4)
      throw new InvalidDataException("Invalid SFF magic bytes.");

    var pages = new List<SffPage>();
    var currentOffset = (int)header.FirstPageOffset;

    for (var i = 0; i < header.PageCount && currentOffset > 0 && currentOffset + SffPageHeader.StructSize <= data.Length; ++i) {
      var pageHeader = SffPageHeader.ReadFrom(span[currentOffset..]);
      var width = (int)pageHeader.LineLength;
      var height = (int)pageHeader.PageHeight;
      var bytesPerRow = (width + 7) / 8;
      var pixelDataLength = bytesPerRow * height;

      var dataStart = currentOffset + SffPageHeader.StructSize;
      var available = Math.Min(pixelDataLength, data.Length - dataStart);
      var pixelData = new byte[pixelDataLength];
      if (available > 0)
        data.AsSpan(dataStart, available).CopyTo(pixelData.AsSpan(0));

      pages.Add(new SffPage {
        Width = width,
        Height = height,
        HResolution = pageHeader.ResolutionHorizontal,
        VResolution = pageHeader.ResolutionVertical,
        PixelData = pixelData
      });

      currentOffset = (int)pageHeader.NextPageOffset;
    }

    return new SffFile {
      Version = header.Version,
      Pages = pages
    };
  }
}
