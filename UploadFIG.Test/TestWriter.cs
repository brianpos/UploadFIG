using System.Text;

public class TestWriter : TextWriter
{
    //public ITestOutputHelper OutputWriter { get; }

    public override Encoding Encoding => Encoding.ASCII;

    //public TestWriter(ITestOutputHelper outputWriter)
    //{
    //    OutputWriter = outputWriter;
    //}
    private StringBuilder cache = new();
    public override void Write(char value)
    {
        if (value == '\r')
            return;
        if (value == '\n')
        {
            // OutputWriter.WriteLine(cache.ToString());
            cache.Clear();
        }
        else
        {
            cache.Append(value);
        }
    }
    public override void Flush()
    {
        if (cache.Length == 0)
            return;
        // OutputWriter.WriteLine(cache.ToString());
        cache.Clear();
    }
}
