-- public.user_data and its RLS policy predate migration tracking entirely (migrations
-- start at 20260722020416, the MFA work) — they were applied directly, so
-- 20260722020510_require_aal2_when_mfa_enrolled.sql already references public.user_data
-- without ever creating it, and a schema rebuilt from this folder alone would not
-- reproduce the table that gates every read/write in sync.
--
-- Backfilled from the live schema (information_schema / pg_constraint / pg_policies),
-- verified column-for-column rather than guessed. IF NOT EXISTS / DROP POLICY IF EXISTS
-- make this idempotent against already being live — it must not touch existing rows.
--
-- Table grants (anon/authenticated/postgres/service_role) are Supabase's default
-- public-schema grants applied automatically on table creation, not a deliberate one-off
-- like the MFA tables' `revoke all` — no explicit GRANT needed here to reproduce them.
create table if not exists public.user_data (
  user_id    uuid primary key references auth.users(id),
  tasks_json text not null default '{}',
  updated_at timestamptz not null default now()
);

alter table public.user_data enable row level security;

drop policy if exists "own data only" on public.user_data;

create policy "own data only"
on public.user_data
as permissive
for all
to public
using (auth.uid() = user_id)
with check (auth.uid() = user_id);
