using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Ea.Tests;

/// <summary>
/// The Electronic Arts container's demuxing behaviour: which chunk becomes which stream's packet,
/// which chunk family a file is recognised as carrying, and how a file built from two runs of
/// pictures back to back — the shape <c>TITLE.CMV</c> itself takes — is walked.
/// </summary>
/// <remarks>
/// Picture-level decoding is not exercised here — <see cref="Codecs.Tests.EaCmvVideoDecoderTests"/>
/// covers the block encodings and the two-buffer motion reference, and the one real file this reader
/// was built and measured against, all 194 of its pictures across both of its runs, was compared frame
/// by frame against ffmpeg's own <c>rgb24</c> decode with no differing sample anywhere. What is worth a
/// hand-built fixture is what that one file's own shape does not force a reader to exercise: a file
/// naming no recognised chunk at all, a chunk whose stated size cannot even cover its own header, an
/// unrecognised chunk sitting between two recognised ones, and a file that ends part way through its
/// last chunk.
/// </remarks>
[TestFixture]
public sealed class EaReaderTests {

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFileNotOpeningWithARecognisedChunkIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => EaContainer.FromBytes(_Chunk("XXXX", [])));
    Assert.That(failure!.Message, Does.Contain("Electronic Arts"));
  }

  [Test]
  [Category("Unit")]
  public void AChunkStatingASizeSmallerThanItsOwnHeaderIsRefused() {
    // The first chunk is a plausible one, so the file opens; the second states a size smaller than
    // the eight-byte header that size is itself supposed to include, which is not a truncation and
    // is refused outright rather than treated as one.
    var malformed = new byte[8];
    Encoding.ASCII.GetBytes("MVIh").CopyTo(malformed, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(4), 4);
    var file = _File([_MviHeader(4, 4, 10, 0, 0, []), malformed]);

    var failure = Assert.Throws<InvalidDataException>(() => EaContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("shorter than its header"));
  }

  [Test]
  [Category("Unit")]
  public void WidthHeightAndFrameRateComeFromTheFirstMVIh() {
    var file = _File([_MviHeader(64, 32, 15, 0, 0, [])]);
    var container = EaContainer.FromBytes(file);

    Assert.That(container.VideoCodec, Is.EqualTo(EaVideoCodecKind.Cmv));
    Assert.That(container.Width, Is.EqualTo(64));
    Assert.That(container.Height, Is.EqualTo(32));
    Assert.That(container.FrameRate, Is.EqualTo(15));
  }

  [Test]
  [Category("Unit")]
  public void ATgvFileIsRecognisedByItsOwnChunkFamily() {
    var file = _File([_KvgtHeader(320, 200, 0, [])]);
    var container = EaContainer.FromBytes(file);

    Assert.That(container.VideoCodec, Is.EqualTo(EaVideoCodecKind.Tgv));
    Assert.That(container.Width, Is.EqualTo(320));
    Assert.That(container.Height, Is.EqualTo(200));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ACmvStreamCarriesTheSynthetic4CharacterCode() {
    var file = _File([_MviHeader(4, 4, 10, 0, 0, [])]);
    var container = EaContainer.FromBytes(file);

    var streams = EaContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Codec, Is.EqualTo(CodecTag.FromCharacters("cmv ")));
  }

  [Test]
  [Category("Unit")]
  public void TheDeclaredFrameCountSumsBothRunsOfAFileWithTwoOfThem() {
    // The shape TITLE.CMV itself takes: an MVIe closing the first run of pictures, immediately
    // followed by a fresh MVIh that restarts a second run without restating a picture of its own.
    var file = _File([
      _MviHeader(4, 4, 10, 0, 0, []),
      _MviFrameIntra(new byte[16]),
      _MviFrameIntra(new byte[16]),
      _Chunk("MVIe", []),
      _MviHeader(4, 4, 10, 0, 0, []),
      _MviFrameIntra(new byte[16]),
    ]);
    var container = EaContainer.FromBytes(file);

    Assert.That(container.VideoFrameCount, Is.EqualTo(3));
  }

  // ============================================================================================
  // Packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryVideoPacketGoesOnStreamZero() {
    var file = _File([_MviHeader(4, 4, 10, 0, 0, []), _MviFrameIntra(new byte[16]), _MviFrameIntra(new byte[16])]);
    var container = EaContainer.FromBytes(file);

    var packets = EaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(3)); // one MVIh packet, two MVIf packets
    Assert.That(packets.All(p => p.StreamIndex == 0), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AnIntraFrameTypeIsReportedAsAKeyFrameAndAnInterOneIsNot() {
    var file = _File([
      _MviHeader(4, 4, 10, 0, 0, []),
      _MviFrameIntra(new byte[16]),
      _MviFrameInter([0, 0, 0, 0], []),
    ]);
    var container = EaContainer.FromBytes(file);

    var pictures = EaContainer.ReadPackets(container).Where(p => _FourCc(p.Data.Span) == "MVIf").ToArray();
    Assert.That(pictures, Has.Length.EqualTo(2));
    Assert.That(pictures[0].IsKeyFrame, Is.True);
    Assert.That(pictures[1].IsKeyFrame, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AnEndChunkBecomesAPacketTheCodecTreatsAsEndOfStream() {
    // It used to be skipped here. A remux has to put it back, and the demuxer is the only thing
    // that saw it, so it is a packet now and the CMV decoder ends its stream on it rather than
    // refusing a chunk it does not paint from.
    var file = _File([_MviHeader(4, 4, 10, 0, 0, []), _Chunk("MVIe", [])]);
    var container = EaContainer.FromBytes(file);

    var packets = EaContainer.ReadPackets(container).ToArray();
    Assert.That(packets, Has.Length.EqualTo(2)); // the MVIh and the MVIe
  }

  [Test]
  [Category("Unit")]
  public void AKnownAudioChunkBetweenTwoVideoOnesBecomesAnAudioPacket() {
    var file = _File([
      _MviHeader(4, 4, 10, 0, 0, []),
      _Chunk("SCHl", new byte[12]), // an audio stream header, which this reader decodes nothing of
      _MviFrameIntra(new byte[16]),
    ]);
    var container = EaContainer.FromBytes(file);

    var packets = EaContainer.ReadPackets(container).ToArray();
    // SCHl is a sound stream header this reader now names rather than steps over, so a remux can
    // put it back where it was. It is an audio packet; nothing decodes it here.
    Assert.That(packets, Has.Length.EqualTo(3));
    Assert.That(packets.Count(p => p.StreamIndex == 0), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void APacketCarriesItsOwnChunkHeader() {
    var file = _File([_MviHeader(4, 4, 10, 0, 0, []), _MviFrameIntra(new byte[16])]);
    var container = EaContainer.FromBytes(file);

    var picture = EaContainer.ReadPackets(container).Single(p => _FourCc(p.Data.Span) == "MVIf");
    Assert.That(_FourCc(picture.Data.Span), Is.EqualTo("MVIf"));
  }

  [Test]
  [Category("Unit")]
  public void AFileThatEndsPartWayThroughItsLastChunkStopsCleanlyRatherThanThrowing() {
    var secondChunk = _MviFrameIntra(new byte[16]);
    var file = _File([_MviHeader(4, 4, 10, 0, 0, []), _MviFrameIntra(new byte[16]), secondChunk]);
    var truncated = file[..^(secondChunk.Length - 2)];

    var container = EaContainer.FromBytes(truncated);

    Assert.DoesNotThrow(() => EaContainer.ReadPackets(container).ToArray());
    Assert.That(EaContainer.ReadPackets(container).Count(p => _FourCc(p.Data.Span) == "MVIf"), Is.EqualTo(1));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static string _FourCc(ReadOnlySpan<byte> packet) => Encoding.ASCII.GetString(packet[..4]);

  private static byte[] _Chunk(string fourCc, byte[] payload) {
    var chunk = new byte[8 + payload.Length];
    Encoding.ASCII.GetBytes(fourCc).CopyTo(chunk, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)chunk.Length);
    payload.CopyTo(chunk, 8);
    return chunk;
  }

  private static byte[] _MviHeader(int width, int height, int frameRate, int palStart, int palCount, byte[] paletteBytes) {
    var payload = new byte[0x10 + paletteBytes.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10), (ushort)frameRate);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12), (ushort)palStart);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14), (ushort)palCount);
    paletteBytes.CopyTo(payload, 0x10);
    return _Chunk("MVIh", payload);
  }

  private static byte[] _KvgtHeader(int width, int height, int palCount, byte[] rest) {
    var payload = new byte[0xC + rest.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), (ushort)palCount);
    rest.CopyTo(payload, 0xC);
    return _Chunk("kVGT", payload);
  }

  private static byte[] _MviFrameIntra(byte[] raster) {
    var payload = new byte[2 + raster.Length];
    raster.CopyTo(payload, 2);
    return _Chunk("MVIf", payload);
  }

  private static byte[] _MviFrameInter(byte[] motionBytes, byte[] escapeBytes) {
    var payload = new byte[2 + motionBytes.Length + escapeBytes.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), 1);
    motionBytes.CopyTo(payload, 2);
    escapeBytes.CopyTo(payload, 2 + motionBytes.Length);
    return _Chunk("MVIf", payload);
  }

  private static byte[] _File(IEnumerable<byte[]> chunks) {
    var all = chunks.ToArray();
    var file = new byte[all.Sum(c => c.Length)];
    var at = 0;
    foreach (var chunk in all) {
      chunk.CopyTo(file, at);
      at += chunk.Length;
    }

    return file;
  }
}
