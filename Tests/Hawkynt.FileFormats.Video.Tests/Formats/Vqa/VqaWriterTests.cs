using System;
using System.Buffers.Binary;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Vqa.Tests;

[TestFixture]
public sealed class VqaWriterTests {

  [Test]
  [Category("Unit")]
  public void WritesFinfBeforeMediaAndPointsAtTheFrame() {
    var stream = _VideoStream();
    var frame = new CodedPacket(0, _Picture(_Chunk("VPT0", [0, 0])), 0, IsKeyFrame: true);

    var file = VideoIO.Mux<VqaWriter>([stream], [frame]);

    Assert.Multiple(() => {
      Assert.That(file.AsSpan(62, 4).ToArray(), Is.EqualTo("FINF"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(66, 4)), Is.EqualTo(4));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(70, 4)), Is.EqualTo(37u));
      Assert.That(file.AsSpan(74, 4).ToArray(), Is.EqualTo("VQFR"u8.ToArray()));
    });
  }

  [Test]
  [Category("Unit")]
  public void FinfMarksFramesThatCarryAPalette() {
    var stream = _VideoStream();
    var frame = new CodedPacket(0, _Picture(_Chunk("CPL0", new byte[768]), _Chunk("VPT0", [0, 0])), 0, IsKeyFrame: true);

    var file = VideoIO.Mux<VqaWriter>([stream], [frame]);
    var entry = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(70, 4));

    Assert.That(entry, Is.EqualTo(0x40000025u));
  }

  [Test]
  [Category("Unit")]
  public void FinfPointsAtTheFirstSoundChunkBelongingToAFrame() {
    var video = _VideoStream();
    var audio = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("WSAD"),
      SampleRate = 22050,
      Channels = 1,
      BitsPerSample = 16,
      TimeBase = new(1, 22050),
    };
    var sound = new CodedPacket(1, new byte[] { 1, 2, 3, 4 }, 0, IsKeyFrame: true);
    var frame = new CodedPacket(0, _Picture(_Chunk("VPT0", [0, 0])), 0, IsKeyFrame: true);

    var file = VideoIO.Mux<VqaWriter>([video, audio], [sound, frame]);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(70, 4)), Is.EqualTo(37u));
      Assert.That(file.AsSpan(74, 4).ToArray(), Is.EqualTo("SND2"u8.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(20 + 2, 2)) & 1, Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(20 + 24, 2)), Is.EqualTo(22050));
      Assert.That(file[20 + 26], Is.EqualTo(1));
      Assert.That(file[20 + 27], Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void HeaderFrameCountAndLargestPointerPayloadAreDerivedFromWrittenPackets() {
    var stream = _VideoStream();
    var first = new CodedPacket(0, _Picture(_Chunk("VPT0", new byte[10])), 0, IsKeyFrame: true);
    var second = new CodedPacket(0, _Picture(_Chunk("VPTZ", new byte[17])), 1, IsKeyFrame: true);

    var file = VideoIO.Mux<VqaWriter>([stream], [first, second]);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(20 + 4, 2)), Is.EqualTo(2));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(20 + 22, 2)), Is.EqualTo(17));
    });
  }

  [Test]
  [Category("Unit")]
  public void MalformedVideoPacketRefusesBeforeWritingAnIndex() {
    var stream = _VideoStream();
    var frame = new CodedPacket(0, new byte[] { (byte)'V', (byte)'P', (byte)'T' }, 0, IsKeyFrame: true);

    Assert.Throws<System.IO.InvalidDataException>(() => VideoIO.Mux<VqaWriter>([stream], [frame]));
  }

  private static MediaStreamInfo _VideoStream() {
    var header = new byte[42];
    BinaryPrimitives.WriteUInt16LittleEndian(header, 2);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 4);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), 2);
    header[10] = 4;
    header[11] = 2;
    header[12] = 15;
    header[13] = 8;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), 256);
    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("WSVQ"),
      Width = 4,
      Height = 2,
      BitsPerPixel = 8,
      TimeBase = new(1, 15),
      FrameRate = new(15, 1),
      CodecPrivateData = header,
    };
  }

  private static byte[] _Picture(params byte[][] chunks) {
    var length = 0;
    foreach (var chunk in chunks)
      length += chunk.Length;
    var result = new byte[length];
    var at = 0;
    foreach (var chunk in chunks) {
      chunk.CopyTo(result, at);
      at += chunk.Length;
    }
    return result;
  }

  private static byte[] _Chunk(string id, byte[] payload) {
    var result = new byte[8 + payload.Length + (payload.Length & 1)];
    System.Text.Encoding.ASCII.GetBytes(id).CopyTo(result, 0);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)payload.Length);
    payload.CopyTo(result, 8);
    return result;
  }
}
