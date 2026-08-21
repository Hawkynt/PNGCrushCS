using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SmackerVideo;

/// <summary>
/// Takes a Smacker file apart into its header, its per-frame tables and the frames themselves, without
/// unpacking a single Huffman tree or reading a single block type.
/// </summary>
/// <remarks>
/// Smacker states everything a demuxer needs up front rather than self-delimiting frame by frame the
/// way a chunk-based container does: a fixed 104-byte header, then two parallel arrays — one length a
/// frame, one flag byte a frame — for every frame the file holds, then one packed section of Huffman
/// trees shared by the whole file, then the frames themselves back to back with no header of their own
/// beyond what those two arrays already said about them.
/// <para/>
/// <b>A frame's stated length is used exactly as stored, bit zero included.</b> RAD's own description
/// calls that bit a keyframe flag and says "you don't need to shift this bit out to get the length" —
/// which reads two ways until it is checked. Summing every frame's stated length, plus the header, the
/// two arrays and the tree section, against the real byte count of five files (3 KB to 325 KB, none of
/// them agreeing with any other on tree size or frame count) lands on the file's exact size every time
/// with nothing left over and nothing short — so the value is the byte count outright, not a byte count
/// with a flag folded into a low bit that then has to be masked off, and reading it any other way loses
/// the file part way through the last frame.
/// <para/>
/// <b>What a "keyframe" bit turns out not to mean.</b> Across two of those files — 100 and 270 frames,
/// between them not one frame with that bit set anywhere — ffmpeg's own demuxer still reports exactly
/// one keyframe each, the first frame. So this reader does the same thing every other self-contained
/// FMV container here does: the first picture is the keyframe because nothing else can be, and RAD's
/// bit is read as part of a frame's stated length and not interpreted as anything else, because nothing
/// measured against this ties it to any observable behaviour beyond that.
/// <para/>
/// <b>A frame's own bytes are not self-describing on their own.</b> Whether a frame carries a palette
/// update and which of up to seven audio tracks contribute a chunk to it is stated once, per frame, in
/// the <c>FrameTypes</c> array outside the frame — nothing inside a frame's own bytes says so. A palette
/// chunk and each audio track's chunk do at least state their own length once a reader knows to look
/// for them, which is what lets this reader split a frame into its sound, its optional palette, and
/// what is left for the picture — the same shape RealMedia's per-piece header takes for a different
/// reason. Audio's own length count was checked the more useful way, against what it is not: it counts
/// itself, so the sound bytes after it are four less than the count states, and ffmpeg's own reported
/// packet size for every one of 741 audio chunks across two files — sizes from 8 to 11 244 bytes — is
/// exactly that count minus four, with no exception anywhere in either file.
/// </remarks>
internal static class SmackerReader {

  private const int _HEADER_LENGTH = 104;
  private const int _AUDIO_TRACK_COUNT = 7;
  private const uint _FLAG_HAS_RING_FRAME = 1;
  private const uint _FRAME_TYPE_HAS_PALETTE = 1;
  // RAD's own description numbers these bits within the dword's upper byte alone; a real file's
  // upper byte of 0xC0 (bits 7 and 6 of that byte, "compressed" and "data presence") checked out
  // against ffprobe's report of that track as compressed mono 8-bit at the frequency the lower three
  // bytes state, which is what fixes all four flags at bit 24 and up rather than bits 0-7.
  private const uint _AUDIO_RATE_COMPRESSED = 1u << 31;
  private const uint _AUDIO_RATE_16_BIT = 1u << 29;
  private const uint _AUDIO_RATE_STEREO = 1u << 28;
  private const uint _AUDIO_RATE_DATA_PRESENT = 1u << 30;
  private const uint _AUDIO_RATE_FREQUENCY_MASK = 0x00FFFFFF;

  internal readonly record struct Summary(
    uint Signature,
    int Width,
    int Height,
    int VideoFrameCount,
    Rational VideoTimeBase,
    int[] FrameSizes,
    byte[] FrameTypes,
    ReadOnlyMemory<byte> CodecPrivateData,
    int FramesDataOffset,
    uint[] AudioTrackRates);

  internal static SmackerContainer Open(ReadOnlyMemory<byte> data) {
    if (data.Length < _HEADER_LENGTH || !_HasSignature(data.Span))
      throw new NotSupportedException(
        "The file does not open with \"SMK2\" or \"SMK4\". This is not a Smacker file.");

    var summary = _Summarise(data);
    return new() {
      Data = data,
      Signature = summary.Signature,
      Width = summary.Width,
      Height = summary.Height,
      VideoFrameCount = summary.VideoFrameCount,
      VideoTimeBase = summary.VideoTimeBase,
      FrameSizes = summary.FrameSizes,
      FrameTypes = summary.FrameTypes,
      CodecPrivateData = summary.CodecPrivateData,
      FramesDataOffset = summary.FramesDataOffset,
      AudioTrackRates = summary.AudioTrackRates,
    };
  }

  private static bool _HasSignature(ReadOnlySpan<byte> header)
    => header[..4].SequenceEqual("SMK2"u8) || header[..4].SequenceEqual("SMK4"u8);

