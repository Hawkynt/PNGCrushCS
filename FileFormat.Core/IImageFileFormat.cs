using System;
using System.IO;

namespace FileFormat.Core;

/// <summary>Legacy convenience interface. New formats should use <see cref="IImageFormatReader{TSelf}"/>, <see cref="IImageToRawImage{TSelf}"/>, etc. directly.</summary>
public interface IImageFileFormat<TSelf> where TSelf : IImageFileFormat<TSelf> {
  static abstract string PrimaryExtension { get; }
  static abstract string[] FileExtensions { get; }
  static virtual FormatCapability Capabilities => FormatCapability.VariableResolution;
  static virtual bool? MatchesSignature(ReadOnlySpan<byte> header) => null;
  static virtual TSelf FromSpan(ReadOnlySpan<byte> data) => TSelf.FromBytes(data.ToArray());
  static abstract TSelf FromBytes(byte[] data);
  static virtual TSelf FromFile(FileInfo file) => TSelf.FromBytes(File.ReadAllBytes(file.FullName));
  static virtual TSelf FromStream(Stream stream) {
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return TSelf.FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return TSelf.FromBytes(ms.ToArray());
  }
  static abstract RawImage ToRawImage(TSelf file);
  static virtual TSelf FromRawImage(RawImage image) => throw new NotSupportedException($"Creating {typeof(TSelf).Name} from RawImage is not supported.");
  static virtual byte[] ToBytes(TSelf file) => throw new NotSupportedException($"Serializing {typeof(TSelf).Name} to bytes is not supported.");
}
