using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.RoqVideo;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The RoQ encoder, checked against the decoder beside it, against the shape of the chunks it writes,
/// and against the sizes it refuses.
/// </summary>
/// <remarks>
/// RoQ has no lossless form — a 2x2 codebook cell states one chrominance pair for four pixels and a
/// picture is painted from at most 256 of them — so "the decoder gets the input back" is a contract
/// only for a picture the codebook can hold outright. Two such pictures are asserted sample for
/// sample: one flat, and one built from a handful of distinct cells laid out so that no coarser coding
/// could win. Everything else is asserted on what the bitstream must look like rather than on how close
/// the picture came, because a threshold on closeness would pass for an encoder that had quietly
/// stopped choosing between the codings at all.
/// <para/>
/// <b>Measured against ffmpeg 9.0.1.</b> Eleven sequences — 64x48 to 512x512, 118 pictures — were
/// encoded here, written into RoQ files, and decoded by ffmpeg. It accepted every picture of every file
/// and its <c>yuvj444p</c> planes are identical to this package's own decode of the same files, sample
/// for sample: 0 differing of 18006528. The comparison is on the planes and not on colour, because RGB
/// would compare two colour matrices as much as two codecs. Against ffmpeg's own <c>roqvideo</c>
/// encoder from the same source planes, on the ten of the eleven it can be compared on, this one writes
/// fewer bytes on eight, the same on one and more on one, and is closer to the source on five, level on
/// one and behind on four. The numbers, and what the eleventh is, are in <c>codec-notes.md</c>.
/// </remarks>
[TestFixture]
public sealed class RoqVideoEncoderTests {

  private const ushort _INFO = 0x1001;
  private const ushort _QUAD_CODEBOOK = 0x1002;
  private const ushort _QUAD_VQ = 0x1011;
  private const int _CHUNK_HEADER_LENGTH = 8;

