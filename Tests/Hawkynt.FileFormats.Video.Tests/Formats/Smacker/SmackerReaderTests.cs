using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.SmackerVideo.Tests;

/// <summary>
/// The Smacker container's demuxing behaviour: the header's own two ways of stating a frame rate,
/// which frame is the ring frame, and how a frame's bytes split into an optional palette, up to seven
/// audio tracks and what is left for the picture.
/// </summary>
/// <remarks>
/// Every structural fact exercised here — the exact byte offsets of the header's fields, using a
/// frame's stated length exactly as stored, the two frame-rate readings, and where a track's own chunk
/// ends — was checked against five real files from samples.ffmpeg.org before being written down here,
/// not assumed from the format's documentation alone. See <see cref="SmackerReader"/> for what that
/// checking found. What is worth a hand-built fixture is what those five files' own shape does not
/// force a reader to exercise: a signature that is not this format's, a picture with no pixels, a zero
/// frame rate, a chunk that runs past its frame, and a file that stops mid-recording.
/// </remarks>
[TestFixture]
public sealed class SmackerReaderTests {

  // ============================================================================================
  // Registration
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheContainerIsRegisteredForItsExtensionAndSignature() {
    var file = _Header("SMK2", 16, 16, 1, 10, 0);

    Assert.That(VideoFormatRegistry.ByExtension(".smk"), Does.Contain(VideoFormat.Smacker));
    Assert.That(VideoFormatRegistry.Detect(file), Is.EqualTo(VideoFormat.Smacker));
    Assert.That(VideoFormatRegistry.ReadStreams(file), Has.Count.EqualTo(1));
  }

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileNotOpeningWithASmackerSignatureIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => SmackerContainer.FromBytes(new byte[120]));
    Assert.That(failure!.Message, Does.Contain("SMK"));
  }

  [Test]
  [Category("Unit")]
  public void AnSmk2SignatureIsAccepted() {
    var container = SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 1, 10, 0));
    Assert.That(container.Width, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void AnSmk4SignatureIsAccepted() {
    var container = SmackerContainer.FromBytes(_Header("SMK4", 16, 16, 1, 10, 0));
    Assert.That(container.Width, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ThePictureSizeComesFromTheHeader() {
    var container = SmackerContainer.FromBytes(_Header("SMK2", 32, 24, 1, 10, 0));
    Assert.That(container.Width, Is.EqualTo(32));
    Assert.That(container.Height, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void AZeroWidthRefuses() {
    Assert.Throws<InvalidDataException>(() => SmackerContainer.FromBytes(_Header("SMK2", 0, 16, 1, 10, 0)));
  }

  [Test]
  [Category("Unit")]
  public void ZeroFramesRefuses() {
    Assert.Throws<InvalidDataException>(() => SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 0, 10, 0)));
  }

  // ============================================================================================
  // Frame rate — the two readings, checked against ffprobe on real files
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APositiveFrameRateIsWholeMillisecondsPerFrame() {
    // Matches wetlands/wetlogo.smk: FrameRate 71 reads as ffprobe's own time base 71/1000.
    var container = SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 1, 71, 0));
    Assert.That(container.VideoTimeBase, Is.EqualTo(new Rational(71, 1000)));
  }

  [Test]
  [Category("Unit")]
  public void ANegativeFrameRateIsHundredthsOfAMillisecond() {
    // Matches mech2/ajfstr1.smk: FrameRate -6700 reads as ffprobe's own time base 67/1000.
    var container = SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 1, -6700, 0));
    Assert.That(container.VideoTimeBase, Is.EqualTo(new Rational(67, 1000)));
  }

  [Test]
  [Category("Unit")]
  public void ANegativeFrameRateThatDoesNotReduceAgainstWholeMillisecondsStillMatchesFfprobe() {
    // Matches smk-deen/credits.smk: FrameRate -14200 reads as ffprobe's own time base 71/500 — only
    // reachable through the hundredths-of-a-millisecond reading, since 14200 does not reduce against
    // 1000 the way a whole-millisecond value would.
    var container = SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 1, -14200, 0));
    Assert.That(container.VideoTimeBase, Is.EqualTo(new Rational(71, 500)));
  }

  [Test]
  [Category("Unit")]
  public void AZeroFrameRateRefuses() {
    var failure = Assert.Throws<NotSupportedException>(() => SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 1, 0, 0)));
    Assert.That(failure!.Message, Does.Contain("FrameRate"));
  }

  // ============================================================================================
  // The ring frame
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void NoRingFrameFlagLeavesTheFrameCountAsStated() {
    var container = SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 9, 10, 0));
    Assert.That(container.VideoFrameCount, Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void TheRingFrameFlagAddsOneMoreFrameThanTheHeaderStates() {
    // Matches smk-deen/credits.smk: Frames states 9 and Flags states a ring frame; ffprobe reads 10
    // video packets.
    var container = SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 9, 10, 1));
    Assert.That(container.VideoFrameCount, Is.EqualTo(10));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileWithNoAudioTrackDeclaresOneStream() {
    var container = SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 1, 10, 0));
    var streams = SmackerContainer.Streams(container);

    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(streams[0].Codec, Is.EqualTo(CodecTag.FromCharacters("SMK2")));
  }

  [Test]
  [Category("Unit")]
  public void AnSmk4FileDeclaresItsVideoStreamWithTheSmk4Tag() {
    var container = SmackerContainer.FromBytes(_Header("SMK4", 16, 16, 1, 10, 0));
    var streams = SmackerContainer.Streams(container);

    Assert.That(streams[0].Codec, Is.EqualTo(CodecTag.FromCharacters("SMK4")));
  }

  [Test]
  [Category("Unit")]
  public void AudioTracksBecomeStreamsInTrackOrderAfterTheVideoStream() {
    var audioRates = new uint[7];
    audioRates[0] = _AudioRate(22050, compressed: true, dataPresent: true);
    audioRates[2] = _AudioRate(11025, compressed: false, dataPresent: true, stereo: true);
    var container = SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 1, 10, 0, audioRates));
    var streams = SmackerContainer.Streams(container);

    Assert.That(streams, Has.Count.EqualTo(3));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(streams[1].TimeBase, Is.EqualTo(new Rational(1, 22050)));
    Assert.That(streams[2].TimeBase, Is.EqualTo(new Rational(1, 11025)));
    Assert.That(streams[1].Codec, Is.EqualTo(CodecTag.FromCharacters("SMKA")));
  }

  [Test]
  [Category("Unit")]
  public void AnAudioRateWithNoDataPresentBitDeclaresNoStream() {
    var audioRates = new uint[7];
    audioRates[0] = 22050; // frequency alone, no flag bits at all
    var container = SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 1, 10, 0, audioRates));

    Assert.That(SmackerContainer.Streams(container), Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void DeclaredFrameCountIncludesTheRingFrame() {
    var container = SmackerContainer.FromBytes(_Header("SMK2", 16, 16, 4, 10, 1));
    Assert.That(SmackerContainer.Streams(container)[0].DeclaredFrameCount, Is.EqualTo(5));
  }

  // ============================================================================================
  // Reading frames
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void OnlyTheFirstFrameIsAKeyFrame() {
    var frames = new[] { _Frame(false, [1, 2, 3]), _Frame(false, [4, 5]), _Frame(false, [6]) };
    var container = _Open(3, frames);

    var video = SmackerContainer.ReadPackets(container).Where(p => p.StreamIndex == 0).ToArray();
    Assert.That(video, Has.Length.EqualTo(3));
    Assert.That(video[0].IsKeyFrame, Is.True);
    Assert.That(video[1].IsKeyFrame, Is.False);
    Assert.That(video[2].IsKeyFrame, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AVideoPacketCarriesItsFrameTypeByteThenTheVideoBytes() {
    var frames = new[] { _Frame(false, [0xAA, 0xBB, 0xCC]) };
    var container = _Open(1, frames);

    var packet = SmackerContainer.ReadPackets(container).Single(p => p.StreamIndex == 0);
    Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 0, 0xAA, 0xBB, 0xCC }));
  }

  [Test]
  [Category("Unit")]
  public void APaletteChunkIsCarriedInFrontOfTheVideoBytesWithTheFrameTypeBitSet() {
    var palette = _PaletteChunk([0x00, 0x11, 0x22]); // one "copy previous" block, three bytes total
    var frames = new[] { _Frame(true, [0xDD, 0xEE], palette) };
    var container = _Open(1, frames);

    var packet = SmackerContainer.ReadPackets(container).Single(p => p.StreamIndex == 0);
    var expected = new byte[] { 1 }.Concat(palette).Concat(new byte[] { 0xDD, 0xEE }).ToArray();
    Assert.That(packet.Data.ToArray(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void AudioBytesAreExcisedFromTheVideoPacket() {
    var audioRates = new uint[7];
    audioRates[0] = _AudioRate(8000, compressed: false, dataPresent: true);
    var audioChunk = _RawAudioChunk([1, 2, 3, 4]); // uncompressed: length dword, then four raw bytes
    var frames = new[] { _Frame(false, [0x99], audio: [(0, audioChunk)]) };
    var container = _Open(1, frames, audioRates);

    var videoPacket = SmackerContainer.ReadPackets(container).Single(p => p.StreamIndex == 0);
    Assert.That(videoPacket.Data.ToArray(), Is.EqualTo(new byte[] { 0b0000_0010, 0x99 }));
  }

  [Test]
  [Category("Unit")]
  public void AnAudioPacketExcludesItsOwnFourByteLengthCounter() {
    var audioRates = new uint[7];
    audioRates[0] = _AudioRate(8000, compressed: false, dataPresent: true);
    var audioChunk = _RawAudioChunk([9, 8, 7, 6, 5]);
    var frames = new[] { _Frame(false, [0x01], audio: [(0, audioChunk)]) };
    var container = _Open(1, frames, audioRates);

    var audioPacket = SmackerContainer.ReadPackets(container).Single(p => p.StreamIndex == 1);
    Assert.That(audioPacket.Data.ToArray(), Is.EqualTo(new byte[] { 9, 8, 7, 6, 5 }));
  }

  [Test]
  [Category("Unit")]
  public void MultipleTracksInOneFrameAreEachTheirOwnPacketInTrackOrder() {
    var audioRates = new uint[7];
    audioRates[0] = _AudioRate(8000, compressed: false, dataPresent: true);
    audioRates[3] = _AudioRate(8000, compressed: false, dataPresent: true);
    var track0 = _RawAudioChunk([1, 1]);
    var track3 = _RawAudioChunk([3, 3, 3]);
    var frames = new[] { _Frame(false, [0x01], audio: [(0, track0), (3, track3)]) };
    var container = _Open(1, frames, audioRates);

    var packets = SmackerContainer.ReadPackets(container).ToArray();
    var stream1 = packets.Single(p => p.StreamIndex == 1);
    var stream2 = packets.Single(p => p.StreamIndex == 2); // track 3 is the second declared stream
    Assert.That(stream1.Data.ToArray(), Is.EqualTo(new byte[] { 1, 1 }));
    Assert.That(stream2.Data.ToArray(), Is.EqualTo(new byte[] { 3, 3, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void AnUncompressedAudioPacketsTimestampAdvancesByItsOwnByteCount() {
    var audioRates = new uint[7];
    audioRates[0] = _AudioRate(8000, compressed: false, dataPresent: true); // mono, 8-bit: one byte a sample
    var frame0 = _Frame(false, [0x01], audio: [(0, _RawAudioChunk([1, 2, 3, 4]))]);
    var frame1 = _Frame(false, [0x01], audio: [(0, _RawAudioChunk([5, 6]))]);
    var container = _Open(2, [frame0, frame1], audioRates);

    var audioPackets = SmackerContainer.ReadPackets(container).Where(p => p.StreamIndex == 1).ToArray();
    Assert.That(audioPackets[0].PresentationTimestamp, Is.EqualTo(0));
    Assert.That(audioPackets[1].PresentationTimestamp, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ACompressedAudioPacketsTimestampAdvancesByTheDecompressedByteCountItStatesItself() {
    var audioRates = new uint[7];
    audioRates[0] = _AudioRate(8000, compressed: true, dataPresent: true);
    // Compressed payload opens with its own decompressed length, here claiming 100 decompressed bytes
    // regardless of how many bytes of compressed data follow it.
    var unpackedLength = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(unpackedLength, 100);
    var compressedPayload = unpackedLength.Concat(new byte[] { 0x00, 0x00 }).ToArray();
    var frame0 = _Frame(false, [0x01], audio: [(0, _RawAudioChunk(compressedPayload))]);
    var frame1 = _Frame(false, [0x01], audio: [(0, _RawAudioChunk(compressedPayload))]);
    var container = _Open(2, [frame0, frame1], audioRates);

    var audioPackets = SmackerContainer.ReadPackets(container).Where(p => p.StreamIndex == 1).ToArray();
    Assert.That(audioPackets[0].PresentationTimestamp, Is.EqualTo(0));
    Assert.That(audioPackets[1].PresentationTimestamp, Is.EqualTo(100));
  }

  // ============================================================================================
  // Malformed chunks
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APaletteChunkRunningPastTheFrameRefuses() {
    // A palette length byte of 200 states an 800-byte chunk in a frame that holds far less.
    var blob = new byte[] { 200, 1, 2 };
    var container = _OpenRaw(1, [blob], frameTypes: [1]);

    var failure = Assert.Throws<InvalidDataException>(() => SmackerContainer.ReadPackets(container).ToArray());
    Assert.That(failure!.Message, Does.Contain("palette"));
  }

  [Test]
  [Category("Unit")]
  public void AnAudioChunkTooShortForItsOwnLengthCounterRefuses() {
    var audioRates = new uint[7];
    audioRates[0] = _AudioRate(8000, compressed: false, dataPresent: true);
    var blob = new byte[] { 1, 2 }; // fewer than the four bytes a length counter needs
    var container = _OpenRaw(1, [blob], frameTypes: [0b0000_0010], audioRates);

    Assert.Throws<InvalidDataException>(() => SmackerContainer.ReadPackets(container).ToArray());
  }

  [Test]
  [Category("Unit")]
  public void AnAudioChunkStatingMoreBytesThanTheFrameHoldsRefuses() {
    var audioRates = new uint[7];
    audioRates[0] = _AudioRate(8000, compressed: false, dataPresent: true);
    var lengthBytes = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(lengthBytes, 1000);
    var container = _OpenRaw(1, [lengthBytes], frameTypes: [0b0000_0010], audioRates);

    Assert.Throws<InvalidDataException>(() => SmackerContainer.ReadPackets(container).ToArray());
  }

  [Test]
  [Category("Unit")]
  public void AFrameCountThatWouldOverflowThirtyTwoBitArithmeticRefusesRatherThanWrapping() {
    // 0x40000000 frames times the four bytes each costs in FrameSizes overflows a 32-bit int back to
    // zero; a reader doing that arithmetic in 32 bits would pass its own bounds check on a file of
    // just the header and read nothing useful at all instead of refusing this file by name.
    var header = new byte[_HeaderLength];
    var span = header.AsSpan();
    "SMK2"u8.CopyTo(span);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 16);
    BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 16);
    BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 0x40000000);
    BinaryPrimitives.WriteInt32LittleEndian(span[16..], 10);

    Assert.Throws<InvalidDataException>(() => SmackerContainer.FromBytes(header));
  }

  [Test]
  [Category("Unit")]
  public void AHeaderStatingMoreFramesThanTheFileHoldsRoomForRefuses() {
    // The header alone, with no per-frame arrays or tree section behind it at all — a header stating
    // 5000 frames needs 25000 more bytes just for the two arrays before a single frame is reached.
    var header = new byte[_HeaderLength];
    var span = header.AsSpan();
    "SMK2"u8.CopyTo(span);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 16);
    BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 16);
    BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 5000);
    BinaryPrimitives.WriteInt32LittleEndian(span[16..], 10);

    Assert.Throws<InvalidDataException>(() => SmackerContainer.FromBytes(header));
  }

  [Test]
  [Category("Unit")]
  public void AFileThatStopsMidRecordingYieldsWhatItHasRatherThanRefusing() {
    var frames = new[] { _Frame(false, [1, 2]), _Frame(false, [3, 4]) };
    var container = _Open(2, frames);
    // Truncate the file to inside the second frame's stated bytes.
    var truncated = SmackerContainer.FromBytes(container.Data.ToArray()[..^1]);

    var video = SmackerContainer.ReadPackets(truncated).Where(p => p.StreamIndex == 0).ToArray();
    Assert.That(video, Has.Length.EqualTo(1));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private const int _HeaderLength = 104;

  private readonly record struct _FrameSpec(bool HasPalette, byte[] Video, byte[]? Palette, (int Track, byte[] Chunk)[] Audio);

  private static _FrameSpec _Frame(bool hasPalette, byte[] video, byte[]? palette = null, (int Track, byte[] Chunk)[]? audio = null)
    => new(hasPalette, video, palette, audio ?? []);

  /// <summary>A minimal one-block palette chunk: a "copy previous colours" block, self-describing its
  /// own length as the format states — total bytes, this length byte included, divided by four.</summary>
  private static byte[] _PaletteChunk(byte[] blocks) {
    var totalBytes = 1 + blocks.Length;
    var lengthByte = (byte)((totalBytes + 3) / 4);
    var padded = new byte[lengthByte * 4 - 1];
    blocks.CopyTo(padded, 0);
    return [lengthByte, .. padded];
  }

  /// <summary>An audio track chunk with its own four-byte self-inclusive length counter in front of
  /// whatever payload bytes are given.</summary>
  private static byte[] _RawAudioChunk(byte[] payload) {
    var chunk = new byte[4 + payload.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(chunk, (uint)chunk.Length);
    payload.CopyTo(chunk, 4);
    return chunk;
  }

  private static uint _AudioRate(int frequency, bool compressed, bool dataPresent, bool stereo = false, bool is16Bit = false) {
    uint value = (uint)frequency;
    if (compressed) value |= 1u << 31;
    if (dataPresent) value |= 1u << 30;
    if (is16Bit) value |= 1u << 29;
    if (stereo) value |= 1u << 28;
    return value;
  }

  /// <summary>Builds a bare, header-only file with no frames — enough for the header- and
  /// frame-rate-focused tests, which never call <see cref="SmackerContainer.ReadPackets"/>.</summary>
  private static byte[] _Header(string signature, int width, int height, int frames, int frameRate, uint flags, uint[]? audioRates = null) {
    var header = new byte[_HeaderLength];
    var span = header.AsSpan();
    System.Text.Encoding.ASCII.GetBytes(signature).CopyTo(span);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)height);
    BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)frames);
    BinaryPrimitives.WriteInt32LittleEndian(span[16..], frameRate);
    BinaryPrimitives.WriteUInt32LittleEndian(span[20..], flags);

    audioRates ??= new uint[7];
    for (var t = 0; t < 7; ++t)
      BinaryPrimitives.WriteUInt32LittleEndian(span[(72 + t * 4)..], audioRates[t]);

    var videoFrameCount = frames + ((flags & 1) != 0 ? 1 : 0);
    var body = new byte[videoFrameCount * 4 + videoFrameCount]; // FrameSizes all zero, FrameTypes all zero
    return [.. header, .. body];
  }

  /// <summary>Builds a complete file from a list of frame specifications, computing every offset and
  /// length the header states from the frames actually given.</summary>
  private static SmackerContainer _Open(int frames, _FrameSpec[] specs, uint[]? audioRates = null) {
    var blobs = new List<byte[]>();
    var frameTypes = new List<byte>();

    foreach (var spec in specs) {
      var frameType = (byte)(spec.HasPalette ? 1 : 0);
      var parts = new List<byte>();
      if (spec.HasPalette)
        parts.AddRange(spec.Palette!);

      foreach (var (track, chunk) in spec.Audio) {
        frameType |= (byte)(2 << track);
        parts.AddRange(chunk);
      }

      parts.AddRange(spec.Video);
      blobs.Add(parts.ToArray());
      frameTypes.Add(frameType);
    }

    return _OpenRaw(frames, blobs.ToArray(), frameTypes.ToArray(), audioRates);
  }

  private static SmackerContainer _OpenRaw(int frames, byte[][] blobs, byte[]? frameTypes = null, uint[]? audioRates = null) {
    var header = new byte[_HeaderLength];
    var span = header.AsSpan();
    "SMK2"u8.CopyTo(span);
    BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 16);
    BinaryPrimitives.WriteUInt32LittleEndian(span[8..], 16);
    BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)frames);
    BinaryPrimitives.WriteInt32LittleEndian(span[16..], 10);
    BinaryPrimitives.WriteUInt32LittleEndian(span[20..], 0);

    audioRates ??= new uint[7];
    for (var t = 0; t < 7; ++t)
      BinaryPrimitives.WriteUInt32LittleEndian(span[(72 + t * 4)..], audioRates[t]);

    var frameSizesBytes = new byte[blobs.Length * 4];
    for (var i = 0; i < blobs.Length; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(frameSizesBytes.AsSpan(i * 4), (uint)blobs[i].Length);

    var frameTypeBytes = frameTypes ?? new byte[blobs.Length];

    var file = header.Concat(frameSizesBytes).Concat(frameTypeBytes);
    foreach (var blob in blobs)
      file = file.Concat(blob);

    return SmackerContainer.FromBytes(file.ToArray());
  }
}
