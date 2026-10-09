/* Service Worker: lưu bản sao trang nghe thử sau khi đã tải khi máy chủ còn bật. */
const CACHE_NAME = 'vk-preview-all-v2';

self.addEventListener('install', function (event) {
    event.waitUntil(
        caches.open(CACHE_NAME).then(function (cache) {
            return cache.addAll(['/Listen/PreviewAll']).catch(function () { });
        }).then(function () { return self.skipWaiting(); })
    );
});

self.addEventListener('activate', function (event) {
    event.waitUntil(
        caches.keys().then(function (keys) {
            return Promise.all(
                keys.filter(function (k) { return k !== CACHE_NAME && k.indexOf('vk-preview') === 0; })
                    .map(function (k) { return caches.delete(k); })
            );
        }).then(function () { return self.clients.claim(); })
    );
});

self.addEventListener('fetch', function (event) {
    var req = event.request;
    if (req.method !== 'GET') return;

    event.respondWith(
        fetch(req)
            .then(function (response) {
                if (response && response.ok && req.url.indexOf(self.location.origin) === 0) {
                    var copy = response.clone();
                    caches.open(CACHE_NAME).then(function (cache) {
                        return cache.put(req, copy);
                    }).catch(function () { });
                }
                return response;
            })
            .catch(function () {
                return caches.match(req).then(function (cached) {
                    if (cached) return cached;
                    if (req.mode === 'navigate')
                        return caches.match('/Listen/PreviewAll');
                    return Promise.reject(new Error('offline'));
                });
            })
    );
});
