namespace Harness.DataAccess.Inspection;

internal static class GitStandardInputWriter
{
    internal static async ValueTask<IOException?> WriteAndCloseAsync(
        StreamWriter writer,
        string content,
        CancellationToken cancellationToken)
    {
        IOException? failure = null;
        try
        {
            await writer.WriteAsync(content.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        catch (IOException exception)
        {
            // A rejecting hook can make Git close stdin before the commit message
            // finishes writing. Its non-zero exit code is the authoritative result.
            failure = exception;
        }
        finally
        {
            try
            {
                writer.Close();
            }
            catch (IOException exception)
            {
                failure ??= exception;
            }
        }

        return failure;
    }
}
