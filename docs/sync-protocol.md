# Hatch Sync Protocol — v2

The wire contract every Hatch client (WinUI 3 and the Kotlin Multiplatform core) must
obey. The C# implementation is the reference; the golden fixtures in
`windows/Hatch.Tests.Unit/Fixtures/` plus the test vectors in this document are the executable
contract — a client implementation is correct when it passes them, in either language.

Changing anything in this document is a protocol version bump and requires a migration
plan for rows already on the server.

## Version history

| version | change                                                     |
|---------|------------------------------------------------------------|
| v1      | initial contract                                            |
| v2      | `IsDeleted` tombstones on `TodoItem` and `TaskList` (§4, §5) |

**v1 → v2 migration: none required.** The envelope (§3) is unchanged, so no row on the
server needs rewriting and no server-side migration runs. `IsDeleted` is optional and
defaults to `false`, so a v2 client reads a v1 payload as entirely live.

Compatibility is one-way, and deliberately so: a **v1 client reading a v2 payload** ignores
the unknown `IsDeleted`, treats the tombstone as a live task, and revives it on its next
push. A mixed-version fleet therefore loses delete propagation until every client is
updated — but never loses data, which is the property §5 has always guaranteed.

## 1. Transport and storage

- Backend: Supabase (PostgREST + GoTrue auth). Clients talk only to their own row.
- Table `user_data`:

  | column       | type        | notes                                   |
  |--------------|-------------|-----------------------------------------|
  | `user_id`    | text, PK    | GoTrue user id                          |
  | `tasks_json` | text        | the encrypted envelope (§3)             |
  | `updated_at` | timestamptz | set by the client on every push, UTC    |

- One row per user, whole-state upsert on push. There is no per-task delta protocol.
- Auth: email/password sign-up + sign-in, or GitHub OAuth (implicit flow, redirect
  `hatch://auth-callback`, tokens in the URL fragment as `access_token`/`refresh_token`).

## 2. Sync behavior rules

- **Push**: serialize the full local state (§4), encrypt (§3), upsert the row, set
  `updated_at = now (UTC)`. Clients MUST refuse to push when no passphrase is set.
- **Pull-if-newer**: fetch the row; apply only when `updated_at` is strictly newer than
  the client's last-synced timestamp (a `force` pull skips the staleness check).
- **Unreadable row rule**: if the row exists but cannot be decrypted or parsed, the
  client MUST treat the account as *having data it cannot read* — never as empty, and
  MUST NOT push over it.
- **Legacy plaintext**: a `tasks_json` value not starting with `HATCHE2E.` is a legacy
  plaintext JSON payload — parse it directly; the next push replaces it with an envelope.
- **Conflict on fresh sign-in**: when both local and server have ≥1 task, ask the user:
  use local (push), use server (force pull), or merge (§5, then push the union).

## 3. Encryption envelope

End-to-end: the server only ever stores this envelope. Format (single line, four
base64 fields joined to the prefix with `.`):

```
HATCHE2E.v1.<salt>.<nonce>.<ciphertext>.<tag>
```

| field      | size     | encoding         |
|------------|----------|------------------|
| salt       | 16 bytes | standard base64 with padding |
| nonce      | 12 bytes | standard base64 with padding |
| ciphertext | plaintext length | standard base64 with padding |
| tag        | 16 bytes | standard base64 with padding |

- Key derivation: PBKDF2-HMAC-SHA256, **600,000 iterations**, 32-byte key, from the
  UTF-8 passphrase and the envelope's salt.
- Cipher: AES-256-GCM. Plaintext is the UTF-8 JSON of §4.
- The salt is generated once when a device sets its passphrase and reused per device
  (key derivation is cached); the nonce MUST be freshly random for every envelope.
- Decrypt always uses the salt from the envelope, so devices with different salts
  interoperate. GCM tag failure = wrong passphrase or tampering → unreadable row rule.

**Test vector** (also asserted in `SyncCryptoTests`):

- passphrase: `hatch-protocol-fixture`
- salt: bytes `01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F 10`
- nonce: bytes `65 66 67 68 69 6A 6B 6C 6D 6E 6F 70`
- plaintext: `{"Tasks":[],"Lists":[]}`
- envelope:
  `HATCHE2E.v1.AQIDBAUGBwgJCgsMDQ4PEA==.ZWZnaGlqa2xtbm9w.8MBXWg99OnKuOx9fdeAkXP6/l9/nYdI=.zG17LsgSGvCuUByNjY0Eog==`

## 4. Payload JSON

Top level:

```json
{ "Tasks": [ TodoItem… ], "Lists": [ TaskList… ] }
```

Conventions (pinned by `Services/SyncWire.cs` and the golden fixture):

