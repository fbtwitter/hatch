-- Stage 4 of docs/mfa-spec.md — the step that makes MFA real rather than decorative.
-- Until this, a session that skipped the client-side challenge could still read and write
-- the whole account.
--
-- RESTRICTIVE, so it ANDs with "own data only" rather than ORing. A permissive policy here
-- would widen access instead of narrowing it.
--
-- Conditional, not universal: requiring aal2 of every account would lock out everyone who
-- has not enrolled, and sync is opt-in on top of an app that needs no account at all (see
-- the HARD STOP in context/project-overview.md). MFA stays opt-in; once opted into, it is
-- enforced by the server rather than by client goodwill.
--
-- WITH CHECK as well as USING: both clients upsert, and a USING-only restrictive policy
-- leaves the INSERT path ungated.
create policy "aal2 required when the account has a verified factor"
on public.user_data
as restrictive
for all
to authenticated
using (
  (select auth.jwt() ->> 'aal') = 'aal2'
  or not public.has_verified_mfa()
)
with check (
  (select auth.jwt() ->> 'aal') = 'aal2'
  or not public.has_verified_mfa()
);

-- Rollback, should a client be stranded at aal1 with no way to reach an authenticator:
--   drop policy "aal2 required when the account has a verified factor" on public.user_data;
