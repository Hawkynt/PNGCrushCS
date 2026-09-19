using System;
using System.Collections.Generic;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs.H261.Tests;

/// <summary>
/// Freeze Picture Release, bit 3 of PTYPE (ITU-T H.261 clause 4.2.1.3), as the entry point of a stream.
/// </summary>
/// <remarks>
/// H.261 states no picture-level intra/inter flag — clause 3.2 puts that choice on every macroblock's
/// own MTYPE — so nothing in a picture header says "you may start decoding here" except this bit, set
/// by an encoder answering a fast update request to tell a decoder it may come out of freeze picture
/// mode. Every decoder that reports a key frame for H.261 at all reports this one; ffmpeg's own encoder
/// writes it for exactly its intra pictures and ffmpeg's decoder hands it back as the decoded frame's
/// key-frame flag. Written clear on every picture, as it was here, a clip has no entry point anything
/// outside this library can find, and the packet's own <see cref="CodedPacket.IsKeyFrame"/> says
/// something the bytes it carries do not.
/// </remarks>
[TestFixture]
public sealed class H261FreezePictureReleaseTests {

  /// <summary>How often the encoder codes an intra picture; the boundary the cases below sit on.</summary>
  private const int _KEY_FRAME_INTERVAL = 12;

  [Test]
  [Category("Unit")]
  public void TheFirstPictureOfAStreamAnnouncesItself() {
    var packets = _Encode(1);

    Assert.That(_FreezePictureRelease(packets[0]), Is.True);
  }

  /// <summary>The picture either side of the intra interval, which is where the choice can go wrong.</summary>
  [TestCase(_KEY_FRAME_INTERVAL - 1, false)]
  [TestCase(_KEY_FRAME_INTERVAL, true)]
  [Category("Unit")]
  public void OnlyAnIntraPictureAnnouncesItself(int index, bool expected) {
    var packets = _Encode(_KEY_FRAME_INTERVAL + 1);

    Assert.That(_FreezePictureRelease(packets[index]), Is.EqualTo(expected));
  }

  /// <summary>
  /// The invariant the two halves have to keep between them: a packet this encoder calls a key frame
  /// is a picture the bitstream says is one, and no other picture is.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void WhatThePacketCallsAKeyFrameIsWhatTheBitstreamSaysItIs() {
    var packets = _Encode(2 * _KEY_FRAME_INTERVAL + 1);

    Assert.Multiple(() => {
      foreach (var packet in packets)
        Assert.That(_FreezePictureRelease(packet), Is.EqualTo(packet.IsKeyFrame));
    });
  }

  /// <summary>And the rest of PTYPE is where it was: a bit added to one field is a bit taken from another.</summary>
  [TestCase(176, 144, false)]
  [TestCase(352, 288, true)]
  [Category("Unit")]
  public void TheRestOfPtypeIsUnmoved(int width, int height, bool isCif) {
    var packet = _Encode(1, width, height)[0];
    var bits = new int[7];

    {
      var reader = _AtPtype(packet);
      for (var index = 0; index < bits.Length; ++index)
        bits[index] = reader.ReadBit();
    }

    // Clause 4.2.1.3's six bits of PTYPE in order, then clause 4.2.1.4's PEI.
    Assert.That(bits, Is.EqualTo(new[] { 0, 0, 1, isCif ? 1 : 0, 1, 1, 0 }));
  }

  private static bool _FreezePictureRelease(CodedPacket packet) {
    var reader = _AtPtype(packet);
    reader.ReadBits(2);

    return reader.ReadBit() == 1;
  }

  /// <summary>A reader positioned on the first bit of PTYPE, having checked what precedes it.</summary>
  private static H263BitReader _AtPtype(CodedPacket packet) {
    var reader = new H263BitReader(packet.Data.Span);
    Assert.That(reader.ReadBits(H261PictureHeader.StartCodeLength), Is.EqualTo(H261PictureHeader.StartCode));
    reader.ReadBits(5);

    return reader;
  }

  private static List<CodedPacket> _Encode(int pictures, int width = 176, int height = 144) {
    var encoder = H261VideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("H261"),
      Width = width,
      Height = height,
      TimeBase = new(1, 25),
      FrameRate = new(25, 1),
    });

    var packets = new List<CodedPacket>();
    for (var index = 0; index < pictures; ++index) {
      Assert.That(encoder.TryEncode(_Picture(width, height, index), index, out var packet), Is.True);
      packets.Add(packet);
    }

    return packets;
  }

  /// <summary>A picture that moves, so the encoder has a reason to code the ones between intra pictures inter.</summary>
  private static RawImage _Picture(int width, int height, int index) {
    var planes = new byte[width * height * 3 / 2];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      planes[y * width + x] = (byte)(16 + (x + y + 4 * index) % 200);

    planes.AsSpan(width * height).Fill(128);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = planes,
    };
  }
}
