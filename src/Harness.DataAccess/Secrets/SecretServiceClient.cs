using System.Text;
using DBus.Services.Secrets;

namespace Harness.DataAccess.Secrets;

internal sealed class SecretServiceClient : ISecretServiceClient
{
    private const string ApplicationAttribute = "application";
    private const string ApplicationName = "Harness.NET";
    private const string KeyAttribute = "key";

    public async ValueTask<string?> GetAsync(string name, CancellationToken cancellationToken)
    {
        SecretService service = await SecretService
            .ConnectAsync(EncryptionType.Dh)
            .WaitAsync(cancellationToken);
        Collection? collection = await service
            .GetDefaultCollectionAsync()
            .WaitAsync(cancellationToken);

        if (collection is null)
        {
            throw new InvalidOperationException("The default Secret Service collection is unavailable.");
        }

        Item[] items = await collection
            .SearchItemsAsync(CreateAttributes(name))
            .WaitAsync(cancellationToken);
        Item? item = items.FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        byte[] value = await item.GetSecretAsync().WaitAsync(cancellationToken);
        return Encoding.UTF8.GetString(value);
    }

    public async ValueTask SetAsync(string name, string value, CancellationToken cancellationToken)
    {
        SecretService service = await SecretService
            .ConnectAsync(EncryptionType.Dh)
            .WaitAsync(cancellationToken);
        Collection? collection = await service
            .GetDefaultCollectionAsync()
            .WaitAsync(cancellationToken);

        if (collection is null)
        {
            throw new InvalidOperationException("The default Secret Service collection is unavailable.");
        }

        Item? item = await collection
            .CreateItemAsync(
                $"{ApplicationName}: {name}",
                CreateAttributes(name),
                Encoding.UTF8.GetBytes(value),
                "text/plain; charset=utf-8",
                replace: true)
            .WaitAsync(cancellationToken);

        if (item is null)
        {
            throw new InvalidOperationException("Secret Service did not create or replace the secret.");
        }
    }

    private static Dictionary<string, string> CreateAttributes(string name) => new()
    {
        [ApplicationAttribute] = ApplicationName,
        [KeyAttribute] = name,
    };
}
