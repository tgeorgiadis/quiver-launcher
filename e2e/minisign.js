/**
 * Minisign keys and signatures, as `tauri signer` makes them for updates
 * (prehashed Ed25519, base64 of the minisign text), so the mock can sign
 * launcher updates the way a release would.
 */
import { createHash, generateKeyPairSync, randomBytes, sign } from "node:crypto";

const b64 = (b) => Buffer.from(b).toString("base64");

export function minisignKey() {
  const { publicKey, privateKey } = generateKeyPairSync("ed25519");
  const keyId = randomBytes(8);
  const raw = Buffer.from(publicKey.export({ format: "jwk" }).x, "base64url");
  const text = `untrusted comment: minisign public key ${keyId.toString("hex").toUpperCase()}\n${b64(Buffer.concat([Buffer.from("Ed"), keyId, raw]))}\n`;
  return {
    /** The public key as Tauri's config takes it. */
    publicKey: b64(text),
    /** A `.sig` file's contents for these bytes. */
    sign(bytes) {
      const signature = sign(null, createHash("blake2b512").update(bytes).digest(), privateKey);
      const trusted = `timestamp:${Math.floor(Date.now() / 1000)}\tfile:update`;
      const global = sign(null, Buffer.concat([signature, Buffer.from(trusted)]), privateKey);
      const sig = `untrusted comment: signature from tauri secret key\n${b64(Buffer.concat([Buffer.from("ED"), keyId, signature]))}\ntrusted comment: ${trusted}\n${b64(global)}\n`;
      return b64(sig);
    },
  };
}
