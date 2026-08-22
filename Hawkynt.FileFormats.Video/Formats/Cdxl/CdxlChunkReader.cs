using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Cdxl;

/// <summary>
/// Splits a Commodore CDXL file (the Amiga CDTV's own motion-video format) into the flat run of chunks
/// it holds, each a thirty-two byte header immediately followed by that frame's own palette, pixels and
/// sound — without decoding a single pixel.
/// </summary>
/// <remarks>
/// The header layout, the twelve-bit palette encoding and the four documented video encodings are read
/// from MultimediaWiki's CDXL page, which names its own sources rather than paraphrasing anybody's
/// decoder: US patent 5,293,606, an Amiga-native decoder with source in Pascal distributed on Aminet as
/// part of AnimFX, and a German technical article. Nothing on the page reads as an account of somebody
/// else's implementation, and two of its own field meanings — what "current chunk size" actually
/// measures, and what the twenty-four documented bytes of frame header leave unstated between one
/// chunk's pixel data and the next chunk's header — are settled below by measurement rather than by the
/// page, which does not state them.
/// <para/>
/// <b>The file has no signature and no field naming a version.</b> It opens straight into the first
/// chunk's own header, so what stands in for one here is the same kind of plausibility check id
/// Cinematic's reader uses: a file type value the page enumerates, an encoding and a plane arrangement
/// the page enumerates, and a picture size within bounds no real CDXL file was ever going to exceed.
/// <para/>
/// <b>What "current chunk size" measures, and what it does not.</b> Four real files —
/// <c>samples.ffmpeg.org/cdxl/cat.cdxl</c>, <c>fruit.cdxl</c>, <c>maku.cdxl</c> and <c>mirage.cdxl</c> —
/// were measured against <c>ffprobe -show_packets</c> on the same files. For two of the four the chunk
/// size stated in the header equals exactly the header, palette, pixel and sound bytes added together;
/// for the other two — both eight-bitplane frames — it is larger by a fixed amount per file (twenty-four
/// bytes with no sound at all in one, and by exactly the sound size again in the other, as though the
/// sound were accounted for twice). ffmpeg's own video and audio packet sizes agree with the sum of the
/// documented fields in all four files regardless, which is what this reader also computes; the
/// difference between that sum and the header's own "current chunk size" is slack this reader steps over
/// to reach the next chunk rather than data belonging to either packet — the header's own size field is
/// authoritative for where the next chunk begins, and the documented fields are authoritative for how
/// long the two packets inside this one are.
/// <para/>
/// <b>The pixel byte count is <c>ceil(width / 8) * height * planes</c>, the plane-major "bit planar"
/// layout the page's plane-arrangement table names as value zero</b> — all of bitplane zero, top to
/// bottom, then all of bitplane one, and so on, each row packed eight pixels to a byte with the
/// leftmost pixel in the most significant bit — confirmed exactly against ffmpeg's own video packet
/// sizes on all four files, at four, six and eight bitplanes and at two different widths. The four other
/// plane arrangements the page names — byte planar, chunky, and the two "line" variants, which most
/// plausibly interleave the planes within a row rather than laying each one out in full before the
/// next — have no example among the files measured, so a chunk naming one of them is refused rather
/// than sized by a formula nothing here has checked.
/// </remarks>
internal static class CdxlChunkReader {

  internal const int HeaderLength = 32;
  private const int _MAX_DIMENSION = 4096;
  private const int _MAX_PLANES = 32;

  internal readonly record struct FrameHeader(
    byte FileType,
    int VideoEncoding,
    bool Stereo,
    int PlaneArrangement,
    uint ChunkSize,
    int Width,
    int Height,
    int Planes,
    int PaletteSize,
    int SoundSize) {

    /// <summary>Bytes of packed pixel data one bit-planar frame of this size and depth occupies.</summary>
    public int VideoPixelBytes => (this.Width + 7) / 8 * this.Height * this.Planes;

    /// <summary>The header, palette and pixel data together — what this reader hands out as the video packet.</summary>
    public int VideoPacketLength => HeaderLength + this.PaletteSize + this.VideoPixelBytes;
  }

  internal static FrameHeader ReadHeader(ReadOnlySpan<byte> header) {
    var info = header[1];
    return new(
      FileType: header[0],
      VideoEncoding: info & 0x07,
      Stereo: (info & 0x08) != 0,
      PlaneArrangement: info >> 5 & 0x07,
      ChunkSize: BinaryPrimitives.ReadUInt32BigEndian(header[2..]),
      Width: BinaryPrimitives.ReadUInt16BigEndian(header[14..]),
      Height: BinaryPrimitives.ReadUInt16BigEndian(header[16..]),
      Planes: BinaryPrimitives.ReadUInt16BigEndian(header[18..]),
      PaletteSize: BinaryPrimitives.ReadUInt16BigEndian(header[20..]),
      SoundSize: BinaryPrimitives.ReadUInt16BigEndian(header[22..]));
  }

