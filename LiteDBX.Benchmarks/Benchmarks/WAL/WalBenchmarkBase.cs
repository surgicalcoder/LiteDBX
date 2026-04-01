using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LiteDbX.Encryption.Gcm;

namespace LiteDbX.Benchmarks.Benchmarks.WAL
{
    public abstract class WalBenchmarkBase
    {
        private const string DefaultPassword = "SecurePassword";
        private static readonly DateTime BaseTimestampUtc = new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        private string _databasePath;

        protected const string CollectionName = "wal_docs";

        protected ILiteDatabase DatabaseInstance { get; private set; }

        protected string DatabasePath => _databasePath ??= CreateDatabasePath();

        protected ILiteCollection<WalBenchmarkDocument> GetCollection()
            => DatabaseInstance.GetCollection<WalBenchmarkDocument>(CollectionName, BsonAutoId.Int32);

        protected Task OpenDatabaseAsync(WalEncryptionMode encryptionMode = WalEncryptionMode.None, int checkpointSize = 0)
        {
            if (encryptionMode == WalEncryptionMode.Gcm)
            {
                GcmEncryptionRegistration.Register();
            }

            ResetStorageFiles();

            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var connectionString = new ConnectionString(DatabasePath)
            {
                Connection = ConnectionType.Direct,
                Password = encryptionMode == WalEncryptionMode.None ? null : DefaultPassword,
                AESEncryption = encryptionMode == WalEncryptionMode.Gcm ? AESEncryptionType.GCM : AESEncryptionType.ECB
            };

            DatabaseInstance = new LiteDatabase(connectionString);
            DatabaseInstance.CheckpointSize = checkpointSize;

            return Task.CompletedTask;
        }

        protected async Task CloseDatabaseAsync()
        {
            if (DatabaseInstance != null)
            {
                await DatabaseInstance.DisposeAsync();
                DatabaseInstance = null;
            }

            ResetStorageFiles();
        }

        protected List<WalBenchmarkDocument> CreateDocuments(int count, int payloadBytes, int startingId, int writerId = 0, int transactionGroup = 0)
        {
            var payload = BuildPayload(payloadBytes, writerId, transactionGroup);
            var documents = new List<WalBenchmarkDocument>(count);

            for (var i = 0; i < count; i++)
            {
                documents.Add(new WalBenchmarkDocument
                {
                    Id = startingId + i,
                    WriterId = writerId,
                    TransactionGroup = transactionGroup,
                    Ordinal = i,
                    Category = $"writer-{writerId}-txn-{transactionGroup}",
                    CreatedUtc = BaseTimestampUtc.AddSeconds(startingId + i),
                    Payload = payload
                });
            }

            return documents;
        }

        protected List<List<WalBenchmarkDocument>> CreateBatches(int batchCount, int documentsPerBatch, int payloadBytes)
        {
            var batches = new List<List<WalBenchmarkDocument>>(batchCount);
            var nextId = 1;

            for (var batch = 0; batch < batchCount; batch++)
            {
                batches.Add(CreateDocuments(documentsPerBatch, payloadBytes, nextId, batch, batch));
                nextId += documentsPerBatch;
            }

            return batches;
        }

        private string CreateDatabasePath()
        {
            var folder = Path.Combine(Path.GetTempPath(), "LiteDbX.Benchmarks", "wal");
            return Path.Combine(folder, $"{GetType().Name}-{Guid.NewGuid():N}.db");
        }

        private void ResetStorageFiles()
        {
            foreach (var path in GetStoragePaths())
            {
                TryDelete(path);
            }
        }

        private IEnumerable<string> GetStoragePaths()
        {
            yield return DatabasePath;
            yield return GetSuffixFile(DatabasePath, "-log");
            yield return GetSuffixFile(DatabasePath, "-tmp");
            yield return GetSuffixFile(DatabasePath, "-lock");
        }

        private static string GetSuffixFile(string filename, string suffix)
        {
            return Path.Combine(Path.GetDirectoryName(filename) ?? string.Empty,
                Path.GetFileNameWithoutExtension(filename) + suffix + Path.GetExtension(filename));
        }

        private static void TryDelete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string BuildPayload(int payloadBytes, int writerId, int transactionGroup)
        {
            if (payloadBytes <= 0)
            {
                return string.Empty;
            }

            var prefix = $"writer:{writerId:D2}|txn:{transactionGroup:D4}|";

            if (prefix.Length >= payloadBytes)
            {
                return prefix.Substring(0, payloadBytes);
            }

            return prefix + new string((char)('a' + (writerId % 26)), payloadBytes - prefix.Length);
        }
    }
}


