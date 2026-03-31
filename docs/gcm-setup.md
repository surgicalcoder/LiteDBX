# Setting up AES-GCM support in LiteDbX

LiteDbX core includes **plain** mode and **ECB** encryption.

**GCM is optional.** To use it, you must:

1. reference the optional GCM package/project
2. register the GCM provider at startup
3. set `AESEncryption` to `GCM`
4. provide a password

---

## 1. Reference the GCM package

If you are working from source in this repository, add a reference to:

- `LiteDbX.Encryption.Gcm`

If you are consuming packaged builds, install the optional GCM package for LiteDbX.

---

## 2. Register the GCM provider

Before opening a database that uses GCM, register the provider once during application startup.

Use:

- `GcmEncryptionRegistration.Register()`

If you skip this step, LiteDbX will throw a missing-provider error when:

- you try to create a GCM-encrypted database, or
- you try to open an existing GCM-encrypted database

---

## 3. Configure LiteDbX to use GCM

Set the encryption mode to:

- `AESEncryptionType.GCM`

and also provide:

- `Password`

If no password is set, encryption is not used.

---

## 4. Typical setup flow

A typical startup sequence is:

1. register the GCM provider
2. create `EngineSettings`
3. set `Password`
4. set `AESEncryption = AESEncryptionType.GCM`
5. open `LiteEngine` or `LiteDatabase`

---

## 5. Connection string usage

If you use connection strings, set:

- `encryption=GCM`
- `password=...`

Example shape:

- `filename=my.db;password=secret;encryption=GCM`

This still requires provider registration before opening the database.

---

## 6. Important behavior notes

### ECB remains built in
You do **not** need the GCM package for ECB.

### GCM uses explicit registration
LiteDbX does not auto-load the GCM provider.
Registration is required by design.

### Existing GCM files
If a file is already GCM-encrypted and the provider is not registered, LiteDbX should fail clearly instead of falling back to ECB.

### Existing ECB files
ECB files continue to use the built-in ECB path.

---

## 7. What to reference in code

From the current repository structure, the main pieces are:

- core package/project: `LiteDbX`
- optional GCM package/project: `LiteDbX.Encryption.Gcm`
- registration entry point: `GcmEncryptionRegistration.Register()`
- mode enum: `AESEncryptionType.GCM`

---

## 8. Minimal checklist

To enable GCM successfully:

- [ ] reference `LiteDbX.Encryption.Gcm`
- [ ] call `GcmEncryptionRegistration.Register()`
- [ ] set a `Password`
- [ ] set `AESEncryption` to `AESEncryptionType.GCM`

---

## 9. Related docs

For implementation details of the GCM file format and stream behavior, see:

- `docs/aes-gcm-mode.md`

