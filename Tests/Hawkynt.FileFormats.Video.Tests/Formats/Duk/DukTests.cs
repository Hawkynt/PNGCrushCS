using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileFormat.Codecs;
using FileFormat.Core;
using FileFormat.Duk;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Duk.Tests;

/// <summary>
/// Star Control II's four-file 3DO cutscenes and their pictures, against NihAV's <c>na_game_tool</c>.
/// </summary>
/// <remarks>
/// No film of the format may be redistributed — the cutscenes exist only on the commercial 3DO disc —
/// so the tests build one: a seeded film of both layouts and both header versions, with odd and even
/// deltas, codebook vectors of every length up to fourteen and junk in the bytes no vector uses. The
/// expected pictures are SHA-256 hashes of what <c>na_game_tool</c> 0.6.0 (<c>-ifmt duck-uqm ... -ofmt
/// avi</c>) wrote for exactly that film: each picture's raw 16-bit words, bottom row first, as its AVI
/// stores them. The opt-in test reruns the tool itself where <c>NA_GAME_TOOL</c> names it. The writer
/// puts that same film back byte for byte, so what na_game_tool read is also what the writer writes.
/// </remarks>
[TestFixture]
public sealed class DukTests {

  /// <summary>Captured from na_game_tool 0.6.0 for <see cref="_Film"/>; regenerate with the opt-in test.</summary>
  private static readonly string[] _Expected = [
    "AE44BC43C318651EADEA9C654FE8CB972B74FD1D5798015AB75B0917DA8A5E07",
    "96EA20C2F669B17D44ECB8F9BEC3DF10E146676F720B21600FE3BAC618ECFC77",
    "EDAC7913936E7E6F8586E1FB35137355F543BA0A1D2A18940BDBD9B8B172A53B",
    "889444B419A9B66EBB7B27982311F69D3696ECB20CF7D6F6D9C67D08A06589DB",
    "48D076AC822C96623632F118B0E44F0A8B233E9C3561C14EF27DAEB6A6619015",
    "A22CDBEB384C5D9711B11B58F62F4CE6C060E3DE2E99DB25110AF28888D68CDB",
  ];

  [Test]
  [Category("Unit")]
  public void TheFourFilesOpenWhateverTheirCaseAndNameTwoStreams() {
    var directory = _Write();
    try {
      var container = DukContainer.FromFile(new(Path.Combine(directory, "Film.hdr")));
      var streams = DukContainer.Streams(container);
      Assert.Multiple(() => {
        Assert.That(streams, Has.Count.EqualTo(2));
        Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
        Assert.That((streams[0].Width, streams[0].Height), Is.EqualTo((_WIDTH, _HEIGHT)));
        Assert.That(streams[0].FrameRate, Is.EqualTo(new Rational(11025, 754)));
        Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
        Assert.That((streams[1].SampleRate, streams[1].Channels), Is.EqualTo((22050, 2)));
        Assert.That(VideoFormatRegistry.CreateDecoder(streams[0]), Is.InstanceOf<DukVideoDecoder>());
      });
    } finally {
      Directory.Delete(directory, true);
    }
  }

  [Test]
  [Category("Unit")]
  public void SoundAndPictureAlternateAsTheChunksHoldThem() {
    var film = _Film();
    var container = DukContainer.Open(film.Duk, film.Hdr, film.Frm, film.Tbl);
    var packets = DukContainer.ReadPackets(container).ToList();
    Assert.That(packets, Has.Count.EqualTo(2 * film.Sounds.Count));
    for (var i = 0; i < film.Sounds.Count; ++i) {
      Assert.That(packets[2 * i].StreamIndex, Is.EqualTo(1));
      Assert.That(packets[2 * i].Data.ToArray(), Is.EqualTo(film.Sounds[i]));
      Assert.That(packets[2 * i + 1].StreamIndex, Is.EqualTo(0));
      Assert.That(packets[2 * i + 1].Data.ToArray(), Is.EqualTo(film.Pictures[i]));
    }
  }

