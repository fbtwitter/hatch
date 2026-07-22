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

Supabase does not issue TOTP recovery codes. A user who loses their authenticator needs
admin intervention against the `auth.mfa_factors` table. For a product with no support desk
that is effectively account loss.

Combined with the passphrase having no recovery either, an enrolled user now has **two**
independent secrets, either of which can permanently cost them their data. This spec should
not be implemented before the passphrase dead-end is designed.

## 7. Open questions

- ~~Does the `aal2` policy apply to all accounts, or only those with a verified factor?~~
  Resolved 2026-07-22 — only those with a verified factor. See §5.
- Should Hatch issue its own recovery codes (random, shown once, stored hashed) to
  compensate for Supabase not providing any? **Now the top open risk**: with stage 4 live,
  a lost authenticator means the server itself refuses the data, so this is no longer
  recoverable by the client at all — only by an admin deleting the factor row.
- Does MFA apply to the GitHub OAuth path, or only password sign-in? Supabase applies AAL
  uniformly, so OAuth users would also be challenged — which may surprise them.
- Is MFA worth the lockout risk for a local-first app where the server holds only ciphertext
  and the authoritative copy lives on the user's own machine?
