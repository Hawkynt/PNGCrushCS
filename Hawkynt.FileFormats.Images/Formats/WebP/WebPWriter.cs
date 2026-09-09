using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Riff;

namespace FileFormat.WebP;

/// <summary>Assembles still and animated WebP files into the RIFF container format.</summary>
public static class WebPWriter {

  private const string _FORM_TYPE = "WEBP";
  private const string _CHUNK_VP8 = "VP8 ";
  private const string _CHUNK_VP8L = "VP8L";
  private const string _CHUNK_VP8X = "VP8X";
  private const string _CHUNK_ALPH = "ALPH";
  private const string _CHUNK_ANIM = "ANIM";
  private const string _CHUNK_ANMF = "ANMF";
  private const string _CHUNK_ICCP = "ICCP";
  private const string _CHUNK_EXIF = "EXIF";
  private const string _CHUNK_XMP = "XMP ";

  public static byte[] ToBytes(WebPFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var isAnimated = file.Frames.Count > 0;
    if (file.Features.IsAnimated != isAnimated)
      throw new InvalidDataException("WebP animation flag and frame list disagree.");

    var needsExtended = file.Features.HasAlpha || isAnimated || file.MetadataChunks.Count > 0;
    var chunks = new List<RiffChunk>();

    if (!needsExtended) {
      chunks.Add(new RiffChunk { Id = file.IsLossless ? _CHUNK_VP8L : _CHUNK_VP8, Data = file.ImageData });
      return RiffWriter.ToBytes(new RiffFile { FormType = _FORM_TYPE, Chunks = chunks });
    }

    chunks.Add(new RiffChunk { Id = _CHUNK_VP8X, Data = _BuildVp8XData(file) });

    // ICCP belongs immediately after VP8X when present.
    _AddMetadata(chunks, file.MetadataChunks, _CHUNK_ICCP);

    if (isAnimated) {
      chunks.Add(new RiffChunk { Id = _CHUNK_ANIM, Data = _BuildAnimData(file.Animation) });
      foreach (var frame in file.Frames)
        chunks.Add(new RiffChunk { Id = _CHUNK_ANMF, Data = _BuildAnmfData(file, frame) });
    } else {
      // ALPH must directly precede its lossy VP8 picture.
      if (file.Features.HasAlpha && !file.IsLossless && file.AlphaData != null)
        chunks.Add(new RiffChunk { Id = _CHUNK_ALPH, Data = _BuildAlphData(file.AlphaData) });

      chunks.Add(new RiffChunk {
        Id = file.IsLossless ? _CHUNK_VP8L : _CHUNK_VP8,
        Data = file.ImageData
      });
    }

    // EXIF and XMP follow the image/animation payload in the extended WebP ordering.
    _AddMetadata(chunks, file.MetadataChunks, _CHUNK_EXIF);
    _AddMetadata(chunks, file.MetadataChunks, _CHUNK_XMP);

    return RiffWriter.ToBytes(new RiffFile { FormType = _FORM_TYPE, Chunks = chunks });
  }

  private static void _AddMetadata(List<RiffChunk> chunks, IReadOnlyList<(string ChunkId, byte[] Data)> metadata, string id) {
    foreach (var (chunkId, data) in metadata)
      if (chunkId == id)
        chunks.Add(new RiffChunk { Id = chunkId, Data = data });
  }