  private static Summary _Summarise(ReadOnlyMemory<byte> data) {
    var header = data.Span[.._HEADER_LENGTH];

    var signature = BinaryPrimitives.ReadUInt32LittleEndian(header);
    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
    // Kept unsigned: a bogus huge value must fail the bounds check below rather than wrap negative
    // and slip past it.
    var frames = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
    var frameRate = BinaryPrimitives.ReadInt32LittleEndian(header[16..]);
    var flags = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
    var treesSize = BinaryPrimitives.ReadUInt32LittleEndian(header[52..]);
    var mMapSize = header[56..60];
    var mClrSize = header[60..64];
    var fullSize = header[64..68];
    var typeSize = header[68..72];

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A Smacker header states a picture of {width}x{height}, which has no pixels.");

    if (frames == 0)
      throw new InvalidDataException("A Smacker header states zero frames.");

    var videoTimeBase = _ReadTimeBase(frameRate);

    var audioTrackRates = new uint[_AUDIO_TRACK_COUNT];
    for (var t = 0; t < _AUDIO_TRACK_COUNT; ++t)
      audioTrackRates[t] = BinaryPrimitives.ReadUInt32LittleEndian(header[(72 + t * 4)..]);

    // Every offset from here on is worked out in a width the header's own 32-bit fields cannot
    // overflow, so a file naming an absurd frame count is caught by the bounds check below rather
    // than wrapping into a small number that would then be read as though it were trustworthy.
    long videoFrameCountLong = frames + ((flags & _FLAG_HAS_RING_FRAME) != 0 ? 1L : 0);
    var frameSizesOffset = (long)_HEADER_LENGTH;
    var frameSizesLength = videoFrameCountLong * 4;
    var frameTypesOffset = frameSizesOffset + frameSizesLength;
    var treesOffset = frameTypesOffset + videoFrameCountLong;
    var framesDataOffset = treesOffset + treesSize;

    if (framesDataOffset > data.Length)
      throw new InvalidDataException(
        $"A Smacker file's own header states {videoFrameCountLong} frames and a {treesSize}-byte tree "
        + $"section, which together run past the file's {data.Length} bytes before a single frame of "
        + "picture data has even been reached.");

    // Proven no larger than the file itself by the check just above, so narrowing back to int from
    // here on is safe.
    var videoFrameCount = (int)videoFrameCountLong;

    var frameSizes = new int[videoFrameCount];
    for (var i = 0; i < videoFrameCount; ++i)
      frameSizes[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Span[((int)frameSizesOffset + i * 4)..]);

    var frameTypes = data.Span.Slice((int)frameTypesOffset, videoFrameCount).ToArray();

    // Matches what ffmpeg's own demuxer packs into its stream's codec-private data, byte for byte: the
    // four in-memory allocation sizes RAD's header states for the MMap, MClr, Full and Type tables,
    // followed by the packed tree bytes themselves — verified against a real file by the arithmetic
    // rather than assumed, since ffmpeg reports that stream's extradata as exactly sixteen bytes more
    // than the header's own TreesSize field, on every file this was checked against.
    var codecPrivateData = new byte[16 + treesSize];
    mMapSize.CopyTo(codecPrivateData);
    mClrSize.CopyTo(codecPrivateData.AsSpan(4));
    fullSize.CopyTo(codecPrivateData.AsSpan(8));
    typeSize.CopyTo(codecPrivateData.AsSpan(12));
    data.Span.Slice((int)treesOffset, (int)treesSize).CopyTo(codecPrivateData.AsSpan(16));

    return new(signature, width, height, videoFrameCount, videoTimeBase, frameSizes, frameTypes, codecPrivateData, (int)framesDataOffset, audioTrackRates);
  }

  /// <summary>
  /// Turns the header's signed <c>FrameRate</c> field into a time base with exactly one tick a frame.
  /// </summary>
  /// <remarks>
  /// RAD's own description calls the field "playback speed in milliseconds per frame" and says nothing
  /// about it ever being negative. Real files disagree: two of five measured state a negative value,
  /// and reading a positive one as whole milliseconds and a negative one as its magnitude in hundredths
  /// of a millisecond reproduces ffmpeg's own computed time base exactly on all four files that open at
  /// all — 7/250 and 71/1000 from two positive fields, 67/1000 and 71/500 from two negative ones, the
  /// last two only reachable at all through the hundredths-of-a-millisecond reading because 8714 and
  /// 14200 do not reduce against 1000 the way a whole-millisecond value would need to. A zero field is
  /// refused rather than guessed at: nothing measured against this states one.
  /// </remarks>
  private static Rational _ReadTimeBase(int frameRate) {
    if (frameRate > 0)
      return _Reduced(frameRate, 1000);

    if (frameRate < 0)
      return _Reduced(-(long)frameRate, 100_000);

    throw new NotSupportedException(
      "This Smacker file's FrameRate field is zero. Nothing measured against this reader states what a "
      + "zero frame rate means, so it is refused rather than defaulted to a guessed value.");
  }

