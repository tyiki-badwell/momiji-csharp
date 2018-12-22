namespace Momiji.Core.WebMidi
{
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
}