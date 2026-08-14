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

  /// <summary>The chunk stating what holds for the animation as a whole.</summary>
  private const string _CHUNK_ANIM = "ANIM";

  /// <summary>
  /// Bytes of frame description before an animation frame's own chunks: where it sits, how big it
  /// is, how long it lasts, and how it is blended.
  /// </summary>
  private const int _ANMF_HEADER_SIZE = 16;

  /// <summary>Lifts a frame's own chunks out of its ANMF wrapper into the file's own lookup.</summary>
  private static void _AddFrameChunks(byte[] frame, Dictionary<string, byte[]> chunks) {
    foreach (var (id, payload) in _EnumerateFrameChunks(frame))
      // The frame's own size wins over the canvas, which is why these are set rather than added to.
      chunks[id] = payload;
  }

  /// <summary>Walks the chunks an ANMF wrapper holds after its 16-byte frame description.</summary>
  private static IEnumerable<(string Id, byte[] Data)> _EnumerateFrameChunks(byte[] frame) {
    var at = _ANMF_HEADER_SIZE;
    while (at + 8 <= frame.Length) {
      var id = Encoding.ASCII.GetString(frame, at, 4);
      // Widened before the bounds check: a chunk claiming a length near int.MaxValue makes the sum
      // wrap negative, and a negative sum passes a check written in int arithmetic.
      var length = (long)BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(at + 4));
      if (at + 8 + length > frame.Length)
        break;

      yield return (id, frame[(at + 8)..(int)(at + 8 + length)]);
      at += (int)(8 + length + (length & 1));
    }
  }

  /// <summary>Reads a 24-bit little-endian field of the kind ANMF headers are made of.</summary>
  private static int _Read24(ReadOnlySpan<byte> data, int at)
    => data[at] | (data[at + 1] << 8) | (data[at + 2] << 16);

  /// <summary>Turns one ANMF chunk into a frame.</summary>
  private static WebPFrame _ParseFrame(byte[] chunk) {
    if (chunk.Length < _ANMF_HEADER_SIZE)
      throw new InvalidDataException($"ANMF chunk is {chunk.Length} bytes, too small for its {_ANMF_HEADER_SIZE}-byte frame description.");

    byte[]? imageData = null;
    byte[]? alphaChunk = null;
    var isLossless = false;
    foreach (var (id, payload) in _EnumerateFrameChunks(chunk))
      switch (id) {
        case _CHUNK_VP8L:
          imageData = payload;
          isLossless = true;
          break;
        case _CHUNK_VP8:
          imageData = payload;
          isLossless = false;
          break;
        case _CHUNK_ALPH:
          alphaChunk = payload;
          break;
      }

    // A frame with no picture in it is a file libwebp's demuxer refuses outright, and quietly
    // dropping it here would make the frame count disagree with the file for no stated reason —
    // every frame after it would answer to the wrong index.
    if (imageData == null)
      throw new InvalidDataException("ANMF chunk holds neither VP8 nor VP8L picture data.");

    // Offsets are stored halved, which is why a frame can only ever start on an even pixel; sizes
    // are stored one short, which is why a frame can never be empty.
    var flags = chunk[15];
    var frameWidth = _Read24(chunk, 6) + 1;
    var frameHeight = _Read24(chunk, 9) + 1;

    // A frame has alpha when an ALPH chunk sits beside its lossy picture or when its lossless
    // header says so. This decides whether the frame can be treated as owing nothing to its
    // predecessors, so guessing it wrong changes the picture and not only the speed.
    var hasAlpha = alphaChunk is { Length: > 0 };
    if (isLossless && imageData.Length >= Vp8LHeader.StructSize)
      hasAlpha |= Vp8LHeader.ReadFrom(imageData).HasAlpha;

    return new WebPFrame {
      X = _Read24(chunk, 0) * 2,
      Y = _Read24(chunk, 3) * 2,
      Width = frameWidth,
      Height = frameHeight,
      DurationMilliseconds = _Read24(chunk, 12),
      BlendMethod = (flags & 0x02) != 0 ? WebPFrameBlendMethod.None : WebPFrameBlendMethod.AlphaBlend,
      DisposalMethod = (flags & 0x01) != 0 ? WebPFrameDisposalMethod.Background : WebPFrameDisposalMethod.None,
      ImageData = imageData,
      IsLossless = isLossless,
      AlphaChunk = alphaChunk,
      HasAlpha = hasAlpha,
    };
  }

  /// <summary>Reads the ANIM chunk: what the animation is shown against, and how often it plays.</summary>
  private static WebPAnimationInfo? _ParseAnimationInfo(byte[] chunk) {
    if (chunk.Length < 6)
      return null;

    return new WebPAnimationInfo {
      BackgroundColorBgra = BinaryPrimitives.ReadUInt32LittleEndian(chunk),
      LoopCount = BinaryPrimitives.ReadUInt16LittleEndian(chunk.AsSpan(4)),
    };
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
    // holding seventeen frames looked to this like a file holding none — and then, once the first
    // one was found, like a file holding exactly one. Every ANMF is a frame; they are kept in file
    // order, which is the order they are shown in.
    var frames = new List<WebPFrame>();
    foreach (var chunk in riff.Chunks) {
      if (chunk.Id.ToString() != _CHUNK_ANMF)
        continue;

      frames.Add(_ParseFrame(chunk.Data));
    }

    // The first frame is the one shown before anything moves, and is what every still viewer draws.
    if (!chunks.ContainsKey(_CHUNK_VP8L) && !chunks.ContainsKey(_CHUNK_VP8)
        && chunks.TryGetValue(_CHUNK_ANMF, out var firstFrame))
      _AddFrameChunks(firstFrame, chunks);

    var animation = chunks.TryGetValue(_CHUNK_ANIM, out var anim) ? _ParseAnimationInfo(anim) : null;

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
    // An animation's frames each carry their own, at their own size, and are decoded when the frame
    // is composited; the canvas-sized decode here would be asking the wrong picture's question.
    byte[]? alphaData = null;
    if (!isLossless && frames.Count == 0 && chunks.TryGetValue(_CHUNK_ALPH, out var alph))
      alphaData = WebPAlphaDecoder.Decode(alph, features.Width, features.Height);

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
      Frames = frames,
      Animation = animation,
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
