using Momiji.Interop;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Momiji.Core.H264
{
    public class H264OutputBuffer : PinnedBuffer<byte[]>
    {
        public int Wrote { get; set; }
        public bool EndOfFrame { get; set; }

        public H264OutputBuffer(int size) : base(new byte[size])
        {
        }
    }

}