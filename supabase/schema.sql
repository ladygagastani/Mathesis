-- Μάθησις: accounts, library sync and the forum.
--
-- Run this once in a new Supabase project (SQL editor → New query → paste →
-- Run). It is safe to run again: every statement checks before it creates.
-- Setup steps for the project itself (email template, site URL, keys) are in
-- supabase/README.md.
--
-- Who may do what is decided here, by row-level security, not by the app:
--   · everyone (signed in or not) can read the forum;
--   · a signed-in reader can post, and delete their own posts and threads;
--   · only moderators (profiles.is_admin) can change a bug report's status or
--     remove other people's posts;
--   · a signed-in reader can report a post; only moderators can see reports;
--   · a library can only ever be read or written by its owner;
--   · a reader can delete their own account and everything stored with it.

-- ---------------------------------------------------------------------------
-- profiles: the name shown on forum posts
-- ---------------------------------------------------------------------------

create table if not exists public.profiles (
  id           uuid primary key references auth.users (id) on delete cascade,
  display_name text not null default ''
               check (char_length(display_name) <= 40),
  is_admin     boolean not null default false,
  created_at   timestamptz not null default now()
);

alter table public.profiles enable row level security;

drop policy if exists "profiles are public" on public.profiles;
create policy "profiles are public" on public.profiles
  for select using (true);

drop policy if exists "own profile insert" on public.profiles;
create policy "own profile insert" on public.profiles
  for insert with check (auth.uid() = id and is_admin = false);

drop policy if exists "own profile update" on public.profiles;
create policy "own profile update" on public.profiles
  for update using (auth.uid() = id)
  with check (auth.uid() = id);

-- Nobody makes themselves a moderator: is_admin can only be changed from the
-- dashboard (as the service role), never through the API.
create or replace function public.keep_admin_flag() returns trigger
language plpgsql as $$
begin
  if auth.role() <> 'service_role' then
    new.is_admin := old.is_admin;
  end if;
  return new;
end $$;

drop trigger if exists keep_admin_flag on public.profiles;
create trigger keep_admin_flag before update on public.profiles
  for each row execute function public.keep_admin_flag();

create or replace function public.is_moderator() returns boolean
language sql stable security definer set search_path = public as $$
  select coalesce((select is_admin from public.profiles where id = auth.uid()), false)
$$;

-- ---------------------------------------------------------------------------
-- libraries: each reader's bookmarks, notes, words and places, as one JSON
-- document. The app merges item by item (see LibraryData.merge), so the
-- server only has to store the latest copy.
-- ---------------------------------------------------------------------------

create table if not exists public.libraries (
  user_id    uuid primary key references auth.users (id) on delete cascade,
  data       jsonb not null default '{}'::jsonb
             check (pg_column_size(data) < 5000000),
  updated_at timestamptz not null default now()
);

alter table public.libraries enable row level security;

drop policy if exists "own library" on public.libraries;
create policy "own library" on public.libraries
  for all using (auth.uid() = user_id) with check (auth.uid() = user_id);

create or replace function public.touch_updated_at() returns trigger
language plpgsql as $$
begin
  new.updated_at := now();
  return new;
end $$;

drop trigger if exists touch_library on public.libraries;
create trigger touch_library before insert or update on public.libraries
  for each row execute function public.touch_updated_at();

-- ---------------------------------------------------------------------------
-- forum
-- ---------------------------------------------------------------------------

create table if not exists public.forum_threads (
  id           uuid primary key default gen_random_uuid(),
  category     text not null
               check (category in ('square', 'passages', 'debate', 'learning', 'bugs', 'suggestions')),
  title        text not null check (char_length(title) between 3 and 140),
  body         text not null check (char_length(body) between 1 and 8000),
  work         text not null default '' check (char_length(work) <= 40),
  ref          text not null default '' check (char_length(ref) <= 40),
  status       text not null default ''
               check (status in ('', 'open', 'confirmed', 'fixed', 'wontfix')),
  author_id    uuid not null references auth.users (id) on delete cascade,
  author_name  text not null default '',
  reply_count  integer not null default 0,
  created_at   timestamptz not null default now(),
  last_post_at timestamptz not null default now()
);

