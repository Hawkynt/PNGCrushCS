namespace SharpAstro.Jxr;

/// <summary>
/// PROFILE_LEVEL_INFO from the JXR codestream — T.832 §8.5. A
/// do-while-terminated list of (PROFILE_IDC, LEVEL_IDC) records used by
/// decoders to verify that the codestream lies within their declared
/// conformance capabilities.
/// </summary>
/// <remarks>
/// <para>Each record is exactly 32 bits: <c>PROFILE_IDC u(8)</c>,
/// <c>LEVEL_IDC u(8)</c>, <c>RESERVED_L u(15)</c>, <c>LAST_FLAG u(1)</c>.
/// The final record sets <c>LAST_FLAG = 1</c>; intermediate records set
/// it to 0 and the loop continues. Because each record is a whole
/// number of bytes, the structure is implicitly byte-aligned at the end.</para>
/// <para>Typical encoders emit a single entry naming the maximum
/// profile/level required by the codestream. Use <see cref="Single"/>
/// for that common case.</para>
/// </remarks>
public sealed class ProfileLevelInfo
{
    /// <summary>One conformance-point record within PROFILE_LEVEL_INFO.</summary>
    public readonly record struct Entry(byte ProfileIdc, byte LevelIdc);

    public List<Entry> Entries { get; } = [];

    /// <summary>
    /// Convenience: a PROFILE_LEVEL_INFO containing exactly one entry. This
    /// is what most JXR encoders emit — multiple records are only useful
    /// when a codestream is jointly conformant against several profile/level
    /// targets at once.
    /// </summary>
    public static ProfileLevelInfo Single(byte profileIdc, byte levelIdc) =>
        new() { Entries = { new Entry(profileIdc, levelIdc) } };

    /// <summary>
    /// Write the PROFILE_LEVEL_INFO record list. Throws if no entries have
    /// been added — the syntax requires at least one record.
    /// </summary>
    public void Write(BitWriter writer)
    {
        if (Entries.Count == 0)
            throw new InvalidOperationException("PROFILE_LEVEL_INFO requires at least one entry");

        for (var i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            var last = i == Entries.Count - 1;
            writer.WriteBits(e.ProfileIdc, 8);
            writer.WriteBits(e.LevelIdc, 8);
            writer.WriteBits(0, 15);          // RESERVED_L = 0
            writer.WriteBit(last);            // LAST_FLAG
        }
    }

    /// <summary>Read PROFILE_LEVEL_INFO; loop terminates when LAST_FLAG=1 is observed.</summary>
    public static ProfileLevelInfo Read(ref BitReader reader)
    {
        var info = new ProfileLevelInfo();
        // Cap iteration so a malformed stream cannot spin forever — the spec
        // does not bound the count explicitly, but realistic codestreams use
        // 1, occasionally 2 or 3. 1024 is well past any sane limit.
        const int safetyCap = 1024;
        for (var i = 0; i < safetyCap; i++)
        {
            var profile = (byte)reader.ReadBits(8);
            var level = (byte)reader.ReadBits(8);
            reader.SkipBits(15);              // RESERVED_L
            var last = reader.ReadBit();
            info.Entries.Add(new Entry(profile, level));
            if (last) return info;
        }
        throw new InvalidDataException($"PROFILE_LEVEL_INFO exceeded safety cap of {safetyCap} entries");
    }
}
