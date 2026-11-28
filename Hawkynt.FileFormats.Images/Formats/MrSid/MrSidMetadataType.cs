namespace FileFormat.MrSid;

/// <summary>The value type code of a MrSID metadata record, as stored in the file.</summary>
/// <remarks>
/// Only the codes seen in real files are named. Each was settled by a record whose value is known from its
/// meaning: <c>IMAGE::WIDTH</c> for 32-bit integers, <c>IMAGE::INPUT_FILE_SIZE</c> for 64-bit floats,
/// <c>IMAGE::TARGET_COMPRESSION_RATIO</c> (15.0) for 32-bit floats. A record of any other code stops the
/// metadata walk, because a record's length field covers its key and not its value.
/// </remarks>
public enum MrSidMetadataType {

  /// <summary>Unsigned bytes (seen as <c>IMAGE::NO_DATA_VALUE</c>).</summary>
  UInt8 = 1,

  /// <summary>Unsigned 16-bit integers (<c>IMAGE::BITS_PER_SAMPLE</c>, GeoTIFF key values).</summary>
  UInt16 = 2,

  /// <summary>Unsigned 32-bit integers (<c>IMAGE::WIDTH</c>, <c>IMAGE::HEIGHT</c>).</summary>
  UInt32 = 3,

  /// <summary>NUL-terminated strings (<c>IMAGE::WKT</c>, GeoTIFF citations).</summary>
  Ascii = 5,

  /// <summary>32-bit IEEE floats (<c>IMAGE::TARGET_COMPRESSION_RATIO</c>, <c>IMAGE::COMPRESSION_GAMMA</c>).</summary>
  Float32 = 6,

  /// <summary>64-bit IEEE floats (<c>IMAGE::XY_ORIGIN</c>, <c>IMAGE::X_RESOLUTION</c>).</summary>
  Float64 = 7,

  /// <summary>Four-byte integers, seen once (<c>IMAGE::COMPRESSION_VERSION</c> = 1, 4, 1); read as signed, which no file has yet tested.</summary>
  Int32 = 10,
}
