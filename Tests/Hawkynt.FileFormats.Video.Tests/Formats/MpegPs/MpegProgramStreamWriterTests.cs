using System;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.MpegPs.Tests;

[TestFixture]
public sealed class MpegProgramStreamWriterTests {

  [Test]
  [Category("Unit")]
  public void ProgramStreamMap_HasNormativeLayoutAndCrc() {
    var video = _Stream(0, MediaStreamKind.Video, "mpg1");
    var audio = _Stream(1, MediaStreamKind.Audio, "aac ");

    var file = VideoIO.Mux<MpegProgramStreamWriter>(
      [video, audio],
      [new CodedPacket(0, _Picture()), new CodedPacket(1, new byte[] { 1, 2, 3, 4 })]);

    byte[] expected = [
      0x00, 0x00, 0x01, 0xBC,
      0x00, 0x12,
      0xE0, 0xFF,
      0x00, 0x00,
      0x00, 0x08,
      0x01, 0xE0, 0x00, 0x00,
      0x0F, 0xC0, 0x00, 0x00,
      0x84, 0x52, 0x04, 0x69,
    ];

    // The writer's canonical MPEG-2 pack is fourteen bytes. The map immediately follows the first
    // pack, before any media PES packet, and its CRC makes the Annex A decoder residual zero.
    Assert.That(file.AsSpan(14, expected.Length).ToArray(), Is.EqualTo(expected));
    Assert.That(_Crc(file.AsSpan(14, expected.Length)), Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void RemuxedMpeg1AndAacKeepTheirCodecIdentity() {
    var video = _Stream(0, MediaStreamKind.Video, "mpg1");
    var audio = _Stream(1, MediaStreamKind.Audio, "aac ");
    var file = VideoIO.Mux<MpegProgramStreamWriter>(
      [video, audio],
      [new CodedPacket(0, _Picture()), new CodedPacket(1, new byte[] { 7, 8, 9 })]);

    var streams = MpegProgramStreamContainer.Streams(MpegProgramStreamReader.FromBytes(file));

    Assert.That(streams.Select(stream => stream.Codec.ToString()), Is.EqualTo(new[] { "mpg1", "aac " }));
  }

  [Test]
  [Category("Unit")]
  public void HandlerCanPreserveAnExactH222StreamType() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("mpga"),
      Handler = new CodecTag(0x04),
      TimeBase = new Rational(1, 90_000),
    };

    var file = VideoIO.Mux<MpegProgramStreamWriter>([stream], [new CodedPacket(0, new byte[] { 1, 2, 3 })]);

    // One-entry PSM: the entry begins twelve bytes after its start code, after the two length fields
    // and the current/version bytes. 0x04 is MPEG-2 audio; deriving only from "mpga" would write 0x03.
    Assert.That(file[14 + 12], Is.EqualTo(0x04));
    Assert.That(file[14 + 13], Is.EqualTo(0xC0));
  }

  [Test]
  [Category("Unit")]
  public void PrivateStreamOne_IsAdvertisedOnlyOnce() {
    var ac3 = _Stream(0, MediaStreamKind.Audio, "ac-3");
    var dts = _Stream(1, MediaStreamKind.Audio, "dts ");
    var file = VideoIO.Mux<MpegProgramStreamWriter>(
      [ac3, dts],
      [new CodedPacket(0, new byte[] { 1, 2 }), new CodedPacket(1, new byte[] { 3, 4 })]);

    // The two DVD substreams share PES stream_id 0xBD. A PSM describes that PES id as private data;
    // the substream byte inside each PES packet remains what distinguishes AC-3 from DTS.
    Assert.That(file[14 + 11], Is.EqualTo(4));
    Assert.That(file[14 + 12], Is.EqualTo(0x06));
    Assert.That(file[14 + 13], Is.EqualTo(0xBD));
  }

  [Test]
  [Category("Unit")]
  public void UnknownVideoCodec_IsRefusedInsteadOfWritingAMisleadingMap() {
    var stream = _Stream(0, MediaStreamKind.Video, "????");

    var failure = Assert.Throws<NotSupportedException>(() => VideoIO.Mux<MpegProgramStreamWriter>([stream], Array.Empty<CodedPacket>()));

    Assert.That(failure!.Message, Does.Contain("stream_type").And.Contain("????"));
  }

  private static MediaStreamInfo _Stream(int index, MediaStreamKind kind, string codec)
    => new() {
      Index = index,
      Kind = kind,
      Codec = CodecTag.FromCharacters(codec),
      TimeBase = new Rational(1, 90_000),
    };

  private static byte[] _Picture() => [0x00, 0x00, 0x01, 0x00, 0x11, 0x22, 0x33, 0x44];

  private static uint _Crc(ReadOnlySpan<byte> data) {
    var crc = 0xFFFFFFFFu;
    foreach (var value in data) {
      crc ^= (uint)value << 24;
      for (var bit = 0; bit < 8; ++bit)
        crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7u : crc << 1;
    }

    return crc;
  }
}
