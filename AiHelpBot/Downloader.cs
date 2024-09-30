namespace AiHelpBot;

public static class Downloader
{
    public static async Task<byte[]?> DownloadFile(string url)
    {
        using var client = new HttpClient();
        using HttpResponseMessage result = await client.GetAsync(url);
        return result.IsSuccessStatusCode ? await result.Content.ReadAsByteArrayAsync() : null;
    }

    public static async Task<string?> DownloadTestFile(string url)
    {
        var bytes = await DownloadFile(url);
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }
}