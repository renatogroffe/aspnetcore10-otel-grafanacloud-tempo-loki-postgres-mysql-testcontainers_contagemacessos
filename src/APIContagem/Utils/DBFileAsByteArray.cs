namespace APIContagem.Utils;

public static class DBFileAsByteArray
{
    public static byte[] GetContent(string sqlFile) =>
        File.ReadAllBytes(
            Path.Combine(Directory.GetCurrentDirectory(), "DB", sqlFile));
}
