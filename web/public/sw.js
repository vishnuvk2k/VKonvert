// Keeps VKonvert working offline: pages and tools are cached the first time they're used.
const CACHE = "vk-2.0-2026-11-09";
const CORE = ["/", "/convert-image", "/heic-to-jpg", "/png-to-jpg", "/jpg-to-png", "/webp-to-jpg", "/jpg-to-webp", "/compress-image", "/resize-image", "/remove-photo-info", "/jpg-to-pdf", "/merge-pdf", "/split-pdf", "/compress-pdf", "/pdf-to-jpg", "/rotate-pdf", "/compress-image-to-20kb", "/compress-image-to-50kb", "/compress-image-to-100kb", "/compress-image-to-200kb", "/passport-size-photo", "/heic-to-png", "/pdf-to-png", "/download", "/privacy", "/assets/vk.css", "/assets/vk.js", "/assets/favicon-32.png", "/assets/icon-192.png"];
self.addEventListener("install", e => e.waitUntil(caches.open(CACHE).then(c => c.addAll(CORE)).then(() => self.skipWaiting())));
self.addEventListener("activate", e => e.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim())));
self.addEventListener("fetch", e => {
  const req = e.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.origin === location.origin && url.pathname.startsWith("/assets/lib/")) {
    // Libraries never change for a given version: cache first.
    e.respondWith(caches.match(req).then(hit => hit || fetch(req).then(res => { const copy = res.clone(); caches.open(CACHE).then(c => c.put(req, copy)); return res; })));
    return;
  }
  // Everything else: network first, so updates show up; cache when offline.
  e.respondWith(fetch(req).then(res => {
    if (res.ok && (url.origin === location.origin || url.hostname.endsWith("gstatic.com") || url.hostname.endsWith("googleapis.com"))) {
      const copy = res.clone(); caches.open(CACHE).then(c => c.put(req, copy));
    }
    return res;
  }).catch(() => caches.match(req).then(hit => hit || caches.match(url.pathname + ".html"))));
});
