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
   `https://mathesis-zeta.vercel.app/` and `http://localhost:5173/`). The
   sign-in link always returns to the site's front page, whichever page the
   reader signed in from, so these root addresses are all that's needed.
4. Supabase's built-in email service sends only a few emails an hour, and
   only for trying things out. Before opening the site to the public, connect
   your own email sender: see **2b** below.

## 2b. Connect an email sender (before launch)

Every sign-in sends an email with a code. Supabase's own sender stops after a
few an hour, so on a busy day people would ask for a code and never get it
(the site then says so and asks them to try later). An email service fixes
that. It takes about half an hour, once, and the free plans are plenty.

**You need a domain name** (an address like `mathesis.org`, about £10 a year
from any domain seller), because email services only send for a domain you
own; they won't send "from" a Gmail address. The site itself can stay on
GitHub Pages.

The steps below use **Resend** (free: 3,000 emails a month, 100 a day). Brevo,
Postmark, Amazon SES and Mailgun work the same way: each gives you the same
five settings.

1. Sign up at resend.com. Under **Domains → Add domain**, enter your domain.
   Resend shows three or four DNS records; add them in your domain seller's
   DNS settings exactly as shown, then press **Verify** (it can take up to an
   hour).
2. Under **API Keys → Create API key**, create a key with "Sending access".
   Copy it: it is shown once.
3. In Supabase, **Project Settings → Authentication → SMTP Settings**, turn on
   **Enable Custom SMTP** and fill in:

   | Field | Value |
   |---|---|
   | Sender email | e.g. `hello@yourdomain.org` (any address at your domain) |
   | Sender name | `Μάθησις` |
   | Host | `smtp.resend.com` |
   | Port | `465` |
   | Username | `resend` |
   | Password | the API key from step 2 |

   Save.
4. **Authentication → Rate Limits**: raise **Rate limit for sending emails**
   (for example to 100 an hour). Supabase keeps it low until a custom sender
   is set.
5. Test: sign out on the site, sign in with your own email, and check the code
   arrives (look in spam the first time, and mark it "not spam").

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
Open, Confirmed, Fixed or Won't fix, delete anyone's posts, and see the
reports readers send: they appear at the top of the forum's front page for
moderators only, each with a "Dealt with" button. Nobody can make themselves a
moderator from the site.

## Updating an existing project

When `schema.sql` changes (the site's release notes say so), open **SQL
Editor**, paste the whole file again and **Run**. It is written to be run
more than once: it adds what is new and leaves your data alone. The September
2026 update adds the `forum_reports` table and the `delete_my_account`
function; until it is run, the Report button and Delete account answer with
an error.

## What is stored

| Table | What | Who can see it |
|---|---|---|
| `auth.users` (Supabase's own) | email address | only the project owner |
| `profiles` | the name shown on posts, moderator flag | everyone (names are public on posts) |
| `libraries` | each reader's bookmarks, notes, words, places, favourites | only that reader |
| `forum_threads`, `forum_posts` | the forum | everyone; posting needs an account |
| `forum_reports` | reports of posts: reason, note, an excerpt | only moderators; readers can only file them |

A reader can post at most 8 times in 10 minutes and report at most 20 times an
hour. A reader can delete their own account from the account page (the
`delete_my_account` function); that, like deleting a user in
**Authentication → Users**, deletes their profile, library, threads (with the
replies in them), replies and reports with them.
