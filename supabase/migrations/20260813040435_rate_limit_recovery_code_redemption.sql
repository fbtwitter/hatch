-- redeem_mfa_recovery_code is callable at aal1 with no throttle of its own, and Supabase
-- has no per-RPC rate limit. A session holding a stolen password (the only prerequisite —
-- aal1 needs no TOTP) could call it as fast as PostgREST allows, leaving the account
-- password as the only thing standing between an attacker and unlimited guesses at the
-- 32^10 code space. This adds a per-user lockout enforced inside the same
-- security-definer function so it can't be bypassed by any other route.
create table public.mfa_recovery_attempts (
  user_id      uuid primary key references auth.users(id) on delete cascade,
  failed_count int not null default 0,
  locked_until timestamptz
);

-- Same lockdown as mfa_recovery_codes: nothing reaches this table except the
-- security-definer functions below.
alter table public.mfa_recovery_attempts enable row level security;
revoke all on public.mfa_recovery_attempts from anon, authenticated;

create or replace function public.redeem_mfa_recovery_code(code text)
returns boolean
language plpgsql
security definer
set search_path = ''
as $$
declare
  uid          uuid := (select auth.uid());
  normalized   text;
  match_id     bigint;
  max_attempts constant int := 5;
  lockout_for  constant interval := interval '15 minutes';
  attempt_row  public.mfa_recovery_attempts%rowtype;
  new_count    int;
begin
  if uid is null then
    raise exception 'Not signed in';
  end if;

  select * into attempt_row from public.mfa_recovery_attempts where user_id = uid;
  if found and attempt_row.locked_until is not null and attempt_row.locked_until > now() then
    raise exception 'Too many attempts. Try again later.';
  end if;

  -- Accept whatever shape the user typed: spaces, lower case, missing or extra dashes.
  -- A malformed code falls through with match_id left null rather than returning early, so
  -- it counts toward the lockout the same as a well-formed-but-wrong one — an attacker can't
  -- dodge the counter by sending garbage on some attempts.
  normalized := upper(regexp_replace(coalesce(code, ''), '[^0-9A-Za-z]', '', 'g'));
  if length(normalized) = 10 then
    normalized := substr(normalized, 1, 5) || '-' || substr(normalized, 6, 5);

    select id into match_id
    from public.mfa_recovery_codes
    where user_id = uid
      and code_hash = extensions.crypt(normalized, code_hash)
    limit 1;
  end if;

  if match_id is null then
    -- A row past its own lockout window starts a fresh count instead of compounding forever
    -- off a stale streak.
    new_count := case
      when found and (attempt_row.locked_until is null or attempt_row.locked_until <= now())
      then attempt_row.failed_count + 1
      else 1
    end;

    insert into public.mfa_recovery_attempts (user_id, failed_count, locked_until)
    values (
      uid,
      new_count,
      case when new_count >= max_attempts then now() + lockout_for else null end
    )
    on conflict (user_id) do update
      set failed_count = excluded.failed_count,
          locked_until = excluded.locked_until;

    -- Boolean only: never disclose how many codes remain, which one matched, or whether
    -- this attempt triggered the lockout.
    return false;
  end if;

  delete from auth.mfa_factors where user_id = uid;
  -- The remaining codes protect a factor that no longer exists.
  delete from public.mfa_recovery_codes where user_id = uid;
  delete from public.mfa_recovery_attempts where user_id = uid;

  return true;
end;
$$;

-- Requires aal2 (already proven ownership via TOTP), so clearing any stale lockout here
-- doesn't hand out a free reset — it just stops a previous mistyped-code streak against the
-- old code set from penalizing a legitimate new one.
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
  delete from public.mfa_recovery_attempts where user_id = uid;

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