  private static byte[] _BuildAnimData(WebPAnimationInfo? animation) {
    var data = new byte[6];
    BinaryPrimitives.WriteUInt32LittleEndian(data, animation?.BackgroundColorBgra ?? 0u);
    var loops = animation?.LoopCount ?? 0;
    if ((uint)loops > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(animation), "WebP loop count must fit in 16 bits.");
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)loops);
    return data;
  }

  private static byte[] _BuildAnmfData(WebPFile file, WebPFrame frame) {
    if (frame.X < 0 || frame.Y < 0 || (frame.X & 1) != 0 || (frame.Y & 1) != 0)
      throw new InvalidDataException("WebP ANMF offsets must be non-negative even pixel coordinates.");
    if (frame.Width <= 0 || frame.Height <= 0)
      throw new InvalidDataException("WebP ANMF dimensions must be positive.");
    if (frame.X + frame.Width > file.Features.Width || frame.Y + frame.Height > file.Features.Height)
      throw new InvalidDataException("WebP ANMF rectangle exceeds the VP8X canvas.");
    if ((uint)frame.DurationMilliseconds > 0xFFFFFFu)
      throw new InvalidDataException("WebP ANMF duration must fit in 24 bits.");
    if (frame.ImageData.Length == 0)
      throw new InvalidDataException("WebP animation frame has no VP8/VP8L payload.");

    using var stream = new MemoryStream();
    Span<byte> header = stackalloc byte[16];
    _Write24(header, 0, frame.X / 2);
    _Write24(header, 3, frame.Y / 2);
    _Write24(header, 6, frame.Width - 1);
    _Write24(header, 9, frame.Height - 1);
    _Write24(header, 12, frame.DurationMilliseconds);
    header[15] = (byte)((frame.DisposalMethod == WebPFrameDisposalMethod.Background ? 0x01 : 0)
                        | (frame.BlendMethod == WebPFrameBlendMethod.None ? 0x02 : 0));
    stream.Write(header);

    if (!frame.IsLossless && frame.HasAlpha) {
      if (frame.AlphaChunk == null)
        throw new InvalidDataException("Lossy animated WebP frame declares alpha but has no ALPH payload.");
      _WriteNestedChunk(stream, _CHUNK_ALPH, frame.AlphaChunk);
    }

    _WriteNestedChunk(stream, frame.IsLossless ? _CHUNK_VP8L : _CHUNK_VP8, frame.ImageData);
    return stream.ToArray();
  }

  private static void _WriteNestedChunk(Stream stream, string id, byte[] payload) {
    Span<byte> header = stackalloc byte[8];
    Encoding.ASCII.GetBytes(id, header[..4]);
    BinaryPrimitives.WriteUInt32LittleEndian(header[4..], checked((uint)payload.Length));
    stream.Write(header);
    stream.Write(payload);
    if ((payload.Length & 1) != 0)
      stream.WriteByte(0);
  }

  private static byte[] _BuildAlphData(byte[] alphaPlane) {
    var data = new byte[1 + alphaPlane.Length];
    data[0] = 0;
    Buffer.BlockCopy(alphaPlane, 0, data, 1, alphaPlane.Length);
    return data;
  }

  private static byte[] _BuildVp8XData(WebPFile file) {
    var data = new byte[10];

    byte flags = 0;
    if (file.Features.HasAlpha || _FramesHaveAlpha(file.Frames))
      flags |= 0x10;
    if (file.Frames.Count > 0)
      flags |= 0x02;

    foreach (var (chunkId, _) in file.MetadataChunks)
      flags |= chunkId switch {
        _CHUNK_ICCP => (byte)0x20,
        _CHUNK_EXIF => (byte)0x08,
        _CHUNK_XMP => (byte)0x04,
        _ => (byte)0
      };

    data[0] = flags;
    _Write24(data, 4, file.Features.Width - 1);
    _Write24(data, 7, file.Features.Height - 1);
    return data;
  }

  private static bool _FramesHaveAlpha(IReadOnlyList<WebPFrame> frames) {
    foreach (var frame in frames)
      if (frame.HasAlpha)
        return true;
    return false;
  }

  private static void _Write24(Span<byte> target, int offset, int value) {
    if ((uint)value > 0xFFFFFFu)
      throw new ArgumentOutOfRangeException(nameof(value), "WebP 24-bit field is out of range.");
    target[offset] = (byte)value;
    target[offset + 1] = (byte)(value >> 8);
    target[offset + 2] = (byte)(value >> 16);
  }
}
