namespace Momiji.Core.WebMidi
{
#pragma warning disable CA1815 // equals および operator equals を値型でオーバーライドします
#pragma warning disable CA1051 // 参照可能なインスタンス フィールドを宣言しません
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
#pragma warning restore CA1051 // 参照可能なインスタンス フィールドを宣言しません
#pragma warning restore CA1815 // equals および operator equals を値型でオーバーライドします
}