  [Test]
  [Category("Unit")]
  public void TheWriterPutsAFilmBackByteForByte() {
    var film = _Film();
    var container = DukContainer.Open(film.Duk, film.Hdr, film.Frm, film.Tbl);
    var writer = DukWriter.Create(DukContainer.Streams(container), VideoMetadata.Empty);
    foreach (var packet in DukContainer.ReadPackets(container))
      writer.WritePacket(packet);
    Assert.Multiple(() => {
      Assert.That(writer.Finish(), Is.EqualTo(film.Duk));
      Assert.That(writer.Header, Is.EqualTo(film.Hdr));
      Assert.That(writer.FrameTable, Is.EqualTo(film.Frm));
      Assert.That(writer.Codebook, Is.EqualTo(film.Tbl));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureWithoutSoundGetsASilentSampleAndTheFilesReadBack() {
    var film = _Film();
    var container = DukContainer.Open(film.Duk, film.Hdr, film.Frm, film.Tbl);
    var video = DukContainer.Streams(container)[0];
    var writer = DukWriter.Create([video], VideoMetadata.Empty);
    foreach (var picture in film.Pictures)
      writer.WritePacket(new(0, picture));
    var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(directory);
    try {
      writer.WriteFiles(new(Path.Combine(directory, "silent.duk")));
      var back = DukContainer.FromFile(new(Path.Combine(directory, "silent.duk")));
      var packets = DukContainer.ReadPackets(back).ToList();
      Assert.That(packets.Where(p => p.StreamIndex == 0).Select(p => p.Data.ToArray()), Is.EqualTo(film.Pictures));
      Assert.That(packets.Where(p => p.StreamIndex == 1).All(p => p.Data.Length == 11), Is.True);
    } finally {
      Directory.Delete(directory, true);
    }
  }

  [Test]
  [Category("Unit")]
  public void TheBytesAloneAreRefusedForWantOfTheirCompanions()
    => Assert.Throws<NotSupportedException>(() => DukContainer.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void AMissingCompanionIsNamed() {
    var directory = _Write();
    try {
      File.Delete(Directory.GetFiles(directory, "*.tbl", SearchOption.TopDirectoryOnly).Concat(Directory.GetFiles(directory, "*.TBL")).First());
      var ex = Assert.Throws<FileNotFoundException>(() => DukContainer.FromFile(new(Path.Combine(directory, "FILM.DUK"))));
      Assert.That(ex!.Message, Does.Contain(".tbl"));
    } finally {
      Directory.Delete(directory, true);
    }
  }

  [Test]
  [Category("Conformance")]
  public void EveryPictureIsTheOneNaGameToolDecodes() {
    var hashes = _OurHashes();
    Assert.That(hashes, Is.EqualTo(_Expected));
  }

  [Test]
  [Category("Unit")]
  public void PicturesComeOutAsRgb24OfTheFilmsSize() {
    var film = _Film();
    var container = DukContainer.Open(film.Duk, film.Hdr, film.Frm, film.Tbl);
    var decoder = DukVideoDecoder.Create(DukContainer.Streams(container)[0]);
    Assert.That(decoder.TryDecode(new(0, film.Pictures[0]), out var picture), Is.True);
    Assert.That((picture.Width, picture.Height, picture.Format), Is.EqualTo((_WIDTH, _HEIGHT, PixelFormat.Rgb24)));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfTheWrongSizeIsRefused() {
    var film = _Film();
    var container = DukContainer.Open(film.Duk, film.Hdr, film.Frm, film.Tbl);
    var decoder = DukVideoDecoder.Create(DukContainer.Streams(container)[0]);
    var wrong = (byte[])film.Pictures[0].Clone();
    BinaryPrimitives.WriteUInt16LittleEndian(wrong.AsSpan(14), _WIDTH / 2 + 4);
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, wrong), out _));
  }

  /// <summary>Opt-in: point <c>NA_GAME_TOOL</c> at a built na_game_tool; prints the hashes to paste into <see cref="_Expected"/>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs na_game_tool; point NA_GAME_TOOL at it.")]
  public void NaGameToolDecodesTheFilmAsThisDecoderDoes() {
    var tool = Environment.GetEnvironmentVariable("NA_GAME_TOOL");
    if (string.IsNullOrEmpty(tool) || !File.Exists(tool))
      Assert.Ignore("NA_GAME_TOOL does not name na_game_tool.");
    var directory = _Write();
    try {
      var avi = Path.Combine(directory, "out.avi");
      using (var process = Process.Start(new ProcessStartInfo(tool, ["-ifmt", "duck-uqm", Path.Combine(directory, "FILM.DUK"), "-ofmt", "avi", avi]) { RedirectStandardOutput = true, RedirectStandardError = true })!) {
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
      }

      var bytes = File.ReadAllBytes(avi);
      var stream = VideoFormatRegistry.ReadStreams(bytes).First(s => s.Kind == MediaStreamKind.Video);
      var theirs = VideoFormatRegistry.ReadPackets(bytes, stream.Index).Select(p => Convert.ToHexString(SHA256.HashData(p.Data.Span))).ToList();
      foreach (var hash in theirs)
        TestContext.Out.WriteLine($"    \"{hash}\",");
      Assert.That(_OurHashes(), Is.EqualTo(theirs));
    } finally {
      Directory.Delete(directory, true);
    }
  }

  // ==========================================================================================

  private const int _WIDTH = 96;
  private const int _HEIGHT = 64;

  private sealed record Film(byte[] Duk, byte[] Hdr, byte[] Frm, byte[] Tbl, List<byte[]> Sounds, List<byte[]> Pictures);

  /// <summary>Each picture as its AVI from na_game_tool stores it: 16-bit words, bottom row first.</summary>
  private static List<string> _OurHashes() {
    var film = _Film();
    var container = DukContainer.Open(film.Duk, film.Hdr, film.Frm, film.Tbl);
    var decoder = DukVideoDecoder.Create(DukContainer.Streams(container)[0]);
    var hashes = new List<string>();
    foreach (var picture in film.Pictures) {
      var words = decoder.DecodeWords(picture);
      var raw = new byte[_WIDTH * _HEIGHT * 2];
      var stride = _WIDTH / 2;
      for (var y = 0; y < _HEIGHT; ++y)
        for (var x = 0; x < stride; ++x) {
          var word = words[y * stride + x];
          var at = ((_HEIGHT - 1 - y) * _WIDTH + 2 * x) * 2;
          BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(at), (ushort)(word >> 16));
          BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(at + 2), (ushort)word);
        }
      hashes.Add(Convert.ToHexString(SHA256.HashData(raw)));
    }

    return hashes;
  }

