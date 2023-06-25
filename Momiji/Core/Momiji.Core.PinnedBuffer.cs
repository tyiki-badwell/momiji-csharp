namespace Momiji.Core.Buffer;

public class BufferLog
{
    private List<(string label, double time)> Log { get; }

    public BufferLog()
    {
        Log = new List<(string, double)>();
    }

    public void Clear()
    {
        Log.Clear();
    }

    public void Add(string label, double time)
    {
        Log.Add((label, time));
    }

    public void Marge(BufferLog source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Marge(source.Log);
    }

    public void Marge(IEnumerable<(string, double)> source)
    {
        Clear();
        Log.InsertRange(0, source);
    }

    public void ForEach(Action<(string label, double time)> action)
    {
        Log.ForEach(action);
    }

    public double SpentTime()
    {
        return Log[^1].time - Log[0].time;
    }

    public double FirstTime()
    {
        return Log[0].time;
    }
}
