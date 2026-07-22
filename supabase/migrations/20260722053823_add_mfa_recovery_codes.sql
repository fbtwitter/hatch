-- Supabase issues no TOTP recovery codes of its own. Since the aal2 policy landed
-- (20260722053823 is the answer to 20260722020510), a lost authenticator means the database
-- itself refuses the row — recoverable only by an admin deleting the factor. These make
-- that self-service.
--
-- THE CONSTRAINT THAT DICTATES THE DESIGN: only GoTrue can mint an aal2 token, and only in
-- exchange for a valid TOTP code. Neither a client nor a database function can produce one.
-- So a recovery code cannot log you in. It can only REMOVE the factor, returning the
-- account to "no MFA enrolled", at which point has_verified_mfa() goes false and the
-- existing aal1 session regains access. Redeeming therefore turns two-factor OFF; it is not
-- a one-time bypass, and the UI on both clients says so in those words.
create table public.mfa_recovery_codes (
  id         bigint generated always as identity primary key,
  user_id    uuid not null references auth.users(id) on delete cascade,
  code_hash  text not null,
  created_at timestamptz not null default now()
);

create index mfa_recovery_codes_user_id_idx on public.mfa_recovery_codes (user_id);

-- RLS on with deliberately NO policies, and privileges revoked: nothing reaches this table
-- except the two security-definer functions below. A user has no reason to read even their
-- own hashes, and withholding the grant means a compromised `authenticated` role cannot
-- harvest or forge them.
alter table public.mfa_recovery_codes enable row level security;
revoke all on public.mfa_recovery_codes from anon, authenticated;

-- Returns the plaintext codes ONCE; only hashes are kept.
--
-- The aal2 requirement is the load-bearing part. Without it an aal1 session could mint a
-- fresh set of codes and immediately redeem one to strip its own MFA — a complete bypass of
-- the factor it was supposed to be blocked by.
create or replace function public.generate_mfa_recovery_codes()
returns text[]
language plpgsql
security definer
set search_path = ''
as $$
declare
  uid      uuid := (select auth.uid());
  -- No 0/O/1/I/L: these get written down and typed back by hand.
  alphabet constant text := '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';
  codes    text[] := '{}';
  code     text;
  i        int;
  j        int;
begin
  if uid is null then
    raise exception 'Not signed in';
  end if;

  if (select auth.jwt() ->> 'aal') is distinct from 'aal2' then
    raise exception 'Recovery codes require a session that has passed two-factor verification';
  end if;

  -- A new set invalidates the old one, so a printout that leaked is not still live.
  delete from public.mfa_recovery_codes where user_id = uid;

  for i in 1..10 loop
    code := '';
    for j in 1..10 loop
      -- 256 % 32 = 0, so no modulo bias.
      code := code || substr(alphabet, 1 + (get_byte(extensions.gen_random_bytes(1), 0) % 32), 1);
    end loop;
    code := substr(code, 1, 5) || '-' || substr(code, 6, 5);
    codes := codes || code;

    insert into public.mfa_recovery_codes (user_id, code_hash)
    values (uid, extensions.crypt(code, extensions.gen_salt('bf')));
  end loop;

  return codes;
end;
$$;

-- Deliberately callable at aal1 — working from a session that cannot pass the factor is the
-- entire point. It is not an unauthenticated route: a valid session means the account
-- password has already been proven.
create or replace function public.redeem_mfa_recovery_code(code text)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare
  uid        uuid := (select auth.uid());
  normalized text;
  match_id   bigint;
begin
  if uid is null then
    raise exception 'Not signed in';
  end if;

  -- Accept whatever shape the user typed: spaces, lower case, missing or extra dashes.
  normalized := upper(regexp_replace(coalesce(code, ''), '[^0-9A-Za-z]', '', 'g'));
  if length(normalized) <> 10 then
    return false;
  end if;
  normalized := substr(normalized, 1, 5) || '-' || substr(normalized, 6, 5);

  select id into match_id
  from public.mfa_recovery_codes
  where user_id = uid
    and code_hash = extensions.crypt(normalized, code_hash)
  limit 1;

  -- Boolean only: never disclose how many codes remain or which one matched.
  if match_id is null then
    return false;
  end if;

  delete from auth.mfa_factors where user_id = uid;
  -- The remaining codes protect a factor that no longer exists.
  delete from public.mfa_recovery_codes where user_id = uid;

  return true;
end;
$$;

revoke execute on function public.generate_mfa_recovery_codes() from public, anon;
grant  execute on function public.generate_mfa_recovery_codes() to authenticated;
revoke execute on function public.redeem_mfa_recovery_code(text) from public, anon;
grant  execute on function public.redeem_mfa_recovery_code(text) to authenticated;
