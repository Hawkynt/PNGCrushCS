using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using FileFormat.Codecs;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests;

/// <summary>
/// Drives Vidvox's reference Hap decoder and an independent BPTC decoder over deterministic
/// Hap R/HDR frames written by this package.
/// </summary>
/// <remarks>
/// The executable is deliberately external to the managed codec. CI builds it from pinned upstream
/// revisions: Vidvox/hap provides <c>HapDecode</c>, and iOrange/bcdec decodes the BC7/BC6H texture
/// bytes that <c>HapDecode</c> returns. This keeps the framing and texture checks independent of both
/// halves implemented in this repository.
/// </remarks>
internal static class HapReferenceOracle {

  private const int _TIMEOUT_MILLISECONDS = 60_000;
  private const int _WIDTH = 4;
  private const int _HEIGHT = 4;
  private const float _BC6_MAX_ERROR = 0.125f;

  public static string? ExecutablePath { get; } = _Locate();

  public static bool IsAvailable => ExecutablePath != null;

  public static void RequireAvailable() {
    if (!IsAvailable)
      Assert.Inconclusive(
        "no Hap reference oracle on this machine. Set HAP_REFERENCE_ORACLE to the CI-built Vidvox/bcdec harness.");
  }

  public static (bool Accepted, string Detail) TryValidate() {
    if (!IsAvailable)
      return (false, "no Hap reference oracle on this machine");

    var configuredArtifacts = Environment.GetEnvironmentVariable("HAP_REFERENCE_ARTIFACTS");
    var keepArtifacts = !string.IsNullOrWhiteSpace(configuredArtifacts);
    var directory = keepArtifacts
      ? Path.GetFullPath(configuredArtifacts!)
      : Path.Combine(Path.GetTempPath(), "hap-reference-" + Path.GetRandomFileName());

    Directory.CreateDirectory(directory);

    try {
      var details = new List<string>(3);

      var rgba = _ConstantRgba(10, 20, 30, 40);
      var hapR = _Encode("Hap7", PixelFormat.Rgba32, rgba);
      var bc7 = _Run("hap-r-bc7", "bc7", hapR, directory);
      if (!bc7.Success)
        return (false, bc7.Detail);
      if (!bc7.Pixels.AsSpan().SequenceEqual(rgba))
        return (false, "bcdec decoded the Hap R BC7 block to different RGBA samples");
      details.Add(bc7.Detail);

      var unsignedHdr = _ConstantRgbF16((Half)1.0f, (Half)0.5f, (Half)2.0f);
      var hapHu = _Encode("HapH", PixelFormat.RgbF16, unsignedHdr);
      var bc6u = _Run("hap-h-bc6u", "bc6u", hapHu, directory);
      if (!bc6u.Success)
        return (false, bc6u.Detail);
      var unsignedComparison = _CompareBc6(unsignedHdr, bc6u.Pixels);
      if (unsignedComparison != null)
        return (false, "unsigned Hap HDR: " + unsignedComparison);
      details.Add(bc6u.Detail);

      var signedHdr = _ConstantRgbF16((Half)(-1.0f), (Half)0.5f, (Half)2.0f);
      var hapHs = _Encode("HapH", PixelFormat.RgbF16, signedHdr);
      var bc6s = _Run("hap-h-bc6s", "bc6s", hapHs, directory);
      if (!bc6s.Success)
        return (false, bc6s.Detail);
      var signedComparison = _CompareBc6(signedHdr, bc6s.Pixels);
      if (signedComparison != null)
        return (false, "signed Hap HDR: " + signedComparison);
      details.Add(bc6s.Detail);

      return (true, string.Join("; ", details));
    } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) {
      return (false, $"{exception.GetType().Name}: {exception.Message}");
    } finally {
      if (!keepArtifacts)
        try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
    }
  }

  private static byte[] _Encode(string code, PixelFormat format, byte[] pixels) {
    var tag = CodecTag.FromCharacters(code);
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = tag,
      Handler = tag,
      Width = _WIDTH,
      Height = _HEIGHT,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
    };
    var image = new RawImage {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = format,
      PixelData = pixels,
    };

    var encoder = HapVideoEncoder.Create(stream);
    if (!encoder.TryEncode(image, 0, out var packet))
      throw new InvalidDataException($"the {code} encoder declined its deterministic oracle frame");

    return packet.Data.ToArray();
  }

  private static OracleResult _Run(string name, string kind, byte[] frame, string directory) {
    var framePath = Path.Combine(directory, name + ".hap");
    var texturePath = Path.Combine(directory, name + ".bptc");
    var pixelsPath = Path.Combine(directory, name + (kind == "bc7" ? ".rgba" : ".rgb32f"));
    File.WriteAllBytes(framePath, frame);

    var startInfo = new ProcessStartInfo(ExecutablePath!) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    foreach (var argument in new[] {
      kind, framePath, texturePath, pixelsPath, _WIDTH.ToString(), _HEIGHT.ToString(),
    })
      startInfo.ArgumentList.Add(argument);

    try {
      using var process = Process.Start(startInfo);
      if (process == null)
        return new(false, $"{name}: the reference oracle would not start", []);

      var stdout = process.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(_TIMEOUT_MILLISECONDS)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        return new(false, $"{name}: the reference oracle timed out", []);
      }

      var output = stdout.Result.Trim();
      var error = stderr.Result.Trim();
      if (process.ExitCode != 0)
        return new(false, $"{name}: oracle exit {process.ExitCode}: {error}\n{output}".Trim(), []);
      if (error.Length != 0)
        return new(false, $"{name}: oracle diagnostic: {error}", []);
      if (!File.Exists(texturePath) || new FileInfo(texturePath).Length != 16)
        return new(false, $"{name}: Vidvox HapDecode did not extract exactly one 16-byte BPTC block", []);
      if (!File.Exists(pixelsPath))
        return new(false, $"{name}: bcdec produced no decoded samples", []);

      var pixels = File.ReadAllBytes(pixelsPath);
      var expectedBytes = kind == "bc7" ? _WIDTH * _HEIGHT * 4 : _WIDTH * _HEIGHT * 3 * sizeof(float);
      if (pixels.Length != expectedBytes)
        return new(false, $"{name}: bcdec produced {pixels.Length} sample bytes instead of {expectedBytes}", []);

      return new(true, $"{name}: {output}", pixels);
    } catch (Win32Exception exception) {
      return new(false, $"{name}: {exception.Message}", []);
    }
  }

  private static string? _CompareBc6(ReadOnlySpan<byte> source, ReadOnlySpan<byte> decoded) {
    for (var sample = 0; sample < _WIDTH * _HEIGHT * 3; ++sample) {
      var expected = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(source[(sample * 2)..]));
      var actual = BinaryPrimitives.ReadSingleLittleEndian(decoded[(sample * sizeof(float))..]);
      if (!float.IsFinite(actual))
        return $"bcdec produced a non-finite value at sample {sample}";

      var error = MathF.Abs(actual - expected);
      if (error >= _BC6_MAX_ERROR)
        return $"sample {sample} decoded as {actual:R}, expected {expected:R} (absolute error {error:R})";
    }

    return null;
  }

  private static byte[] _ConstantRgba(byte r, byte g, byte b, byte a) {
    var result = new byte[_WIDTH * _HEIGHT * 4];
    for (var pixel = 0; pixel < _WIDTH * _HEIGHT; ++pixel) {
      var at = pixel * 4;
      result[at] = r;
      result[at + 1] = g;
      result[at + 2] = b;
      result[at + 3] = a;
    }
    return result;
  }

  private static byte[] _ConstantRgbF16(Half r, Half g, Half b) {
    var result = new byte[_WIDTH * _HEIGHT * 6];
    for (var pixel = 0; pixel < _WIDTH * _HEIGHT; ++pixel) {
      var at = pixel * 6;
      _WriteHalf(result, at, r);
      _WriteHalf(result, at + 2, g);
      _WriteHalf(result, at + 4, b);
    }
    return result;
  }

  private static void _WriteHalf(Span<byte> destination, int offset, Half value)
    => BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], BitConverter.HalfToUInt16Bits(value));

  private static string? _Locate() {
    var configured = Environment.GetEnvironmentVariable("HAP_REFERENCE_ORACLE");
    if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
      return configured;

    var name = OperatingSystem.IsWindows() ? "hap-reference-oracle.exe" : "hap-reference-oracle";
    foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
      if (string.IsNullOrWhiteSpace(directory))
        continue;
      try {
        var candidate = Path.Combine(directory, name);
        if (File.Exists(candidate))
          return candidate;
      } catch (ArgumentException) {
        // Ignore malformed PATH entries.
      }
    }

    return null;
  }

  private readonly record struct OracleResult(bool Success, string Detail, byte[] Pixels);
}
