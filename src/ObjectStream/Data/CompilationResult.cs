namespace ObjectStream.Data
{
    [Serializable]
    public class CompilationResult
    {
        public string Name { get; set; }
        public byte[] Data { get; set; }
        public byte[] Symbols { get; set; }

        public CompilationResult()
        {
            Data = Array.Empty<byte>();
            Symbols = Array.Empty<byte>();
        }
    }
}
