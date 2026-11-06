//! A mouse pointer from the controller, for game launchers that only take the
//! mouse (Bloodborne's bbport launcher window). In Game Mode nothing else moves a
//! pointer, so such a window couldn't be used at all.
//!
//! While a game started from Big Picture runs, holding Select turns the right
//! stick into the pointer, RT into the left button, LT into the right one, and
//! the left stick into the scroll wheel (pads.rs). The mouse is a virtual device
//! made through /dev/uinput, as Steam Input makes its own, so gamescope and the
//! desktop show the pointer.
use std::fs::File;
use std::io::Write;
use std::os::fd::AsRawFd;

const EV_SYN: u16 = 0;
const EV_KEY: u16 = 1;
const EV_REL: u16 = 2;
const REL_X: u16 = 0;
const REL_Y: u16 = 1;
const REL_WHEEL: u16 = 8;
pub const BTN_LEFT: u16 = 0x110;
pub const BTN_RIGHT: u16 = 0x111;

// <linux/uinput.h>: _IOW('U', 100/101/102, int), _IOW('U', 3, struct uinput_setup), _IO('U', 1/2).
const UI_SET_EVBIT: libc::c_ulong = 0x4004_5564;
const UI_SET_KEYBIT: libc::c_ulong = 0x4004_5565;
const UI_SET_RELBIT: libc::c_ulong = 0x4004_5566;
const UI_DEV_SETUP: libc::c_ulong = 0x405c_5503;
const UI_DEV_CREATE: libc::c_ulong = 0x5501;
const UI_DEV_DESTROY: libc::c_ulong = 0x5502;

/// Pointer speed at full tilt, in pixels a second (before the desktop's acceleration).
const SPEED: f32 = 1400.0;
/// Scroll speed at full tilt, in wheel steps a second.
const SCROLL: f32 = 14.0;
const DEAD_ZONE: f32 = 0.15;

pub struct VirtualMouse {
    device: File,
    /// Movement not sent yet, under a whole pixel or wheel step.
    carry: (f32, f32, f32),
}

impl VirtualMouse {
    pub fn new() -> std::io::Result<VirtualMouse> {
        let device = std::fs::OpenOptions::new().write(true).open("/dev/uinput")?;
        let fd = device.as_raw_fd();
        let ioctl = |request: libc::c_ulong, value: libc::c_ulong| {
            // SAFETY: uinput requests on our own open file, with plain integer arguments.
            if unsafe { libc::ioctl(fd, request as _, value) } < 0 {
                Err(std::io::Error::last_os_error())
            } else {
                Ok(())
            }
        };
        ioctl(UI_SET_EVBIT, EV_KEY.into())?;
        ioctl(UI_SET_EVBIT, EV_REL.into())?;
        ioctl(UI_SET_KEYBIT, BTN_LEFT.into())?;
        ioctl(UI_SET_KEYBIT, BTN_RIGHT.into())?;
        for rel in [REL_X, REL_Y, REL_WHEEL] {
            ioctl(UI_SET_RELBIT, rel.into())?;
        }
        // struct uinput_setup { struct input_id { u16 bustype, vendor, product, version }; char name[80]; u32 ff_effects_max }
        let mut setup = [0u8; 92];
        setup[0..2].copy_from_slice(&0x06u16.to_ne_bytes()); // BUS_VIRTUAL
        let name = b"Big Quiver Launcher pointer";
        setup[8..8 + name.len()].copy_from_slice(name);
        // SAFETY: `setup` is laid out as struct uinput_setup and outlives the call.
        if unsafe { libc::ioctl(fd, UI_DEV_SETUP as _, setup.as_ptr()) } < 0 {
            return Err(std::io::Error::last_os_error());
        }
        ioctl(UI_DEV_CREATE, 0)?;
        Ok(VirtualMouse { device, carry: (0.0, 0.0, 0.0) })
    }

    fn send(&mut self, events: &[(u16, u16, i32)]) {
        let mut bytes = Vec::with_capacity((events.len() + 1) * 24);
        for &(kind, code, value) in events.iter().chain(&[(EV_SYN, 0, 0)]) {
            // struct input_event { struct timeval time (zero: the kernel stamps it); u16 type; u16 code; s32 value }
            bytes.extend_from_slice(&[0u8; 16]);
            bytes.extend_from_slice(&kind.to_ne_bytes());
            bytes.extend_from_slice(&code.to_ne_bytes());
            bytes.extend_from_slice(&value.to_ne_bytes());
        }
        let _ = self.device.write_all(&bytes);
    }

    pub fn button(&mut self, code: u16, down: bool) {
        self.send(&[(EV_KEY, code, down as i32)]);
    }

    /// Moves the pointer and scrolls for `seconds` with the sticks where they are
    /// (-1 to 1, SDL's way: y is positive downwards).
    pub fn steer(&mut self, right: (f32, f32), left_y: f32, seconds: f32) {
        let (dx, dy) = curve(right.0, right.1);
        let wheel = -curve(0.0, left_y).1;
        self.carry.0 += dx * SPEED * seconds;
        self.carry.1 += dy * SPEED * seconds;
        self.carry.2 += wheel * SCROLL * seconds;
        let (x, y, w) = (self.carry.0.trunc(), self.carry.1.trunc(), self.carry.2.trunc());
        self.carry = (self.carry.0 - x, self.carry.1 - y, self.carry.2 - w);
        let mut events = Vec::new();
        if x != 0.0 {
            events.push((EV_REL, REL_X, x as i32));
        }
        if y != 0.0 {
            events.push((EV_REL, REL_Y, y as i32));
        }
        if w != 0.0 {
            events.push((EV_REL, REL_WHEEL, w as i32));
        }
        if !events.is_empty() {
            self.send(&events);
        }
    }
}

impl Drop for VirtualMouse {
    fn drop(&mut self) {
        // SAFETY: destroys the device this file made.
        unsafe { libc::ioctl(self.device.as_raw_fd(), UI_DEV_DESTROY as _, 0) };
    }
}

/// A stick's position past its dead zone, squared for fine control near the middle.
fn curve(x: f32, y: f32) -> (f32, f32) {
    let length = (x * x + y * y).sqrt();
    if length <= DEAD_ZONE {
        return (0.0, 0.0);
    }
    let scaled = ((length.min(1.0) - DEAD_ZONE) / (1.0 - DEAD_ZONE)).powi(2);
    (x / length * scaled, y / length * scaled)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_stick_is_quiet_in_its_dead_zone_and_full_at_the_edge() {
        assert_eq!(curve(0.1, 0.05), (0.0, 0.0));
        let (x, y) = curve(1.0, 0.0);
        assert!((x - 1.0).abs() < 1e-6 && y == 0.0);
        let (half, _) = curve(0.575, 0.0);
        assert!(half > 0.2 && half < 0.3, "{half}");
    }
}
