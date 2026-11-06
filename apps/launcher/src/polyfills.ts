/**
 * What older Android System WebViews lack, which the launcher still runs on (`minSdk` 24, and a device's WebView can be
 * years behind). Imported first in main.tsx. Without `Object.hasOwn` (before Chrome 93), every app card crashed the page.
 */
if (!Object.hasOwn) {
  Object.defineProperty(Object, "hasOwn", {
    value: (object: object, key: PropertyKey) => Object.prototype.hasOwnProperty.call(Object(object), key),
    configurable: true,
    writable: true,
  });
}

export {};
