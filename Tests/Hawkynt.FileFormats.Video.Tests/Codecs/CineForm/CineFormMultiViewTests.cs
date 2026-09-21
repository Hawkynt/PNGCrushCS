using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using FileFormat.Avi;
using FileFormat.Codecs.CineForm;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;
using NUnit.Framework;

namespace FileFormat.Codecs.CineForm.Tests;

[TestFixture]
public sealed class CineFormMultiViewTests {
  private static readonly CodecTag _Cfhd = CodecTag.FromCharacters("CFHD");

  [Test]
  [Category("Unit")]
  public void StereoRoundTripPreservesNativeViewIdentityQualityAndDistinctPictures() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormMultiViewEncoder.Create(_Stream(WIDTH, HEIGHT));
    var source = new RawMultiViewImage([
      new(_Flat(WIDTH, HEIGHT, 220), 0, RawImageViewRole.Left, 1),
      new(_Flat(WIDTH, HEIGHT, 780), 1, RawImageViewRole.Right, 2),
    ]);

    Assert.That(encoder.TryEncode(source, 42, out var packet), Is.True);
    var samples = CineFormMultiViewFraming.Split(packet.Data);

    Assert.Multiple(() => {
      Assert.That(samples, Has.Count.EqualTo(2));
      Assert.That(samples[0].Info.ViewCount, Is.EqualTo(2));
      Assert.That(samples[0].Info.ViewNumber, Is.EqualTo(0));
      Assert.That(samples[0].Info.QualityRank, Is.EqualTo(1));
      Assert.That(samples[1].Info.ViewCount, Is.EqualTo(2));
      Assert.That(samples[1].Info.ViewNumber, Is.EqualTo(1));
      Assert.That(samples[1].Info.QualityRank, Is.EqualTo(2));
      Assert.That(samples[0].Sample.Length + samples[1].Sample.Length, Is.EqualTo(packet.Data.Length));
    });

