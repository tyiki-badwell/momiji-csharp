﻿namespace Momiji.Core.WebMidi;

public struct MIDIMessageEvent
{
    public double receivedTime;
    public byte data0;
    public byte data1;
    public byte data2;
    public byte data3;
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051:参照可能なインスタンス フィールドを宣言しません", Justification = "<保留中>")]
public struct MIDIMessageEvent2
{
    public MIDIMessageEvent midiMessageEvent;
    public double receivedTimeUSec;
}
