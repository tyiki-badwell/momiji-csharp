using System;

namespace Momiji.Core.WebMidi
{
    public struct MIDIMessageEvent
    {
        public double receivedTime;
        public byte[] data;
    }
}