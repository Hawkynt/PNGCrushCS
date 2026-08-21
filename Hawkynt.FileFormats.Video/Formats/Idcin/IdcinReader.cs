using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Idcin;

/// <summary>
/// Splits an id Cinematic file (Quake II's <c>.cin</c> FMV format) into the header, the 64KiB Huffman
/// table that opens every one, and the flat run of frame commands that follows — without decoding a
/// single pixel or a single Huffman code.
/// </summary>
/// <remarks>
/// The header, the table and the per-frame command values are stated in Tim Ferguson's format
/// description (mirrored at <c>multimedia.cx/mirror/idcin.html</c>, and the source MultimediaWiki's own
/// CIN page points to): a file is five little-endian words — width, height, audio sample rate, audio
/// sample width in bytes, and channel count — immediately followed by the 256x256-byte Huffman table,
/// one 256-entry histogram per tree, one tree per possible previous-pixel value. What comes after is a
/// flat run of frame commands, video and (when the header states a sample rate) audio alternating one
/// for one. Each video command opens with a four-byte word Ferguson's table gives as <c>0x0002</c> for
/// end of file, <c>0x0001</c> when a 768-byte palette follows, and <c>0x0000</c> for "no palette, the
/// previous one still applies" — the only value either real sample this was measured against ever
/// carries besides <c>1</c> and <c>2</c>; then the fields Ferguson names "Huffman count" and "Decode
/// count", four bytes each, and the Huffman-coded picture itself.
/// <para/>
/// <b>What "Huffman count" measures is not stated, and was settled by measurement.</b> Ferguson's page
/// names the field without saying whether the picture bytes that follow are that many, or that many
/// minus the four bytes "Decode count" itself occupies. Reading it as the picture's own length runs
/// both real samples out of file after two pictures each; reading it as covering "Decode count" and the
/// picture together — so the picture itself is <c>Huffman count - 4</c> bytes — is the one reading that
/// reaches the end of both files, forty-eight and eighty-two pictures, with no other candidate examined
/// (five combinations of bit order and tie-breaking crossed with this) surviving past a handful of
/// pictures. "Decode count" itself is never read back: on every picture of both real files it equals
/// width times height exactly, which is already known before a picture is reached, so there is nothing
/// in it a caller could learn.
/// <para/>
/// The format has no signature of its own: the header is five plain dimension and audio fields running
/// straight into the Huffman table, with no fixed bytes anywhere for a reader to check. What
/// <see cref="LooksPlausible"/> does instead is a plausibility heuristic of this reader's own devising —
/// a picture size and, where a sample rate is stated at all, a sample width and channel count within
/// bounds no real file was ever going to exceed.
/// <para/>
/// Not every sample carries a clean end-of-file command. One of the two this was measured against —
/// <c>quake.cin</c> — ends mid-picture with no command <c>0x0002</c> anywhere in it; this reader stops
/// at the forty-eighth picture, the last one that fits, rather than refusing the file outright. A file
/// that runs out of room for its next command or its next chunk is read as far as it goes and no
/// further.
/// </remarks>
internal static class IdcinReader {

  private const int _HEADER_LENGTH = 20;
  private const int _HUFFMAN_TABLE_LENGTH = 64 * 1024;
  private const int _PALETTE_LENGTH = 768;
  private const uint _COMMAND_PALETTE = 1;
  private const uint _COMMAND_END_OF_FILE = 2;
  private const int _FRAMES_PER_SECOND = 14;
  private const int _MAX_DIMENSION = 1024;

  internal readonly record struct RawChunk(bool IsVideo, int Offset, int Length);

  internal static bool LooksPlausible(ReadOnlySpan<byte> header) {
    if (header.Length < _HEADER_LENGTH)
      return false;

    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(header);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
    var sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
    var bytesPerSample = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
    var channels = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);

    if (width is <= 0 or > _MAX_DIMENSION || height is <= 0 or > _MAX_DIMENSION)
      return false;

