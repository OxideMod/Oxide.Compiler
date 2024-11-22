using Newtonsoft.Json;

namespace Oxide.CompilerServices.Models.Compiler
{
    [Serializable]
    public class CompilerFile
    {
        public string Name { get; set; }
        public byte[] Data { get; set; }

        [JsonConstructor]
        public CompilerFile()
        {

        }

        public CompilerFile(string name, byte[] data)
        {
            Name = name;
            Data = data;
        }

        public CompilerFile(string directory, string name)
        {
            Name = name;
            Data = File.ReadAllBytes(Path.Combine(directory, Name));
        }

        public CompilerFile(string path)
        {
            Name = Path.GetFileName(path);
            Data = File.ReadAllBytes(path);
        }
    }
}
