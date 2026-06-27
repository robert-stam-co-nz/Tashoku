# Colour Logic — PWA

## Why you can't just double-click index.html

Browsers block service workers (the thing that makes an app installable
and work offline) on the `file://` protocol — it's a security restriction,
not a bug here. To actually test installability, the app needs to be
served over `http://` or `https://`. Two easy ways to do that:

### Option A — test locally (no install/deploy needed)

From this folder, run one of:

```bash
python3 -m http.server 8000
# or, if you have Node:
npx serve .
```

Then open `http://localhost:8000` in Chrome on your phone (same Wi-Fi
network, use your computer's local IP instead of localhost) or in Chrome
desktop. You'll see the install prompt / "Install app" button once the
service worker registers, usually within a second or two of load.

### Option B — deploy it somewhere real (so you can actually install it on
your phone day-to-day)

Any static host works since this is just flat files — no build step, no
backend. Two free options:

**GitHub Pages**
1. Push this folder to a GitHub repo.
2. Repo Settings → Pages → set source to your main branch.
3. Your app will be live at `https://<username>.github.io/<repo>/`.

**Netlify** (drag-and-drop, no git needed)
1. Go to app.netlify.com/drop
2. Drag this whole folder onto the page.
3. You get a live HTTPS URL immediately.

Once it's live, open the URL on your phone in Chrome (Android) or Safari
(iOS) and use "Add to Home Screen" (iOS doesn't support the automatic
install prompt — that's an iOS limitation, not something fixable from this
end).

## What's in here

| File | Purpose |
|---|---|
| `index.html` | The game itself — same logic as before, plus PWA hooks |
| `manifest.json` | Tells the OS the app's name, icon, colours, display mode |
| `sw.js` | Service worker — caches the app shell so it works offline |
| `icons/` | App icons at the sizes Android/iOS/desktop expect |

## What's already wired up

- **Install prompt** — a custom "Install app" button appears (Chrome/Edge/
  Android only — iOS has no install API to hook into).
- **Share result** — once you solve a puzzle, a "Share result" button
  appears. It builds a Wordle-style PNG card (the solved grid in its real
  peg colours, plus difficulty/clue/hint/mistake stats) and, on phones
  with the Web Share API, opens the native share sheet with that image
  attached. Everywhere else it falls back to downloading the PNG and
  copying a text summary to the clipboard.
- **Haptics** — a short buzz on an invalid placement, a stronger pattern
  on running out of mistakes, a little flourish on solve. Only fires on
  devices that support `navigator.vibrate` (mobile Chrome/Android; iOS
  Safari doesn't expose this API at all).
- **Pencil marks, auto-candidates, and hints** — same gameplay as the
  standalone build: manual candidate marks, live auto-computed
  candidates, and a 3-hint cap. Three wrong placements ends the puzzle
  and reveals the solution.

## Extending it

The share text is built in `buildShareText()` and the share image in
`buildShareImage()`, both in `index.html` — those are the spots to
change the message format or card design (e.g. a timer, a streak count).

For anything that needs a backend (leaderboards, cross-device sync,
daily-puzzle-of-the-day), you'd need actual server infrastructure — the
service worker's fetch handler already has a network-first fallback path
for non-cached requests, so adding API calls won't fight with the
caching logic.

## Updating the app after you've changed something

Bump `CACHE_VERSION` at the top of `sw.js` (e.g. `'v2'` → `'v3'`) whenever
you change any cached file. The service worker uses that string to know
its own cache is stale and automatically deletes the old one — without
this, installed users would keep seeing a cached old version even after
you've redeployed.
