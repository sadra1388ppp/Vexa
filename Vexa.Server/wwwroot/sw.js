const CACHE = 'vexa-mobile-v1';
const ASSETS = ['/', '/app.css', '/app.js', '/manifest.webmanifest'];
self.addEventListener('install', event => event.waitUntil(caches.open(CACHE).then(cache => cache.addAll(ASSETS)).then(() => self.skipWaiting())));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', event => {
  if (event.request.method !== 'GET' || !new URL(event.request.url).pathname.startsWith('/')) return;
  event.respondWith(fetch(event.request).catch(() => caches.match(event.request)));
});