-- Boards added after the table was first created: widen the check on a table
-- that already exists (the "create table" above only applies to a new one).
alter table public.forum_threads drop constraint if exists forum_threads_category_check;
alter table public.forum_threads add constraint forum_threads_category_check
  check (category in ('square', 'passages', 'debate', 'learning', 'bugs', 'suggestions'));

create index if not exists forum_threads_board on public.forum_threads (category, last_post_at desc);
create index if not exists forum_threads_passage on public.forum_threads (work, ref);

create table if not exists public.forum_posts (
  id          uuid primary key default gen_random_uuid(),
  thread_id   uuid not null references public.forum_threads (id) on delete cascade,
  body        text not null check (char_length(body) between 1 and 8000),
  author_id   uuid not null references auth.users (id) on delete cascade,
  author_name text not null default '',
  created_at  timestamptz not null default now()
);

create index if not exists forum_posts_thread on public.forum_posts (thread_id, created_at);

alter table public.forum_threads enable row level security;
alter table public.forum_posts enable row level security;

drop policy if exists "threads are public" on public.forum_threads;
create policy "threads are public" on public.forum_threads for select using (true);

drop policy if exists "post a thread" on public.forum_threads;
create policy "post a thread" on public.forum_threads
  for insert with check (auth.uid() = author_id);

drop policy if exists "moderate threads" on public.forum_threads;
create policy "moderate threads" on public.forum_threads
  for update using (public.is_moderator());

drop policy if exists "delete own or moderate thread" on public.forum_threads;
create policy "delete own or moderate thread" on public.forum_threads
  for delete using (auth.uid() = author_id or public.is_moderator());

drop policy if exists "posts are public" on public.forum_posts;
create policy "posts are public" on public.forum_posts for select using (true);

drop policy if exists "reply" on public.forum_posts;
create policy "reply" on public.forum_posts
  for insert with check (auth.uid() = author_id);

drop policy if exists "delete own or moderate post" on public.forum_posts;
create policy "delete own or moderate post" on public.forum_posts
  for delete using (auth.uid() = author_id or public.is_moderator());

-- The author's name comes from their profile, never from what the client
-- sends; new threads start with the status the board allows; and a reader can
-- post at most 8 times in 10 minutes (threads and replies together).
create or replace function public.forum_before_insert() returns trigger
language plpgsql security definer set search_path = public as $$
declare
  recent integer;
begin
  select count(*) into recent from (
    select 1 from public.forum_threads where author_id = auth.uid() and created_at > now() - interval '10 minutes'
    union all
    select 1 from public.forum_posts where author_id = auth.uid() and created_at > now() - interval '10 minutes'
  ) x;
  if recent >= 8 then
    raise exception 'You are posting very quickly. Please wait a few minutes.';
  end if;
  new.author_name := coalesce(nullif((select display_name from public.profiles where id = auth.uid()), ''), 'A reader');
  new.created_at := now();
  if tg_table_name = 'forum_threads' then
    new.status := case when new.category = 'bugs' then 'open' else '' end;
    new.reply_count := 0;
    new.last_post_at := now();
  end if;
  return new;
end $$;

drop trigger if exists forum_threads_before_insert on public.forum_threads;
create trigger forum_threads_before_insert before insert on public.forum_threads
  for each row execute function public.forum_before_insert();

drop trigger if exists forum_posts_before_insert on public.forum_posts;
create trigger forum_posts_before_insert before insert on public.forum_posts
  for each row execute function public.forum_before_insert();

-- Moderators change a thread's status only; its text and author stay as posted.
create or replace function public.forum_threads_guard() returns trigger
language plpgsql as $$
begin
  new.title := old.title; new.body := old.body; new.category := old.category;
  new.author_id := old.author_id; new.author_name := old.author_name;
  new.work := old.work; new.ref := old.ref; new.created_at := old.created_at;
  return new;
end $$;

drop trigger if exists forum_threads_guard on public.forum_threads;
create trigger forum_threads_guard before update on public.forum_threads
  for each row when (pg_trigger_depth() = 0) execute function public.forum_threads_guard();