  /// <summary>The seeded film: six chunks, the layouts alternating, every codebook byte random where it is unused.</summary>
  private static Film _Film() {
    var random = new Random(1381);
    var hdr = new byte[DukContainer.HeaderLength];
    BinaryPrimitives.WriteUInt32BigEndian(hdr, 3);
    BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(12), _WIDTH / 8);
    BinaryPrimitives.WriteUInt16BigEndian(hdr.AsSpan(14), _HEIGHT / 8);
    short[] luma = [-21, -8, -3, 0, 1, 4, 9, 30], chroma = [-6, -3, -1, 0, 1, 2, 5, 7];
    for (var i = 0; i < 8; ++i) {
      BinaryPrimitives.WriteInt16BigEndian(hdr.AsSpan(16 + 2 * i), luma[i]);
      BinaryPrimitives.WriteInt16BigEndian(hdr.AsSpan(32 + 2 * i), chroma[i]);
    }

    var tbl = new byte[DukContainer.CodebookLength];
    random.NextBytes(tbl);
    for (var v = 0; v < 256; ++v) {
      var size = v % 15; // every length from nought to fourteen
      tbl[16 * v] = (byte)size;
      var used = Math.Max(2, (size + 1) & ~1);
      for (var i = 1; i <= used; ++i)
        tbl[16 * v + i] = (byte)random.Next(8);
    }

    var duk = new MemoryStream();
    var offsets = new List<uint>();
    var sounds = new List<byte[]>();
    var pictures = new List<byte[]>();
    for (var chunk = 0; chunk < 6; ++chunk) {
      var samples = 40 + chunk;
      var sound = new byte[10 + samples];
      sound[0] = 0xF7;
      sound[1] = 0x7F;
      BinaryPrimitives.WriteUInt16BigEndian(sound.AsSpan(2), (ushort)samples);
      sound[7] = (byte)random.Next(89);
      sound[9] = (byte)random.Next(89);
      random.NextBytes(sound.AsSpan(10));

      var method = chunk % 2 == 0 ? 1 : 3;
      var picture = new List<byte>(new byte[16]) { [0] = (byte)method };
      picture[12] = _HEIGHT & 0xFF;
      picture[13] = _HEIGHT >> 8;
      picture[14] = (_WIDTH / 2) & 0xFF;
      picture[15] = (_WIDTH / 2) >> 8;
      var stride = _WIDTH / 2;
      var pairs = method == 1 ? (_HEIGHT / 8) * (stride / 4) * 9 : (_HEIGHT / 4) * (stride + stride / 4);
      while (pairs > 0) {
        var v = random.Next(256);
        picture.Add((byte)v);
        pairs -= Math.Max(1, (tbl[16 * v] + 1) / 2);
      }

      offsets.Add((uint)duk.Position);
      Span<byte> lengths = stackalloc byte[8];
      BinaryPrimitives.WriteUInt32BigEndian(lengths, (uint)sound.Length);
      BinaryPrimitives.WriteUInt32BigEndian(lengths[4..], (uint)picture.Count);
      duk.Write(lengths);
      duk.Write(sound);
      duk.Write(picture.ToArray());
      sounds.Add(sound);
      pictures.Add([.. picture]);
    }

    var frm = new byte[4 * offsets.Count];
    for (var i = 0; i < offsets.Count; ++i)
      BinaryPrimitives.WriteUInt32BigEndian(frm.AsSpan(4 * i), offsets[i]);
    return new(duk.ToArray(), hdr, frm, tbl, sounds, pictures);
  }

  /// <summary>The film's four files in a fresh directory, each named in a different case.</summary>
  private static string _Write() {
    var film = _Film();
    var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(directory);
    File.WriteAllBytes(Path.Combine(directory, "FILM.DUK"), film.Duk);
    File.WriteAllBytes(Path.Combine(directory, "film.hdr"), film.Hdr);
    File.WriteAllBytes(Path.Combine(directory, "Film.frm"), film.Frm);
    File.WriteAllBytes(Path.Combine(directory, "film.TBL"), film.Tbl);
    return directory;
  }
}
