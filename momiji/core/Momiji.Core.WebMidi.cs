namespace Momiji.Core.WebMidi
{
#pragma warning disable CA1815 // equals および operator equals を値型でオーバーライドします
    public struct MIDIMessageEvent
    {
        public double receivedTime;
        public byte data0;
        public byte data1;
        public byte data2;
        public byte data3;
    }

    public struct MIDIMessageEvent2
    {
        public MIDIMessageEvent midiMessageEvent;
        public double receivedTimeUSec;
    }
#pragma warning restore CA1815 // equals および operator equals を値型でオーバーライドします
}