-- Replies keep the thread's count and "last active" time current.
create or replace function public.forum_posts_after() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if tg_op = 'INSERT' then
    update public.forum_threads
       set reply_count = reply_count + 1, last_post_at = new.created_at
     where id = new.thread_id;
  elsif tg_op = 'DELETE' then
    update public.forum_threads
       set reply_count = greatest(reply_count - 1, 0)
     where id = old.thread_id;
  end if;
  return null;
end $$;

drop trigger if exists forum_posts_after on public.forum_posts;
create trigger forum_posts_after after insert or delete on public.forum_posts
  for each row execute function public.forum_posts_after();

-- A changed display name shows on everything that person has posted.
create or replace function public.profiles_rename() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if new.display_name is distinct from old.display_name then
    update public.forum_threads set author_name = coalesce(nullif(new.display_name, ''), 'A reader') where author_id = new.id;
    update public.forum_posts   set author_name = coalesce(nullif(new.display_name, ''), 'A reader') where author_id = new.id;
  end if;
  return new;
end $$;

drop trigger if exists profiles_rename on public.profiles;
create trigger profiles_rename after update on public.profiles
  for each row execute function public.profiles_rename();

-- ---------------------------------------------------------------------------
-- reports: a reader flags a thread or a reply for the moderators. Only
-- moderators can read, resolve or remove reports; a reader can file at most
-- 20 an hour, and one per post.
-- ---------------------------------------------------------------------------

create table if not exists public.forum_reports (
  id           uuid primary key default gen_random_uuid(),
  thread_id    uuid not null references public.forum_threads (id) on delete cascade,
  post_id      uuid references public.forum_posts (id) on delete cascade,
  reason       text not null check (reason in ('spam', 'abuse', 'offtopic', 'other')),
  note         text not null default '' check (char_length(note) <= 1000),
  thread_title text not null default '' check (char_length(thread_title) <= 200),
  excerpt      text not null default '' check (char_length(excerpt) <= 400),
  reporter_id  uuid not null references auth.users (id) on delete cascade,
  status       text not null default 'open' check (status in ('open', 'done')),
  created_at   timestamptz not null default now()
);

create unique index if not exists forum_reports_once
  on public.forum_reports (reporter_id, thread_id, coalesce(post_id, '00000000-0000-0000-0000-000000000000'::uuid));
create index if not exists forum_reports_open on public.forum_reports (status, created_at desc);

alter table public.forum_reports enable row level security;

drop policy if exists "file a report" on public.forum_reports;
create policy "file a report" on public.forum_reports
  for insert with check (auth.uid() = reporter_id and status = 'open');

drop policy if exists "moderators read reports" on public.forum_reports;
create policy "moderators read reports" on public.forum_reports
  for select using (public.is_moderator());

drop policy if exists "moderators resolve reports" on public.forum_reports;
create policy "moderators resolve reports" on public.forum_reports
  for update using (public.is_moderator());

drop policy if exists "moderators remove reports" on public.forum_reports;
create policy "moderators remove reports" on public.forum_reports
  for delete using (public.is_moderator());

create or replace function public.forum_reports_before_insert() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if (select count(*) from public.forum_reports
       where reporter_id = auth.uid() and created_at > now() - interval '1 hour') >= 20 then
    raise exception 'You have sent a lot of reports in the last hour. Please wait a while.';
  end if;
  new.created_at := now();
  new.status := 'open';
  return new;
end $$;

drop trigger if exists forum_reports_before_insert on public.forum_reports;
create trigger forum_reports_before_insert before insert on public.forum_reports
  for each row execute function public.forum_reports_before_insert();

-- ---------------------------------------------------------------------------
-- deleting your own account: removes the sign-in and, through "on delete
-- cascade", the profile, the synced library, every thread, reply and report
-- the reader made. Nothing is kept.
-- ---------------------------------------------------------------------------

create or replace function public.delete_my_account() returns void
language plpgsql security definer set search_path = public, auth as $$
begin
  if auth.uid() is null then
    raise exception 'Sign in first.';
  end if;
  delete from auth.users where id = auth.uid();
end $$;

revoke all on function public.delete_my_account() from public, anon;
grant execute on function public.delete_my_account() to authenticated;
