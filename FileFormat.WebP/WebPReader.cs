using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Riff;

namespace FileFormat.WebP;

/// <summary>Parses WebP files from the RIFF container level.</summary>
public static class WebPReader {

  private const string _FORM_TYPE = "WEBP";
  private const string _CHUNK_VP8 = "VP8 ";
  private const string _CHUNK_VP8L = "VP8L";
  private const string _CHUNK_VP8X = "VP8X";
  private const string _CHUNK_ALPH = "ALPH";
  private const string _CHUNK_ICCP = "ICCP";
  private const string _CHUNK_EXIF = "EXIF";
  private const string _CHUNK_XMP = "XMP ";

  private static readonly HashSet<string> _MetadataChunkIds = [_CHUNK_ICCP, _CHUNK_EXIF, _CHUNK_XMP];

  public static WebPFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WebP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WebPFile FromStream(Stream stream) {
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

  public static WebPFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < 12)
      throw new InvalidDataException("Data is too small to be a valid WebP file.");

    var riff = RiffReader.FromBytes(data.ToArray());
    if (riff.FormType.ToString() != _FORM_TYPE)
      throw new InvalidDataException($"Invalid WebP form type: expected '{_FORM_TYPE}', got '{riff.FormType}'.");

    var chunks = _BuildChunkLookup(riff.Chunks);

    var hasVp8X = chunks.ContainsKey(_CHUNK_VP8X);
    var hasVp8L = chunks.ContainsKey(_CHUNK_VP8L);
    var hasVp8 = chunks.ContainsKey(_CHUNK_VP8);

    if (!hasVp8L && !hasVp8)
      throw new InvalidDataException("WebP file contains neither VP8 nor VP8L image data.");

    var isLossless = hasVp8L;
    byte[] imageData;
    WebPFeatures features;
    var metadataChunks = new List<(string ChunkId, byte[] Data)>();

    if (hasVp8X) {
      features = _ParseVp8X(chunks[_CHUNK_VP8X], isLossless);
    } else if (isLossless) {
      imageData = chunks[_CHUNK_VP8L];
      features = _ParseVp8L(imageData);
    } else {
      imageData = chunks[_CHUNK_VP8];
      features = _ParseVp8(imageData);
    }

    imageData = isLossless
      ? (chunks.TryGetValue(_CHUNK_VP8L, out var vp8lData) ? vp8lData : [])
      : (chunks.TryGetValue(_CHUNK_VP8, out var vp8Data) ? vp8Data : []);

    // Collect metadata chunks
    foreach (var chunk in riff.Chunks) {
      var id = chunk.Id.ToString();
      if (_MetadataChunkIds.Contains(id))
        metadataChunks.Add((id, chunk.Data));
    }

    return new WebPFile {
      Features = features,
      ImageData = imageData,
      IsLossless = isLossless,
      MetadataChunks = metadataChunks
    };
  }

  public static WebPFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static Dictionary<string, byte[]> _BuildChunkLookup(List<RiffChunk> chunks) {
    var lookup = new Dictionary<string, byte[]>();
    foreach (var chunk in chunks) {
      var id = chunk.Id.ToString();
      lookup.TryAdd(id, chunk.Data);
    }

    return lookup;
  }

  private static WebPFeatures _ParseVp8X(ReadOnlySpan<byte> data, bool isLossless) {
    if (data.Length < Vp8XHeader.StructSize)
      throw new InvalidDataException("VP8X chunk is too small.");

    var header = Vp8XHeader.ReadFrom(data);
    return new WebPFeatures(header.CanvasWidth, header.CanvasHeight, header.HasAlpha, isLossless, header.IsAnimated);
  }

  internal static WebPFeatures _ParseVp8L(ReadOnlySpan<byte> data) {
    if (data.Length < Vp8LHeader.StructSize)
      throw new InvalidDataException("VP8L chunk is too small.");

    var header = Vp8LHeader.ReadFrom(data);
    if (header.Signature != 0x2F)
      throw new InvalidDataException($"Invalid VP8L signature byte: 0x{header.Signature:X2}, expected 0x2F.");

    return new WebPFeatures(header.Width, header.Height, header.HasAlpha, IsLossless: true, IsAnimated: false);
  }

  internal static WebPFeatures _ParseVp8(ReadOnlySpan<byte> data) {
    if (data.Length < Vp8FrameHeader.StructSize)
      throw new InvalidDataException("VP8 chunk is too small.");

    var header = Vp8FrameHeader.ReadFrom(data);
    if (!header.IsKeyframe)
      throw new InvalidDataException("VP8 chunk does not start with a keyframe.");
    if (!header.HasValidSignature)
      throw new InvalidDataException("Invalid VP8 keyframe signature.");

    return new WebPFeatures(header.Width, header.Height, HasAlpha: false, IsLossless: false, IsAnimated: false);
  }
}
