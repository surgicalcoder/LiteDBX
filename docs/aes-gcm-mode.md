# AES-GCM mode in LiteDbX

This document explains how **AES-GCM** encryption works in LiteDbX.

It is intentionally implementation-focused: it describes how the current encrypted stream layer behaves, how GCM differs from the legacy ECB path, and what compatibility guarantees exist when opening older encrypted databases.

---

## Overview

LiteDbX now supports two encrypted stream modes:

- `ECB` - the legacy encrypted stream format implemented by `AesStream`
- `GCM` - the newer authenticated encryption format implemented by `AesGcmStream`

`ECB` is built into the core `LiteDbX` package.
`GCM` is provided by an **optional add-on package** and must be registered explicitly before use.

For new encrypted databases, the configured `AESEncryptionType` selects which format will be created.
For existing encrypted databases, LiteDbX prefers the **stored file format** over the requested setting so older encrypted databases can still be reopened safely.

---

## Why GCM has its own stream implementation

GCM is not just "ECB with a different cipher mode".

The legacy `AesStream` is structured around:

- `CryptoStream`
- a hidden first page
- a simple password check written into that first page
- page-sized encrypted reads and writes without per-page authentication metadata

AES-GCM has different requirements:

- each encrypted unit must be authenticated
- a **nonce** is required for each encryption operation
- an authentication **tag** must be stored and verified
- tampering must be detected during read/decrypt, not silently ignored

Because of that, LiteDbX uses a separate `AesGcmStream` implementation instead of trying to stretch the ECB stream to support both models.

---

## What AES-GCM gives you

Compared to ECB, GCM provides:

- **confidentiality** - page contents are encrypted
- **integrity** - modified ciphertext is detected
- **authenticity** - the page data must match its authentication tag
- **page binding** - pages are authenticated with page-specific associated data so a valid encrypted page cannot simply be moved to another page slot without detection

In practice, this means GCM can detect:

- wrong passwords
- modified page ciphertext
- modified authentication tags
- page substitution between logical page indexes

In the current implementation, these failures surface as `LiteException.InvalidPassword()`.
That exception should be understood as **authentication failure**, not only literal password mismatch.

---

## Configuration

You can request GCM through `EngineSettings.AESEncryption` or the connection string `encryption` option.

Because GCM is modular, configuration alone is not enough. Applications must also reference the GCM package and register its provider before opening a GCM-backed database or creating a new one.

Examples:

- `AESEncryptionType.GCM`
- `encryption=GCM`

Relevant defaults:

- encryption mode defaults to `ECB`
- if no password is supplied, encryption is not used

---

## High-level file layout

Like ECB, GCM reserves the **first page** of the encrypted file as hidden metadata.

### Hidden first page

The first page stores:

- encrypted-file marker byte
- salt used for key derivation
- a GCM-specific header marker
- password-check nonce
- password-check authentication tag
- password-check ciphertext

This first page is not exposed as a normal data page to the rest of the engine.

### Encrypted data pages

After the hidden page, each logical LiteDbX page is stored as one authenticated GCM record:

- `12` bytes nonce
- `PAGE_SIZE` bytes ciphertext
- `16` bytes authentication tag

So the physical on-disk size of a GCM page record is:

`12 + PAGE_SIZE + 16`

This is larger than the plaintext page size, which is why GCM needs its own logical-to-physical length mapping.

---

## Header marker and mode detection

GCM files are identified by a dedicated header marker stored in the hidden page:

`LDBXGCM1`

LiteDbX checks for this marker when opening an encrypted stream.

### Detection rules

When opening an encrypted stream:

1. If the stream already contains a valid GCM marker, LiteDbX opens it as `AesGcmStream`
2. If the stream is new and the requested mode is `GCM`, LiteDbX resolves the registered GCM provider, creates a new GCM header, and opens it as `AesGcmStream`
3. Otherwise LiteDbX falls back to the legacy `AesStream` ECB format

This means the configured mode is primarily a **creation-time preference**.
For existing encrypted files, the stored format wins.

If GCM is requested for a new encrypted database and no GCM provider has been registered, LiteDbX throws a provider-registration error instead of silently falling back to ECB.

That behavior is important for compatibility because it lets applications change configuration without breaking the ability to reopen older ECB-encrypted files.

---

## Key derivation

GCM uses a per-file salt stored in the hidden first page.

The encryption key is derived from:

- the user password
- the stored salt

The current implementation derives a 32-byte key using `Rfc2898DeriveBytes`.

This keeps the password out of the file and ensures two different encrypted files created with the same password still derive different keys if their salts differ.

---

## Password validation

GCM does not reuse the ECB "bytes 32..64" password-check convention.

