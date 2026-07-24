# Spec — TOTP multi-factor authentication

**Status:** proposed, not scheduled. Written 2026-07-22 so the design is captured while the
sync work is fresh. Nothing here is implemented.

Adds authenticator-app (TOTP) verification to Hatch sync sign-in, using Supabase Auth's
built-in MFA. Compatible with Google Authenticator, Authy, 1Password, Aegis and anything
else implementing RFC 6238.

## 1. What this protects, and what it does not

MFA and the sync passphrase solve different problems and neither replaces the other.

| Layer | Protects | If an attacker defeats only this |
|---|---|---|
| Password / GitHub OAuth | sign-in | can fetch the encrypted row |
| **TOTP (this spec)** | sign-in | cannot sign in at all without the enrolled device |
| Sync passphrase (§3 of sync-protocol.md) | the data itself | holds ciphertext they cannot read |

**Non-goal: this does not remove the passphrase.** A TOTP secret is shared with the server
in order to be verified, so it can never be encryption key material without handing the
server the ability to decrypt — the exact property end-to-end encryption exists to deny.
TOTP codes also rotate every 30 seconds, while AES-GCM needs the same key indefinitely.

**Non-goal: this is not a replacement for the missing passphrase recovery path.** It makes
the account *harder* to reach, which raises the stakes of the existing dead-end rather than
easing it. See §6.

## 2. Server-side enforcement (the part that is easy to get wrong)

Client-side MFA alone is **advisory**. A session that never completed the challenge still
carries `aal1`, and the current policy accepts it:

```sql
-- today: satisfied by an aal1 token
using (auth.uid() = user_id)
```

Anyone talking directly to PostgREST with an `aal1` access token would bypass the UI
entirely. Enforcement requires the assurance level in the policy.

**Shipped 2026-07-22** as `supabase/migrations/20260722020510_require_aal2_when_mfa_enrolled.sql`,
as a **restrictive** policy that ANDs with `own data only` rather than replacing it:

```sql
using      ((select auth.jwt() ->> 'aal') = 'aal2' or not public.has_verified_mfa())
with check ((select auth.jwt() ->> 'aal') = 'aal2' or not public.has_verified_mfa())
```

Two things the obvious version got wrong, both found by testing against the live database:

- **The subquery on `auth.mfa_factors` cannot be inlined.** `authenticated` has no
  privileges on that table and the policy fails with `permission denied`. Granting `SELECT`
  is a trap: the table has RLS enabled with no policy for `authenticated`, so the count
  comes back 0 and the gate silently never fires. Hence the `security definer` helper in
  `20260722020416_add_has_verified_mfa_helper.sql`.
- **`WITH CHECK` is required, not just `USING`.** Both clients upsert, and a `USING`-only
  restrictive policy leaves the INSERT path ungated.

**Consequence:** any client without the challenge step is now locked out of an enrolled
account. Ordering was not optional — see §5.

## 3. Client flows

**Enrolment** (`mfa.enroll(FactorType.TOTP, friendlyName, issuer)`)

1. Call enrol; the response carries `data.qrCode` (an SVG string) and `data.secret`.
2. Show the QR **and the secret as selectable text** — see §4.
3. User adds it to their authenticator, enters the 6-digit code.
4. `mfa.createChallengeAndVerify(factor.id, code)` promotes the session to `aal2`.
5. An unverified factor must be removed if the user abandons enrolment, or it lingers and
   blocks a clean retry.

**Sign-in**

1. Password or OAuth as today → session at `aal1`.
2. If a verified factor exists, prompt for the code and verify → `aal2`.
3. Only then attempt any read or write of `user_data`; with §2 in place an `aal1` session
   sees an empty result set, which must **not** be reported as "no tasks" (that would be
   indistinguishable from the unreadable-row rule and equally misleading).

**Un-enrolment** requires an `aal2` session — otherwise a stolen `aal1` session could
disable MFA.

## 4. Platform notes

- **Windows** should be the primary enrolment surface. Displaying a QR on the phone you are
  meant to scan it with is awkward, so the phone must offer the secret as copyable text and
  a "already have it, enter code" path.
- **Android/iOS** need the code prompt in the sign-in flow, not a separate screen — the
  session is unusable between `aal1` and `aal2`.
- **All three clients** need the challenge step before §2 is enforced.

## 5. Rollout order

