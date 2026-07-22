using System;
using UnityEngine;

namespace RumOverboard.StateMachine.Serialization
{
    /// <summary>
    /// Metadata tag written as the first byte of every payload buffer. It tells the
    /// receiver which struct to deserialize the remaining bytes into. Keep values
    /// stable — they travel over the wire.
    /// </summary>
    public enum PayloadType : byte
    {
        None = 0,
        ClimbAttach = 1,
        RumSip = 2,
    }

    /// <summary>
    /// A piece of structured data a state can broadcast alongside its replicated id.
    /// Each implementer owns its own (de)serialization — see the partial structs in
    /// Payloads.cs / Payloads.Serialization.cs.
    /// </summary>
    public interface IStatePayload
    {
        PayloadType Type { get; }
        void Write(ref PayloadWriter writer);
        void Read(ref PayloadReader reader);
    }

    /// <summary>Allocation-free forward-only writer over a caller-owned byte buffer (little-endian).</summary>
    public struct PayloadWriter
    {
        private readonly byte[] _buffer;
        private int _pos;

        public PayloadWriter(byte[] buffer)
        {
            _buffer = buffer;
            _pos = 0;
        }

        public int Length => _pos;

        public void WriteByte(byte v) => _buffer[_pos++] = v;

        public void WriteInt(int v)
        {
            _buffer[_pos++] = (byte)v;
            _buffer[_pos++] = (byte)(v >> 8);
            _buffer[_pos++] = (byte)(v >> 16);
            _buffer[_pos++] = (byte)(v >> 24);
        }

        public void WriteFloat(float v) => WriteInt(BitConverter.SingleToInt32Bits(v));

        public void WriteVector3(Vector3 v)
        {
            WriteFloat(v.x);
            WriteFloat(v.y);
            WriteFloat(v.z);
        }
    }

    /// <summary>Allocation-free forward-only reader mirroring <see cref="PayloadWriter"/>.</summary>
    public struct PayloadReader
    {
        private readonly byte[] _buffer;
        private readonly int _length;
        private int _pos;

        public PayloadReader(byte[] buffer, int length)
        {
            _buffer = buffer;
            _length = length;
            _pos = 0;
        }

        public bool HasMore => _pos < _length;

        public byte ReadByte() => _buffer[_pos++];

        public int ReadInt()
        {
            int v = _buffer[_pos]
                    | (_buffer[_pos + 1] << 8)
                    | (_buffer[_pos + 2] << 16)
                    | (_buffer[_pos + 3] << 24);
            _pos += 4;
            return v;
        }

        public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());

        public Vector3 ReadVector3() => new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
    }

    /// <summary>
    /// Frames payloads for the wire: a one-byte <see cref="PayloadType"/> header
    /// (the metadata) followed by the struct's own bytes. Decoding reads the header,
    /// constructs the matching struct, and lets it read the rest. Register new
    /// payload types in <see cref="Create"/>.
    /// </summary>
    public static class PayloadCodec
    {
        /// <summary>Writes header + body into <paramref name="destination"/>; returns byte count.</summary>
        public static int Encode(IStatePayload payload, byte[] destination)
        {
            var writer = new PayloadWriter(destination);
            writer.WriteByte((byte)payload.Type); // metadata header
            payload.Write(ref writer);
            return writer.Length;
        }

        /// <summary>Reads header, builds the matching struct, and fills it. Null if unknown/empty.</summary>
        public static IStatePayload Decode(byte[] source, int length)
        {
            if (length <= 0) return null;
            var reader = new PayloadReader(source, length);
            var type = (PayloadType)reader.ReadByte();
            IStatePayload payload = Create(type);
            payload?.Read(ref reader);
            return payload;
        }

        private static IStatePayload Create(PayloadType type)
        {
            switch (type)
            {
                case PayloadType.ClimbAttach: return new ClimbAttachPayload();
                case PayloadType.RumSip: return new RumSipPayload();
                default: return null;
            }
        }
    }
}