  /// <summary>
  /// Whether a header looks like a CDXL chunk rather than the start of some other, signature-less
  /// format: a documented file type, a documented encoding, the one plane arrangement this reader can
  /// size a packet for, and a picture no real file was ever going to exceed.
  /// </summary>
  internal static bool LooksPlausible(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderLength)
      return false;

    var h = ReadHeader(header);
    if (h.FileType > 2)
      return false;

    if (h.VideoEncoding > 3)
      return false;

    if (h.PlaneArrangement != 0)
      return false;

    if (h.Width is <= 0 or > _MAX_DIMENSION || h.Height is <= 0 or > _MAX_DIMENSION)
      return false;

    if (h.Planes is <= 0 or > _MAX_PLANES)
      return false;

    var needed = (long)HeaderLength + h.PaletteSize + h.VideoPixelBytes + h.SoundSize;
    return h.ChunkSize >= needed;
  }

  internal static CdxlContainer Open(ReadOnlyMemory<byte> data) {
    if (data.Length < HeaderLength)
      throw new NotSupportedException(
        $"The file is {data.Length} bytes, short of the thirty-two byte header every CDXL chunk opens "
        + "with. This is not a CDXL file.");

    var first = data.Span[..HeaderLength];
    if (!LooksPlausible(first))
      throw new NotSupportedException(
        "This file's first chunk does not state a documented file type or video encoding, states a "
        + "plane arrangement other than bit planar, states an implausible picture size, or states a "
        + "chunk size too small to hold its own header, palette, pixel and sound bytes. CDXL carries no "
        + "signature of its own, so this is the only check a container can make.");

    var header = ReadHeader(first);

    var frameCount = 0;
    foreach (var (_, _) in _WalkChunks(data))
      ++frameCount;

    return new() {
      Data = data,
      Width = header.Width,
      Height = header.Height,
      HasAudio = header.SoundSize > 0,
      Stereo = header.Stereo,
      FrameCount = frameCount,
    };
  }

  /// <summary>Walks every chunk once, in one pass, stopping cleanly at the first one that does not fully
  /// fit in what remains of the file, and refusing outright the first one that states a plane
  /// arrangement this reader was not measured against — a genuinely different, unverified shape rather
  /// than a truncated file, so it is not simply stepped over.</summary>
  private static IEnumerable<(int Position, FrameHeader Header)> _WalkChunks(ReadOnlyMemory<byte> data) {
    var length = data.Length;
    var pos = 0;
    var index = 0;

    while (pos + HeaderLength <= length) {
      var header = ReadHeader(data.Span.Slice(pos, HeaderLength));

      if (header.PlaneArrangement != 0)
        throw new NotSupportedException(
          $"Chunk {index} states plane arrangement {header.PlaneArrangement}, not the bit planar layout "
          + "(zero) this reader was measured against. Reading it would mean guessing at a byte layout "
          + "nothing here has checked.");

      if (header.VideoEncoding > 3)
        throw new NotSupportedException(
          $"Chunk {index} states video encoding {header.VideoEncoding}, which CDXL's own documentation "
          + "does not name.");

      var packetLength = header.VideoPacketLength + header.SoundSize;
      if (pos + packetLength > length)
        yield break;

      yield return (pos, header);

      // The header's own chunk size is authoritative for where the next one begins — see the type's
      // remarks for the measured slack between it and the sum of the documented fields.
      var stride = header.ChunkSize > 0 ? (long)header.ChunkSize : packetLength;
      if (pos + stride > length)
        yield break;

      pos += (int)stride;
      ++index;
    }
  }

  internal static IEnumerable<CodedPacket> ReadPackets(CdxlContainer container) {
    var data = container.Data;
    var audioStreamIndex = container.HasAudio ? 1 : -1;

    long frame = 0;
    foreach (var (pos, header) in _WalkChunks(data)) {
      var videoLength = header.VideoPacketLength;

      yield return new(
        StreamIndex: 0,
        Data: data.Slice(pos, videoLength),
        PresentationTimestamp: frame,
        DecodeTimestamp: frame,
        Duration: 1,
        IsKeyFrame: true);

      if (audioStreamIndex >= 0 && header.SoundSize > 0)
        yield return new(
          StreamIndex: audioStreamIndex,
          Data: data.Slice(pos + videoLength, header.SoundSize),
          PresentationTimestamp: frame,
          IsKeyFrame: true);

      ++frame;
    }
  }
}
