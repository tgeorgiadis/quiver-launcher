/**
 * The computer's PC specs, sent with the player's feedback (feedback.tsx) so the app's developer, and other players
 * unless the player unticks them, know what it ran on: the system and its version, processor, graphics card and
 * memory. The Rust side reads the hardware (specs.rs); a graphics card it can't name comes from WebGL. Read once, when
 * a feedback form first opens, and never sent with usage data.
 */
import type { FeedbackSpecs } from "@quiverlauncher/api";
import { native, type Config } from "./native";

/** Linux distributions as they write their own names, by the ID in os-release. */
const DISTRIBUTIONS: Record<string, string> = {
  arch: "Arch Linux",
  bazzite: "Bazzite",
  cachyos: "CachyOS",
  chimeraos: "ChimeraOS",
  debian: "Debian",
  endeavouros: "EndeavourOS",
  fedora: "Fedora",
  linuxmint: "Linux Mint",
  manjaro: "Manjaro",
  nixos: "NixOS",
  nobara: "Nobara",
  "opensuse-tumbleweed": "openSUSE Tumbleweed",
  "opensuse-leap": "openSUSE Leap",
  pop: "Pop!_OS",
  steamos: "SteamOS",
  ubuntu: "Ubuntu",
  zorin: "Zorin OS",
};

/**
 * The system and its version as people say it, from device.rs's reading: "11.26200" on Windows is "Windows 11",
 * "steamos 3.7.13" is "SteamOS 3.7.13", "15.5" on a Mac is "macOS 15.5".
 */
export function systemName(os: Config["os"], version: string | null | undefined): string | undefined {
  const v = version?.trim();
  if (!v) return undefined;
  if (os === "windows") return `Windows ${v.split(".")[0]}`;
  if (os === "macos") return `macOS ${v}`;
  if (os === "android") return `Android ${v}`;
  if (os === "linux") {
    const [id, ...rest] = v.split(" ");
    const name = DISTRIBUTIONS[id] ?? id.charAt(0).toUpperCase() + id.slice(1);
    return [name, ...rest].join(" ");
  }
  return undefined;
}

/**
 * The graphics card WebGL draws with, from its renderer string: "ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 (0x00002504)
 * Direct3D11 vs_5_0 ps_5_0, D3D11)" is "NVIDIA GeForce RTX 3060"; "Mesa Intel(R) UHD Graphics 620 (KBL GT2)" is "Intel
 * UHD Graphics 620". A software renderer names no card.
 */
export function rendererName(renderer: string | null | undefined): string | undefined {
  let name = renderer?.trim();
  if (!name) return undefined;
  const angle = /^ANGLE \((?:[^,]*), (.+?)(?:, [^,]*)?\)$/.exec(name);
  if (angle) name = angle[1];
  name = name
    .replace(/^.*Renderer:\s*/, "")
    .replace(/\s*\(0x[0-9a-f]+\)/i, "")
    .replace(/\s+(Direct3D\d*|OpenGL|Vulkan|Metal)\b.*$/i, "")
    .replace(/^Mesa\s+/, "")
    .replace(/\s*\([^)]*\)\s*$/, "")
    .replace(/\(R\)|\(TM\)/gi, "")
    .replace(/\s+/g, " ")
    .trim();
  if (!name || /swiftshader|llvmpipe|softpipe|software|basic render/i.test(name)) return undefined;
  return name;
}

function webglRenderer(): string | undefined {
  try {
    const gl = document.createElement("canvas").getContext("webgl");
    const info = gl?.getExtension("WEBGL_debug_renderer_info");
    return info ? (gl!.getParameter(info.UNMASKED_RENDERER_WEBGL) as string) : undefined;
  } catch {
    return undefined;
  }
}

let read: Promise<FeedbackSpecs | null> | undefined;

/** This computer's specs, or null when nothing could be read. */
export function pcSpecs(config: Pick<Config, "os" | "osVersion">): Promise<FeedbackSpecs | null> {
  read ??= native.pcSpecs().then(
    (hardware) => hardware,
    () => ({ cpu: null, gpu: null, ramGb: null }),
  ).then((hardware) => {
    const specs: FeedbackSpecs = {};
    const osVersion = systemName(config.os, config.osVersion);
    const gpu = hardware.gpu ?? rendererName(webglRenderer());
    if (osVersion) specs.osVersion = osVersion;
    if (hardware.cpu) specs.cpu = hardware.cpu;
    if (gpu) specs.gpu = gpu;
    if (hardware.ramGb) specs.ramGb = hardware.ramGb;
    return Object.keys(specs).length ? specs : null;
  });
  return read;
}

/** For tests: read them again. */
export function forgetSpecs() {
  read = undefined;
}