1. Implement the challenge step in **all** clients and release them.
2. Give users time to update — an old client is a locked-out client.
3. Only then apply the `aal2` policy migration.
4. Enrolment stays optional per user; the policy change makes it *enforced* for accounts
   that have a verified factor. Requiring `aal2` for accounts with **no** factor would lock
   out everyone, so the policy needs care, or enrolment must become mandatory in one step.

**Resolved 2026-07-22:** conditional enforcement. The policy requires `aal2` only of
accounts that have a verified factor; an account with none is unaffected. Universal
enforcement was rejected outright — sync is opt-in on top of an app that needs no account
at all, so MFA cannot become a precondition for using it.

Verified against the live database before and after applying, all four paths:
`aal1` + enrolled → 0 rows and writes rejected by name; `aal2` + enrolled → read and write
both succeed; `aal1` + no factor → allowed; helper returns `false` for a factor-less user
(i.e. it does not fail open).

## 6. Lockout and recovery

Supabase does not issue TOTP recovery codes. Before §4 landed, a lost authenticator was a
client-side inconvenience; after it, the database itself refuses the row, and the only way
back was admin intervention against `auth.mfa_factors`. For a product with no support desk
that is account loss.

**Shipped 2026-07-22** as `supabase/migrations/20260722053823_add_mfa_recovery_codes.sql`.

### The constraint that dictates the design

Only GoTrue can mint an `aal2` token, and only in exchange for a valid TOTP code. Neither a
client nor a database function can produce one. **A recovery code therefore cannot log you
in.** The only thing it can do is *remove the factor*, returning the account to "no MFA
enrolled", at which point `has_verified_mfa()` goes false and the existing `aal1` session
regains access.

So redeeming a code **turns two-factor off**. It is not a one-time bypass, and both clients
say so in those words — the expectation otherwise is that it signs you in this once, and a
user who believes that will not re-enrol.

### Shape

- 10 codes, 10 characters each from `23456789ABCDEFGHJKLMNPQRSTUVWXYZ` (no `0/O/1/I/L`,
  because these get written on paper and typed back), formatted `XXXXX-XXXXX`. 50 bits, and
  useless without the account password.
- Only bcrypt hashes are stored. `mfa_recovery_codes` has RLS on with **no policies** and
  privileges revoked from `authenticated`: the two `security definer` functions are the only
  things that touch it.
- `generate_mfa_recovery_codes()` **requires `aal2`**. This is load-bearing: without it an
  `aal1` session could mint a fresh set and immediately redeem one to strip its own MFA — a
  complete bypass of the factor blocking it.
- `redeem_mfa_recovery_code(code)` is callable at `aal1` by design; a valid session already
  proves the password. It returns a bare boolean and never reveals how many codes remain.
- Redemption deletes every remaining code, since they protect a factor that no longer exists.

Verified against the live database in rolled-back transactions: `aal1` generation refused;
10 distinct well-formed codes with 10 hashes stored; redemption at `aal1` removes the
factor, clears the codes, flips `has_verified_mfa()` to false and restores row access;
lower-case and space-separated input accepted; wrong/empty/short/null codes rejected with
the factor intact; another user's code rejected with the victim's factor intact.

### Still unresolved

The **sync passphrase** has no equivalent and remains the larger risk: recovery codes
restore *account access*, not decryption. An enrolled user has two independent secrets and
only one of them is now recoverable. The UI states this explicitly wherever codes appear,
but stating it is not solving it.

There is also no rate limit on redemption — Supabase has no per-RPC throttle, and the
password requirement is currently carrying that weight.

## 7. Open questions

- ~~Does the `aal2` policy apply to all accounts, or only those with a verified factor?~~
  Resolved 2026-07-22 — only those with a verified factor. See §5.
- ~~Should Hatch issue its own recovery codes (random, shown once, stored hashed) to
  compensate for Supabase not providing any?~~ Resolved 2026-07-22 — yes, shipped. See §6.
  The top open risk is now the **passphrase** dead-end, which recovery codes do not address.
- Does MFA apply to the GitHub OAuth path, or only password sign-in? Supabase applies AAL
  uniformly, so OAuth users would also be challenged — which may surprise them.
- Is MFA worth the lockout risk for a local-first app where the server holds only ciphertext
  and the authoritative copy lives on the user's own machine?