    var decoder = CineFormMultiViewDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    var left = decoded.GetView(0);
    var right = decoded.GetView(1);
    Assert.Multiple(() => {
      Assert.That(left.Role, Is.EqualTo(RawImageViewRole.Left));
      Assert.That(right.Role, Is.EqualTo(RawImageViewRole.Right));
      Assert.That(left.QualityRank, Is.EqualTo(1));
      Assert.That(right.QualityRank, Is.EqualTo(2));
      Assert.That(left.Image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(right.Image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(right.Image.PixelData[0], Is.GreaterThan(left.Image.PixelData[0] + 80));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryEmbeddedViewRemainsACompleteOrdinaryCfhdSample() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormMultiViewEncoder.Create(_Stream(WIDTH, HEIGHT));
    var source = new RawMultiViewImage([
      new(_Flat(WIDTH, HEIGHT, 300), 0, RawImageViewRole.Left),
      new(_Flat(WIDTH, HEIGHT, 700), 1, RawImageViewRole.Right),
    ]);

    Assert.That(encoder.TryEncode(source, 3, out var packet), Is.True);
    var samples = CineFormMultiViewFraming.Split(packet.Data);
    var single = CineFormVideoDecoder.Create(encoder.DescribeStream());

    foreach (var (sample, info) in samples) {
      var subpacket = packet with { Data = sample };
      Assert.That(single.TryDecode(subpacket, out var image), Is.True, $"view {info.ViewNumber}");
      Assert.That(image.Width, Is.EqualTo(WIDTH));
      Assert.That(image.Height, Is.EqualTo(HEIGHT));
    }
  }

  [Test]
  [Category("Unit")]
  public void MulticamRequiresContiguousNativeViewNumbers() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormMultiViewEncoder.Create(_Stream(WIDTH, HEIGHT));
    var source = new RawMultiViewImage([
      new(_Flat(WIDTH, HEIGHT, 300), 0),
      new(_Flat(WIDTH, HEIGHT, 500), 2),
      new(_Flat(WIDTH, HEIGHT, 700), 4),
    ]);

    var error = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(source, 0, out _));
    Assert.That(error!.Message, Does.Contain("contiguous"));
  }

  [Test]
  [Category("Unit")]
  public void StereoRefusesSpatialRolesThatTheLegacyOrderingWouldChange() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormMultiViewEncoder.Create(_Stream(WIDTH, HEIGHT));
    var source = new RawMultiViewImage([
      new(_Flat(WIDTH, HEIGHT, 300), 0, RawImageViewRole.Right),
      new(_Flat(WIDTH, HEIGHT, 700), 1, RawImageViewRole.Left),
    ]);

    Assert.Throws<NotSupportedException>(() => encoder.TryEncode(source, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void SplitRejectsDuplicateEncodedViewNumbers() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormMultiViewEncoder.Create(_Stream(WIDTH, HEIGHT));
    var source = new RawMultiViewImage([
      new(_Flat(WIDTH, HEIGHT, 300), 0, RawImageViewRole.Left),
      new(_Flat(WIDTH, HEIGHT, 700), 1, RawImageViewRole.Right),
    ]);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var bytes = packet.Data.ToArray();
    var first = CineFormMultiViewFraming.Inspect(bytes);
    _PatchHeaderTag(bytes.AsSpan(first.Length), CineFormTags.EncodedViewNumber, 0);

    Assert.Throws<InvalidDataException>(() => CineFormMultiViewFraming.Split(bytes));
  }

  [Test]
  [Category("Unit")]
  public void MulticamPreservesNumbersAndQualityWithoutInventingStereoRoles() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormMultiViewEncoder.Create(_Stream(WIDTH, HEIGHT));
    var source = new RawMultiViewImage([
      new(_Flat(WIDTH, HEIGHT, 250), 0, QualityRank: 2),
      new(_Flat(WIDTH, HEIGHT, 500), 1, QualityRank: 1),
      new(_Flat(WIDTH, HEIGHT, 750), 2, QualityRank: 3),
    ]);

    Assert.That(encoder.TryEncode(source, null, out var packet), Is.True);
    var decoder = CineFormMultiViewDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(decoded.Views, Has.Count.EqualTo(3));
      Assert.That(decoded.GetView(0).QualityRank, Is.EqualTo(2));
      Assert.That(decoded.GetView(1).QualityRank, Is.EqualTo(1));
      Assert.That(decoded.GetView(2).QualityRank, Is.EqualTo(3));
      Assert.That(decoded.Views, Has.All.Property(nameof(RawImageView.Role)).EqualTo(RawImageViewRole.Unspecified));
    });
  }

  /// <summary>
  /// Holds the framing against something outside this repository, which the round-trip tests above
  /// cannot do: they run our writer into our reader, so any misplaced byte the two agree on is
  /// invisible to them.
  /// </summary>
  /// <remarks>
  /// ffmpeg cannot be asked about the whole packet. Its <c>cfhd</c> decoder has no CineForm 3-D or
  /// multicam support at all -- it does not know tags 92-94, and returns one frame per packet -- so a
  /// concatenation of complete samples makes it try to allocate a second picture into a buffer it is
  /// already holding, and it fails. That is a documented gap in ffmpeg, not a verdict on the framing,
  /// and asserting on it here would pin this suite to a limitation we would rather see removed.
  /// <para/>
  /// What ffmpeg can settle is the part this codec actually claims: that a multi-view packet is outer
  /// framing around complete, ordinary CFHD samples, and that the boundaries between them come from
  /// the channel-size index rather than from hunting the entropy-coded payload for bytes that look
  /// like markers. So each view is split out and handed to ffmpeg on its own. A boundary off by even
  /// one byte, a group trailer landing in the wrong place, or view tags inserted somewhere that
  /// disturbs ordinary tag parsing all turn into an ffmpeg that either refuses the sample or returns
  /// the wrong picture. Four views rather than two, because two views cannot show that the ordinals
  /// and the pictures stay paired.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  public void FfmpegDecodesEveryViewSplitOutOfAMulticamPacket() {
    FFmpegOracle.RequireAvailable();

    const int WIDTH = 64;
    const int HEIGHT = 48;
    ushort[] lumas = [180, 420, 660, 900];

    var encoder = CineFormMultiViewEncoder.Create(_Stream(WIDTH, HEIGHT));
    var source = new RawMultiViewImage([
      new(_Flat(WIDTH, HEIGHT, lumas[0]), 0, QualityRank: 1),
      new(_Flat(WIDTH, HEIGHT, lumas[1]), 1, QualityRank: 2),
      new(_Flat(WIDTH, HEIGHT, lumas[2]), 2, QualityRank: 3),
      new(_Flat(WIDTH, HEIGHT, lumas[3]), 3, QualityRank: 4),
    ]);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var samples = CineFormMultiViewFraming.Split(packet.Data);
    Assert.That(samples, Has.Count.EqualTo(lumas.Length));

    var stream = encoder.DescribeStream();
    Assert.Multiple(() => {
      for (var i = 0; i < samples.Count; ++i) {
        var (sample, info) = samples[i];
        Assert.That(info.ViewNumber, Is.EqualTo(i), $"view {i} ordinal");

        var raw = _DecodeLoneViewWithFfmpeg(stream, packet with { Data = sample }, WIDTH, HEIGHT);
        Assert.That(
          BinaryPrimitives.ReadUInt16LittleEndian(raw),
          Is.EqualTo(lumas[i]),
          $"ffmpeg decoded view {i} to the wrong picture");
      }
    });
  }

  private static byte[] _DecodeLoneViewWithFfmpeg(MediaStreamInfo stream, CodedPacket view, int width, int height) {
    var avi = VideoIO.Mux<AviWriter>([stream], [view]);
    var inputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    var outputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".raw");

    try {
      File.WriteAllBytes(inputPath, avi);
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-i", inputPath,
        "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "yuv422p10le", outputPath,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo);
      Assert.That(process, Is.Not.Null, "ffmpeg would not start");
      var stdout = process!.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(60_000)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        Assert.Fail("ffmpeg timed out while decoding a CineForm multi-view sample");
      }

      var diagnostics = string.Concat(stdout.Result, stderr.Result).Trim();
      Assert.That(process.ExitCode, Is.Zero, diagnostics);
      Assert.That(diagnostics, Is.Empty, diagnostics);

      var raw = File.ReadAllBytes(outputPath);
      Assert.That(raw, Has.Length.EqualTo(width * height * 2 * 2));
      return raw;
    } finally {
      try { File.Delete(inputPath); } catch { /* best effort */ }
      try { File.Delete(outputPath); } catch { /* best effort */ }
    }
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    // Zero, because these go through the AVI writer and an AVI's stream index is not a label: it is
    // written into every chunk identifier, so the streams have to run densely from nought.
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = _Cfhd,
    Handler = _Cfhd,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Flat(int width, int height, ushort y) {
    const ushort CHROMA = 512;
    var chromaWidth = width / 2;
    var data = new byte[(width * height + chromaWidth * height * 2) * 2];
    var offset = 0;

    for (var i = 0; i < width * height; ++i) {
      BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), y);
      offset += 2;
    }
    for (var plane = 0; plane < 2; ++plane)
    for (var i = 0; i < chromaWidth * height; ++i) {
      BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), CHROMA);
      offset += 2;
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv422P10,
      PixelData = data,
      ColorInfo = RawImageColorInfo.Bt709Limited,
    };
  }

  private static void _PatchHeaderTag(Span<byte> sample, int wantedTag, ushort replacement) {
    var position = 0;
    while (position + 4 <= sample.Length) {
      var tag = BinaryPrimitives.ReadInt16BigEndian(sample[position..]);
      var value = BinaryPrimitives.ReadUInt16BigEndian(sample[(position + 2)..]);
      if (tag == wantedTag) {
        BinaryPrimitives.WriteUInt16BigEndian(sample[(position + 2)..], replacement);
        return;
      }

      position += 4;
      if (tag == 2) {
        position += checked(value * 4);
        continue;
      }
      if (tag == 4 && value == 0x1A4A)
        break;
    }

    Assert.Fail($"CineForm sample did not contain header tag {wantedTag}.");
  }
}