  private static Rational _Reduced(long numerator, long denominator) {
    var a = numerator;
    var b = denominator;
    while (b != 0)
      (a, b) = (b, a % b);

    var gcd = a == 0 ? 1 : a;
    return new(numerator / gcd, denominator / gcd);
  }

  /// <summary>Walks every frame once, handing out a picture packet on stream 0 and, for each audio
  /// track the file declares, a sound packet on that track's own stream.</summary>
  internal static IEnumerable<CodedPacket> ReadPackets(SmackerContainer container) {
    var data = container.Data;
    var frameSizes = container.FrameSizes;
    var frameTypes = container.FrameTypes;
    var audioStreamIndex = new int[_AUDIO_TRACK_COUNT];
    var nextAudioStreamIndex = 1;
    for (var t = 0; t < _AUDIO_TRACK_COUNT; ++t)
      audioStreamIndex[t] = (container.AudioTrackRates[t] & _AUDIO_RATE_DATA_PRESENT) != 0 ? nextAudioStreamIndex++ : -1;

    var audioSamplePosition = new long[_AUDIO_TRACK_COUNT];
    var offset = container.FramesDataOffset;

    for (var i = 0; i < frameSizes.Length; ++i) {
      var blobLength = frameSizes[i];
      if (blobLength < 0)
        // Only reachable with the raw stored dword's own top bit set, which no real file needs since a
        // single frame anywhere near two gigabytes is not a thing Smacker was ever used to hold; named
        // rather than left to throw on the slice below with no frame number attached to the message.
        throw new InvalidDataException($"Frame {i} states a negative length.");

      if ((long)offset + blobLength > data.Length)
        // A real capture is free to stop mid-recording, the same shape RoQ's and VQA's own truncated
        // samples take; the frames read so far are still handed out.
        yield break;

      var blob = data.Slice(offset, blobLength);
      var frameType = frameTypes[i];
      var at = 0;

      ReadOnlyMemory<byte> palette = default;
      if ((frameType & _FRAME_TYPE_HAS_PALETTE) != 0) {
        if (at >= blob.Length)
          throw new InvalidDataException($"Frame {i} states a palette chunk but has no bytes left to hold one.");

        var paletteLength = blob.Span[at] * 4;
        if (at + paletteLength > blob.Length)
          throw new InvalidDataException(
            $"Frame {i}'s palette chunk states {paletteLength} bytes, which runs past the frame's own {blob.Length}.");

        palette = blob.Slice(at, paletteLength);
        at += paletteLength;
      }

      for (var t = 0; t < _AUDIO_TRACK_COUNT; ++t) {
        if ((frameType & (2 << t)) == 0)
          continue;

        if (at + 4 > blob.Length)
          throw new InvalidDataException($"Frame {i}'s audio track {t} chunk has no room for its own four-byte length.");

        var chunkLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(blob.Span[at..]);
        if (chunkLength < 4 || (long)at + chunkLength > blob.Length)
          throw new InvalidDataException(
            $"Frame {i}'s audio track {t} chunk states {chunkLength} bytes, which is short of its own "
            + $"four-byte length field or runs past the frame's own {blob.Length}.");

        var streamIndex = audioStreamIndex[t];
        if (streamIndex >= 0) {
          // The four-byte length field counts itself; ffmpeg's own reported packet size for every
          // audio chunk measured against this is that count minus four, so the payload handed out
          // starts right after it.
          var payload = blob.Slice(at + 4, chunkLength - 4);
          var rate = container.AudioTrackRates[t];
          var bytesPerSample = (rate & _AUDIO_RATE_16_BIT) != 0 ? 2 : 1;
          var channels = (rate & _AUDIO_RATE_STEREO) != 0 ? 2 : 1;

          // Nothing here decodes this track's samples, but the format states how many there are
          // without needing to: a compressed chunk opens with its own decompressed byte count, and an
          // uncompressed one simply is that many bytes already. Either way the byte count divides
          // evenly by the sample's own width, which is what turns a packet's length into a position on
          // the stream's timeline stated in samples rather than in packets.
          var decompressedLength = (rate & _AUDIO_RATE_COMPRESSED) != 0 && payload.Length >= 4
            ? (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Span)
            : payload.Length;
          var sampleCount = decompressedLength / (bytesPerSample * channels);

          yield return new(
            StreamIndex: streamIndex,
            Data: payload,
            PresentationTimestamp: audioSamplePosition[t],
            Duration: sampleCount,
            IsKeyFrame: true);
          audioSamplePosition[t] += sampleCount;
        }

        at += chunkLength;
      }

      var video = blob[at..];
      var videoPayload = new byte[1 + palette.Length + video.Length];
      videoPayload[0] = frameType;
      palette.Span.CopyTo(videoPayload.AsSpan(1));
      video.Span.CopyTo(videoPayload.AsSpan(1 + palette.Length));

      yield return new(
        StreamIndex: 0,
        Data: videoPayload,
        PresentationTimestamp: i,
        DecodeTimestamp: i,
        Duration: 1,
        IsKeyFrame: i == 0);

      offset += blobLength;
    }
  }
}