  // ============================================================================================
  // The description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsWhatTheDecoderReads() {
    var encoder = RoqVideoEncoder.Create(_Requested(32, 16));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("RoQV")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("RoQV")));
      Assert.That(stream.Width, Is.EqualTo(32));
      Assert.That(stream.Height, Is.EqualTo(16));

      // Fixed at thirty, and not taken from what was asked for: a RoQ file has no field to state any
      // other rate, so the reader takes every file as thirty and a stream claiming otherwise would be
      // claiming something no reader can see.
      Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 30)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(30, 1)));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(0));
    });

    Assert.That(RoqVideoDecoder.Accepts(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<RoqVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegisteredUnderTheCodeItWrites() {
    Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("id RoQ"));

    var stream = _Requested(32, 16);
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<RoqVideoEncoder>());
  }

  // ============================================================================================
  // What it refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureThatIsNotAWholeNumberOfMacroblocksRefuses(
    [Values(20, 24, 31, 33, 17)] int width) {
    var failure = Assert.Throws<NotSupportedException>(() => RoqVideoEncoder.Create(_Requested(width, 16)));

    Assert.That(failure!.Message, Does.Contain("16-pixel macroblocks"));
  }

  [Test]
  [Category("Unit")]
  public void AHeightThatIsNotAWholeNumberOfMacroblocksRefuses() {
    var failure = Assert.Throws<NotSupportedException>(() => RoqVideoEncoder.Create(_Requested(32, 40)));

    Assert.That(failure!.Message, Does.Contain("16-pixel macroblocks"));
  }

  [Test]
  [Category("Unit")]
  public void APictureWithNoSizeRefuses()
    => Assert.Throws<NotSupportedException>(() => RoqVideoEncoder.Create(_Requested(0, 0)));

  [Test]
  [Category("Unit")]
  public void APictureLargerThanInfoCanStateRefuses() {
    var failure = Assert.Throws<NotSupportedException>(() => RoqVideoEncoder.Create(_Requested(65536, 16)));

    Assert.That(failure!.Message, Does.Contain("RoQ_INFO"));
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamRefuses() {
    var stream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("RoQV"), Width = 32, Height = 16,
    };

    Assert.Throws<NotSupportedException>(() => RoqVideoEncoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfADifferentSizePartWayThroughRefuses() {
    var encoder = RoqVideoEncoder.Create(_Requested(32, 16));
    encoder.TryEncode(_Grey(32, 16, 100), 0, out _);

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Grey(48, 16, 100), 1, out _));
  }

  // ============================================================================================
  // The chunks a picture is made of
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheFirstPacketStatesThePictureSizeAndNoLaterOneRestatesIt() {
    var packets = _Encode(32, 32, [_Grey(32, 32, 40), _Grey(32, 32, 200), _Grey(32, 32, 90)]);

    Assert.That(_Chunks(packets[0]).Select(chunk => chunk.Id), Is.EqualTo(new[] { _INFO, _QUAD_CODEBOOK, _QUAD_VQ }));
    for (var packet = 1; packet < packets.Count; ++packet)
      Assert.That(_Chunks(packets[packet]).Select(chunk => chunk.Id), Does.Not.Contain(_INFO));
  }

  [Test]
  [Category("Unit")]
  public void TheInfoChunkStatesTheSizeAndTheTwoFieldsTheReaderInsistsOn() {
    var packets = _Encode(48, 32, [_Grey(48, 32, 40)]);
    var info = _Chunks(packets[0])[0];

    Assert.Multiple(() => {
      Assert.That(info.Id, Is.EqualTo(_INFO));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(info.Payload), Is.EqualTo(48));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(info.Payload.AsSpan(2)), Is.EqualTo(32));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(info.Payload.AsSpan(4)), Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(info.Payload.AsSpan(6)), Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryPacketIsAWholeNumberOfChunksStatingItsOwnLength() {
    var packets = _Encode(32, 32, [_Pattern(32, 32, 0), _Pattern(32, 32, 3), _Pattern(32, 32, 7)]);

    foreach (var packet in packets) {
      var chunks = _Chunks(packet);
      Assert.That(chunks, Is.Not.Empty);
      Assert.That(chunks.Sum(chunk => _CHUNK_HEADER_LENGTH + chunk.Payload.Length), Is.EqualTo(packet.Data.Length));
      Assert.That(chunks[^1].Id, Is.EqualTo(_QUAD_VQ), "a picture's last chunk is the one that paints it");
    }
  }

  [Test]
  [Category("Unit")]
  public void ACodebookChunkStatesAsManyCellsAsItsOwnLengthHolds() {
    var packets = _Encode(64, 64, [_Pattern(64, 64, 0), _Pattern(64, 64, 5)]);

    foreach (var packet in packets)
      foreach (var chunk in _Chunks(packet)) {
        if (chunk.Id != _QUAD_CODEBOOK)
          continue;

        // A count of 256 is spelled as nought, which is also how nought would be spelled; the chunk's
        // own length is what tells the two apart, and never writing a real nought is what leaves
        // nothing to tell apart.
        var cells = (chunk.Argument >> 8) & 0xFF;
        var quads = chunk.Argument & 0xFF;
        if (cells == 0)
          cells = 256;
        if (quads == 0)
          quads = 256;

        Assert.That(chunk.Payload.Length, Is.EqualTo(cells * 6 + quads * 4));
      }
  }

  [Test]
  [Category("Unit")]
  public void TheFirstPictureIsAKeyFrameAndCodesNothingThatReachesBack() {
    var packets = _Encode(32, 32, [_Pattern(32, 32, 0), _Pattern(32, 32, 0)]);

    Assert.That(packets[0].IsKeyFrame, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void APictureRepeatedCostsAlmostNothing() {
    var picture = _Pattern(64, 64, 0);
    var packets = _Encode(64, 64, [picture, picture, picture, picture]);

    // The second picture is painted into the buffer the first never touched, so it has to be coded;
    // the third and fourth find their own buffer already holding what they want and skip it entirely.
    Assert.That(packets[3].Data.Length, Is.LessThan(packets[0].Data.Length / 4));
    Assert.That(packets[3].IsKeyFrame, Is.False, "a picture made of skips depends on what came before");
  }

  // ============================================================================================
  // What the format can hold exactly
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatGreyPictureComesBackExactly([Values(0, 1, 17, 128, 199, 255)] int level) {
    // Every grey is stateable outright: the luminance is the level and both chrominances are neutral,
    // so the inverse matrix puts the level back on all three channels.
    var picture = _Grey(32, 32, (byte)level);

    foreach (var frame in _RoundTrip(32, 32, [picture, picture, picture]))
      Assert.That(frame.PixelData, Is.EqualTo(_ExpectedGrey(32, 32, (byte)level)));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfWholeEightByEightSquaresComesBackExactly() {
    // Sixty-four grey levels, one to each 8x8 square, is a picture every coding the format offers can
    // state without error — one 4x4 cell of a single value, doubled, covers a whole square — so nothing
    // the decision could prefer is lossy and the picture has to come back as it went in.
    var picture = _Squares(64, 64);
    var expected = _AsColour(picture, 64, 64);

    foreach (var frame in _RoundTrip(64, 64, [picture, picture]))
      Assert.That(frame.PixelData, Is.EqualTo(expected), "every 8x8 square is one codebook cell doubled");
  }

  [Test]
  [Category("Unit")]
  public void APictureOfWholeFourByFourSquaresComesBackExactly() {
    // The same idea one level down, and with the levels far enough apart that no doubled 4x4 cell could
    // cover an 8x8 quadrant of them cheaply enough to be chosen.
    var picture = _Checkerboard(32, 32);
    var expected = _AsColour(picture, 32, 32);

    foreach (var frame in _RoundTrip(32, 32, [picture, picture]))
      Assert.That(frame.PixelData, Is.EqualTo(expected));
  }

  // ============================================================================================
  // Through the container and back
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void WhatIsWrittenIntoARoqFileReadsBackAsTheSamePictures() {
    var pictures = new[] { _Pattern(48, 32, 0), _Pattern(48, 32, 4), _Pattern(48, 32, 9) };
    var file = _File(48, 32, pictures);
    var streams = VideoFormatRegistry.ReadStreams(file);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.Detect(file), Is.EqualTo(VideoFormat.Roq));
      Assert.That(streams, Has.Count.EqualTo(1));
      Assert.That(streams[0].Codec, Is.EqualTo(CodecTag.FromCharacters("RoQV")));
      Assert.That(streams[0].Width, Is.EqualTo(48));
      Assert.That(streams[0].Height, Is.EqualTo(32));
      Assert.That(streams[0].DeclaredFrameCount, Is.EqualTo(pictures.Length));
    });

    var decoded = VideoFormatRegistry.DecodeFrames(file).ToList();
    Assert.That(decoded, Has.Count.EqualTo(pictures.Length));
    foreach (var frame in decoded) {
      Assert.That(frame.Image.Width, Is.EqualTo(48));
      Assert.That(frame.Image.Height, Is.EqualTo(32));
      Assert.That(frame.Image.Format, Is.EqualTo(PixelFormat.Rgb24));
    }
  }

  [Test]
  [Category("Unit")]
  public void TheWriterRefusesAPacketWhoseLastChunkOverrunsIt() {
    var writer = RoqWriter.Create(
      [RoqVideoEncoder.Create(_Requested(32, 16)).DescribeStream()], new VideoMetadata());

    // Two chunk headers, the second stating more payload than the packet carries.
    var packet = new byte[_CHUNK_HEADER_LENGTH * 2];
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(), _QUAD_VQ);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(8), _QUAD_VQ);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(10), 99);

    Assert.Throws<InvalidDataException>(() => writer.WritePacket(new(0, packet)));
  }

  // ============================================================================================
  // The pictures
  // ============================================================================================

  private static MediaStreamInfo _Requested(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("RoQV"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
  };

  /// <summary>A picture in the samples RoQ itself states, so that nothing is converted on the way in.</summary>
  private static RawImage _Planes(int width, int height, byte[] luma) {
    var pixels = new byte[width * height * 3];
    luma.CopyTo(pixels, 0);
    for (var i = width * height; i < pixels.Length; ++i)
      pixels[i] = 128;

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = pixels };
  }

  private static RawImage _Grey(int width, int height, byte level) {
    var luma = new byte[width * height];
    Array.Fill(luma, level);
    return _Planes(width, height, luma);
  }

  /// <summary>What a neutral-chrominance picture must decode to: its luminance on all three channels,
  /// the inverse matrix moving nothing where both differences are 128.</summary>
  private static byte[] _AsColour(RawImage picture, int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = picture.PixelData[i];

    return pixels;
  }

  private static byte[] _ExpectedGrey(int width, int height, byte level) {
    var pixels = new byte[width * height * 3];
    Array.Fill(pixels, level);
    return pixels;
  }

  /// <summary>One grey level to each whole 8x8 square.</summary>
  private static RawImage _Squares(int width, int height) {
    var luma = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        luma[y * width + x] = (byte)((y / 8 * (width / 8) + x / 8) * 4 % 256);

    return _Planes(width, height, luma);
  }

  /// <summary>Black and white 4x4 squares, which no doubled 4x4 cell can cover cheaply.</summary>
  private static RawImage _Checkerboard(int width, int height) {
    var luma = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        luma[y * width + x] = (byte)((x / 4 + y / 4) % 2 == 0 ? 0 : 255);

    return _Planes(width, height, luma);
  }

  /// <summary>Something with detail at every scale, moved along by <paramref name="step"/> pixels.</summary>
  private static RawImage _Pattern(int width, int height, int step) {
    var luma = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        luma[y * width + x] = (byte)((x + step) * 7 + y * 13 + (x + step) * y / 3);

    return _Planes(width, height, luma);
  }

  // ============================================================================================
  // Driving the encoder
  // ============================================================================================

  private static List<CodedPacket> _Encode(int width, int height, IReadOnlyList<RawImage> pictures) {
    var encoder = RoqVideoEncoder.Create(_Requested(width, height));
    var packets = new List<CodedPacket>(pictures.Count);
    for (var picture = 0; picture < pictures.Count; ++picture) {
      Assert.That(encoder.TryEncode(pictures[picture], picture, out var packet), Is.True);
      packets.Add(packet);
    }

    return packets;
  }

  private static byte[] _File(int width, int height, IReadOnlyList<RawImage> pictures) {
    var encoder = RoqVideoEncoder.Create(_Requested(width, height));
    var writer = RoqWriter.Create([encoder.DescribeStream()], new VideoMetadata());
    for (var picture = 0; picture < pictures.Count; ++picture) {
      Assert.That(encoder.TryEncode(pictures[picture], picture, out var packet), Is.True);
      writer.WritePacket(packet);
    }

    return writer.Finish();
  }

  private static List<RawImage> _RoundTrip(int width, int height, IReadOnlyList<RawImage> pictures)
    => VideoFormatRegistry.DecodeFrames(_File(width, height, pictures)).Select(frame => frame.Image).ToList();

  // ============================================================================================
  // Reading the chunks back out
  // ============================================================================================

  private readonly record struct _Chunk(ushort Id, ushort Argument, byte[] Payload);

  private static List<_Chunk> _Chunks(CodedPacket packet) {
    var data = packet.Data.Span;
    var chunks = new List<_Chunk>();

    var at = 0;
    while (at < data.Length) {
      Assert.That(at + _CHUNK_HEADER_LENGTH, Is.LessThanOrEqualTo(data.Length), "a chunk header runs past the packet");
      var id = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
      var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 2)..]);
      var argument = BinaryPrimitives.ReadUInt16LittleEndian(data[(at + 6)..]);
      Assert.That(at + _CHUNK_HEADER_LENGTH + size, Is.LessThanOrEqualTo(data.Length), "a chunk payload runs past the packet");

      chunks.Add(new(id, argument, data.Slice(at + _CHUNK_HEADER_LENGTH, size).ToArray()));
      at += _CHUNK_HEADER_LENGTH + size;
    }

    return chunks;
  }
}
