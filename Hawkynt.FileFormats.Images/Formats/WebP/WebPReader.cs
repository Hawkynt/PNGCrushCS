using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

  /// <summary>The chunk one frame of an animation sits in.</summary>
  private const string _CHUNK_ANMF = "ANMF";

  /// <summary>
  /// Bytes of frame description before an animation frame's own chunks: where it sits, how big it
  /// is, how long it lasts, and how it is blended.
  /// </summary>
  private const int _ANMF_HEADER_SIZE = 16;

  /// <summary>Lifts a frame's own chunks out of its ANMF wrapper into the file's own lookup.</summary>
  private static void _AddFrameChunks(byte[] frame, Dictionary<string, byte[]> chunks) {
    var at = _ANMF_HEADER_SIZE;
    while (at + 8 <= frame.Length) {
      var id = Encoding.ASCII.GetString(frame, at, 4);
      var length = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(at + 4));
      if (length < 0 || at + 8 + length > frame.Length)
        break;

      // The frame's own size wins over the canvas, which is why these are set rather than added to.
      chunks[id] = frame[(at + 8)..(at + 8 + length)];
      at += 8 + length + (length & 1);
    }
  }

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

    // An animation keeps its pictures inside ANMF chunks rather than at the top level, so a file
    // holding seventeen frames looked to this like a file holding none. The first frame is the one
    // shown before anything moves, and is what every still viewer draws.
    if (!chunks.ContainsKey(_CHUNK_VP8L) && !chunks.ContainsKey(_CHUNK_VP8)
        && chunks.TryGetValue(_CHUNK_ANMF, out var firstFrame))
      _AddFrameChunks(firstFrame, chunks);

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

    // ALPH chunk (only present for VP8 lossy + alpha — lossless format carries alpha inline).
    byte[]? alphaData = null;
    if (!isLossless && chunks.TryGetValue(_CHUNK_ALPH, out var alph))
      alphaData = _DecodeAlphChunk(alph, features.Width, features.Height);

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
      MetadataChunks = metadataChunks,
      AlphaData = alphaData,
    };
  }

  /// <summary>Decode an ALPH chunk into a flat alpha-plane byte buffer (one byte per pixel).
  /// Currently supports compression method 0 (uncompressed). Method 1 (VP8L-encoded alpha)
  /// returns null — caller treats absence of alphaData as opaque.</summary>
  private static byte[]? _DecodeAlphChunk(byte[] data, int width, int height) {
    if (data.Length < 1) return null;
    var flagByte = data[0];
    var compression = flagByte & 0x03;
    if (compression != 0) return null; // method 1 (VP8L) decoding not implemented yet
    var expectedLength = width * height;
    if (data.Length < 1 + expectedLength) return null;
    var alpha = new byte[expectedLength];
    System.Buffer.BlockCopy(data, 1, alpha, 0, expectedLength);
    return alpha;
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
