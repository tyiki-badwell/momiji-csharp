using System;
using System.Collections.Generic;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.WebMidi
{
    public struct MIDIMessageEvent
    {
        public double receivedTime;
        public byte[] data;
    }
}