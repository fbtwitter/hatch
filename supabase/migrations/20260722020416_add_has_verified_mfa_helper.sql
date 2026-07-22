-- Reports whether the CALLING user has a verified MFA factor.
--
-- security definer because `authenticated` has no privileges on auth.mfa_factors: a plain
-- subquery in the policy errors with "permission denied for table mfa_factors". Granting
-- SELECT instead would be worse than useless — auth.mfa_factors has RLS enabled with no
-- policy for `authenticated`, so the count would silently come back 0 and the aal2 gate
-- would never fire on any account.
--
-- Discloses only whether the caller themselves has MFA, which is their own information.
create or replace function public.has_verified_mfa()
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
  select exists (
    select 1
    from auth.mfa_factors
    where user_id = (select auth.uid())
      and status = 'verified'
  );
$$;

revoke execute on function public.has_verified_mfa() from public, anon;
grant execute on function public.has_verified_mfa() to authenticated;
