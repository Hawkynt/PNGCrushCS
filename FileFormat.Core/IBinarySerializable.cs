using System;

namespace FileFormat.Core;

/// <summary>Defines a type that can be read from and written to a binary span. Implemented by source-generated header structs.</summary>
public interface IBinarySerializable<TSelf> where TSelf : IBinarySerializable<TSelf> {

  /// <summary>The fixed size in bytes of the serialized representation.</summary>
  static abstract int SerializedSize { get; }

  /// <summary>Reads an instance from the given byte span.</summary>
  static abstract TSelf ReadFrom(ReadOnlySpan<byte> source);

  /// <summary>Writes this instance to the given byte span.</summary>
  void WriteTo(Span<byte> destination);
}
