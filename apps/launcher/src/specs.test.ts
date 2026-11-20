import { expect, it } from "vitest";
import { rendererName, systemName } from "./specs";

it("names the system as people say it", () => {
  expect(systemName("windows", "11.26200")).toBe("Windows 11");
  expect(systemName("windows", "10.19045")).toBe("Windows 10");
  expect(systemName("linux", "steamos 3.7.13")).toBe("SteamOS 3.7.13");
  expect(systemName("linux", "ubuntu 24.04")).toBe("Ubuntu 24.04");
  expect(systemName("linux", "arch")).toBe("Arch Linux");
  expect(systemName("linux", "gentoo 2.17")).toBe("Gentoo 2.17");
  expect(systemName("macos", "15.5")).toBe("macOS 15.5");
  expect(systemName("android", "14")).toBe("Android 14");
  expect(systemName("linux", null)).toBeUndefined();
  expect(systemName("unknown", "1")).toBeUndefined();
});

it("names the graphics card WebGL draws with, and no software renderer", () => {
  expect(rendererName("ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 (0x00002504) Direct3D11 vs_5_0 ps_5_0, D3D11)")).toBe("NVIDIA GeForce RTX 3060");
  expect(rendererName("ANGLE (Intel, Intel(R) UHD Graphics 630 (0x00003E92) Direct3D11 vs_5_0 ps_5_0, D3D11)")).toBe("Intel UHD Graphics 630");
  expect(rendererName("ANGLE (Apple, ANGLE Metal Renderer: Apple M2, Unspecified Version)")).toBe("Apple M2");
  expect(rendererName("AMD Custom GPU 0405 (radeonsi, vangogh, LLVM 17.0.6, DRM 3.57, 6.5.0-valve)")).toBe("AMD Custom GPU 0405");
  expect(rendererName("Mesa Intel(R) UHD Graphics 620 (KBL GT2)")).toBe("Intel UHD Graphics 620");
  expect(rendererName("ANGLE (Google, Vulkan 1.3.0 (SwiftShader Device (Subzero) (0x0000C0DE)), SwiftShader driver)")).toBeUndefined();
  expect(rendererName("llvmpipe (LLVM 17.0.6, 256 bits)")).toBeUndefined();
  expect(rendererName("")).toBeUndefined();
});
