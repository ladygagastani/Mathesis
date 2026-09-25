# Switching on accounts, sync and the forum

Everything else in Μάθησις runs in the reader's browser. Accounts, library
sync and the forum need a small server, and the app uses a free
[Supabase](https://supabase.com) project for it. Until one is connected, the
site works as before: My library stays in the browser, and the Forum and
Sign in pages say they aren't switched on yet (bug reports then go to GitHub
Issues).

It takes about fifteen minutes, once.

## 1. Create the project

1. Sign up at supabase.com and create a new project. Any region; note the
   database password somewhere safe (the app never needs it).
2. In the project, open **SQL Editor → New query**, paste the whole of
   `supabase/schema.sql`, and press **Run**. It creates the tables and the
   rules that decide who may read and write what.

## 2. Set up sign-in by email code

1. **Authentication → Sign In / Providers → Email**: leave Email enabled.
   Passwords are never used: the app only asks for a one-time code.
2. **Authentication → Email Templates → Magic Link**: make sure the email
   contains the code as well as the link. Replace the body with, for example:

   ```html
   <h2>Your Μάθησις sign-in code</h2>
   <p>Type this code on the sign-in page: <strong>{{ .Token }}</strong></p>
   <p>Or open this link on the same device: <a href="{{ .ConfirmationURL }}">sign in</a></p>
   <p>If you didn't ask to sign in, ignore this email.</p>
   ```

3. **Authentication → URL Configuration**: set **Site URL** to the address
   people use (for example `https://ladygagastani.github.io/Mathesis/`) and
   add every other address under **Redirect URLs** (for example
   `https://mathesis-zeta.vercel.app/` and `http://localhost:5173/`).
4. Supabase's built-in email service sends only a few emails an hour. Before
   opening the site to many people, connect your own email sender under
   **Project Settings → Authentication → SMTP**.

## 3. Give the site the project's address and public key

In **Project Settings → API** (or **Data API**), copy the **Project URL** and
the **anon public** key. The anon key is meant to be public: what it can do
is limited by the rules in `schema.sql`. Never use the `service_role` key.

- **GitHub Pages:** in the repository, **Settings → Secrets and variables →
  Actions → Variables**, add `SUPABASE_URL` and `SUPABASE_ANON_KEY`. The next
  publish picks them up.
- **Vercel:** **Project → Settings → Environment Variables**, add
  `VITE_SUPABASE_URL` and `VITE_SUPABASE_ANON_KEY`, then redeploy.
- **On your own computer:** create a file `.env.local` next to
  `package.json` with the same two `VITE_…` lines.

## 4. Make yourself a moderator

Sign in on the site once, then in Supabase open **Table Editor → profiles**,
find your row and set `is_admin` to `true`. Moderators can mark bug reports
Open, Confirmed, Fixed or Won't fix, and delete anyone's posts. Nobody can
make themselves a moderator from the site.

## What is stored

| Table | What | Who can see it |
|---|---|---|
| `auth.users` (Supabase's own) | email address | only the project owner |
| `profiles` | the name shown on posts, moderator flag | everyone (names are public on posts) |
| `libraries` | each reader's bookmarks, notes, words, places, favourites | only that reader |
| `forum_threads`, `forum_posts` | the forum | everyone; posting needs an account |

A reader can post at most 8 times in 10 minutes. Deleting a user in
**Authentication → Users** deletes their library and posts with them.
