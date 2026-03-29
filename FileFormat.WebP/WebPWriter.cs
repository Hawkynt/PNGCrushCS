using System;
using System.Collections.Generic;
using FileFormat.Riff;

namespace FileFormat.WebP;

/// <summary>Assembles WebP files into the RIFF container format.</summary>
public static class WebPWriter {

  private const string _FORM_TYPE = "WEBP";
  private const string _CHUNK_VP8 = "VP8 ";
  private const string _CHUNK_VP8L = "VP8L";
  private const string _CHUNK_VP8X = "VP8X";

  public static byte[] ToBytes(WebPFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var needsExtended = file.Features.HasAlpha
                        || file.Features.IsAnimated
                        || file.MetadataChunks.Count > 0;

    var chunks = new List<RiffChunk>();

    if (needsExtended) {
      chunks.Add(new RiffChunk { Id = _CHUNK_VP8X, Data = _BuildVp8XData(file) });

      // Image data chunk
      chunks.Add(new RiffChunk {
        Id = file.IsLossless ? _CHUNK_VP8L : _CHUNK_VP8,
        Data = file.ImageData
      });

      // Metadata chunks
      foreach (var (chunkId, data) in file.MetadataChunks)
        chunks.Add(new RiffChunk { Id = chunkId, Data = data });
    } else {
      // Simple format: just the image chunk
      chunks.Add(new RiffChunk {
        Id = file.IsLossless ? _CHUNK_VP8L : _CHUNK_VP8,
        Data = file.ImageData
      });
    }

    var riffFile = new RiffFile { FormType = _FORM_TYPE, Chunks = chunks };
    return RiffWriter.ToBytes(riffFile);
  }

  private static byte[] _BuildVp8XData(WebPFile file) {
    var data = new byte[10];

    byte flags = 0;
    if (file.Features.HasAlpha)
      flags |= 0x10;
    if (file.Features.IsAnimated)
      flags |= 0x02;

    // Check for metadata chunk types
    foreach (var (chunkId, _) in file.MetadataChunks) {
      switch (chunkId) {
        case "ICCP":
          flags |= 0x20;
          break;
        case "EXIF":
          flags |= 0x08;
          break;
        case "XMP ":
          flags |= 0x04;
          break;
      }
    }

    data[0] = flags;
    // bytes 1-3 reserved (zero)

    var w = file.Features.Width - 1;
    data[4] = (byte)(w & 0xFF);
    data[5] = (byte)((w >> 8) & 0xFF);
    data[6] = (byte)((w >> 16) & 0xFF);

    var h = file.Features.Height - 1;
    data[7] = (byte)(h & 0xFF);
    data[8] = (byte)((h >> 8) & 0xFF);
    data[9] = (byte)((h >> 16) & 0xFF);

    return data;
  }
}
