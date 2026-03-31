using System;
using System.IO;

namespace LiteDbX.Engine;

/// <summary>
/// All engine settings used to starts new engine
/// </summary>
public class EngineSettings
{
    /// <summary>
    /// Get/Set custom stream to be used as datafile (can be MemoryStream or TempStream). Do not use FileStream - to use
    /// physical file, use "filename" attribute (and keep DataStream/WalStream null)
    /// </summary>
    public Stream DataStream { get; set; } = null;

    /// <summary>
    /// Get/Set custom stream to be used as log file. If is null, use a new TempStream (for TempStream datafile) or
    /// MemoryStream (for MemoryStream datafile)
    /// </summary>
    public Stream LogStream { get; set; } = null;

    /// <summary>
    /// Get/Set custom stream to be used as temp file. If is null, will create new FileStreamFactory with "-tmp" on name
    /// </summary>
    public Stream TempStream { get; set; } = null;

    /// <summary>
    /// Full path or relative path from DLL directory. Can use ':temp:' for temp database or ':memory:' for in-memory database.
    /// (default: null)
    /// </summary>
    public string Filename { get; set; }

    /// <summary>
    /// Get database password to decrypt pages
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// If database is new, initialize with allocated space (in bytes) (default: 0)
    /// </summary>
    public long InitialSize { get; set; } = 0;

    /// <summary>
    /// Create database with custom string collection (used only to create database) (default: Collation.Default)
    /// </summary>
    public Collation Collation { get; set; }

    /// <summary>
    /// Indicate that engine will open files in readonly mode (and will not support any database change)
    /// </summary>
    public bool ReadOnly { get; set; } = false;

    /// <summary>
    /// After a Close with exception do a database rebuild on next open
    /// </summary>
    public bool AutoRebuild { get; set; } = false;

    /// <summary>
    /// If detect it's a older version (v4) do upgrade in datafile to new v5. A backup file will be keeped in same directory
    /// </summary>
    public bool Upgrade { get; set; } = false;

    /// <summary>
    /// Is used to transform a <see cref="BsonValue" /> from the database on read. This can be used to upgrade data from older
    /// versions.
    /// </summary>
    public Func<string, BsonValue, BsonValue> ReadTransform { get; set; }

    /// <summary>
    /// Optional memory-cache trimming settings.
    /// Defaults preserve the current cache behavior unless trimming is explicitly enabled.
    /// </summary>
    public MemoryCacheConfig MemoryCacheConfig { get; set; } = new MemoryCacheConfig();

    /// <summary>
    /// Optional value for type of AES Encryption used. Defaults to ECB, options are ECB or GCM. If password not provided, will not take affect.
    /// </summary>
    public AESEncryptionType AESEncryption { get; set; }

    /// <summary>
    /// Create new IStreamFactory for datafile
    /// </summary>
    internal IStreamFactory CreateDataFactory(bool useAesStream = true)
    {
        if (DataStream != null)
        {
            return new StreamFactory(DataStream, Password, AESEncryption);
        }

        if (Filename == ":memory:")
        {
            return new StreamFactory(new MemoryStream(), Password, AESEncryption);
        }

        if (Filename == ":temp:")
        {
            return new StreamFactory(new TempStream(), Password, AESEncryption);
        }

        if (!string.IsNullOrEmpty(Filename))
        {
            return new FileStreamFactory(Filename, Password, ReadOnly, false, useAesStream, AESEncryption);
        }

        throw new ArgumentException("EngineSettings must have Filename or DataStream as data source");
    }

    /// <summary>
    /// Create new IStreamFactory for logfile
    /// </summary>
    internal IStreamFactory CreateLogFactory()
    {
        if (LogStream != null)
        {
            return new StreamFactory(LogStream, Password, AESEncryption);
        }

        if (Filename == ":memory:")
        {
            return new StreamFactory(new MemoryStream(), Password, AESEncryption);
        }

        if (Filename == ":temp:")
        {
            return new StreamFactory(new TempStream(), Password, AESEncryption);
        }

        if (!string.IsNullOrEmpty(Filename))
        {
            var logName = FileHelper.GetLogFile(Filename);

            return new FileStreamFactory(logName, Password, ReadOnly, false, aesEncryption: AESEncryption);
        }

        return new StreamFactory(new MemoryStream(), Password, AESEncryption);
    }

    /// <summary>
    /// Create new IStreamFactory for temporary file (sort)
    /// </summary>
    internal IStreamFactory CreateTempFactory()
    {
        if (TempStream != null)
        {
            return new StreamFactory(TempStream, Password, AESEncryption);
        }

        if (Filename == ":memory:")
        {
            return new StreamFactory(new MemoryStream(), Password, AESEncryption);
        }

        if (Filename == ":temp:")
        {
            return new StreamFactory(new TempStream(), Password, AESEncryption);
        }

        if (!string.IsNullOrEmpty(Filename))
        {
            var tempName = FileHelper.GetTempFile(Filename);

            return new FileStreamFactory(tempName, Password, false, true, aesEncryption: AESEncryption);
        }

        return new StreamFactory(new TempStream(), Password, AESEncryption);
    }
}