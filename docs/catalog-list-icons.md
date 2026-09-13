# Catalog list icons

Catalog authors can supply an optional `iconUrl` at the top level of a catalog document:

```json
{
  "name": "Example apps",
  "description": "A curated collection of apps",
  "version": "1.0.0",
  "iconUrl": "https://example.com/catalog-icon.png",
  "apps": []
}
```

Use a square PNG (for example, 128 × 128 pixels) with enough padding to remain legible at 48 × 48. Images retain their proportions and are not cropped. Absolute HTTP and HTTPS URLs are supported; relative paths and local files are not.

The launcher loads list icons asynchronously through its shared artwork cache. A built-in catalog symbol appears while loading and when the URL is missing, invalid, or the image cannot be decoded or downloaded. Cached images remain available offline.

Omitting `iconUrl`, setting it to `null`, or providing a blank value removes a previously supplied icon on the next successful catalog refresh. A failed catalog fetch keeps the cached metadata. Existing catalogs need no changes, and this field does not affect review counts or app icons.

When replacing an image, change its URL (for example, `catalog-icon-v2.png`) so clients do not keep using the cached image at the old URL.
