using System.Text.Json;

static class Program
{
    public static async Task Main(string[] args)
    {
        string? serverPath = null;
        string? clientPath = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--server="))
                serverPath = arg.Split('=')[1].Trim('"');
            else if (arg.StartsWith("--client="))
                clientPath = arg.Split('=')[1].Trim('"');
        }

        if (string.IsNullOrWhiteSpace(serverPath) || string.IsNullOrWhiteSpace(clientPath))
        {
            Console.WriteLine("Usage: benchmarkRunner.exe --server=path\\to\\server.exe --client=path\\to\\client.exe");
            return;
        }

        string configPath = Path.Combine(AppContext.BaseDirectory, "benchmark-config.json");

        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config file not found: {configPath}");
            return;
        }

        try
        {
            string configJson = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<BenchmarkConfig>(configJson);

            if (config == null)
            {
                Console.WriteLine("Failed to parse benchmark config.");
                return;
            }

            var runner = new BenchmarkRunner(config, serverPath, clientPath);
            await runner.RunAllAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex);
        }
    }
}