using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Vtf;

/// <summary>Reads VTF files from bytes, streams, or file paths.</summary>
public static class VtfReader {

  public static VtfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("VTF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VtfFile FromStream(Stream stream) {
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

  public static VtfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < VtfHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid VTF file.");

    var span = data.AsSpan();
    var header = VtfHeader.ReadFrom(span);

    if (header.Sig0 != (byte)'V' || header.Sig1 != (byte)'T' || header.Sig2 != (byte)'F' || header.Sig3 != 0)
      throw new InvalidDataException("Invalid VTF signature.");

    var width = header.Width;
    var height = header.Height;
    var mipmapCount = header.MipmapCount;
    var highResFormat = (VtfFormat)header.HighResFormat;
    var lowResFormat = (VtfFormat)header.LowResFormat;
    var flags = (VtfFlags)header.Flags;
    var frames = header.Frames;
    var headerSize = header.HeaderSize;

    var offset = headerSize;

    // Read low-res thumbnail data if present
    byte[]? thumbnailData = null;
    if (lowResFormat != VtfFormat.None && header.LowResWidth > 0 && header.LowResHeight > 0) {
      var thumbSize = _CalculateDataSize(header.LowResWidth, header.LowResHeight, lowResFormat);
      if (offset + thumbSize <= data.Length) {
        thumbnailData = new byte[thumbSize];
        data.AsSpan(offset, thumbSize).CopyTo(thumbnailData.AsSpan(0));
        offset += thumbSize;
      }
    }

    // Read high-res mipmap surfaces (stored smallest-to-largest)
    var surfaces = new List<VtfSurface>();
    for (var mip = mipmapCount - 1; mip >= 0; --mip) {
      var mipWidth = Math.Max(1, width >> mip);
      var mipHeight = Math.Max(1, height >> mip);
      var mipSize = _CalculateDataSize(mipWidth, mipHeight, highResFormat);

      for (var frame = 0; frame < frames; ++frame) {
        if (offset + mipSize > data.Length)
          break;

        var surfaceData = new byte[mipSize];
        data.AsSpan(offset, mipSize).CopyTo(surfaceData.AsSpan(0));
        offset += mipSize;

        surfaces.Add(new VtfSurface {
          Width = mipWidth,
          Height = mipHeight,
          MipLevel = mip,
          Frame = frame,
          Data = surfaceData
        });
      }
    }

    return new VtfFile {
      Width = width,
      Height = height,
      MipmapCount = mipmapCount,
      Format = highResFormat,
      Flags = flags,
      Frames = frames,
      VersionMajor = header.VersionMajor,
      VersionMinor = header.VersionMinor,
      ThumbnailData = thumbnailData,
      Surfaces = surfaces
    };
  }

  internal static int _CalculateDataSize(int width, int height, VtfFormat format) => format switch {
    VtfFormat.Rgba8888 => width * height * 4,
    VtfFormat.Abgr8888 => width * height * 4,
    VtfFormat.Argb8888 => width * height * 4,
    VtfFormat.Bgra8888 => width * height * 4,
    VtfFormat.Rgb888 => width * height * 3,
    VtfFormat.Bgr888 => width * height * 3,
    VtfFormat.Rgb888Bluescreen => width * height * 3,
    VtfFormat.Bgr888Bluescreen => width * height * 3,
    VtfFormat.Rgb565 => width * height * 2,
    VtfFormat.Ia88 => width * height * 2,
    VtfFormat.Uv88 => width * height * 2,
    VtfFormat.I8 => width * height,
    VtfFormat.A8 => width * height,
    VtfFormat.Dxt1 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8,
    VtfFormat.Dxt3 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16,
    VtfFormat.Dxt5 => Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16,
    VtfFormat.Rgba16161616F => width * height * 8,
    VtfFormat.Rgba16161616 => width * height * 8,
    _ => 0
  };
}
