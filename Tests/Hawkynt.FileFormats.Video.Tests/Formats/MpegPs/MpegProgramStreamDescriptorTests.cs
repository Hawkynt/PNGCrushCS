using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.MpegPs.Tests;

[TestFixture]
public sealed class MpegProgramStreamDescriptorTests {

  private static readonly byte[] _DESCRIPTORS = [
    0x05, 0x04, 0x54, 0x45, 0x53, 0x54, // registration_descriptor("TEST")
    0xEE, 0x02, 0x12, 0x34,             // deliberately unknown extension, retained opaquely
  ];

  [Test]
  [Category("Unit")]
  public void Reader_PreservesElementaryDescriptorLoopOutsideCodecPrivateData() {
    var file = _ProgramStream(0x01, _DESCRIPTORS);

    var stream = MpegProgramStreamContainer.Streams(MpegProgramStreamReader.FromBytes(file)).Single();

    Assert.That(stream.Codec.ToString(), Is.EqualTo("mpg1"));
    Assert.That(stream.ContainerPrivateData.ToArray(), Is.EqualTo(_DESCRIPTORS));
    Assert.That(stream.CodecPrivateData.IsEmpty, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void Reader_FutureProgramStreamMapDoesNotReplaceCurrentDeclaration() {
    var file = _ProgramStream(0x01, _DESCRIPTORS, current: false);

    var stream = MpegProgramStreamContainer.Streams(MpegProgramStreamReader.FromBytes(file)).Single();

    // The pack is MPEG-2, so without a current PSM the truthful fallback remains MPEG-2 video.
    Assert.That(stream.Codec.ToString(), Is.EqualTo("mpg2"));
    Assert.That(stream.ContainerPrivateData.IsEmpty, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void Reader_ProgramDescriptorsAreRefusedRatherThanSilentlyDiscarded() {
    byte[] programDescriptors = [0x05, 0x04, 0x50, 0x52, 0x4F, 0x47];
    var file = _ProgramStream(0x01, _DESCRIPTORS, programDescriptors);

    var failure = Assert.Throws<NotSupportedException>(() => MpegProgramStreamReader.FromBytes(file));

    Assert.That(failure!.Message, Does.Contain("programme descriptors").And.Contain("VideoMetadata"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_ZeroProgramStreamMapMarkerBitIsMalformed() {
    var file = _ProgramStream(0x01, _DESCRIPTORS, marker: 0xFE);

    var failure = Assert.Throws<InvalidDataException>(() => MpegProgramStreamReader.FromBytes(file));

    Assert.That(failure!.Message, Does.Contain("marker bit"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_ProgramDescriptorLengthPastMapIsMalformed() {
    var file = _ProgramStream(0x01, [], declaredProgramInfoLength: 0x40);

    var failure = Assert.Throws<InvalidDataException>(() => MpegProgramStreamReader.FromBytes(file));

    Assert.That(failure!.Message, Does.Contain("program descriptor loop").And.Contain("run past"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_ElementaryMapLengthPastMapIsMalformed() {
    var file = _ProgramStream(0x01, [], declaredElementaryMapLength: 0x40);

    var failure = Assert.Throws<InvalidDataException>(() => MpegProgramStreamReader.FromBytes(file));

    Assert.That(failure!.Message, Does.Contain("elementary-stream map").And.Contain("run past"));
  }

  [Test]
  [Category("Unit")]
  public void Reader_ElementaryDescriptorLengthPastEntryIsMalformed() {
    var file = _ProgramStream(0x01, [0xEE, 0x00], declaredStreamInfoLength: 0x20);

    var failure = Assert.Throws<InvalidDataException>(() => MpegProgramStreamReader.FromBytes(file));

    Assert.That(failure!.Message, Does.Contain("descriptor loop").And.Contain("only"));
  }

  [Test]
  [Category("Unit")]
  public void Writer_WritesAndRoundTripsElementaryDescriptorBytesExactly() {
    var stream = _Stream(0, MediaStreamKind.Video, "mpg1", _DESCRIPTORS);

    var file = VideoIO.Mux<MpegProgramStreamWriter>([stream], [new CodedPacket(0, _Picture())]);
    var read = MpegProgramStreamContainer.Streams(MpegProgramStreamReader.FromBytes(file)).Single();

    // Canonical 14-byte pack, then PSM. Its elementary entry starts twelve bytes into the PSM.
    Assert.That(file[14 + 12], Is.EqualTo(0x01));
    Assert.That(file[14 + 13], Is.EqualTo(0xE0));
    Assert.That(file[14 + 14], Is.Zero);
    Assert.That(file[14 + 15], Is.EqualTo(_DESCRIPTORS.Length));
    Assert.That(file.AsSpan(14 + 16, _DESCRIPTORS.Length).ToArray(), Is.EqualTo(_DESCRIPTORS));
    Assert.That(read.ContainerPrivateData.ToArray(), Is.EqualTo(_DESCRIPTORS));
  }

  [Test]
  [Category("Unit")]
  public void Writer_SharedPrivateStreamIdWithIdenticalDescriptorsIsRepresentable() {
    var ac3 = _Stream(0, MediaStreamKind.Audio, "ac-3", _DESCRIPTORS);
    var dts = _Stream(1, MediaStreamKind.Audio, "dts ", _DESCRIPTORS);

    var file = VideoIO.Mux<MpegProgramStreamWriter>(
      [ac3, dts],
      [new CodedPacket(0, new byte[] { 1, 2 }), new CodedPacket(1, new byte[] { 3, 4 })]);
    var streams = MpegProgramStreamContainer.Streams(MpegProgramStreamReader.FromBytes(file));

    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams.All(stream => stream.ContainerPrivateData.Span.SequenceEqual(_DESCRIPTORS)), Is.True);
    // One elementary-map declaration: 4-byte entry header plus the descriptor loop.
    Assert.That((file[14 + 10] << 8) | file[14 + 11], Is.EqualTo(4 + _DESCRIPTORS.Length));
  }

  [Test]
  [Category("Unit")]
  public void Writer_SharedPrivateStreamIdWithDifferentDescriptorsIsRefused() {
    // byte[] rather than a collection expression: ReadOnlyMemory<byte> has no collection
    // initializer to target (CS9174), it just converts from an array.
    var ac3 = _Stream(0, MediaStreamKind.Audio, "ac-3", new byte[] { 0xEE, 0x00 });
    var dts = _Stream(1, MediaStreamKind.Audio, "dts ", new byte[] { 0xEF, 0x00 });

    var failure = Assert.Throws<NotSupportedException>(
      () => VideoIO.Mux<MpegProgramStreamWriter>([ac3, dts], Array.Empty<CodedPacket>()));

    Assert.That(failure!.Message, Does.Contain("0xBD").And.Contain("descriptor loops differ"));
  }

  [Test]
  [Category("Unit")]
  public void Writer_CodecPrivateDataIsRefusedRatherThanDiscarded() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("mpg1"),
      TimeBase = new Rational(1, 90_000),
      CodecPrivateData = new byte[] { 1, 2, 3 },
    };

    var failure = Assert.Throws<NotSupportedException>(
      () => VideoIO.Mux<MpegProgramStreamWriter>([stream], Array.Empty<CodedPacket>()));

    Assert.That(failure!.Message, Does.Contain("codec-private data").And.Contain("elementary-stream bytes"));
  }

  [Test]
  [Category("Unit")]
  public void Writer_DescriptorLoopAtProgramStreamMapLimitIsAccepted() {
    var descriptors = _DescriptorLoop(1004);
    var stream = _Stream(0, MediaStreamKind.Video, "mpg1", descriptors);

    var file = VideoIO.Mux<MpegProgramStreamWriter>([stream], Array.Empty<CodedPacket>());

    // 10 fixed PSM bytes + 4-byte elementary declaration + 1004 descriptor bytes = 1018.
    Assert.That((file[18] << 8) | file[19], Is.EqualTo(0x03FA));
  }

  [Test]
  [Category("Unit")]
  public void Writer_DescriptorLoopOneBytePastProgramStreamMapLimitIsRefused() {
    var descriptors = _DescriptorLoop(1005);
    var stream = _Stream(0, MediaStreamKind.Video, "mpg1", descriptors);

    var failure = Assert.Throws<NotSupportedException>(
      () => VideoIO.Mux<MpegProgramStreamWriter>([stream], Array.Empty<CodedPacket>()));

    Assert.That(failure!.Message, Does.Contain("1018-byte").And.Contain("H.222.0"));
  }

  private static MediaStreamInfo _Stream(
    int index,
    MediaStreamKind kind,
    string codec,
    ReadOnlyMemory<byte> descriptors)
    => new() {
      Index = index,
      Kind = kind,
      Codec = CodecTag.FromCharacters(codec),
      TimeBase = new Rational(1, 90_000),
      ContainerPrivateData = descriptors,
    };

  private static byte[] _ProgramStream(
    byte streamType,
    byte[] descriptors,
    byte[]? programDescriptors = null,
    bool current = true,
    byte marker = 0xFF,
    int? declaredProgramInfoLength = null,
    int? declaredElementaryMapLength = null,
    int? declaredStreamInfoLength = null) {
    programDescriptors ??= [];

    using var elementary = new MemoryStream();
    elementary.WriteByte(streamType);
    elementary.WriteByte(0xE0);
    _WriteUInt16(elementary, declaredStreamInfoLength ?? descriptors.Length);
    elementary.Write(descriptors);
    var elementaryBytes = elementary.ToArray();

    using var body = new MemoryStream();
    body.WriteByte(current ? (byte)0xE0 : (byte)0x60);
    body.WriteByte(marker);
    _WriteUInt16(body, declaredProgramInfoLength ?? programDescriptors.Length);
    body.Write(programDescriptors);
    _WriteUInt16(body, declaredElementaryMapLength ?? elementaryBytes.Length);
    body.Write(elementaryBytes);
    var bodyBytes = body.ToArray();

    using var map = new MemoryStream();
    map.Write([0x00, 0x00, 0x01, 0xBC]);
    _WriteUInt16(map, bodyBytes.Length + 4);
    map.Write(bodyBytes);
    var crc = _Crc(map.ToArray());
    map.WriteByte((byte)(crc >> 24));
    map.WriteByte((byte)(crc >> 16));
    map.WriteByte((byte)(crc >> 8));
    map.WriteByte((byte)crc);

    using var file = new MemoryStream();
    file.Write([
      0x00, 0x00, 0x01, 0xBA,
      0x44, 0x00, 0x04, 0x00, 0x04, 0x01, 0x00, 0x00, 0x03, 0xF8,
    ]);
    file.Write(map.ToArray());

    var picture = _Picture();
    file.Write([0x00, 0x00, 0x01, 0xE0]);
    _WriteUInt16(file, 3 + picture.Length);
    file.Write([0x80, 0x00, 0x00]);
    file.Write(picture);
    file.Write([0x00, 0x00, 0x01, 0xB9]);
    return file.ToArray();
  }

  private static byte[] _DescriptorLoop(int length) {
    if (length < 0 || length == 1)
      throw new ArgumentOutOfRangeException(nameof(length));

    using var output = new MemoryStream(length);
    var remaining = length;
    var tag = 0x80;
    while (remaining > 0) {
      if (remaining < 2)
        throw new InvalidOperationException("Descriptor loop cannot end in a single byte.");

      var payload = Math.Min(255, remaining - 2);
      // Avoid leaving a one-byte tail: shorten this descriptor by one so the next can have a header.
      if (remaining - payload - 2 == 1)
        --payload;

      output.WriteByte((byte)tag++);
      output.WriteByte((byte)payload);
      for (var i = 0; i < payload; ++i)
        output.WriteByte((byte)(i + tag));
      remaining -= payload + 2;
    }

    return output.ToArray();
  }

  private static byte[] _Picture() => [0x00, 0x00, 0x01, 0x00, 0x11, 0x22, 0x33, 0x44];

  private static void _WriteUInt16(Stream output, int value) {
    if ((uint)value > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(value));
    output.WriteByte((byte)(value >> 8));
    output.WriteByte((byte)(value & 0xFF));
  }

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
