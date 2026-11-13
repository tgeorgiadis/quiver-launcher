/** Which computer a release file is for, from its name. */
import type { Architecture, Os } from "./index";

export function inferPlatform(filename: string): { os: Os; architecture: Architecture } {
  const s = filename.toLowerCase();
  const os: Os = /windows|(?<!dar)win(?:32|64)|(?:^|[^a-z])win(?:[^a-z]|$)|\.exe$|\.msi$/.test(s)
    ? "windows"
    : /linux|appimage|flatpak|steamdeck|\.deb$|\.rpm$/.test(s)
      ? "linux"
      : /macos|darwin|osx|(?:^|[^a-z])mac(?:[^a-z]|$)|\.dmg$/.test(s)
        ? "macos"
        : /android|\.apk$/.test(s)
          ? "android"
          : /(?:^|[^a-z])ios(?:[^a-z]|$)|iphone|ipados|\.ipa$/.test(s)
            ? "ios"
            : "unknown";
  // "win64" and "linux64" name the architecture too; "darwin64" isn't Windows.
  const architecture: Architecture = /aarch64|arm64/.test(s)
    ? "arm64"
    : /x86[_-]64|amd64|x64|(?<!dar)win(?:dows)?[ _.-]?64|linux[ _.-]?64|(?:^|[^a-z0-9])64[ _-]?bit/.test(s)
      ? "x64"
      : /i[3-6]86|ia32|x86|(?<!dar)win(?:dows)?[ _.-]?32|linux[ _.-]?32|(?:^|[^a-z0-9])32[ _-]?bit/.test(s)
        ? "x86"
        : /universal/.test(s)
          ? "universal"
          : // "arm" only as its own word or armv7/armhf/armel, so "harmony" doesn't count.
            /(?:^|[^a-z])arm(?:v\d|hf|el|[^a-z]|$)/.test(s)
            ? "arm"
            : "unknown";
  return { os, architecture };
}