    return sampleRate == 0 || (bytesPerSample is 1 or 2 && channels is 1 or 2 && sampleRate is >= 4000 and <= 96000);
  }

  internal static IdcinContainer Open(ReadOnlyMemory<byte> data) {
    if (data.Length < _HEADER_LENGTH + _HUFFMAN_TABLE_LENGTH)
      throw new NotSupportedException(
        "The file is shorter than the twenty-byte header and 64KiB Huffman table every id Cinematic "
        + "file opens with. This is not an id Cinematic file.");

    var header = data.Span[.._HEADER_LENGTH];
    if (!LooksPlausible(header))
      throw new NotSupportedException(
        "This file's header does not state a plausible picture size, or states an implausible audio "
        + "sample width or channel count. This is not an id Cinematic file — the format carries no "
        + "signature of its own, so this is the only check a container can make.");

    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(header);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
    var sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
    var bytesPerSample = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
    var channels = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
    var huffmanTable = data.Slice(_HEADER_LENGTH, _HUFFMAN_TABLE_LENGTH);

    var frameCount = 0;
    foreach (var chunk in _WalkChunks(data, width, height, sampleRate, bytesPerSample, channels))
      if (chunk.IsVideo)
        ++frameCount;

    return new() {
      Data = data,
      Width = width,
      Height = height,
      AudioSampleRate = sampleRate,
      AudioBytesPerSample = bytesPerSample,
      AudioChannels = channels,
      VideoFrameCount = frameCount,
      HuffmanTable = huffmanTable,
    };
  }

  /// <summary>Walks the frame commands once, video and audio alike, stopping cleanly at an explicit
  /// end-of-file command or at the first command that does not fully fit in what remains of the file.</summary>
  private static IEnumerable<RawChunk> _WalkChunks(
    ReadOnlyMemory<byte> data, int width, int height, int sampleRate, int bytesPerSample, int channels) {
    var length = data.Length;
    var pos = _HEADER_LENGTH + _HUFFMAN_TABLE_LENGTH;
    var hasAudio = sampleRate != 0;

    // Ferguson's page states this formula outright — "audio width * audio channels * audio rate/14
    // bytes" per frame — and nothing else. It does not say what happens to the remainder when the
    // sample rate does not divide fourteen evenly, and neither real sample this was measured against
    // has one that doesn't: 22050 (idlog.cin) divides exactly, and quake.cin carries no audio at all.
    // So this is the formula as documented, not a redistribution scheme read from anywhere and never
    // exercised — a file whose sample rate leaves a remainder is outside what either the page or the
    // measurement here covers.
    var audioChunkSize = hasAudio ? sampleRate / _FRAMES_PER_SECOND * bytesPerSample * channels : 0;

    var nextIsVideo = true;

    while (pos < length) {
      if (nextIsVideo) {
        if (pos + 4 > length)
          yield break;

        var chunkStart = pos;
        var command = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[pos..]);
        if (command == _COMMAND_END_OF_FILE)
          yield break;

        var cursor = pos + 4;
        if (command == _COMMAND_PALETTE) {
          if (cursor + _PALETTE_LENGTH > length)
            yield break;
          cursor += _PALETTE_LENGTH;
        }

        if (cursor + 8 > length)
          yield break;

        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[cursor..]);
        // The four bytes right after are Ferguson's "Decode count": on every picture of both real
        // files measured it equals width times height exactly, which is already known before a
        // picture is reached, so nothing here reads it back.
        cursor += 8;

        if (chunkSize < 4)
          throw new System.IO.InvalidDataException(
            $"A video command at byte {chunkStart} states a chunk size of {chunkSize}, short of the "
            + "four bytes its own decoded-pixel-count field alone needs.");

        var videoDataLength = (int)(chunkSize - 4);
        if (cursor + videoDataLength > length)
          yield break;

        yield return new(IsVideo: true, Offset: chunkStart, Length: cursor + videoDataLength - chunkStart);
        pos = cursor + videoDataLength;
        nextIsVideo = !hasAudio;
      } else {
        if (pos + audioChunkSize > length)
          yield break;

        yield return new(IsVideo: false, Offset: pos, Length: audioChunkSize);
        pos += audioChunkSize;
        nextIsVideo = true;
      }
    }
  }

  /// <summary>Walks the film's frame commands a second time, handing out the ones a caller can do
  /// anything with as packets — pictures on stream 0, sound on stream 1.</summary>
  internal static IEnumerable<CodedPacket> ReadPackets(IdcinContainer container) {
    var data = container.Data;
    var audioStreamIndex = container.AudioSampleRate != 0 ? 1 : -1;
    var samplesPerAudioPacket = container.AudioBytesPerSample > 0 && container.AudioChannels > 0
      ? container.AudioBytesPerSample * container.AudioChannels
      : 1;

    long videoFrame = 0;
    long audioSample = 0;

    foreach (var chunk in _WalkChunks(
      data, container.Width, container.Height, container.AudioSampleRate, container.AudioBytesPerSample, container.AudioChannels)) {
      if (chunk.IsVideo) {
        yield return new(
          StreamIndex: 0,
          Data: data.Slice(chunk.Offset, chunk.Length),
          PresentationTimestamp: videoFrame,
          DecodeTimestamp: videoFrame,
          Duration: 1,
          IsKeyFrame: true);
        ++videoFrame;
      } else if (audioStreamIndex >= 0) {
        yield return new(
          StreamIndex: audioStreamIndex,
          Data: data.Slice(chunk.Offset, chunk.Length),
          PresentationTimestamp: audioSample,
          IsKeyFrame: true);
        audioSample += chunk.Length / samplesPerAudioPacket;
      }
    }
  }
}
