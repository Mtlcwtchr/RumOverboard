namespace RumOverboard.StateMachine.Serialization
{
    // The (de)serialization half of the payload structs — kept separate from the
    // data via `partial`. Each struct fully owns how it maps to/from bytes; the
    // PayloadType header is written by PayloadCodec, not here.

    public partial struct ClimbAttachPayload
    {
        public void Write(ref PayloadWriter writer)
        {
            writer.WriteInt(ClimbableId);
            writer.WriteVector3(LocalPoint);
        }

        public void Read(ref PayloadReader reader)
        {
            ClimbableId = reader.ReadInt();
            LocalPoint = reader.ReadVector3();
        }
    }

    public partial struct RumSipPayload
    {
        public void Write(ref PayloadWriter writer)
        {
            writer.WriteFloat(Progress);
            writer.WriteByte(SipCount);
        }

        public void Read(ref PayloadReader reader)
        {
            Progress = reader.ReadFloat();
            SipCount = reader.ReadByte();
        }
    }
}
