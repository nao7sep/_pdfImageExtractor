using System.Security.Cryptography;
using System.Text;

namespace _pdfImageExtractor;

public class DupFinder
{
    public readonly Dictionary <string, List <(string FilePath, long FileSize)>> Files = []; // Hash, (file path, file size)

    public static string ComputeHash (string filePath)
    {
        try
        {
            using StreamReader xReader = new (filePath);
            return Encoding.ASCII.GetString (SHA1.HashData (xReader.BaseStream));
        }

        finally
        {
            GC.Collect ();
        }
    }

    public bool Contains (string hash, string filePath) =>
        Files.ContainsKey (hash) && Files [hash].Any (x => x.FileSize == new FileInfo (filePath).Length);

    public void Add (string hash, string filePath)
    {
        if (Files.ContainsKey (hash) == false)
            Files [hash] = [];

        Files [hash].Add ((filePath, new FileInfo (filePath).Length));
    }
}
