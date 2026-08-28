namespace Harness.DataAccess.Tests;

internal static class TestDirectoryCleanup
{
    private const int MaximumAttempts = 5;

    internal static void Delete(string path)
    {
        for (int attempt = 1; Directory.Exists(path); attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < MaximumAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25 * attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < MaximumAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25 * attempt));
            }
        }
    }
}