Instead, LiteDbX stores a small encrypted validation payload in the hidden page:

- a nonce
- a tag
- a ciphertext block representing a known plaintext pattern

When reopening the file:

1. LiteDbX reads the hidden page
2. derives the key from password + salt
3. decrypts the password-check payload using the GCM header marker as associated data
4. compares the result with the expected known plaintext

If that authentication or comparison fails, LiteDbX throws `LiteException.InvalidPassword()`.

---

## Per-page encryption model

Each logical page is encrypted independently.

For a page write, LiteDbX:

1. computes the logical page index
2. generates a fresh 12-byte nonce
3. encrypts the page with AES-GCM
4. stores the ciphertext and tag alongside the nonce

For a page read, LiteDbX:

1. locates the page record
2. reads nonce + ciphertext + tag
3. decrypts and authenticates the record
4. returns the plaintext page to the engine

This page-by-page model keeps the rest of the engine working in normal `PAGE_SIZE` units.

---

## Associated data

Each GCM page uses associated data made from:

- the GCM header marker
- the logical page index

This matters because associated data is authenticated even though it is not encrypted.

By including the page index, LiteDbX ensures a page encrypted for one logical position cannot be copied to another logical position and still validate successfully.

---

## Length and physical storage math

ECB and GCM do **not** have the same physical file layout.

### ECB

ECB effectively uses:

- one hidden page
- then page-sized encrypted content

### GCM

GCM uses:

- one hidden page
- then one authenticated record per logical page

Because each GCM page record includes nonce and tag overhead, the physical file length is larger than the logical database length.

LiteDbX therefore normalizes GCM lengths separately:

- incomplete trailing page records are cropped away
- logical length is calculated from complete authenticated page records only
- the engine still sees logical page-sized data

This is handled by `EncryptedStreamFactory` and `AesGcmStream` rather than by the rest of the engine.

---

## Partial-page writes

The GCM stream supports partial logical-page writes by:

1. reading the existing page when needed
2. merging the modified portion into a page buffer
3. re-encrypting the whole logical page as a fresh authenticated record

This is necessary because GCM authenticates the whole encrypted record.
You cannot safely update only part of the stored ciphertext in place without recalculating the authentication tag.

---

## Wrong-password and tamper behavior

The current behavior is intentionally conservative.

The following cases are treated as authentication failure:

- wrong password
- modified page ciphertext
- modified page tag
- modified hidden-page password-check payload

In all of these cases LiteDbX throws:

- `LiteException.InvalidPassword()`

This keeps behavior simple and avoids leaking whether the failure came from password mismatch or post-write tampering.

---

## Backward compatibility with ECB

LiteDbX preserves compatibility with older ECB-encrypted files.

### What stays compatible

- existing ECB files still open as ECB
- existing GCM files reopen as GCM even if the requested mode changes
- new files can be explicitly created as GCM

### Important behavior

If an existing file already contains a GCM header marker, LiteDbX will open it as GCM even if the current settings request `ECB`.
Likewise, a legacy encrypted file without the GCM marker will continue to open through the ECB stream.

This avoids accidental lockout after configuration changes.

---

## Stream and engine integration

The engine still works in logical page units.

The encryption layer hides the physical storage differences:

- `AesStream` exposes legacy ECB behavior
- `AesGcmStream` exposes GCM behavior through the optional GCM package
- `EncryptedStreamFactory` selects and normalizes the correct mode
- `EncryptionProviderRegistry` holds explicitly registered optional providers
- `FileStreamFactory` and `StreamFactory` route both file-backed and custom-stream-backed encrypted paths through the same selection logic

---

## Practical notes

### Recommendation for new databases

For new encrypted databases, prefer `GCM` over `ECB`.

GCM provides authenticated encryption and is substantially safer against unnoticed data tampering.

### File growth

A GCM-encrypted file is expected to be physically larger than the plaintext database because each page stores:

- a nonce
- ciphertext
- a tag

### Existing exceptions

Authentication failures currently map to `LiteException.InvalidPassword()`.
That may represent either:

- an incorrect password, or
- an integrity/authentication failure in the encrypted content

---

## Summary

LiteDbX GCM mode works by:

- reserving a hidden first page for GCM metadata
- deriving a file-specific key from password + salt
- storing a GCM-specific header marker (`LDBXGCM1`)
- validating the password through an authenticated header payload
- encrypting each logical database page independently with AES-GCM
- storing nonce + ciphertext + tag for each page
- authenticating each page with page-specific associated data
- auto-detecting the stored encryption mode when reopening existing encrypted files

This design keeps legacy ECB files readable while providing authenticated encryption for new GCM-backed databases.