- Property names are **PascalCase**, exactly as listed below.
- Enums serialize as **integers**.
- Absent optional values serialize as JSON `null` (writers include them; readers must
  accept both `null` and missing).
- Readers MUST ignore unknown properties (older writers emitted derived fields like
  `HasRecurrence`, `ShowAddDateHint`; they are noise).
- GUIDs: lowercase hyphenated (`"11111111-1111-1111-1111-111111111111"`).
- Unicode may appear raw or `\uXXXX`-escaped; both are valid JSON and equivalent.

### TodoItem

| property      | type                  | notes                                            |
|---------------|-----------------------|--------------------------------------------------|
| `Id`          | GUID string           | identity for merge (§5)                          |
| `Title`       | string                |                                                  |
| `IsCompleted` | bool                  |                                                  |
| `CompletedAt` | ISO-8601 offset date-time or null | set when completed, cleared on un-complete |
| `IsDeleted`   | bool                  | tombstone (§5); absent means `false`             |
| `IsStarred`   | bool                  | "Important"                                      |
| `IsInMyDay`   | bool                  | cleared client-side each new day                 |
| `MyDayDate`   | `YYYY-MM-DD` or null  | last date added to My Day                        |
| `DueDate`     | ISO-8601 offset date-time or null | calendar day, read as written — see note below |
| `ListId`      | GUID string           | `00000000-…` = default list                      |
| `Recurrence`  | int                   | 0 None, 1 Daily, 2 Weekdays, 3 Weekly, 4 Monthly |
| `Priority`    | int                   | 0 None, 1 Low, 2 Medium, 3 High                  |
| `Tags`        | string array          | empty array when none                            |
| `CreatedAt`   | ISO-8601 date-time    | may carry `Z`, an offset, or no suffix (legacy local) |
| `UpdatedAt`   | ISO-8601 offset date-time | stamped on real edits only; drives merge (§5) |
| `Notes`       | string or null        |                                                  |

**`DueDate` is a calendar day, not an instant.** The task's day is the date portion of the
value *as written* (in its own offset). Readers MUST NOT convert through a time zone before
taking the date — that shifts the day in any zone that disagrees with the stored offset.
Writers SHOULD emit midnight `+00:00`; rows written by Windows builds before 2026-07-28 may
instead carry midnight at the writer's local offset, and readers MUST accept both spellings.
Ordering by the raw string is safe: the `YYYY-MM-DD` prefix dominates. `CreatedAt`,
`UpdatedAt` and `CompletedAt` are the opposite — real instants, compared as instants (§5),
converted to local time only for display.

### TaskList

| property      | type                  | notes                             |
|---------------|-----------------------|-----------------------------------|
| `Id`          | GUID string           |                                   |
| `Name`        | string                |                                   |
| `AccentColor` | string                | `#RRGGBB`                         |
| `IsPinned`    | bool                  |                                   |
| `SortOrder`   | int                   | ascending nav order               |
| `CustomIcon`  | string or null        | emoji                             |
| `IsDeleted`   | bool                  | tombstone (§5); absent means `false` |
| `UpdatedAt`   | ISO-8601 offset date-time | stamped on rename/recolor/pin/icon/reorder |

Golden fixture: `windows/Hatch.Tests.Unit/Fixtures/tasks-golden.json` — writers must produce a
payload value-equal to it from the canonical objects in `SyncWireTests`; readers must
restore every field from it.

## 5. Merge (conflict resolution "Merge" choice)

Record-level last-write-wins union, per collection (`Tasks`, `Lists`), keyed by `Id`:

- Present on one side only → kept.
- Present on both → the copy with the later `UpdatedAt` wins; on an exact tie the
  **local** copy wins.
- Nothing is ever dropped.

### Deletion (v2)

Deleting sets `IsDeleted = true` and stamps `UpdatedAt`; the record itself stays in the
payload as a **tombstone**. The merge rule above is unchanged and needs no special case — a
tombstone is an ordinary record, so last-write-wins already decides it:

- Delete on one device, older live copy on the other → the tombstone wins, and the task
  stays deleted everywhere.
- Delete on one device, **newer** edit on the other → the edit wins and the task returns.
  This is intended: a later edit beats an earlier delete exactly as it beats an earlier
  edit of any other field.

Client obligations:

- Tombstones MUST be excluded from every task view, count, smart list and notification.
- Tombstones MUST be included in every push and every local save. A client that drops a
  deleted record instead of tombstoning it will have the deletion undone by the next merge.
- Tombstone fields are retained, not blanked, so a delete can be undone exactly.
- Tombstones are **never purged**. A purge horizon would let a device that was offline
  longer than the horizon re-upload a task whose tombstone had been dropped; unbounded row
  growth is the accepted cost.

Reference: `Services/SyncMerge.cs`, `SyncMergeTests`, `SyncMergeTest.kt`.
