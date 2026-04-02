using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX;

/// <summary>
/// Manage ConnectionString to connect and create databases. Connection string are NameValue using Name1=Value1;
/// Name2=Value2
/// </summary>
public class ConnectionString
{
    private readonly Dictionary<string, string> _values;

    /// <summary>
    /// Initialize empty connection string
    /// </summary>
    public ConnectionString()
    {
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Initialize connection string parsing string in "key1=value1;key2=value2;...." format or only "filename" as default
    /// (when no ; char found)
    /// </summary>
    public ConnectionString(string connectionString)
        : this()
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString));
        }

        // create a dictionary from string name=value collection
        if (connectionString.Contains("="))
        {
            _values.ParseKeyValue(connectionString);
        }
        else
        {
            _values["filename"] = connectionString;
        }

        // setting values to properties
        Connection = _values.GetValue("connection", Connection);
        
        AESEncryption = _values.GetValue("encryption", AESEncryption);
        
        Filename = _values.GetValue("filename", Filename).Trim();

        Password = _values.GetValue("password", Password);

        if (Password == string.Empty)
        {
            Password = null;
        }

        InitialSize = _values.GetFileSize(@"initial size", InitialSize);
        ReadOnly = _values.GetValue("readonly", ReadOnly);

        Collation = _values.ContainsKey("collation") ? new Collation(_values.GetValue<string>("collation")) : Collation;

        Upgrade = _values.GetValue("upgrade", Upgrade);
        AutoRebuild = _values.GetValue("auto-rebuild", AutoRebuild);
    }

    /// <summary>
    /// "connection": Select how the engine will be opened (default: Direct).
    ///
    /// - Direct: normal fully capable mode; supports explicit transactions.
    /// - Shared: async-safe in-process serialized mode; no cross-process guarantee and no explicit transactions.
    /// - LockFile: physical-file cross-process write-coordination mode; no explicit transactions.
    /// </summary>
    public ConnectionType Connection { get; set; } = ConnectionType.Direct;
    
    /// <summary>
    /// "encryption": Return how AES encryption will be used (default: ECB).
    /// Only for file-based databases. For in-memory databases, encryption is not supported and will be ignored if specified. If no password specified, encryption will not be used.
    /// For file-based databases, if encryption is specified, the database file will be encrypted using AES encryption with the specified mode.
    /// ECB is built in. GCM requires the optional provider package to be referenced and registered.
    /// The password provided in the "password" attribute will be used to encrypt and decrypt the data pages.
    /// If encryption is not specified, the database file will not be encrypted.              
    /// </summary>
    public AESEncryptionType AESEncryption { get; set; } = AESEncryptionType.ECB;

    /// <summary>
    /// "filename": Full path or relative path from DLL directory
    /// </summary>
    public string Filename { get; set; } = "";

    /// <summary>
    /// "password": Database password used to encrypt/decypted data pages
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// "initial size": If database is new, initialize with allocated space - support KB, MB, GB (default: 0)
    /// </summary>
    public long InitialSize { get; set; }

    /// <summary>
    /// "readonly": Open datafile in readonly mode (default: false)
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// "upgrade": Check if data file is an old version and convert before open (default: false)
    /// </summary>
    public bool Upgrade { get; set; }

    /// <summary>
    /// "auto-rebuild": If last close database exception result a invalid data state, rebuild datafile on next open (default:
    /// false)
    /// </summary>
    public bool AutoRebuild { get; set; }

    /// <summary>
    /// "collation": Set default collaction when database creation (default: "[CurrentCulture]/IgnoreCase")
    /// </summary>
    public Collation Collation { get; set; }

    /// <summary>
    /// Get value from parsed connection string. Returns null if not found
    /// </summary>
    public string this[string key] => _values.GetOrDefault(key);

    /// <summary>
    /// Create an <see cref="ILiteEngine"/> from the parsed connection string using the legacy
    /// constructor-based lifecycle.
    ///
    /// This method is retained only as compatibility glue for constructor-era callers such as
    /// <see cref="LiteDatabase"/> and <see cref="LiteRepository"/> synchronous constructors.
    /// Prefer <see cref="OpenEngine"/> for the supported async-first lifecycle.
    /// </summary>
    internal ILiteEngine CreateEngine(Action<EngineSettings> engineSettingsAction = null)
    {
        var settings = CreateSettings(engineSettingsAction);

        return Connection switch
        {
            ConnectionType.Direct => new LiteEngine(settings),
            ConnectionType.Shared => new SharedEngine(settings),
            ConnectionType.LockFile => new LockFileEngine(settings),
            _ => throw new NotImplementedException()
        };
    }

    /// <summary>
    /// Open an engine using the supported async-first lifecycle.
    ///
    /// This is the canonical lifecycle boundary for connection-string driven engine creation.
    /// Direct mode opens a dedicated <see cref="LiteEngine"/>.
    /// Shared mode returns an in-process serialized wrapper.
    /// LockFile mode returns a physical-file cross-process coordination wrapper.
    /// </summary>
    internal async ValueTask<ILiteEngine> OpenEngine(
        Action<EngineSettings> engineSettingsAction = null,
        CancellationToken cancellationToken = default)
    {
        var settings = CreateSettings(engineSettingsAction);

        return Connection switch
        {
            ConnectionType.Direct => await LiteEngine.Open(settings, cancellationToken).ConfigureAwait(false),
            ConnectionType.Shared => new SharedEngine(settings),
            ConnectionType.LockFile => new LockFileEngine(settings),
            _ => throw new NotImplementedException()
        };
    }

    private EngineSettings CreateSettings(Action<EngineSettings> engineSettingsAction)
    {
        var settings = new EngineSettings
        {
            Filename = Filename,
            Password = Password,
            InitialSize = InitialSize,
            ReadOnly = ReadOnly,
            Collation = Collation,
            Upgrade = Upgrade,
            AutoRebuild = AutoRebuild,
            AESEncryption = AESEncryption
        };

        engineSettingsAction?.Invoke(settings);

        return settings;
    }
}