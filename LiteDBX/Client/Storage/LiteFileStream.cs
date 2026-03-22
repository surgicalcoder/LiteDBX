// ─────────────────────────────────────────────────────────────────────────────
// PHASE 5 — FILE STORAGE REDESIGN
// ─────────────────────────────────────────────────────────────────────────────
// LiteFileStream<TFileId> has been removed as part of the async-only redesign.
//
// Reason: LiteFileStream<TFileId> inherited from System.IO.Stream, which requires
// implementation of synchronous Read(byte[], int, int), Write(byte[], int, int),
// and Flush() methods.  These synchronous overrides violate the async-only contract.
//
// Replacement: LiteFileHandle<TFileId> implements ILiteFileHandle<TFileId>, which
// exposes only async operations (Read, Write, Flush, Seek, DisposeAsync).
// It is created internally by LiteStorage<TFileId>.OpenRead() / OpenWrite().
//
// Public surface: ILiteFileHandle<TFileId> (see ILiteFileHandle.cs)
//                 ILiteStorage<TFileId>     (see ILiteStorage.cs)
// ─────────────────────────────────────────────────────────────────────────────
